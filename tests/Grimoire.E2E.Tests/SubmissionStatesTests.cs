using Grimoire.Agent;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Grimoire.E2E.Tests;

/// <summary>
/// What the browser puts on the screen for every submission: exactly one of the four states, and —
/// where it has a run — that run's model, the tokens it has spent and the tool calls it has made
/// (ACCESS-005).
/// </summary>
/// <remarks>
/// Two scenarios, which is what a user story is allowed at this level (Constitution III.4). The
/// response shape behind them is proven a level down, in the Fast suite; this is only about what a
/// real browser renders from it — the state, and beside it the opening of the submitted text and
/// when it was made (ACCESS-004).
/// <para>
/// The submissions below are driven one after another so that each reaches the state this is about
/// before the next is made. With a queue they no longer have to be submitted that way: a text
/// handed over while a run is under way is accepted and waits its turn (RUNS-002).
/// </para>
/// </remarks>
[Trait("level", "e2e")]
[Trait("req", "ACCESS-005")]
[Trait("req", "ACCESS-004")]
public sealed class SubmissionStatesTests : PageTest
{
    [Fact]
    public async Task List_ShowsEachSubmissionInItsState()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var done = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);
        hub.Agent.ReportIn(done);
        hub.Agent.End(done, RunOutcome.Done);

        var failed = await hub.SubmitAsync("Grace Hopper found the first bug.", token);
        hub.Agent.ReportIn(failed);
        hub.Agent.End(failed, RunOutcome.Failed);

        // Acknowledged, so that the queue moves on and a third submission can reach a state at all:
        // a failure nobody has seen holds it (RUNS-003). The row still reads failed.
        await hub.AcknowledgeAsync(failed, token);

        // Accepted, and the agent has not reported in yet: that is where submitted begins and ends.
        var underWay = await hub.SubmitAsync("Alan Turing described a universal machine.", token);

        await Page.GotoAsync(hub.Address);

        await Expect(State(underWay)).ToHaveTextAsync("submitted");
        await Expect(State(failed)).ToHaveTextAsync("failed");
        await Expect(State(done)).ToHaveTextAsync("done");

        // The agent reports in while the page is open. The browser polls; nothing is pushed to it
        // (contracts/hub-http-api.md).
        hub.Agent.ReportIn(underWay);

        await Expect(State(underWay)).ToHaveTextAsync("running");
    }

    [Fact]
    public async Task List_ShowsTheModelAndBothFigures_ForARunThatHasEnded()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);
        hub.Agent.ReportIn(submission);
        hub.Agent.Spend(submission, 148_233);
        hub.Agent.Called(submission, "read_page", """{"path":"ada.md"}""");
        hub.Agent.Called(submission, "write_page", """{"path":"ada.md"}""");
        hub.Agent.End(submission, RunOutcome.Done);

        await Page.GotoAsync(hub.Address);

        // Beside the state: the model the run ran on and the two figures it ended with. This is the
        // half of ACCESS-005 no in-process test reaches — what the browser actually renders.
        await Expect(State(submission)).ToHaveTextAsync("done");
        await Expect(Row(submission).Locator(".model")).ToHaveTextAsync("claude-opus-4-5-20251101");
        await Expect(Row(submission).Locator(".tokens")).ToHaveTextAsync("148\u2009233");
        await Expect(Row(submission).Locator(".calls")).ToHaveTextAsync("2");

        // And no identifier is rendered — neither the submission's nor the run's.
        await Expect(Row(submission)).Not.ToContainTextAsync(submission.ToString());
    }

    [Fact]
    public async Task List_ShowsNoRunFigures_ForASubmissionWaitingItsTurn()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        // The first submission's run holds the queue, so the second waits its turn and has no run
        // (RUNS-002). A row with no run shows no figures at all — not zeros.
        var running = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);
        hub.Agent.ReportIn(running);
        var waiting = await hub.SubmitAsync("Grace Hopper found the first bug.", token);

        await Page.GotoAsync(hub.Address);

        await Expect(State(waiting)).ToHaveTextAsync("submitted");
        await Expect(Row(waiting).Locator(".model")).ToHaveCountAsync(0);
        await Expect(Row(waiting).Locator(".figure")).ToHaveCountAsync(0);
        await Expect(Row(running).Locator(".model")).ToHaveCountAsync(1);
    }

    private ILocator Row(Guid submission) => Page.Locator($"#submissions li[data-id='{submission}']");

    private ILocator State(Guid submission) => Row(submission).Locator(".state");
}
