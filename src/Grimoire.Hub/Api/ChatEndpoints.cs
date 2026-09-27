using System.Net.ServerSentEvents;
using System.Text.Json.Serialization;
using Grimoire.Agent;
using Grimoire.Runs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Grimoire.Hub.Api;

/// <summary>One thing the agent did to reach an answer, as the browser is told it (ACCESS-007).</summary>
public sealed record ChatStepView(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("tool")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Tool,
    [property: JsonPropertyName("content")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Content);

/// <summary>
/// One question in the chat: what was asked, the answer as it stands, the steps under it, and — where
/// it has a run — what that run has spent (ACCESS-007, ACCESS-008).
/// </summary>
/// <param name="State">
/// Exactly one of <c>waiting</c> · <c>answering</c> · <c>answered</c> · <c>no-answer</c>. Four values
/// inside one requirement, never one requirement per value (Constitution IV.7).
/// </param>
/// <param name="Because">
/// Why it got no answer, and <b>absent</b> in every other case — a reason on a question that was
/// answered would be a reason for nothing (QUERY-006).
/// </param>
/// <param name="CostSpent">
/// What its run has spent, in the quantity the cost ceiling counts. <b>Absent where it has no run</b>,
/// and not a zero: a question waiting its turn has nothing true to say about a run (ACCESS-008).
/// </param>
/// <param name="AwaitingAcknowledgement">
/// Absent unless it got no answer and that failure is still waiting to be seen, and then always true —
/// so the chat either offers the one control or says nothing about it (ACCESS-003, RUNS-003).
/// </param>
public sealed record ChatTurnView(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("askedAt")] DateTimeOffset AskedAt,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("answer")] string Answer,
    [property: JsonPropertyName("steps")] IReadOnlyList<ChatStepView> Steps,
    [property: JsonPropertyName("because")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Because = null,
    [property: JsonPropertyName("costSpent")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    long? CostSpent = null,
    [property: JsonPropertyName("awaitingAcknowledgement")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? AwaitingAcknowledgement = null)
{
    public static ChatTurnView Of(ChatTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);

        return Of(new ChatTurnAsItWas(turn.Question, turn.Answer, turn.Steps));
    }

    /// <summary>
    /// A turn as it stood when a snapshot was taken. The answer and the steps come from that instant;
    /// the state and the figures are read now, because they are absolute and re-reading one says the
    /// same thing rather than doubling anything (<c>Chat.Snapshot</c>).
    /// </summary>
    public static ChatTurnView Of(ChatTurnAsItWas turn)
    {
        ArgumentNullException.ThrowIfNull(turn);

        // One reading of the question's state, its reason, its acknowledgement and its figures, under the
        // board's one lock. Asked separately, a run ending between two answers would put `answering`
        // beside a final figure, or beside an offered acknowledgement — pairs that never existed
        // (ACCESS-007, ACCESS-008).
        var status = turn.Question.Status;

        return new ChatTurnView(
            turn.Question.Id.ToString(),
            turn.Question.Text,
            turn.Question.AskedAt,
            WireNameOf(status.State),
            turn.Answer,
            [.. turn.Steps.Select(step => new ChatStepView(step.Kind, step.Tool, step.Content))],
            status.Because is { } because ? ReasonFor(because) : null,
            status.Run?.CostSpent,
            status.AwaitingAcknowledgement ? true : null);
    }

    /// <summary>Exactly one of the four the chat shows (ACCESS-007).</summary>
    public static string WireNameOf(QuestionState state) => state switch
    {
        QuestionState.Waiting => "waiting",
        QuestionState.Answering => "answering",
        QuestionState.Answered => "answered",
        QuestionState.NoAnswer => "no-answer",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "not one of the four the chat shows"),
    };

    /// <summary>
    /// Why a question got no answer, in the words the chat shows (QUERY-006).
    /// </summary>
    /// <remarks>
    /// The seven reasons a record's tail names, said for a reader of a chat rather than of a record.
    /// Two of them cannot arise for a question's run — a question is not asked for a log entry — and
    /// they are answered anyway rather than left to throw: a reason the chat cannot render would put an
    /// exception where an explanation belongs, on the one path the user is already being told something
    /// went wrong.
    /// </remarks>
    public static string ReasonFor(RunEndedBecause because) => because switch
    {
        RunEndedBecause.TimeCeiling => "it ran out of time",
        RunEndedBecause.CostCeiling => "it reached what a run may spend",
        RunEndedBecause.ToolsWereNotTheGrant => "it was offered tools it was not granted",
        RunEndedBecause.AgentProcessDied => "the agent stopped working",
        RunEndedBecause.GrimoireStopped => "Grimoire was stopped while it was being answered",
        _ => "the run did not finish",
    };
}

