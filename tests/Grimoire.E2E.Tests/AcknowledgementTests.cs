using Grimoire.Agent;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Grimoire.E2E.Tests;

/// <summary>
/// The user clears a failure from the browser and the queue moves on (ACCESS-003, RUNS-003).
/// </summary>
/// <remarks>
/// One scenario, of the two this user story is allowed (Constitution III.4). What the rule is —
/// that nothing starts while a failure stands, and that the acknowledged run stays failed — is
/// proven a level down against the real board. ACCESS-003 says "in the browser", and that half
/// cannot be proven below a real one: this is a person finding the control on the right row and
/// pressing it.
/// </remarks>
[Trait("level", "e2e")]
[Trait("req", "ACCESS-003")]
[Trait("req", "RUNS-003")]
public sealed class AcknowledgementTests : PageTest
{
    [Fact]
    public async Task Acknowledge_StartsTheWaitingSubmission_AndLeavesTheRunFailed()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var failed = await hub.SubmitAsync("Alan Turing described a universal machine.", token);
        hub.Agent.ReportIn(failed);
        hub.Agent.End(failed, RunOutcome.Failed);

        // Handed over while the first was still under way, and waiting behind it ever since.
        var waiting = await hub.SubmitAsync("Grace Hopper found the first bug in a relay.", token);

        await Page.GotoAsync(hub.Address);

        // Nothing has started: the failure holds the queue, and the waiting text reads submitted.
        await Expect(State(waiting)).ToHaveTextAsync("submitted");
        await Expect(State(failed)).ToHaveTextAsync("failed");

        // The control is offered on the failed row and on no other. Which row that is, the user
        // tells from the opening of their own text beside it (ACCESS-004).
        await Expect(Acknowledgement(waiting)).ToHaveCountAsync(0);
        await Expect(Row(failed)).ToContainTextAsync("Alan Turing described a universal machine.");

        await Acknowledgement(failed).ClickAsync();

        // The queue moved: the waiting text's run started, and the agent can now report in on it.
        hub.Agent.ReportIn(waiting);
        await Expect(State(waiting)).ToHaveTextAsync("running");

        // And the run the user acknowledged still reads failed, with the control gone.
        await Expect(State(failed)).ToHaveTextAsync("failed");
        await Expect(Acknowledgement(failed)).ToHaveCountAsync(0);
    }

    private ILocator Row(Guid submission) => Page.Locator($"#submissions li[data-id='{submission}']");

    private ILocator State(Guid submission) => Row(submission).Locator(".state");

    private ILocator Acknowledgement(Guid submission) => Row(submission).GetByRole(AriaRole.Button);
}
