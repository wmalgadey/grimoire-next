using Grimoire.Agent;
using Grimoire.Hub.Api;
using Grimoire.Hub.Mcp;
using Grimoire.Runs;
using Grimoire.Wiki;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Grimoire.Hub;

/// <summary>
/// What the hub was started with. Every run is served the same three: the wiki it writes into, the
/// two texts it is given, and the model it runs on.
/// </summary>
/// <param name="Model">
/// A pinned model id, never an alias and never the default — a start-up input, not a per-submission
/// choice (research.md R-11).
/// </param>
/// <param name="QuestionInstructionPath">
/// Grimoire's own question instruction, versioned in this repository. Changing it is an owner decision
/// named in the PR, exactly as changing the ingest one is (QUERY-004, Constitution V.1).
/// </param>
/// <param name="VaultName">
/// The Obsidian vault the wiki is read in, or null where the owner has not said. Optional, and its
/// absence refuses nothing: the answer still arrives and the page's name is still readable in it
/// (ACCESS-009).
/// </param>
/// <param name="WikiPathInVault">
/// The wiki's own path <b>inside</b> that vault — <c>wiki</c>, not <c>~/Vault/wiki</c> — which is what
/// a reference's target hangs off: the browser joins this to the target to reach the page.
/// <para>
/// It is <em>derived</em> from what the owner gives. They pass <c>--vault-root</c>, the directory they
/// actually have open in Obsidian, and the entry point works out where the wiki sits inside it — see
/// <c>StartUp.Read</c>. Carrying the owner's directory here instead would put an absolute filesystem
/// path into a link that addresses a place inside a vault, which is a different thing entirely.
/// </para>
/// </param>
public sealed record HubOptions(
    string InstructionPath,
    string QuestionInstructionPath,
    string PurposeDescriptionPath,
    string WikiRoot,
    string Model,
    string? VaultName = null,
    string? WikiPathInVault = null);

/// <summary>
/// The composition root: the one place that knows every context (plan.md, Structure Decision).
/// </summary>
/// <remarks>
/// The agent adapter is a parameter rather than a lookup, because which one is in place is exactly
/// what distinguishes a real run from an exercised one: <c>HarnessProcess</c> runs the real CLI,
/// and the E2E suite supplies an in-memory adapter at the same port (Constitution III.9).
/// </remarks>
public static class HubApplication
{
    /// <summary>
    /// Everything the last Grimoire left behind, put back before this one serves anything
    /// (RUNS-004, RUNS-006).
    /// </summary>
    /// <remarks>
    /// The order is the requirement's rather than an implementation detail, because all of it is
    /// observable: an agent that outlived a stop Grimoire could not act on is terminated
    /// <b>first</b>, the runs that were in progress read failed <b>second</b>, and only then may
    /// anything start. The browser must never show failed while the agent is still at work, and no
    /// second run may begin beside a first that is still writing (research.md R-11).
    /// <para>
    /// A run with no recorded process never had a child, and one whose recorded identity is no
    /// longer a live process is left alone by the adapter. Nothing is resumed and nothing is
    /// retried; what an interrupted run wrote stays in the wiki (WIKI-003).
    /// </para>
    /// <para>
    /// Between those two the record is closed. A stop Grimoire could act on writes the tail as the
    /// run ends (<c>RunConductor.Finish</c>); a kill or a power cut leaves the file head-and-moments
    /// only, and this is the next moment Grimoire has. Without it the record and the submission
    /// would disagree for good — the row reading failed beside a record that never says the run
    /// ended, let alone why (RUNS-007, RUNS-008). It is written <b>before</b> the board is told, for
    /// the reason the conductor writes it there: the browser reads the row and the record at once, so
    /// a row that reads ended must not reach a page whose record does not say the run ended.
    /// </para>
    /// </remarks>
    public static void RestoreAfterAStop(
        ISubmissionStore submissions,
        RunBoard board,
        IAgentHarness harness,
        IRunRecord record,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(submissions);
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(harness);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(clock);

        var held = submissions.Load();
        var interrupted = held.Where(s => s.WasUnderWay).Select(s => s.Run!).ToList();

        // And the runs a question caused that were in progress. They have no submission to read a
        // state off, which is why the store is asked for them separately — and they have to be asked
        // for at all, because a run with nothing on disk would leave an orphaned `claude` holding the
        // granted tools with no ceiling on it (RUNS-006, research.md R-04).
        var questions = submissions.LoadRunsWithoutASubmission();

        foreach (var identity in interrupted.Concat(questions)
            .Select(r => r.AgentProcess).OfType<AgentProcessIdentity>())
        {
            harness.Terminate(identity);
        }

        foreach (var run in interrupted)
        {
            record.Ended(TailOfAnInterruptedRun(run, clock.GetUtcNow()));
        }

        // **No tail for a question's run** — it has no record (RUNS-007) — and nothing is restored
        // into a chat, because QUERY-005 empties it. What is left to do is mark the run ended failed,
        // so that the next start-up does not read it as one to terminate again.
        foreach (var run in questions)
        {
            submissions.RunEnded(run.Id, run.CostSpent, run.Tokens, run.ToolCalls, run.EntriesLost);
        }

        board.Restore(held);
    }

