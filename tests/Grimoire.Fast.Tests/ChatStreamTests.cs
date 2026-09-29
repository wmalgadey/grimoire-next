using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Grimoire.Agent;
using Grimoire.Hub;
using Grimoire.Hub.Api;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The chat, sent as it changes: it opens with the whole of it, and every event after that carries
/// <b>the one thing that changed</b> and nothing else (ACCESS-007, ACCESS-008).
/// </summary>
/// <remarks>
/// <para>
/// In process, through <c>HubApplication.Build</c> with in-memory adapters at every owned port, which
/// is the application the composition root builds too (Constitution III.9). Nothing outside the
/// process is involved: loopback, no files of the wiki's, and a clock the test moves itself.
/// </para>
/// <para>
/// That <c>TypedResults.ServerSentEvents</c> frames an event, and that a browser's
/// <c>EventSource</c> reconnects, are framework and browser behaviour and are not tested
/// (Constitution III.8, research.md R-01). What is tested is what we put on the stream: each
/// event's <b>name</b> and the fields of its one line of JSON — because "the one thing that changed"
/// is half the name and half the body, and one field more would be a mechanism with no consumer
/// (Constitution II.1).
/// </para>
/// <para>
/// No test here waits for real time. Every change a stream reports is one this test makes through
/// the harness, so the event is already on its way before it is asked for (Constitution III.7). A
/// <see cref="HostedHub"/> is a real server, so there are as few of them as the scenarios allow and
/// a snapshot is read by opening another stream on the hub that is already up.
/// </para>
/// </remarks>
[Trait("level", "fast")]
public sealed class ChatStreamTests
{
    private const string AboutAda = "What does the wiki say about Ada Lovelace?";
    private const string AboutHerMother = "And who was her mother?";

    [Fact]
    [Trait("req", "ACCESS-007")]
    [Trait("req", "ACCESS-008")]
    public async Task Stream_OpensWithTheChatAsItStands()
    {
        await using var hub = new HostedHub();

        // A turn with a run under it, so that the state, the steps and the figures are part of what
        // the snapshot is read for: an empty chat would say nothing about any of them.
        var question = await hub.AskAsync(AboutAda);
        Said(hub, question, "She wrote the first program.");
        hub.Agent.Called(question, "read_page", """{"path":"people/ada-lovelace.md"}""");
        hub.Agent.Spend(question, 12_000);

        await using var stream = await hub.WatchAsync("/api/chat/events");

        var opening = await stream.NextAsync<ChatView>("chat");
        var turn = Assert.Single(opening.Turns);

        // Every turn whole — what was asked, the answer as it stands and the steps under it — because
        // a browser that has just connected has nothing to update in place and must be given all of
        // it (ACCESS-007).
        Assert.Equal(question.ToString(), turn.Id);
        Assert.Equal(AboutAda, turn.Text);
        Assert.Equal("answering", turn.State);
        Assert.Equal("She wrote the first program.", turn.Answer);
        var step = Assert.Single(turn.Steps);

        Assert.Equal(ChatStep.Called, step.Kind);
        Assert.Equal("read_page", step.Tool);
        Assert.Equal("""{"path":"people/ada-lovelace.md"}""", step.Content);

        // The total, and the ceiling each question's own figure is written against. The figure means
        // nothing alone — input-token equivalents have no scale a reader carries in their head — so a
        // browser that only ever reads the stream must be given it too (ACCESS-008, GUARD-004).
        Assert.Equal(12_000, opening.Total);
        Assert.Equal(Ceilings.Fixed.Cost, opening.CostCeiling);
    }

