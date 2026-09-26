using Grimoire.Agent;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Grimoire.E2E.Tests;

/// <summary>
/// The owner opens a run from the list and reads its record: the frame, then what the run did in the
/// order it happened, with each result folded until they want it (ACCESS-006).
/// </summary>
/// <remarks>
/// The folding and the segmentation are the browser's, and only a real browser exercises them: the
/// endpoint's answer is proven a level down, in the Fast suite, and what <c>run.js</c> makes of it is
/// only observable here (DEC-019's precedent for ACCESS-001).
/// </remarks>
[Trait("level", "e2e")]
[Trait("req", "ACCESS-006")]
public sealed class RunRecordViewTests : PageTest
{
    /// <summary>
    /// A result long enough that folding it is the point, and holding a fence and a heading of its
    /// own — which is what a run reading wiki pages full of code returns, and what the record's fence
    /// rule exists for (research.md R-04).
    /// </summary>
    private const string ALongResult =
        "## Ada Lovelace\n\n```\nthe first program\n```\n\nShe wrote the first program.";

    [Fact]
    public async Task Run_IsOpenedFromItsRowAndReadInOrder()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);
        hub.Agent.ReportIn(submission);
        hub.Agent.Called(submission, "read_page", """{"path":"ada.md"}""");
        hub.Agent.Returned(submission, "read_page", ALongResult);
        hub.Agent.Said(submission, "Ada Lovelace already has a page. I will add the date.");
        hub.Agent.Called(submission, "write_page", """{"path":"ada.md"}""");
        hub.Agent.End(submission, RunOutcome.Done);

        await Page.GotoAsync(hub.Address);

        // Opened from the row, which is the only way in: the link carries the submission, not the run.
        await Row(submission).Locator(".open-run").ClickAsync();

        // The frame first — the model it ran on and both ceilings (RUNS-008).
        await Expect(Page.Locator("#frame")).ToContainTextAsync("claude-opus-4-5-20251101");
        await Expect(Page.Locator("#frame")).ToContainTextAsync("Granted tools");

        // Then what the run did, in the order it happened, and the tail last.
        await Expect(Segments()).ToHaveCountAsync(5);
        await Expect(Heading(0)).ToContainTextAsync("called read_page");
        await Expect(Heading(1)).ToContainTextAsync("read_page returned");
        await Expect(Heading(2)).ToContainTextAsync("the agent");
        await Expect(Heading(3)).ToContainTextAsync("called write_page");
        await Expect(Heading(4)).ToContainTextAsync("ended done");

        // The agent's own text is prose and is simply there.
        await Expect(Segments().Nth(2)).ToContainTextAsync("I will add the date.");
    }

    [Fact]
    public async Task Result_IsFoldedUntilTheUserOpensItAndIsThenWhole()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);
        hub.Agent.ReportIn(submission);
        hub.Agent.Called(submission, "read_page", """{"path":"ada.md"}""");
        hub.Agent.Returned(submission, "read_page", ALongResult);
        hub.Agent.End(submission, RunOutcome.Done);

        await Page.GotoAsync($"{hub.Address}/run.html?submission={submission}");

        var result = Segments().Nth(1).Locator("details.result");

        // Folded: the user can follow what the run did without reading the results in full.
        await Expect(result.Locator("pre")).Not.ToBeVisibleAsync();

        await result.Locator("summary").ClickAsync();

        // And whole once they reach for it — the fence and the heading inside it included, because
        // nothing of a result is cut and nothing is escaped (RUNS-009).
        await Expect(result.Locator("pre")).ToBeVisibleAsync();
        await Expect(result.Locator("pre")).ToHaveTextAsync(ALongResult);
    }

    [Fact]
    [Trait("req", "RUNS-009")]
    public async Task Result_IsOneSegment_WhenItHoldsALineStartingWithTwoHashes()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);
        hub.Agent.ReportIn(submission);
        hub.Agent.Called(submission, "read_page", """{"path":"ada.md"}""");
        hub.Agent.Returned(submission, "read_page", ALongResult);
        hub.Agent.End(submission, RunOutcome.Done);

        await Page.GotoAsync($"{hub.Address}/run.html?submission={submission}");

        // The result holds `## Ada Lovelace` at column one. A reader finds the fence first and skips
        // to its close, so that line is no boundary and the result stays one segment
        // (contracts/run-record.md, rule 4). Three segments: the call, its result, the tail.
        await Expect(Segments()).ToHaveCountAsync(3);
        await Expect(Heading(1)).ToContainTextAsync("read_page returned");
    }

    [Fact]
    public async Task AgentText_IsShownWhole_WhenItHoldsAFencedBlock()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        // An agent writes fenced code as a matter of course. Read as a result, the fence would be
        // folded away and every word around it thrown out of the rendering (ACCESS-006).
        const string said = "I will add this to the page:\n\n```\nada.md\n```\n\nand then log it.";

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);
        hub.Agent.ReportIn(submission);
        hub.Agent.Said(submission, said);
        hub.Agent.End(submission, RunOutcome.Done);

        await Page.GotoAsync($"{hub.Address}/run.html?submission={submission}");

        var agent = Segments().Nth(0);

        await Expect(Heading(0)).ToContainTextAsync("the agent");

        // Prose, not a folded result — and all of it, both sides of the fence included.
        await Expect(agent.Locator("details.result")).ToHaveCountAsync(0);
        await Expect(agent.Locator(".prose")).ToContainTextAsync("I will add this to the page:");
        await Expect(agent.Locator(".prose")).ToContainTextAsync("and then log it.");
        await Expect(agent.Locator(".prose")).ToContainTextAsync("ada.md");
    }

    [Fact]
    public async Task Moments_ArriveBelowWhatIsThere_WithoutDisturbingIt()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);
        hub.Agent.ReportIn(submission);
        hub.Agent.Called(submission, "read_page", """{"path":"ada.md"}""");
        hub.Agent.Returned(submission, "read_page", ALongResult);

        await Page.GotoAsync($"{hub.Address}/run.html?submission={submission}");
        await Expect(Segments()).ToHaveCountAsync(2);

        // The user opens the result and reads it. The run is still under way.
        var result = Segments().Nth(1).Locator("details.result");
        await result.Locator("summary").ClickAsync();
        await Expect(result.Locator("pre")).ToBeVisibleAsync();

        var openedBefore = await Segments().Nth(1).BoundingBoxAsync();

        // More happens while the page is left open. The page polls; nothing is pushed to it.
        hub.Agent.Said(submission, "Ada Lovelace already has a page. I will add the date.");
        hub.Agent.Called(submission, "write_page", """{"path":"ada.md"}""");

        await Expect(Segments()).ToHaveCountAsync(4);

        // Below what was already there, in the order it happened.
        await Expect(Heading(2)).ToContainTextAsync("the agent");
        await Expect(Heading(3)).ToContainTextAsync("called write_page");

        // And what the user was reading is untouched: still open, and still where it was. An element
        // already on the page is never replaced, which is what makes that true (ACCESS-006).
        await Expect(result.Locator("pre")).ToBeVisibleAsync();

        var openedAfter = await Segments().Nth(1).BoundingBoxAsync();
        Assert.Equal(openedBefore!.Y, openedAfter!.Y);
        Assert.Equal(openedBefore.Height, openedAfter.Height);

        // The run then ends while the page is still open, and the tail arrives the same way.
        hub.Agent.End(submission, RunOutcome.Done);

        await Expect(Segments()).ToHaveCountAsync(5);
        await Expect(Heading(4)).ToContainTextAsync("ended done");
        await Expect(result.Locator("pre")).ToBeVisibleAsync();
    }

    private ILocator Row(Guid submission) => Page.Locator($"#submissions li[data-id='{submission}']");

    private ILocator Segments() => Page.Locator("#record li");

    private ILocator Heading(int at) => Segments().Nth(at).Locator(".heading");
}