/// <summary>
/// The Obsidian vault the wiki is read in, and the wiki's own path inside it (ACCESS-009).
/// </summary>
/// <remarks>
/// On the snapshot rather than written into the page, for the reason the cost ceiling is: these are
/// the hub's start-up values and not the browser's. The browser joins <see cref="WikiPath"/> to a
/// reference's target — which is that page's path relative to the wiki's root — to reach the page in
/// the owner's own vault.
/// </remarks>
public sealed record VaultView(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("wikiPath")] string WikiPath)
{
    /// <summary>
    /// What the browser is told, or nothing — <b>both or neither</b> (ACCESS-009).
    /// </summary>
    /// <remarks>
    /// Half the setting is the same as none of it: a vault with no path inside it addresses the wrong
    /// place, and a path inside a vault nobody named addresses nothing. So the browser is told nothing
    /// rather than something it cannot use, and it says opening is not set up — which is a truer thing
    /// to say than a link that goes somewhere wrong.
    /// <para>
    /// Named here rather than left inside the composition root so that the rule can be read, and
    /// asked, without starting a server: what the browser is <em>told</em> is the boundary and needs
    /// one, but which of the four inputs produce a vault at all is a decision of ours.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Where the wiki sits <b>inside</b> the vault, in the form a link uses — or <c>null</c> where it
    /// does not sit inside it at all (ACCESS-009).
    /// </summary>
    /// <remarks>
    /// What the owner gives is the directory they have open in Obsidian; what a link needs is the
    /// wiki's path within it. <c>--wiki ~/Vault/wiki --vault-root ~/Vault</c> makes <c>wiki</c>, and the
    /// browser joins that to a reference's target.
    /// <para>
    /// It lives here rather than in the entry point because it is a <b>computation</b> and not an
    /// argument being read: passing the owner's directory straight through put an absolute filesystem
    /// path into a link that addresses a place inside a vault, and every reference pointed at nothing.
    /// Argument reading is not tested (III.8); this is, which is the difference that matters.
    /// </para>
    /// <para>
    /// Forward slashes whatever the platform separates paths with, because it is going into a URL and
    /// not onto a disk. Empty where the wiki <em>is</em> the vault, and the target then stands alone.
    /// </para>
    /// </remarks>
    public static string? InVaultPathOf(string vaultRoot, string wikiRoot)
    {
        var inside = Path.GetRelativePath(vaultRoot, wikiRoot);

        // Outside the vault: `GetRelativePath` climbs out, or gives back a rooted path where the two
        // share nothing at all. Either way there is no path inside that vault to give.
        if (inside.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("..")
            || Path.IsPathRooted(inside))
        {
            return null;
        }

        return inside == "." ? string.Empty : inside.Replace(Path.DirectorySeparatorChar, '/').Trim('/');
    }

    /// <remarks>
    /// <b>An empty <paramref name="wikiPath"/> is a value, not a missing one</b>: it is what
    /// <see cref="InVaultPathOf"/> gives when the wiki <em>is</em> the vault, and a reference's target
    /// then stands alone. Rejected as blank, that perfectly ordinary setup would draw no links at all
    /// and say opening was not set up. Null is what "not given" looks like.
    /// </remarks>
    public static VaultView? FromStartUp(string? name, string? wikiPath) =>
        string.IsNullOrWhiteSpace(name) || wikiPath is null
            ? null
            : new VaultView(name, wikiPath);
}