    [Fact]
    [Trait("req", "ACCESS-007")]
    public async Task Stream_SendsTheOneThingThatChanged_AfterEachChange()
    {
        await using var hub = new HostedHub();

        // Subscribed before anything happens: the subscriber is registered when enumeration starts,
        // so everything below reaches this stream as an increment rather than as a snapshot.
        await using var stream = await hub.WatchAsync("/api/chat/events");

        Assert.Empty((await stream.NextAsync<ChatView>("chat")).Turns);

        var question = await hub.AskAsync(AboutAda);
        var id = question.ToString();

        // A question joined the chat: what was asked, when, and the state it is in. Not the chat
        // around it and not the total, which has not moved (ACCESS-007).
        var asked = await NextNewsAsync(stream);

        Assert.Equal(ChatEvents.Asked, asked.Event);
        Assert.Equal(["id", "text", "askedAt", "state"], FieldsOf(asked.Data));

        Said(hub, question, "She wrote the first program.");

        // The piece of the answer that arrived, and which question it belongs under. **Nothing
        // else**: no state, no total and no steps — the browser appends this to the text node that
        // is already there, and a field it does not read would be one nobody consumes.
        var answer = await NextNewsAsync(stream);

        Assert.Equal(ChatEvents.Answer, answer.Event);
        Assert.Equal(["id", "append"], FieldsOf(answer.Data));

        var appended = Read<AnswerSent>(answer.Data);

        Assert.Equal(id, appended.Id);
        Assert.Equal("She wrote the first program.", appended.Append);

        hub.Agent.Called(question, "read_page", """{"path":"people/ada-lovelace.md"}""");

        // And the step that happened, the one step and not the list it joined.
        var happened = await NextNewsAsync(stream);

        Assert.Equal(ChatEvents.Step, happened.Event);
        Assert.Equal(["id", "step"], FieldsOf(happened.Data));

        var one = Read<StepSent>(happened.Data);

        Assert.Equal(id, one.Id);
        Assert.Equal(ChatStep.Called, one.Step.Kind);
        Assert.Equal("read_page", one.Step.Tool);
    }

    [Fact]
    [Trait("req", "ACCESS-007")]
    public async Task Stream_CarriesTheOneStateEachQuestionIsIn()
    {
        await using var hub = new HostedHub();

        var first = await hub.AskAsync(AboutAda);
        var second = await hub.AskAsync(AboutHerMother);

        // Read off whether there is a run: nothing was ahead of the first, so it has one and reads
        // `answering`; the second has none while the first holds the one slot, and reads `waiting`
        // (RUNS-002).
        Assert.Equal(["answering", "waiting"], States(await SnapshotAsync(hub)));

        await using var stream = await hub.WatchAsync("/api/chat/events");
        await stream.NextAsync<ChatView>("chat");

        // The first one's agent stops inside both ceilings and its process exits cleanly, which is
        // the whole of a question being answered (RUNS-005, RUNS-008) — and the second one's run then
        // starts, with nobody asking anything.
        await hub.Agent.StoppedAsync(first);
        hub.Agent.Exit(first, exitCode: 0);

        await UntilAsync(stream, first, "answered");
        await UntilAsync(stream, second, "answering");

        // And a run that ended any other way leaves its question with no answer.
        hub.Agent.End(second, RunOutcome.Failed, RunEndedBecause.TimeCeiling);

        await UntilAsync(stream, second, "no-answer");

        var chat = await SnapshotAsync(hub);

        // Four values inside one requirement, and a question is in exactly one of them: read from
        // whether there is a run and whether it has ended, never held beside the run and never two
        // at once (ACCESS-007, research.md R-03).
        Assert.Equal(["answered", "no-answer"], States(chat));

        // Which is also why a question that was answered carries no reason: a reason there would be
        // a reason for nothing.
        Assert.Null(chat.Turns[0].Because);
        Assert.NotNull(chat.Turns[1].Because);
    }

