using System.Text.RegularExpressions;
using Grimoire.Agent;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Grimoire.E2E.Tests;

/// <summary>
/// What the browser puts on the screen for every submission: exactly one of the four states, and —
/// where it has a run — that run's model, what it has spent against the cost ceiling and the tool
/// calls it has made
/// (ACCESS-005).
/// </summary>
/// <remarks>
/// Each test here names its browser-only reason (Constitution III.4). The
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
public sealed class SubmissionStatesTests : PageTest
{
    [Fact]
    [Trait("req", "ACCESS-005")]
    public async Task List_ShowsEachSubmissionInItsState()
    {
        // Why a browser (III.4): live push — the open page is sent the state change.
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

        // The agent reports in while the page is open, and the page is sent the list again — it asks
        // for nothing (contracts/hub-http-api.md).
        hub.Agent.ReportIn(underWay);

        await Expect(State(underWay)).ToHaveTextAsync("running");
    }

    [Fact]
    [Trait("req", "ACCESS-005")]
    [Trait("req", "ACCESS-004")]
    public async Task List_ShowsTheModelAndBothFigures_ForARunThatHasEnded()
    {
        // Why a browser (III.4): geometry — the opening of the text is rendered wide enough to read
        // beside the figures.
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);
        hub.Agent.ReportIn(submission);
        hub.Agent.Spend(submission, 148_233);
        hub.Agent.Called(submission, "read_page", """{"path":"ada.md"}""");
        hub.Agent.Called(submission, "write_page", """{"path":"ada.md"}""");
        hub.Agent.End(submission, RunOutcome.Done);

        await Page.GotoAsync(hub.Address);

        // Beside the state: the model the run ran on and the two figures it ended with. The cost is
        // written against the ceiling it was held to and carries no unit — the quantity has none
        // (GUARD-004). This is the half of ACCESS-005 no in-process test reaches: what the browser
        // actually renders.
        await Expect(State(submission)).ToHaveTextAsync("done");
        await Expect(Row(submission).Locator(".model")).ToHaveTextAsync("claude-opus-4-5-20251101");
        await Expect(Row(submission).Locator(".cost")).ToHaveTextAsync("148\u2009233 / 2\u2009000\u2009000");
        await Expect(Row(submission).Locator(".calls")).ToHaveTextAsync("2");

        // And no identifier is rendered — neither the submission's nor the run's.
        await Expect(Row(submission)).Not.ToContainTextAsync(submission.ToString());


        // The opening of the text is readable, not squeezed to a character. Asserted as a rendered
        // width, because the DOM text is there either way: a row that carries the model and two
        // figures beside the time has more than one line's worth, and the opening was the only item
        // able to give way. Showing it is what ACCESS-004 asks for, and a row showing "F…" tells one
        // submission from another not at all.
        var excerpt = await Row(submission).Locator(".excerpt").BoundingBoxAsync();
        Assert.True(
            excerpt!.Width >= 140,
            $"the opening of the text was rendered {excerpt.Width}px wide, too narrow to read");

