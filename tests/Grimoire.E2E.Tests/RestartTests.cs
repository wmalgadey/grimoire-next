using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Grimoire.E2E.Tests;

/// <summary>
/// Grimoire stopped and started again: the browser shows the same submissions, and the run that
/// was in progress reads failed (RUNS-004).
/// </summary>
/// <remarks>
/// One scenario, of the two this user story is allowed (Constitution III.4). The rule is proven in
/// the Fast suite and the file in the Contract suite; what only a real hub started twice over one
/// state can show is that the two are wired to each other — that what the board wrote really went
/// into the file, and that what the second process read really came back out of it (research.md
/// R-09).
/// </remarks>
[Trait("level", "e2e")]
[Trait("req", "RUNS-004")]
public sealed class RestartTests : PageTest
{
    [Fact]
    public async Task Restart_ShowsEverySubmission_WithTheRunThatWasInProgressFailed()
    {
        var token = TestContext.Current.CancellationToken;
        await using var before = await HubUnderTest.StartAsync(token);

        var finished = await before.SubmitAsync("Ada Lovelace wrote the first program.", token);
        before.Agent.ReportIn(finished);
        before.Agent.End(finished, Grimoire.Agent.RunOutcome.Done);

        // Under way at the moment of the stop, with a text waiting behind it.
        var interrupted = await before.SubmitAsync("Alan Turing described a universal machine.", token);
        before.Agent.ReportIn(interrupted);
        var waiting = await before.SubmitAsync("Grace Hopper found the first bug in a relay.", token);

        await using var after = await HubUnderTest.RestartedAsync(before, token);
        await Page.GotoAsync(after.Address);

        // Every submission is still listed, each with the state it carried — and the one that was
        // running reads failed, because nothing is resumed and nothing is retried.
        await Expect(State(finished)).ToHaveTextAsync("done");
        await Expect(State(interrupted)).ToHaveTextAsync("failed");

        // The waiting text did not start: that failure holds the queue until it is acknowledged
        // (RUNS-003).
        await Expect(State(waiting)).ToHaveTextAsync("submitted");
        await Expect(Row(waiting)).ToContainTextAsync("Grace Hopper found the first bug in a relay.");

        await Acknowledgement(interrupted).ClickAsync();

        // The queue moves on across the restart: the waiting text's run started, and its agent —
        // this hub's, not the one that went down with the last process — can report in on it.
        after.Agent.ReportIn(waiting);
        await Expect(State(waiting)).ToHaveTextAsync("running");
        await Expect(State(interrupted)).ToHaveTextAsync("failed");
    }

    private ILocator Row(Guid submission) => Page.Locator($"#submissions li[data-id='{submission}']");

    private ILocator State(Guid submission) => Row(submission).Locator(".state");

    private ILocator Acknowledgement(Guid submission) => Row(submission).GetByRole(AriaRole.Button);
}