    [Fact]
    [Trait("req", "ACCESS-008")]
    [Trait("req", "RUNS-010")]
    public async Task Stream_CarriesWhatARunSpent_WithoutOneForAQuestionWaitingItsTurn()
    {
        await using var hub = new HostedHub();

        var answering = await hub.AskAsync(AboutAda);
        var waiting = await hub.AskAsync(AboutHerMother);

        hub.Agent.Spend(answering, 12_000);

        await using var stream = await hub.WatchAsync("/api/chat/events");

        var (name, data) = await stream.NextAsync();

        Assert.Equal(ChatEvents.Chat, name);

        var opening = Read<ChatView>(data);

        // The run's own figure, in the quantity the cost ceiling counts — never a second count of its
        // own, and never currency (RUNS-010, DEC-015).
        Assert.Equal(12_000, opening.Turns[0].CostSpent);
        Assert.Equal(12_000, opening.Total);
        Assert.Equal(Ceilings.Fixed.Cost, opening.CostCeiling);

        // The one waiting its turn has **no `costSpent` at all**, read off the JSON rather than off a
        // deserialised null: a zero would claim a run that has spent nothing rather than no run, and
        // there is nothing true to say about a run that does not exist (ACCESS-008, RUNS-002).
        var turns = TurnsOf(data);

        Assert.Contains("costSpent", FieldsOf(turns[0]));
        Assert.DoesNotContain("costSpent", FieldsOf(turns[1]));
        Assert.Equal(waiting.ToString(), Read<TurnSent>(turns[1]).Id);

        // And the total stands with **no ceiling beside it**: the one ceiling the snapshot carries is
        // the one each question's own figure is written against, and a total dressed as `x / y` would
        // invent a second that does not exist (ACCESS-008).
        Assert.Equal(
            ["costCeiling"],
            FieldsOf(data).Where(field => field.Contains("eiling", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("req", "ACCESS-007")]
    [Trait("req", "QUERY-005")]
    public async Task Stream_OpensWithWhatArrivedWhileNobodyWasReading()
    {
        await using var hub = new HostedHub();

        var question = await hub.AskAsync(AboutAda);

        await using (var reading = await hub.WatchAsync("/api/chat/events"))
        {
            Assert.Empty(Assert.Single((await reading.NextAsync<ChatView>("chat")).Turns).Answer);
        }

        // The tab is closed, and the run goes on writing into the chat: the chat is the hub's and not
        // a subscriber's, so what arrives now reaches nobody and is held all the same (QUERY-005).
        Said(hub, question, "She wrote the first program.");
        hub.Agent.Called(question, "read_page", """{"path":"people/ada-lovelace.md"}""");
        hub.Agent.Spend(question, 12_000);

        await using var again = await hub.WatchAsync("/api/chat/events");

        // A browser that subscribes again is given a fresh snapshot, and its **first** event is that
        // snapshot — not an increment replayed from a buffer, of which there is none: this subscriber
        // starts at the end of what has changed, because the snapshot already carried all of it
        // (ACCESS-007).
        var opening = await again.NextAsync<ChatView>("chat");
        var turn = Assert.Single(opening.Turns);

        Assert.Equal("She wrote the first program.", turn.Answer);
        Assert.Equal("read_page", Assert.Single(turn.Steps).Tool);
        Assert.Equal(12_000, turn.CostSpent);
        Assert.Equal(12_000, opening.Total);

        // And the next event it is sent is the next change, not the ones it was never told about —
        // which is the whole of ACCESS-007's reconnect clause, with no `Last-Event-ID` anywhere in it.
        Said(hub, question, " Her mother was Annabella Milbanke.");

        var next = await NextNewsAsync(again);

        Assert.Equal(ChatEvents.Answer, next.Event);
        Assert.Equal(" Her mother was Annabella Milbanke.", Read<AnswerSent>(next.Data).Append);
    }

    /// <summary>The agent's own text, which is what the answer is made of (research.md R-08).</summary>
    [Fact]
    [Trait("req", "ACCESS-007")]
    public async Task Asked_SaysTheQuestionIsWaiting_EvenWhereItsRunHasAlreadyStarted()
    {
        await using var hub = new HostedHub();
        await using var stream = await hub.WatchAsync("/api/chat/events");

        await stream.NextAsync<ChatView>("chat");

        // Nothing else holds the queue, so asking starts the question's run before this returns: by the
        // time the event below is read, the question is already being answered.
        await hub.AskAsync(AboutAda);

        var (name, data) = await stream.NextAsync();

        // The `asked` event records a question **joining the chat**, and a question joins it waiting its
        // turn (QUERY-001). Read at the moment it is serialised instead, it would say `answering` —
        // a state the question arrived in only afterwards, drawn for the instant before the `question`
        // event that actually reports the change (contracts/hub-http-api.md).
        Assert.Equal("asked", name);
        Assert.Contains("\"state\":\"waiting\"", data, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("req", "QUERY-001")]
    [Trait("req", "ACCESS-007")]
    public async Task Asked_ReachesTheBrowser_WhileTheQuestionWaitsBehindSomethingElse()
    {
        await using var hub = new HostedHub();

        // A submission takes the one run slot, so the question is accepted and then **waits**
        // (RUNS-002). Nothing about it changes again until the queue reaches it.
        await hub.SubmitAsync("Ada Lovelace wrote the first program.");

        await using var stream = await hub.WatchAsync("/api/chat/events");
        await stream.NextAsync<ChatView>("chat");

        await hub.AskAsync(AboutAda);

        // It is drawn while it waits. The board raises its own change before the question is on the
        // chat, so that one reaches no turn — and without the chat raising one of its own when the turn
        // is added, an accepted question would sit there unanswered *and* undrawn until something else
        // happened to change (Chat.Changed).
        var (name, data) = await stream.NextAsync();

        Assert.Equal("asked", name);
        Assert.Contains("\"state\":\"waiting\"", data, StringComparison.Ordinal);
        Assert.Contains(AboutAda, data, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("req", "ACCESS-009")]
    public async Task Stream_OpensWithTheVaultTheWikiIsReadIn_WhenGrimoireWasToldBoth()
    {
        await using var hub = new HostedHub(vaultName: "Notes", wikiPathInVault: "wiki");

        var opening = await SnapshotAsync(hub);

        // Both settings as the owner gave them, so that the browser can build the link itself: the
        // vault to open, and the wiki's own path inside it, which a reference's target hangs off
        // (ACCESS-009).
        Assert.Equal(new VaultView("Notes", "wiki"), opening.Vault);
    }

    [Fact]
    [Trait("req", "ACCESS-009")]
    public async Task Stream_OpensWithNoVault_WhereGrimoireWasNotToldBoth()
    {
        await using var hub = new HostedHub();

        // The field is **absent**, not null: the browser then shows a page's name as plain text and
        // says opening is not set up, and a key that was there holding nothing would be a setting it
        // had to interpret (ACCESS-009).
        Assert.DoesNotContain("vault", await SnapshotFieldsAsync(hub));
    }

    [Theory]
    [Trait("req", "ACCESS-009")]
    [InlineData("/home/me/Vault", "/home/me/Vault/wiki", "wiki")]
    [InlineData("/home/me/Vault", "/home/me/Vault/notes/wiki", "notes/wiki")]
    [InlineData("/home/me/Vault", "/home/me/Vault", "")]
    [InlineData("/home/me/Vault/", "/home/me/Vault/wiki/", "wiki")]
    public void Vault_CarriesWhereTheWikiSitsInsideIt(string vaultRoot, string wiki, string inside)
    {
        // What the owner gives is the directory they have open in Obsidian; what a link needs is the
        // wiki's path **within** it. Passed straight through, an absolute filesystem path went into a
        // link that addresses a place inside a vault, and every reference pointed at nothing
        // (ACCESS-009, quickstart.md).
        Assert.Equal(inside, VaultView.InVaultPathOf(vaultRoot, wiki));
    }

    [Theory]
    [InlineData("/home/me/Vault", "/home/me/elsewhere/wiki")]
    [InlineData("/home/me/Vault/wiki", "/home/me/Vault")]
    [InlineData("/home/me/Vault", "/etc/wiki")]
    public void Vault_IsNothing_WhereTheWikiIsNotInsideIt(string vaultRoot, string wiki)
    {
        // A wiki outside the vault has no path inside it, so there is nothing to build a link from.
        // The entry point refuses such a start rather than drawing links that address a place that is
        // not there — silently drawing none would leave the owner wondering why a setting they gave
        // does nothing.
        Assert.Null(VaultView.InVaultPathOf(vaultRoot, wiki));
    }

    [Fact]
    [Trait("req", "ACCESS-009")]
    public void Vault_IsCarried_WhereTheWikiIsTheVaultItself()
    {
        // An empty path is a **value**, not a missing one: it is what the wiki being the vault makes,
        // and a reference's target then stands alone. Read as blank, that ordinary setup would draw no
        // links and say opening was not set up (ACCESS-009).
        Assert.Equal(new VaultView("Notes", string.Empty), VaultView.FromStartUp("Notes", string.Empty));
    }

    [Theory]
    [Trait("req", "ACCESS-009")]
    [InlineData("Notes", null)]
    [InlineData(null, "wiki")]
    [InlineData(null, null)]
    [InlineData("  ", "wiki")]
    public void Vault_IsNothing_WhereOnlyOneOfTheTwoWasGiven(string? name, string? wikiPath)
    {
        // **Both or neither.** Half the setting is the same as none of it: a vault with no path inside
        // it addresses the wrong place, and a path inside a vault nobody named addresses nothing.
        //
        // Read without a server, because this is which inputs make a vault at all — a decision of ours.
        // That what it decides then reaches the browser, present or absent, is the two tests above it,
        // and that is the boundary (Constitution III.6).
        Assert.Null(VaultView.FromStartUp(name, wikiPath));
    }

    [Fact]
    [Trait("req", "ACCESS-007")]
    public async Task Stream_CarriesOneStepForEachThingTheAgentDid()
    {
        await using var hub = new HostedHub();

        var question = await hub.AskAsync(AboutAda);

        await using var stream = await hub.WatchAsync("/api/chat/events");
        await stream.NextAsync<ChatView>("chat");

        hub.Agent.Called(question, "read_page", """{"path":"people/ada-lovelace.md"}""");
        Returned(hub, question, "read_page", "# Ada Lovelace");
        hub.Agent.Called(question, "list_pages", "{}");

        // One event per call and one per result, in the order they happened: the user checking an
        // answer reads what the agent did as a sequence, and a pair folded into one entry — or a result
        // arriving before the call it belongs to — would not be what happened (ACCESS-007).
        Assert.Equal(
            [
                (ChatStep.Called, "read_page", """{"path":"people/ada-lovelace.md"}"""),
                (ChatStep.Returned, "read_page", "# Ada Lovelace"),
                (ChatStep.Called, "list_pages", "{}"),
            ],
            [await NextStepAsync(stream), await NextStepAsync(stream), await NextStepAsync(stream)]);
    }

    [Fact]
    [Trait("req", "ACCESS-007")]
    public async Task Stream_CarriesWhatCameBackWhole()
    {
        await using var hub = new HostedHub();

        var question = await hub.AskAsync(AboutAda);

        await using var stream = await hub.WatchAsync("/api/chat/events");
        await stream.NextAsync<ChatView>("chat");

        var page = APageOfSomeLength();

        Returned(hub, question, "read_page", page);

        // Exactly what came back, to the character: a wiki page of some length, with its blank lines,
        // a fenced block and a name that is not ASCII. Never cut to a first line, never a count of
        // lines, never a sentence about it — the user checking an answer is checking this very text
        // against the page it came from (ACCESS-007).
        Assert.Equal((ChatStep.Returned, "read_page", page), await NextStepAsync(stream));
    }

    /// <summary>
    /// A page long enough that any cutting shows, and made of the things a wiki page is made of: blank
    /// lines, a fenced block and a non-ASCII name.
    /// </summary>
    private static string APageOfSomeLength()
    {
        var page = new StringBuilder("---\ntitle: Ada Lovelace\n---\n\n# Ada Lovelace\n\n");

        page.Append("Countess of Lovelace, née Byron — she wrote the first program.\n\n");
        page.Append("```\nBEGIN\n  note G\nEND\n```\n");

        while (page.Length < 4000)
        {
            page.Append("\nShe worked on the Analytical Engine, and on note G in particular.\n");
        }

        return page.ToString();
    }

    [Fact]
    [Trait("req", "QUERY-006")]
    [Trait("req", "ACCESS-007")]
    public async Task Stream_CarriesWhyAQuestionGotNoAnswer()
    {
        await using var hub = new HostedHub();

        var question = await hub.AskAsync(AboutAda);

        hub.Agent.End(question, RunOutcome.Failed, RunEndedBecause.CostCeiling);

        var turn = Assert.Single((await SnapshotAsync(hub)).Turns);

        // The state and the reason together, in the words the chat shows: a question that got no
        // answer without one would tell the user something went wrong and nothing about what
        // (QUERY-006).
        Assert.Equal("no-answer", turn.State);
        Assert.Equal(ChatTurnView.ReasonFor(RunEndedBecause.CostCeiling), turn.Because);
        Assert.NotEmpty(turn.Because!);
    }

    [Fact]
    [Trait("req", "QUERY-006")]
    [Trait("req", "ACCESS-003")]
    public async Task Stream_OffersTheAcknowledgement_WhileTheFailureIsUnacknowledged()
    {
        await using var hub = new HostedHub();

        var question = await hub.AskAsync(AboutAda);

        hub.Agent.End(question, RunOutcome.Failed, RunEndedBecause.TimeCeiling);

        // There, and always `true`: the chat offers the one control on the strength of this, and a
        // failed question blocks the queue until it is used (ACCESS-003, RUNS-003).
        Assert.Equal("true", ValueOf(Assert.Single(await SnapshotTurnsAsync(hub)), "awaitingAcknowledgement"));
    }

    [Fact]
    [Trait("req", "ACCESS-003")]
    public async Task Stream_OffersNoAcknowledgement_AfterTheFailureWasAcknowledged()
    {
        await using var hub = new HostedHub();

        var question = await hub.AskAsync(AboutAda);

        hub.Agent.End(question, RunOutcome.Failed, RunEndedBecause.TimeCeiling);
        await AcknowledgeAsync(hub, question);

        // The key is **absent**, read off the JSON rather than off a deserialised false: the chat
        // either offers the control or says nothing about it, and a field standing there holding
        // `false` would be one the browser had to interpret (ACCESS-003).
        var turn = Assert.Single(await SnapshotTurnsAsync(hub));

        Assert.DoesNotContain("awaitingAcknowledgement", FieldsOf(turn));

        // And the question still reads *got no answer*, as an acknowledged submission still reads
        // failed: acknowledging says the user has seen it and is not a state (QUERY-006).
        Assert.Equal("no-answer", Read<QuestionSent>(turn).State);
    }

    [Fact]
    public async Task Acknowledge_AnswersWithNothing_WhenItClearedAFailure()
    {
        await using var hub = new HostedHub();

        var question = await hub.AskAsync(AboutAda);

        hub.Agent.End(question, RunOutcome.Failed, RunEndedBecause.TimeCeiling);

        // No body either way. What the user reads afterwards is the chat, which the stream has already
        // been sent (contracts/hub-http-api.md).
        Assert.Equal(HttpStatusCode.NoContent, (await AcknowledgeAsync(hub, question)).StatusCode);
    }

    [Fact]
    public async Task Acknowledge_AnswersWithNothing_WhenItWasAcknowledgedAlready()
    {
        await using var hub = new HostedHub();

        var question = await hub.AskAsync(AboutAda);

        hub.Agent.End(question, RunOutcome.Failed, RunEndedBecause.TimeCeiling);
        await AcknowledgeAsync(hub, question);

        // The same answer for a request that cleared nothing: a page loaded before the last run failed
        // sends this, and it did exactly what it should (QUERY-006, RUNS-003).
        Assert.Equal(HttpStatusCode.NoContent, (await AcknowledgeAsync(hub, question)).StatusCode);
    }

    [Fact]
    public async Task Acknowledge_AnswersWithNothing_WhenTheQuestionIsUnknown()
    {
        await using var hub = new HostedHub();

        // A question of the chat that is gone, which is every question after a new chat is started: it
        // is on no screen and on no disk, and answering with an error would put a failure on the user's
        // screen for a request that did nothing (QUERY-005, contracts/hub-http-api.md).
        Assert.Equal(HttpStatusCode.NoContent, (await AcknowledgeAsync(hub, Guid.NewGuid())).StatusCode);
    }

    /// <summary>The user has seen that this question got no answer (ACCESS-003).</summary>
    private static Task<HttpResponseMessage> AcknowledgeAsync(HostedHub hub, Guid question) =>
        hub.PostAsync($"/api/chat/questions/{question}/acknowledgement");

    /// <summary>
    /// The snapshot's turns, each still as the JSON it was sent as — which is how a field that is
    /// absent is told from one sent holding nothing.
    /// </summary>
    private static async Task<IReadOnlyList<string>> SnapshotTurnsAsync(HostedHub hub)
    {
        await using var stream = await hub.WatchAsync("/api/chat/events");

        var (name, data) = await stream.NextAsync();

        Assert.Equal(ChatEvents.Chat, name);

        return TurnsOf(data);
    }

    /// <summary>
    /// What one named field was sent as, read off the JSON — or null where it is absent. A field
    /// carrying <c>true</c> is told from one carrying <c>false</c>, and both from one that is not there.
    /// </summary>
    private static string? ValueOf(string json, string field)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.TryGetProperty(field, out var value) ? value.GetRawText() : null;
    }

    [Fact]
    [Trait("req", "QUERY-005")]
    [Trait("req", "RUNS-003")]
    public async Task NewChat_StartsWhatWasWaitingBehindAFailureItTookAway()
    {
        await using var hub = new HostedHub();

        var failed = await hub.AskAsync(AboutAda);

        hub.Agent.End(failed, RunOutcome.Failed, RunEndedBecause.AgentProcessDied);

        // A failure nobody has acknowledged holds the queue (RUNS-003), so this waits.
        var waiting = await hub.SubmitAsync("Ada Lovelace wrote the first program.");

        Assert.Null(hub.Runs.Of(waiting));

        // **Starting a new chat takes that failure off the screen**, and with it the only control that
        // could have cleared it. A failure the user can no longer see must not hold the queue — the
        // clause RUNS-003 gained for a stop, which a new chat reaches as surely. Without this the queue
        // was blocked by a question on no screen with no way to clear it, until Grimoire was restarted
        // — and a new chat is the remedy this feature offers for a failed question, so the remedy was
        // the trap.
        (await hub.PostAsync("/api/chat")).EnsureSuccessStatusCode();

        Assert.NotNull(hub.Runs.Of(waiting));
    }

    [Fact]
    [Trait("req", "QUERY-005")]
    public async Task Stream_SendsTheEmptyChat_AfterANewOneWasStarted()
    {
        await using var hub = new HostedHub();
        await using var stream = await hub.WatchAsync("/api/chat/events");

        await stream.NextAsync<ChatView>("chat");

        var question = await hub.AskAsync(AboutAda);
        Said(hub, question, "She wrote the first program.");

        (await hub.PostAsync("/api/chat")).EnsureSuccessStatusCode();

        // The increments the asking and the answering put on the stream come first; what has to arrive
        // is a fresh snapshot, and an empty one. The generation and the turns are read together, so a
        // new chat starting between the two cannot spend this subscriber's wake producing no event and
        // leave the browser showing a conversation that is gone.
        //
        // Read forward to it rather than counting what came before: how many increments one question
        // makes is not what this test is about, and pinning it would break on any change to that.
        ChatView? afresh = null;

        for (var read = 0; read < 12 && afresh is null; read++)
        {
            var (name, data) = await stream.NextAsync();

            if (name == "chat")
            {
                afresh = JsonSerializer.Deserialize<ChatView>(data);
            }
        }

        Assert.NotNull(afresh);
        Assert.Empty(afresh.Turns);
        Assert.Equal(0, afresh.Total);
    }

    [Fact]
    [Trait("req", "ACCESS-003")]
    public async Task Acknowledge_ClearsNoSubmission_WhenItsIdIsPostedToTheChat()
    {
        await using var hub = new HostedHub();

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.");

        hub.Agent.ReportIn(submission);
        hub.Agent.End(submission, RunOutcome.Failed);

        var behind = await hub.SubmitAsync("Grace Hopper found the first bug.");
        Assert.Null(hub.Runs.Of(behind));

        // The chat's acknowledgement addresses a **question**. Given a submission's id — which the chat
        // never showed and the user could only have from elsewhere — it clears nothing, and the failure
        // goes on holding the queue until it is acknowledged where it is shown (ACCESS-003).
        var answered = await hub.PostAsync($"/api/chat/questions/{submission}/acknowledgement");

        Assert.Equal(HttpStatusCode.NoContent, answered.StatusCode);
        Assert.Null(hub.Runs.Of(behind));

        // And the door it belongs to does clear it.
        (await hub.PostAsync($"/api/submissions/{submission}/acknowledgement")).EnsureSuccessStatusCode();

        Assert.NotNull(hub.Runs.Of(behind));
    }

    private static void Said(HostedHub hub, Guid question, string text) =>
        hub.Agent.Did(question, new TranscriptMoment(RunMomentKind.AgentSaid, null, text));

    /// <summary>What a tool call came back with, which is the other half of the pair a run makes.</summary>
    private static void Returned(HostedHub hub, Guid question, string tool, string content) =>
        hub.Agent.Did(question, new TranscriptMoment(RunMomentKind.ToolReturned, tool, content));

    /// <summary>The next <c>step</c> event, as the three things one step is (ACCESS-007).</summary>
    private static async Task<(string Kind, string? Tool, string? Content)> NextStepAsync(EventStream stream)
    {
        var sent = await NextNewsAsync(stream);

        Assert.Equal(ChatEvents.Step, sent.Event);

        var step = Read<StepSent>(sent.Data).Step;

        return (step.Kind, step.Tool, step.Content);
    }

    /// <summary>
    /// Which fields the snapshot carries, read off the JSON itself: <c>vault</c> is written only when
    /// there is one, and a deserialised null cannot tell a field that is absent from one sent as null.
    /// </summary>
    private static async Task<IReadOnlyList<string>> SnapshotFieldsAsync(HostedHub hub)
    {
        await using var stream = await hub.WatchAsync("/api/chat/events");

        var (name, data) = await stream.NextAsync();

        Assert.Equal(ChatEvents.Chat, name);

        return FieldsOf(data);
    }

    /// <summary>
    /// The chat as it now stands, read the one way a browser reads it — by opening the stream and
    /// taking the snapshot it opens with. No asked-for endpoint answers the chat.
    /// </summary>
    private static async Task<ChatView> SnapshotAsync(HostedHub hub)
    {
        await using var stream = await hub.WatchAsync("/api/chat/events");

        return await stream.NextAsync<ChatView>("chat");
    }

    /// <summary>
    /// The next event that is news about a turn rather than the chat saying a question changed.
    /// </summary>
    /// <remarks>
    /// A run being handed to a question is several changes at once — it left the queue, it has a run,
    /// the run reported in — and each of them is one <c>question</c> event, because the chat says that
    /// something about a question changed and never which of its facts it was. So a test reads past
    /// those to the event it is about, and everything else fails the assertion on the name rather
    /// than being skipped.
    /// </remarks>
    private static async Task<(string Event, string Data)> NextNewsAsync(EventStream stream)
    {
        while (true)
        {
            var sent = await stream.NextAsync();

            if (!string.Equals(sent.Event, ChatEvents.Question, StringComparison.Ordinal))
            {
                return sent;
            }
        }
    }

    /// <summary>
    /// Read forward until the chat reports this question in this state. A test names what it waits
    /// for rather than counting events, for the reason <see cref="NextNewsAsync"/> gives.
    /// </summary>
    private static async Task UntilAsync(EventStream stream, Guid question, string state)
    {
        var id = question.ToString();

        while (true)
        {
            var (name, data) = await stream.NextAsync();

            if (!string.Equals(name, ChatEvents.Question, StringComparison.Ordinal))
            {
                continue;
            }

            var changed = Read<QuestionSent>(data);

            if (string.Equals(changed.Id, id, StringComparison.Ordinal)
                && string.Equals(changed.State, state, StringComparison.Ordinal))
            {
                return;
            }
        }
    }

    private static IEnumerable<string> States(ChatView chat) => chat.Turns.Select(turn => turn.State);

    /// <summary>
    /// Which fields one event's JSON actually carries — the assertion "and nothing else" is made of.
    /// </summary>
    private static IReadOnlyList<string> FieldsOf(string json)
    {
        using var document = JsonDocument.Parse(json);

        return [.. document.RootElement.EnumerateObject().Select(field => field.Name)];
    }

    /// <summary>The snapshot's turns, each still as the JSON it was sent as.</summary>
    private static IReadOnlyList<string> TurnsOf(string json)
    {
        using var document = JsonDocument.Parse(json);

        return [.. document.RootElement.GetProperty("turns").EnumerateArray().Select(turn => turn.GetRawText())];
    }

    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json)!;

    /// <summary>An <c>answer</c> event, which is a question and a piece of prose to append.</summary>
    private sealed record AnswerSent(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("append")] string Append);

    /// <summary>A <c>step</c> event, which is a question and the one step that happened.</summary>
    private sealed record StepSent(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("step")] ChatStepView Step);

    /// <summary>A <c>question</c> event, as far as a test reading states needs to know it.</summary>
    private sealed record QuestionSent(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("state")] string State);

    /// <summary>One turn of the snapshot, as far as a test reading raw JSON needs to know it.</summary>
    private sealed record TurnSent([property: JsonPropertyName("id")] string Id);
}