    /// <summary>
    /// The tail of a run that was in progress when Grimoire stopped without being given the chance
    /// to write one (RUNS-008's seventh reason).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ending is read off what the store kept, because it is all there is: the process that
    /// knew the rest is gone. <c>TokensPerModel</c> is therefore empty — the per-model breakdown of
    /// DEC-015 is streamed and never stored, while the run's cost and the four raw counts behind it
    /// are kept and written. An empty breakdown beside them says which of the two survived;
    /// inventing one entry for the run's model would claim a figure nobody measured.
    /// </para>
    /// <para>
    /// <c>EndedAt</c> is now, because now is when the run was ended, and <c>Elapsed</c> is
    /// <c>null</c>: this start-up knows when the run began and not when it stopped running, and the
    /// span between the two is mostly however long Grimoire was down. A record that stated it would
    /// be stating a duration nobody timed — and one that could read past the elapsed ceiling beside
    /// a reason that is not a ceiling. The head still holds the run's start.
    /// </para>
    /// </remarks>
    private static RunFrameTail TailOfAnInterruptedRun(StoredRun run, DateTimeOffset at) =>
        new(
            run.Id,
            at,
            RunOutcome.Failed,
            RunEndedBecause.GrimoireStopped,
            Elapsed: null,
            run.CostSpent,
            run.Tokens,
            Ceilings.Fixed,
            new Dictionary<string, ModelTokens>(StringComparer.Ordinal));

    /// <summary>
    /// The hub is going down: nothing further may start, and what is under way is stopped with it
    /// (RUNS-006).
    /// </summary>
    /// <remarks>
    /// The order is the point, and it is three steps rather than two. Stopping a run ends it, and
    /// an ending lets the next one start — so stopping first would dispatch an agent behind the
    /// shutdown. Closing admission alone is not enough either: a pump already past its own check
    /// can be holding a submission the board has handed out, and that one would be dispatched into
    /// a hub that had finished stopping, with nothing watching it. So: close, drain, then stop
    /// (RUNS-006).
    /// </remarks>
    public static async Task StopEverythingAsync(RunQueue queue, RunConductor conductor)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(conductor);