/// <summary>
/// The chat as the browser reads it (QUERY-005, ACCESS-007, ACCESS-008).
/// </summary>
/// <param name="Total">
/// What every question in this chat has spent altogether, a failed one included. <b>No ceiling stands
/// beside it</b>: each question carries its own, and <c>x / y</c> would invent one that does not exist
/// (ACCESS-008).
/// </param>
/// <param name="CostCeiling">
/// The ceiling each question's own figure is written against (GUARD-004). On the snapshot and not on
/// each turn, because it is the hub's value and the same for every run in the chat — the same reason
/// the submissions list carries it once.
/// </param>
/// <param name="Vault">
/// Where a referenced page can be opened, and <b>absent where Grimoire was not told both</b> — the
/// browser then shows page names as plain text and says opening is not set up (ACCESS-009).
/// </param>
public sealed record ChatView(
    [property: JsonPropertyName("turns")] IReadOnlyList<ChatTurnView> Turns,
    [property: JsonPropertyName("total")] long Total,
    [property: JsonPropertyName("costCeiling")] long CostCeiling,
    [property: JsonPropertyName("vault")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    VaultView? Vault = null);

/// <summary>What the browser posts to ask the wiki.</summary>
public sealed record QuestionRequest([property: JsonPropertyName("text")] string? Text);

/// <summary>
/// The chat's door into the hub, exactly as <c>contracts/hub-http-api.md</c> specifies it.
/// </summary>
public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChat(
        this IEndpointRouteBuilder endpoints,
        ChatIntake intake,
        Chat chat,
        RunBoard board,
        RunQueue queue,
        SubmissionsEndpoints.StartUpInputsCheck startUpInputs,
        LiveUpdates live,
        VaultView? vault)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(intake);
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(live);

        endpoints.MapPost("/api/chat/questions", async (QuestionRequest? request) =>
        {
            // No request token reaches the run: it outlives the request that asked for it (QUERY-001).
            var result = await intake.AskAsync(request?.Text ?? string.Empty, startUpInputs())
                .ConfigureAwait(false);

            // 202 with the turn as the stream's snapshot carries one, and no location: there is no
            // endpoint for one question, and the browser reads the chat. **No run identifier is in it**
            // — the chat addresses the question (contracts/hub-http-api.md).
            return result.Accepted is { } accepted
                ? Results.Json(TurnFor(chat, accepted), statusCode: StatusCodes.Status202Accepted)
                : Refused(result.Refused!.Value);
        });

        // The chat, sent as it changes. It opens with the whole of it and then carries **the one thing
        // that changed** — one more field would be a mechanism with no consumer (Constitution II.1).
        endpoints.MapGet("/api/chat/events", (CancellationToken token) =>
            TypedResults.ServerSentEvents(
                live.Watch(
                LiveUpdates.Chat,
                sent => Opening(chat, sent, vault),
                sent => Increments(chat, sent, vault),
                token)));

        // A new chat (QUERY-005). Every browser reading the chat is sent the new, empty snapshot,
        // because there is one chat and they all read it — so this empties both tabs rather than the
        // one it was asked from. `Chat.Start` wakes them itself.
        //
        // **A question still being answered is not stopped.** Its run is not a chat and goes on being
        // a run: it holds the queue until it ends and its figures stay with it (RUNS-010). What it
        // produces belongs to the chat that is gone (research.md R-13).
        endpoints.MapPost("/api/chat", async () =>
        {
            // The questions this chat was about, read before it goes. One of them may be a failure that
            // is holding the queue, and its control is about to be removed from the screen along with
            // the turn it sat on.
            var asked = chat.Snapshot().Turns.Select(turn => turn.Question.Id).ToList();

            chat.Start();

            // **A failure the user can no longer see does not hold the queue** — RUNS-003's last
            // clause, which a new chat reaches as surely as a stop does. Without this, starting a new
            // chat over an unacknowledged failure left the queue blocked by a question on no screen
            // with no control to clear it, until Grimoire was restarted — and a new chat is the remedy
            // this feature offers for a failed question, so the remedy was the trap.
            //
            // A question still being answered is untouched: it is not a failure, and its run goes on
            // holding the queue exactly as it should (QUERY-005, research.md R-13).
            board.AcknowledgeQuestions(asked);

            // Asked either way, as the acknowledgement below asks: the board decides whether anything
            // may start, and one that cleared nothing simply leaves it deciding no.
            await queue.PumpAsync().ConfigureAwait(false);

            return Results.NoContent();
        });

        // The user has seen that this question got no answer (ACCESS-003, RUNS-003, QUERY-006).
        //
        // It exists because a failed question blocks the queue exactly as a failed ingest does, and
        // there is no row in the submissions list to clear it from — a question is not a submission.
        endpoints.MapPost("/api/chat/questions/{id:guid}/acknowledgement", async (Guid id) =>
        {
            board.AcknowledgeQuestion(id);

            // Asked either way. The board decides whether anything may start, and an acknowledgement
            // that cleared nothing simply leaves it deciding no.
            await queue.PumpAsync().ConfigureAwait(false);

            // **One status for both cases**, as the submission's acknowledgement already gives: a page
            // loaded before the last run failed can acknowledge a failure that has already been
            // cleared, and answering that with an error would put a failure on the user's screen for a
            // request that did exactly what it should — nothing.
            return Results.NoContent();
        });

        return endpoints;
    }

    /// <summary>
    /// The snapshot: every turn, the total and the ceiling (ACCESS-007, ACCESS-008).
    /// </summary>
    /// <remarks>
    /// This subscriber is put at the <b>end</b> of what has changed, not the beginning: the snapshot
    /// already carries all of it, so there is nothing of the past to send and nothing is replayed from
    /// the change log. That is the whole of ACCESS-007's reconnect clause — a browser that comes back
    /// reads the chat as it then stands, including what arrived while it was away.
    /// </remarks>
    private static IEnumerable<SseItem<object>> Opening(Chat chat, Sent sent, VaultView? vault)
    {
        // The turns and the position in the change log, taken together. Read apart, a piece of an
        // answer arriving between the two would be in this snapshot *and* past the position — so the
        // next wake would send it again and the browser would show it twice.
        yield return SnapshotOf(chat.Snapshot(), sent, vault);
    }

    /// <summary>The whole chat as one event, and this subscriber moved to where that reading ends.</summary>
    private static SseItem<object> SnapshotOf(ChatSnapshot snapshot, Sent sent, VaultView? vault)
    {
        sent.Generation = snapshot.Generation;
        sent.Changes = snapshot.Changes.Count;

        return new SseItem<object>(
            new ChatView(
                [.. snapshot.Turns.Select(ChatTurnView.Of)],
                snapshot.Turns.Sum(turn => turn.Question.Figures?.CostSpent ?? 0),
                Ceilings.Fixed.Cost,
                vault),
            ChatEvents.Chat);
    }

    /// <summary>
    /// What this subscriber has not been told, one event per change (ACCESS-007).
    /// </summary>
    /// <remarks>
    /// A new chat is a fresh snapshot rather than a run of increments: the turns it would refer to are
    /// gone, and every browser reading the chat is sent the new, empty one (QUERY-005).
    /// </remarks>
    private static IEnumerable<SseItem<object>> Increments(Chat chat, Sent sent, VaultView? vault)
    {
        // **One reading, and the generation read from it** — not asked for separately before it. Asked
        // first, a new chat starting between the question and the snapshot would give this pass an
        // empty chat with a new generation and nothing to send: the wake that the new chat raised would
        // be spent here producing no event, and the browser would go on showing the old conversation
        // until some later change happened to arrive. Which, on a chat that was just emptied, may be
        // never.
        var snapshot = chat.Snapshot();

        if (snapshot.Generation != sent.Generation)
        {
            yield return SnapshotOf(snapshot, sent, vault);
            yield break;
        }

        for (var at = sent.Changes; at < snapshot.Changes.Count; at++)
        {
            var change = snapshot.Changes[at];

            // A turn a descriptor names is always in the same snapshot, so this cannot be missed — only
            // a new chat starting between the generation check above and this reading takes one away,
            // and the next wake answers that with a snapshot of its own.
            if (snapshot.Turns.FirstOrDefault(turn => turn.Question.Id == change.Question) is not { } turn)
            {
                continue;
            }

            yield return Increment(chat, turn, change);
        }

        sent.Changes = snapshot.Changes.Count;
    }

    private static SseItem<object> Increment(Chat chat, ChatTurnAsItWas turn, ChatChange change)
    {
        var id = turn.Question.Id.ToString();

        return change.Event switch
        {
            // **Always `waiting`**, and not the state read now. This descriptor records a question
            // joining the chat, and a question joins it waiting its turn (QUERY-001); by the time a
            // subscriber consumes the descriptor its run may already have started, and carrying
            // `answering` here would say the question arrived in a state it was never in — and draw it
            // that way for the instant before the `question` event that actually reports the change
            // (contracts/hub-http-api.md).
            ChatEvents.Asked => new SseItem<object>(
                new
                {
                    id,
                    text = turn.Question.Text,
                    askedAt = turn.Question.AskedAt,
                    state = ChatTurnView.WireNameOf(QuestionState.Waiting),
                },
                ChatEvents.Asked),

            // The piece of the answer this change named, read out of the turn that holds it. The slice
            // is stable because an answer only grows (ChatChange).
            ChatEvents.Answer => new SseItem<object>(
                new { id, append = turn.Answer[change.From..change.To] },
                ChatEvents.Answer),

            ChatEvents.Step => new SseItem<object>(
                new
                {
                    id,
                    step = new ChatStepView(
                        turn.Steps[change.Step].Kind,
                        turn.Steps[change.Step].Tool,
                        turn.Steps[change.Step].Content),
                },
                ChatEvents.Step),

            // The state, the reason and both figures — read as one instant, as the snapshot's turn is.
            // The total comes with it because a question's spend is what moves it, and a browser told
            // one without the other would show a total that does not add up to what is above it
            // (ACCESS-008).
            _ => new SseItem<object>(QuestionChanged(chat, turn), ChatEvents.Question),
        };
    }

    private static object QuestionChanged(Chat chat, ChatTurnAsItWas turn)
    {
        var view = ChatTurnView.Of(turn);

        return new
        {
            id = view.Id,
            state = view.State,
            because = view.Because,
            costSpent = view.CostSpent,

            // **Beyond what contracts/hub-http-api.md lists for this event**, and it has to be. The
            // control ACCESS-003 asks for is offered on the strength of this, and a question's failure
            // arrives as an increment — a browser that only learnt it from a snapshot would show no
            // control until something else made it reconnect. Whether a failure is still waiting to be
            // seen is part of the question's state, which is what this event is for.
            awaitingAcknowledgement = view.AwaitingAcknowledgement,
            total = chat.Total,
        };
    }

    /// <summary>The accepted question's turn, as the stream's snapshot carries one.</summary>
    private static ChatTurnView TurnFor(Chat chat, Question accepted) =>
        chat.Snapshot().Turns.FirstOrDefault(turn => turn.Question.Id == accepted.Id) is { } turn
            ? ChatTurnView.Of(turn)
            : throw new InvalidOperationException($"question {accepted.Id} was accepted but is in no chat");

    /// <summary>
    /// The one reason a question was refused, in the order the contract checks them (QUERY-003).
    /// </summary>
    private static IResult Refused(QuestionRefusal refusal)
    {
        var (reason, message) = refusal switch
        {
            QuestionRefusal.QuestionInstructionMissing => (
                "question-instruction-missing",
                "Grimoire's question instruction is missing. No question can be answered without it."),
            QuestionRefusal.PurposeDescriptionMissing => (
                "purpose-description-missing",
                "The purpose description is missing. No question can be answered without it."),
            QuestionRefusal.TextEmpty => (
                "question-empty",
                "There is no question to ask."),

            _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "no such refusal"),
        };

        return Results.Json(
            new RefusalView(reason, message), statusCode: StatusCodes.Status422UnprocessableEntity);
    }
}
