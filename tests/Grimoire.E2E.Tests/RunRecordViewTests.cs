using System.Text;
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
        // A call with no arguments at all, which is what a listing tool makes. Its block is the
        // shortest a record can hold, and a reader that mistakes its closing fence for an opening one
        // swallows the segment behind it.
        hub.Agent.Called(submission, "list_pages", "{}");
        hub.Agent.End(submission, RunOutcome.Done);

        await Page.GotoAsync(hub.Address);

        // Opened from the row, which is the only way in: the link carries the submission, not the run.
        await Row(submission).Locator(".open-run").ClickAsync();

        // The frame first — the model it ran on and both ceilings (RUNS-008).
        await Expect(Page.Locator("#frame")).ToContainTextAsync("claude-opus-4-5-20251101");
        await Expect(Page.Locator("#frame")).ToContainTextAsync("Granted tools");

        // Then what the run did, in the order it happened, and the tail last. A call and what it
        // returned are one entry — one question and its answer — so four entries carry five moments.
        await Expect(Segments()).ToHaveCountAsync(4);
        await Expect(Heading(0)).ToContainTextAsync("called read_page");
        await Expect(Heading(1)).ToContainTextAsync("the agent");
        await Expect(Heading(2)).ToContainTextAsync("called list_pages");
        await Expect(Heading(3)).ToContainTextAsync("ended done");


        // The call carries both halves, each folded and each named.
        await Expect(Segments().Nth(0).Locator("details.result")).ToHaveCountAsync(2);
        await Expect(Segments().Nth(0).Locator("summary").Nth(0)).ToContainTextAsync("arguments");
        await Expect(Segments().Nth(0).Locator("summary").Nth(1)).ToContainTextAsync("returned");

        // The agent's own text is prose and is simply there.
        await Expect(Segments().Nth(1)).ToContainTextAsync("I will add the date.");

        // And the tail is the record's second two-column table, read as a table rather than as pipes.
        await Expect(Segments().Nth(3).Locator("table.frame-table")).ToHaveCountAsync(1);
        await Expect(Segments().Nth(3)).ToContainTextAsync("Elapsed");
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

        // The result sits in the entry of the call it answers, as its second folded block.
        var result = Segments().Nth(0).Locator("details.result").Nth(1);

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
        // to its close, so that line is no boundary and the result stays one moment
        // (contracts/run-record.md, rule 4). Two entries: the call with its answer, and the tail.
        await Expect(Segments()).ToHaveCountAsync(2);
        await Expect(Heading(0)).ToContainTextAsync("called read_page");
        await Expect(Segments().Nth(0).Locator("details.result")).ToHaveCountAsync(2);
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
        await Expect(Segments()).ToHaveCountAsync(1);

        // The user opens the result and reads it — the second block of the call's own entry. The run
        // is still under way.
        var result = Segments().Nth(0).Locator("details.result").Nth(1);
        await result.Locator("summary").ClickAsync();
        await Expect(result.Locator("pre")).ToBeVisibleAsync();

        // Enough of the run to push the page well past one screen, so that there is a scroll position
        // to keep at all. Without this the document never scrolls and the assertion below would hold
        // whatever the implementation did.
        for (var i = 0; i < 12; i++)
        {
            hub.Agent.Said(submission, $"Reading page {i}. {new string('x', 2_000)}");
        }

        await Expect(Segments()).ToHaveCountAsync(13);

        // The user scrolls to where they were reading and stays there. Scrolled through the document
        // rather than with the wheel: a wheel event is delivered and applied asynchronously, and on a
        // CI runner it had not landed by the time the position was read — which the guard below caught
        // rather than letting the test pass on an unscrolled page.
        var scrolledTo = await Page.EvaluateAsync<double>(
            "() => { window.scrollTo(0, Math.floor(document.body.scrollHeight / 2)); return window.scrollY; }");

        Assert.True(scrolledTo > 0, "the page did not scroll, so there is no scroll position to keep");

        var openedBefore = await Segments().Nth(0).BoundingBoxAsync();

        // More happens while the page is left open. The page polls; nothing is pushed to it.
        hub.Agent.Said(submission, "Ada Lovelace already has a page. I will add the date.");
        hub.Agent.Called(submission, "write_page", """{"path":"ada.md"}""");

        await Expect(Segments()).ToHaveCountAsync(15);

        // Below what was already there, in the order it happened.
        await Expect(Heading(13)).ToContainTextAsync("the agent");
        await Expect(Heading(14)).ToContainTextAsync("called write_page");

        // The scroll is where the user left it. This is the assertion T040 is actually about, and a
        // bounding box cannot make it: that is a layout coordinate, and it would hold even if the
        // document had jumped under the reader.
        Assert.Equal(scrolledTo, await Page.EvaluateAsync<double>("window.scrollY"));

        // And what the user was reading is untouched: still open, and still where it was. An element
        // already on the page is never replaced, which is what makes both true (ACCESS-006).
        await Expect(result.Locator("pre")).ToBeVisibleAsync();

        var openedAfter = await Segments().Nth(0).BoundingBoxAsync();
        Assert.Equal(openedBefore!.Y, openedAfter!.Y);
        Assert.Equal(openedBefore.Height, openedAfter.Height);

        // The run then ends while the page is still open, and the tail arrives the same way.
        hub.Agent.End(submission, RunOutcome.Done);

        await Expect(Segments()).ToHaveCountAsync(16);
        await Expect(Heading(15)).ToContainTextAsync("ended done");
        await Expect(result.Locator("pre")).ToBeVisibleAsync();
        Assert.Equal(scrolledTo, await Page.EvaluateAsync<double>("window.scrollY"));
    }

    [Fact]
    [Trait("req", "RUNS-007")]
    public async Task Record_ServedIsTheFileOnDisk_AndTheWikiHoldsNoneOfIt()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);
        hub.Agent.ReportIn(submission);
        hub.Agent.Called(submission, "read_page", """{"path":"ada.md"}""");
        hub.Agent.Returned(submission, "read_page", ALongResult);
        hub.Agent.Said(submission, "Ada Lovelace already has a page. I will add the date.");
        hub.Agent.End(submission, RunOutcome.Done);

        // What is on disk. One file, under the state directory Grimoire owns, named for the run.
        var records = Directory.GetFiles(Path.Combine(hub.StateDirectory, "runs"), "*.md");
        var onDisk = await File.ReadAllBytesAsync(Assert.Single(records), token);

        // Byte for byte, which is what ACCESS-006 promises and what makes the browser a window onto
        // the record rather than a second place the run lives. Compared as bytes and not as text: a
        // line-by-line comparison would pass on a response that had been reordered, had a line
        // repeated, or had its blank lines dropped — and the blank lines are what separate one segment
        // from the next (US3, contracts/run-record.md).
        Assert.Equal(onDisk, await hub.RecordBytesAsync(submission, token));

        // The record really does hold the run, rather than both being empty and equal.
        var text = Encoding.UTF8.GetString(onDisk);
        Assert.Contains("Ada Lovelace already has a page.", text, StringComparison.Ordinal);
        Assert.Contains("ended done", text, StringComparison.Ordinal);

        // And the wiki holds none of it. Grimoire's bookkeeping in the user's repository would turn up
        // in the version history that is their only undo (Invariants 1 and 3, DEC-023).
        //
        // Every file under the wiki, whatever it is called: a record written there under another name
        // or another extension is the same mistake, and an assertion that only looked at `.md` files
        // would pass on it.
        var inTheWiki = Directory.GetFiles(hub.WikiDirectory, "*", SearchOption.AllDirectories);

        foreach (var file in inTheWiki)
        {
            var held = await File.ReadAllTextAsync(file, token);

            Assert.DoesNotContain($"# Run {Path.GetFileNameWithoutExtension(records[0])}", held, StringComparison.Ordinal);
            Assert.DoesNotContain("ended done", held, StringComparison.Ordinal);
            Assert.NotEqual(Path.GetFileName(records[0]), Path.GetFileName(file));
        }
    }

    [Fact]
    [Trait("req", "RUNS-007")]
    public async Task View_SaysLinesAreMissing_WhenTheRecordCouldNotHoldThem()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);
        hub.Agent.ReportIn(submission);
        hub.Agent.Called(submission, "read_page", """{"path":"ada.md"}""");

        await Page.GotoAsync($"{hub.Address}/run.html?submission={submission}");
        await Expect(Segments()).ToHaveCountAsync(1);

        // The disk refuses the rest. The run goes on — that is what RUNS-007 decided — and the record
        // keeps what it already has.
        hub.StopTheRecordBeingWritten();

        hub.Agent.Returned(submission, "read_page", ALongResult);
        hub.Agent.Said(submission, "Ada Lovelace already has a page.");

        // The view says so, while the user is looking at it. A gap that passed for an agent doing
        // nothing would be worse than the gap (ACCESS-006, RUNS-007).
        await Expect(Page.Locator("#missing")).ToContainTextAsync("2");
        await Expect(Page.Locator("#missing")).Not.ToBeEmptyAsync();

        // And what did get written is still there to read.
        await Expect(Heading(0)).ToContainTextAsync("called read_page");
    }

    private ILocator Row(Guid submission) => Page.Locator($"#submissions li[data-id='{submission}']");

    private ILocator Segments() => Page.Locator("#record li");

    private ILocator Heading(int at) => Segments().Nth(at).Locator(".heading");
}