        // The link to the run says which run it opens. "Open" is enough beside the row it sits in and
        // nothing on its own, and a list of runs would otherwise read as "Open, Open, Open" to
        // anything that announces links out of context. Named by the opening of the submitted text,
        // which is what tells one submission from another (ACCESS-004, docs/ux.md).
        await Expect(Row(submission).Locator(".open-run"))
            .ToHaveAttributeAsync("aria-label", new Regex("Ada Lovelace wrote the first program"));
    }

    [Fact]
    [Trait("req", "ACCESS-005")]
    public async Task List_ShowsNoRunFigures_ForASubmissionWaitingItsTurn()
    {
        // Why a browser (III.4): JavaScript logic no other runner reaches — app.js draws no
        // figures, not zeros, for a row with no run.
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

    [Fact]
    [Trait("req", "ACCESS-005")]
    public async Task Figures_RiseWhileTheRunIsUnderWay_WithoutMovingTheRows()
    {
        // Why a browser (III.4): geometry — a figure gaining a digit moves neither its row nor the
        // row above.
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        // A row above it and a row below it, so that anything moving has somewhere to move to.
        var above = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);
        hub.Agent.ReportIn(above);
        hub.Agent.End(above, RunOutcome.Done);

        var watched = await hub.SubmitAsync("Grace Hopper found the first bug in a relay.", token);
        hub.Agent.ReportIn(watched);
        hub.Agent.Spend(watched, 1_000);
        hub.Agent.Called(watched, "read_page", """{"path":"ada.md"}""");

        await Page.GotoAsync(hub.Address);
        await Expect(Row(watched).Locator(".cost")).ToHaveTextAsync("1\u2009000 / 2\u2009000\u2009000");

        var before = await Row(watched).BoundingBoxAsync();
        var otherBefore = await Row(above).BoundingBoxAsync();

        // Ten times the figure, which is one digit wider — the change that would widen a proportional
        // column and reflow the row around it. The ceiling beside it does not move, which is what
        // makes the growing half the only thing that could (ACCESS-005, research.md R-09).
        hub.Agent.Spend(watched, 10_000);
        hub.Agent.Called(watched, "write_page", """{"path":"ada.md"}""");

        await Expect(Row(watched).Locator(".cost")).ToHaveTextAsync("10\u2009000 / 2\u2009000\u2009000");
        await Expect(Row(watched).Locator(".calls")).ToHaveTextAsync("2");

        // The figures followed the run, and nothing moved: not the row they are in, and not the row
        // above it. This is the half of ACCESS-005 no in-process test reaches — geometry.
        var after = await Row(watched).BoundingBoxAsync();
        var otherAfter = await Row(above).BoundingBoxAsync();

        Assert.Equal(before!.X, after!.X);
        Assert.Equal(before.Y, after.Y);
        Assert.Equal(before.Width, after.Width);
        Assert.Equal(before.Height, after.Height);
        Assert.Equal(otherBefore!.Y, otherAfter!.Y);

        // And the state is still what it was: a figure rising is not a state changing.
        await Expect(State(watched)).ToHaveTextAsync("running");
    }

    [Fact]
    [Trait("req", "ACCESS-003")]
    [Trait("req", "ACCESS-005")]
    [Trait("req", "RUNS-003")]
    public async Task Row_IsNotRebuiltUnderTheUser_WhileTheListIsSentWhatHappens()
    {
        // Why a browser (III.4): focus and DOM identity — the focused control survives the list
        // being sent again.
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);
        hub.Agent.ReportIn(submission);
        hub.Agent.End(submission, RunOutcome.Failed);

        await Page.GotoAsync(hub.Address);

        var acknowledge = Row(submission).Locator(".acknowledge");
        await Expect(acknowledge).ToBeVisibleAsync();
        await acknowledge.FocusAsync();

        // The list is sent something while the control is under the user's finger. It used to call
        // replaceChildren on every poll, which took the control away and dropped the focus with it
        // (research.md R-09); a row is written to rather than rebuilt, so what the user is reaching for
        // stays where it is however often an event arrives.
        //
        // A change is made rather than a delay waited out: with the poll gone there is no interval to
        // wait two of, and waiting for a second that nothing happens in would prove nothing at all.
        var second = await hub.SubmitAsync("Grace Hopper found the first bug in a relay.", token);

        await Expect(Row(second)).ToBeVisibleAsync();
        await Expect(acknowledge).ToBeFocusedAsync();

        // And it still does what it is for.
        await acknowledge.ClickAsync();
        await Expect(Row(submission).Locator(".acknowledge")).ToHaveCountAsync(0);
        await Expect(State(submission)).ToHaveTextAsync("failed");
    }

    [Fact]
    [Trait("req", "ACCESS-004")]
    public async Task List_PutsANewSubmissionFirst_WhileThePageIsOpen()
    {
        // Why a browser (III.4): live push and JavaScript logic no other runner reaches — app.js
        // moves a row it appended into the order the list was sent in.
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var first = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);
        hub.Agent.ReportIn(first);

        await Page.GotoAsync(hub.Address);
        await Expect(Row(first)).ToBeVisibleAsync();

        // Submitted from elsewhere while this page is open — a second browser, or the same user in
        // another tab. The page is sent the list; it asks for nothing.
        var second = await hub.SubmitAsync("Grace Hopper found the first bug in a relay.", token);

        await Expect(Row(second)).ToBeVisibleAsync();

        // Newest first, which is the order the server sends (contracts/hub-http-api.md). Rows are
        // written to rather than rebuilt now, so a new one is appended to the list as an element and
        // has to be moved into place — without that it would arrive at the bottom, under every
        // submission the user made before it (ACCESS-004).
        var order = await Page.Locator("#submissions li").EvaluateAllAsync<string[]>(
            "rows => rows.map(r => r.dataset.id)");

        Assert.Equal([second.ToString(), first.ToString()], order);
    }

    private ILocator Row(Guid submission) => Page.Locator($"#submissions li[data-id='{submission}']");

    private ILocator State(Guid submission) => Row(submission).Locator(".state");
}
