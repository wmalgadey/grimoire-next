using System.Text.RegularExpressions;
using Grimoire.Agent;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Grimoire.E2E.Tests;

/// <summary>
/// What the browser puts on the screen for every submission: exactly one of the four states, and
/// nothing further about the run (ACCESS-002).
/// </summary>
/// <remarks>
/// The response shape behind it is proven a level down, in the Fast suite: this is only about what
/// a real browser renders from it. One run at a time (INGEST-005) is why the submissions below are
/// driven one after another — a second text is refused while the first is under way, so the states
/// on the page at any moment are terminal ones plus at most one submission still under way.
/// </remarks>
[Trait("level", "e2e")]
[Trait("req", "ACCESS-002")]
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

        // Accepted, and the agent has not reported in yet: that is where submitted begins and ends.
        var submitted = await hub.SubmitAsync("Alan Turing described a universal machine.", token);

        await Page.GotoAsync(hub.Address);

        await Expect(State(submitted)).ToHaveTextAsync("submitted");
        await Expect(State(failed)).ToHaveTextAsync("failed");
        await Expect(State(done)).ToHaveTextAsync("done");
    }

    [Fact]
    public async Task List_ShowsRunning_AfterTheAgentReportsIn()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);

        await Page.GotoAsync(hub.Address);
        await Expect(State(submission)).ToHaveTextAsync("submitted");

        hub.Agent.ReportIn(submission);

        // The browser polls; nothing is pushed to it (contracts/hub-http-api.md).
        await Expect(State(submission)).ToHaveTextAsync("running");
    }

    [Fact]
    public async Task List_ShowsNothingBeyondTheState()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);
        hub.Agent.ReportIn(submission);
        hub.Agent.End(submission, RunOutcome.Done);

        await Page.GotoAsync(hub.Address);

        // The whole of what the row says: when the text was submitted, and the state. No step, no
        // reasoning, no duration, no cost, no history — OUT-02 owns everything more.
        await Expect(Row(submission)).ToHaveTextAsync(
            new Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2} UTC done$"));

        await Expect(Page.Locator("#submissions li")).ToHaveCountAsync(1);
    }

    private ILocator Row(Guid submission) => Page.Locator($"#submissions li[data-id='{submission}']");

    private ILocator State(Guid submission) => Row(submission).Locator(".state");
}