        queue.StopStartingRuns();
        await queue.DrainAsync().ConfigureAwait(false);
        await conductor.StopEverythingAsync().ConfigureAwait(false);
    }

    public static WebApplication Build(
        string[] args,
        HubOptions options,
        IAgentHarness harness,
        IWikiStore wiki,
        ISubmissionStore submissions,
        IRunRecord record,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(harness);
        ArgumentNullException.ThrowIfNull(submissions);

        // The content root is the hub's own base directory rather than whatever directory it was
        // launched from, so the page under wwwroot/ is found the same way whether the hub was
        // started from the command line or built by a test.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        // One line per entry, stamped, so that what the console says can be held against the clock
        // while a run is under way. UTC, because everything else the running system shows is UTC
        // too — the browser's list of submissions, and the `generated.at` on every page a run
        // writes — and a log that needs an offset applied before it can be compared is a log that
        // will be compared wrongly.
        builder.Logging.AddSimpleConsole(console =>
        {
            console.SingleLine = true;
            console.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
            console.UseUtcTimestamp = true;
        });

        // What is left after this is one line as a request arrives and one as it finishes — the
        // agent's tool calls among them, which is how a run is watched. The three categories
        // turned down here only restate that same request: the endpoint that was selected, the
        // static file that was sent, the result type that was written.
        builder.Logging.AddFilter("Microsoft.AspNetCore.Routing", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore.StaticFiles", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Http.Result", LogLevel.Warning);

        // The wiki tools, served from the hub itself over streamable HTTP. Putting them here keeps
        // the stamping, the grant and both ceilings in one place where Fast tests reach them, and
        // leaves the agent one door into the wiki (research.md R-02).
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(clock);
        builder.Services.AddSingleton(wiki);
        builder.Services.AddSingleton(sp => new RunAddress(
            sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(), options.Model));
        builder.Services.AddWikiToolSurfaces();

        // **The session's catalogue is its grant.** `WithTools` is deliberately not called: it builds
        // one collection for the whole server, and `MapMcp` serves that same one at every pattern — so
        // the two routes below would both serve all five tools, which is what GUARD-005 exists to
        // prevent (WikiToolSurfaces). Each session is given the catalogue its route calls for instead,
        // and what is not in it does not exist for that run.
        builder.Services.AddMcpServer()
            .WithHttpTransport(http => http.ConfigureSessionOptions = (context, options, _) =>
            {
                options.ToolCollection = WikiToolSurfaces.For(context, context.RequestServices);
                return Task.CompletedTask;
            });

        var app = builder.Build();

        // The pages are static content served from wwwroot/ — three HTML files and three scripts, no
        // build step and no bundler (DEC-019, research.md R-10, R-14).
        app.UseDefaultFiles();
        app.UseStaticFiles();

        var instructions = new InstructionLoader(
            options.InstructionPath, options.QuestionInstructionPath, options.PurposeDescriptionPath);

        // What the browser is sent while it has a page open. Built here and given to everything that
        // knows something changed, so every suite gets the streams the browser gets (Constitution
        // III.9) — the wiring itself is not tested; what it wires is (III.8).
        var live = new LiveUpdates();

        // The one chat, held for as long as this hub runs and written down nowhere (QUERY-005). It
        // wakes the browsers reading it itself, at every place it changes something, so no caller has
        // to remember to — which is what left an accepted question undrawn while it waited.
        var chat = new Chat(() => live.Changed(LiveUpdates.Chat));

        // Which stream a change matters to is decided here and not by the board, which does not know
        // there are two. A submission's change is the list; a question's is the chat. Told the wrong
        // one, a page would sit still while what it shows moved on.
        var board = new RunBoard(
            clock,
            submissions,
            queued =>
        {
            // A question's change is the chat's, and the chat has to be told *which* question before its
            // subscribers are woken: they read the change log forward, so a wake with nothing recorded
            // is a wake with nothing to send.
            if (queued is Question)
            {
                // The run is read here, inside the board's lock, and handed over — the chat must never
                // take this lock from inside its own (Chat.answering). The chat wakes its own readers.
                chat.QuestionChanged(queued.Id, queued.RunId);
                return;
            }

            live.Changed(LiveUpdates.Submissions);
        },

            // A question joins the chat here, inside the board's lock, in the same breath as it was
            // accepted — so the order the chat shows is the order the queue will run them in, and no
            // pump can hand this question a run before there is a turn for that run to be mapped to
            // (RunBoard.Accepted).
            queued =>
            {
                if (queued is Question asked)
                {
                    chat.Ask(asked);
                }
            });

        // The knot the conductor and the queue make, tied here because neither may hold the other
        // whole: a run that ends is what lets the next one start, and starting one is what gives
        // the conductor a run to watch. The composition root is where that is allowed to be known
        // (plan.md, Structure Decision).
        RunQueue? queue = null;
        var conductor = new RunConductor(
            board, harness, wiki, record, chat, live, clock, options.Model, () => queue!.PumpAsync());
        queue = new RunQueue(
            board,
            conductor,
            harness,
            instructions.Assemble,
            (question, runId) => instructions.AssembleQuestion(chat, question, runId));

        var intake = new SubmissionIntake(board, queue);
        var asking = new ChatIntake(board, queue);

        app.MapSubmissions(intake, board, queue, instructions.Read, live);
        app.MapChat(
            asking,
            chat,
            board,
            queue,
            instructions.Read,
            live,
            VaultView.FromStartUp(options.VaultName, options.WikiPathInVault));
        app.MapRunRecord(board, record, live);

        // One endpoint per run: the identifier in the path is how a tool call is attributed to
        // its run. Unauthenticated and on loopback, per docs/product.md §2.
        //
        // **Two doors, and the second serves two tools.** Which catalogue a session gets is decided
        // per session from the route it was opened on (`WikiToolSurfaces`), because mapping a second
        // pattern does not give a second catalogue. Which door a run is dispatched at travels on its
        // grant, so the two cannot be crossed (GUARD-005, DEC-011).
        app.MapMcp($"{WikiToolSurfaces.RunsDoor}/{{runId}}");
        app.MapMcp($"{WikiToolSurfaces.QuestionsDoor}/{{runId}}");

        RestoreAfterAStop(submissions, board, harness, record, clock);

        // The pump waits for the server. A run is told where its own tools are served before it
        // starts, so starting one before this hub is listening would hand the agent an address
        // that answers nothing and end the run on its first tool call (GUARD-001).
        app.Lifetime.ApplicationStarted.Register(() => _ = queue.PumpAsync());

        // No agent goes on working on a run Grimoire has ended (RUNS-006). The hook itself is
        // framework wiring and is not tested; what it calls is (Constitution III.8, research.md R-05).
        app.Lifetime.ApplicationStopping.Register(() => StopEverythingAsync(queue, conductor).GetAwaiter().GetResult());

        return app;
    }
}
