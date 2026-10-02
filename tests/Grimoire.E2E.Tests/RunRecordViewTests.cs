using Grimoire.Agent;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Grimoire.E2E.Tests;

/// <summary>
/// The owner opens a run from the list and reads its record: the frame, then what the run did in the
/// order it happened, with each result folded until they want it (ACCESS-006).
/// </summary>
/// <remarks>
/// <para>
/// The folding and the segmentation are the browser's, and only a real browser exercises them: the
/// endpoint's answer is proven a level down, in the Fast suite, and what <c>run.js</c> makes of it is
/// only observable here (DEC-019's precedent for ACCESS-001). How one result is folded and segmented
/// is <c>CallResultTests</c>, on one setup.
/// </para>
/// <para>
/// That the record served is the file on disk, byte for byte, and that the file lies under Grimoire's
/// state and never in the wiki, needs no browser: <c>RunRecordEndpointTests.Record_IsAnsweredWithItsBytesUnaltered</c>
/// (Fast) and <c>MarkdownRunRecordTests.Record_IsAMarkdownFileNamedAfterTheRun</c> (Contract) prove it
/// (Constitution III.4, III.6).
/// </para>
/// </remarks>
[Trait("level", "e2e")]
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
    [Trait("req", "RUNS-008")]
    [Trait("req", "RUNS-009")]
    [Trait("req", "ACCESS-006")]
    public async Task Run_IsOpenedFromItsRowAndReadInOrder()
    {
        // Why a browser (III.4): JavaScript logic no other runner reaches — the row's link is
        // app.js's, and the segments, the folded calls and the tail's table are run.js's.
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);
        hub.Agent.ReportIn(submission);

        // A run as the agent works it: it says what it is about to do, makes the calls that do it, and
        // says what it found.
        hub.Agent.Said(submission, "I will read what the wiki already holds.");
        hub.Agent.Called(submission, "read_page", """{"path":"ada.md"}""");
        hub.Agent.Returned(submission, "read_page", ALongResult);
        hub.Agent.Called(submission, "list_pages", "{}");
        hub.Agent.Returned(submission, "list_pages", "ada.md");
        hub.Agent.Said(submission, "Ada Lovelace already has a page. I will add the date.");
        hub.Agent.End(submission, RunOutcome.Done);

        await Page.GotoAsync(hub.Address);
        await Row(submission).Locator(".open-run").ClickAsync();

        // The frame first — the model it ran on and both ceilings (RUNS-008).
        await Expect(Page.Locator("#frame")).ToContainTextAsync("claude-opus-4-5-20251101");
        await Expect(Page.Locator("#frame")).ToContainTextAsync("Granted tools");


        // Three sections: what the agent said, what it said afterwards, and the tail. The two calls
        // are not sections of their own — they sit inside the sentence that explains them.
        await Expect(Segments()).ToHaveCountAsync(3);
        await Expect(Heading(0)).ToContainTextAsync("the agent");
        await Expect(Segments().Nth(0)).ToContainTextAsync("I will read what the wiki already holds.");
        await Expect(Heading(2)).ToContainTextAsync("ended done");

        // The calls of that turn, folded away behind their count.
        var calls = Segments().Nth(0).Locator("details.calls");
        await Expect(calls.Locator("summary").First).ToHaveTextAsync("2 tool calls");
        await Expect(calls.Locator(".call")).ToHaveCountAsync(2);

        // Each one is a line of its own, with its arguments on it, and can be opened on its own.
        await Expect(calls.Locator(".call").Nth(0)).ToContainTextAsync("called read_page");
        await Expect(calls.Locator(".call").Nth(0)).ToContainTextAsync("ada.md");
        await Expect(calls.Locator(".call").Nth(1)).ToContainTextAsync("called list_pages");
        await Expect(calls.Locator(".call").Nth(0).Locator("details.result")).ToHaveCountAsync(1);

        // And the tail is the record's second two-column table, read as a table rather than as pipes.
        await Expect(Segments().Nth(2).Locator("table.frame-table")).ToHaveCountAsync(1);
        await Expect(Segments().Nth(2)).ToContainTextAsync("Elapsed");
    }

    [Fact]
    [Trait("req", "ACCESS-006")]
    public async Task AgentText_IsShownWhole_WhenItHoldsAFencedBlock()
    {
        // Why a browser (III.4): JavaScript logic no other runner reaches — run.js tells prose from
        // a result.
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
    [Trait("req", "ACCESS-006")]
    public async Task Moments_ArriveBelowWhatIsThere_WithoutDisturbingIt()
    {
        // Why a browser (III.4): scroll, geometry and live push — moments arrive while the user
        // reads, and neither the scroll nor the opened result moves.
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);
        hub.Agent.ReportIn(submission);
        hub.Agent.Called(submission, "read_page", """{"path":"ada.md"}""");
        hub.Agent.Returned(submission, "read_page", ALongResult);

        await Page.GotoAsync($"{hub.Address}/run.html?submission={submission}");
        await Expect(Segments()).ToHaveCountAsync(1);

        // The user opens the result and reads it, folded under the call's one line. The run is still
        // under way.
        var result = Segments().Nth(0).Locator("details.result");
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

        // More happens while the page is left open, and the page is sent the bytes appended since — it
        // asks for nothing (contracts/hub-http-api.md).
        hub.Agent.Said(submission, "Ada Lovelace already has a page. I will add the date.");
        hub.Agent.Called(submission, "write_page", """{"path":"ada.md"}""");

        // Fourteen, not fifteen: the call sits inside the sentence that explains it rather than
        // opening a section of its own.
        await Expect(Segments()).ToHaveCountAsync(14);

        // Below what was already there, in the order it happened.
        await Expect(Heading(13)).ToContainTextAsync("the agent");
        await Expect(Segments().Nth(13).Locator(".call")).ToContainTextAsync("called write_page");

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

        await Expect(Segments()).ToHaveCountAsync(15);
        await Expect(Heading(14)).ToContainTextAsync("ended done");
        await Expect(result.Locator("pre")).ToBeVisibleAsync();
        Assert.Equal(scrolledTo, await Page.EvaluateAsync<double>("window.scrollY"));
    }

    [Fact]
    [Trait("req", "RUNS-007")]
    [Trait("req", "ACCESS-006")]
    public async Task View_SaysLinesAreMissing_WhenTheRecordCouldNotHoldThem()
    {
        // Why a browser (III.4): live push — the gap is said on the open page as it happens.
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

    [Fact]
    [Trait("req", "ACCESS-006")]
    public async Task Answer_ArrivesWhileThePageIsOpen_InTheCallItAnswers()
    {
        // Why a browser (III.4): live push and JavaScript logic no other runner reaches — an answer
        // arrives inside a segment already drawn, and run.js nests it in its call.
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);
        hub.Agent.ReportIn(submission);
        hub.Agent.Said(submission, "I will read what the wiki already holds.");
        hub.Agent.Called(submission, "read_page", """{"path":"ada.md"}""");

        await Page.GotoAsync($"{hub.Address}/run.html?submission={submission}");

        var calls = Segments().Nth(0).Locator("details.calls");
        await Expect(calls.Locator("summary").First).ToHaveTextAsync("1 tool call");
        await Expect(calls.Locator(".call details.result")).ToHaveCountAsync(0);

        // The answer arrives while the page is open. It is written inside the call it answers, so it
        // adds no segment at the top level — and a page that only watched the top level would show it
        // just after a reload, which is not what ACCESS-006 promises.
        hub.Agent.Returned(submission, "read_page", ALongResult);

        await Expect(calls.Locator(".call details.result")).ToHaveCountAsync(1);
        await Expect(calls.Locator(".call details.result > summary")).ToContainTextAsync("7 lines");

        // And a second call of the same turn arrives the same way, into the fold that is already there.
        hub.Agent.Called(submission, "list_pages", "{}");

        await Expect(calls.Locator("summary").First).ToHaveTextAsync("2 tool calls");
        await Expect(calls.Locator(".call")).ToHaveCountAsync(2);
    }

    [Fact]
    [Trait("req", "ACCESS-006")]
    public async Task Turn_CountsItsCalls_WhenTheirAnswersAreWrittenBesideThem()
    {
        // Why a browser (III.4): JavaScript logic no other runner reaches — run.js attributes
        // answers written beside their calls.
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);
        hub.Agent.ReportIn(submission);
        hub.Agent.Said(submission, "I will read both pages.");

        // Two calls before either answer, which is how a turn that batches them arrives. Neither
        // answer can be nested — the call above the first is the second — so the record writes both
        // beside the calls, at the calls' own depth.
        hub.Agent.Called(submission, "read_page", """{"path":"ada.md"}""");
        hub.Agent.Called(submission, "read_page", """{"path":"grace.md"}""");
        hub.Agent.Returned(submission, "read_page", "# Ada");
        hub.Agent.Returned(submission, "read_page", "# Grace");
        hub.Agent.End(submission, RunOutcome.Done);

        await Page.GotoAsync($"{hub.Address}/run.html?submission={submission}");

        var calls = Segments().Nth(0).Locator("details.calls");

        // Two calls, not four sections. An answer written beside a call is not one more thing the
        // agent did.
        await Expect(calls.Locator("summary").First).ToHaveTextAsync("2 tool calls");
        await Expect(calls.Locator(".call")).ToHaveCountAsync(2);

        // And each answer sits in the call it answers, oldest first — which is the order the record
        // attributes them by.
        await Expect(calls.Locator(".call").Nth(0).Locator("details.result")).ToHaveCountAsync(1);
        await Expect(calls.Locator(".call").Nth(1).Locator("details.result")).ToHaveCountAsync(1);

        // Opened: the turn's fold first, then the answer inside the call it belongs to.
        await calls.Locator("summary").First.ClickAsync();
        await calls.Locator(".call").Nth(0).Locator("details.result > summary").ClickAsync();

        await Expect(calls.Locator(".call").Nth(0).Locator("pre")).ToHaveTextAsync("# Ada");
    }

    [Fact]
    [Trait("req", "ACCESS-006")]
    public async Task Record_OpensWhenTheRunBegins_WhereThePageWasOpenedBeforeIt()
    {
        // Why a browser (III.4): live push and JavaScript logic no other runner reaches — run.js
        // opens the record's stream when the list's stream says the run began, and asks nothing on
        // a timer.
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        // A run under way, and a submission waiting behind it: it has no run, so it has no record yet.
        var first = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);
        hub.Agent.ReportIn(first);

        var waiting = await hub.SubmitAsync("Grace Hopper found the first bug in a relay.", token);

        // Every request the page makes for the record's stream, counted where it leaves the browser.
        var asked = 0;
        await Page.RouteAsync("**/record/events", async route =>
        {
            Interlocked.Increment(ref asked);
            await route.ContinueAsync();
        });

        await Page.GotoAsync($"{hub.Address}/run.html?submission={waiting}");
        await Expect(Page.Locator("#message")).Not.ToBeEmptyAsync();

        // **Nothing polls** (DEC-035). Asked once, refused, and not asked again while nothing changed —
        // a retry on a timer would have asked three times by now.
        await Page.WaitForTimeoutAsync(3_000);
        Assert.Equal(1, Volatile.Read(ref asked));

        // The queue reaches it. The page learns that from the list's stream and opens the record's.
        hub.Agent.End(first, RunOutcome.Done);

        // Its head is there before its agent has said anything.
        await Expect(Page.Locator("#frame")).ToContainTextAsync("Granted tools");

        hub.Agent.ReportIn(waiting);
        hub.Agent.Said(waiting, "I will read what the wiki already holds.");

        await Expect(Heading(0)).ToContainTextAsync("the agent");
        await Expect(Page.Locator("#message")).ToBeEmptyAsync();

        Assert.Equal(2, Volatile.Read(ref asked));
    }

    private ILocator Row(Guid submission) => Page.Locator($"#submissions li[data-id='{submission}']");

    private ILocator Segments() => Page.Locator("#record li");

    /// <summary>
    /// A segment's own first line. Not the headings of the calls inside it, which are their own.
    /// </summary>
    private ILocator Heading(int at) => Segments().Nth(at).Locator("> .heading");
}
