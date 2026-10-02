using Grimoire.Agent;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Grimoire.E2E.Tests;

/// <summary>
/// There is <b>exactly one</b> chat and every browser reads that same one, a new chat empties all of
/// them, and a question waiting its turn or one that got no answer says so where the user is looking
/// (QUERY-005, QUERY-006, ACCESS-007, RUNS-002).
/// </summary>
/// <remarks>
/// <para>
/// "A question asked in one tab appears in the other" is geometry only <em>two</em> real browsers
/// have, and so is a new chat emptying both. Nothing below a real browser can hold two readers of one
/// thing at once (research.md R-11, DEC-020).
/// </para>
/// <para>
/// The second page is a second context rather than a second tab of the first, which is the stronger
/// claim: nothing is shared between them but the hub — no storage, no session, nothing that could
/// carry a chat from one to the other except the stream they both read.
/// </para>
/// </remarks>
[Trait("level", "e2e")]
public sealed class ChatLifetimeTests : PageTest
{
    private const string AboutAda = "What does the wiki say about Ada Lovelace?";

    private static ILocator Turn(IPage page, Guid question) =>
        page.Locator($"#chat li[data-id=\"{question}\"]");

    /// <summary>A second browser on the same hub, sharing nothing with the first but the hub.</summary>
    private async Task<IPage> SecondBrowserAsync(HubUnderTest hub)
    {
        var context = await Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{hub.Address}/chat.html");

        return page;
    }

    [Fact]
    [Trait("req", "QUERY-005")]
    public async Task Question_AppearsInEveryBrowserReadingTheChat()
    {
        // Why a browser (III.4): two readers — two browsers sharing nothing but the hub.
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        await Page.GotoAsync($"{hub.Address}/chat.html");
        var second = await SecondBrowserAsync(hub);

        // Asked in the first tab, through the form, the way a person asks.
        await Page.Locator("#text").FillAsync(AboutAda);
        await Page.Locator("#ask").ClickAsync();

        await Expect(Page.Locator("#chat li")).ToHaveCountAsync(1);

        var question = Guid.Parse((await Page.Locator("#chat li").First.GetAttributeAsync("data-id"))!);

        // **And it is in the other one.** There is one chat, and every browser reads that same one —
        // nothing tells one reader from another (QUERY-005).
        await Expect(Turn(second, question).Locator(".asked")).ToHaveTextAsync(AboutAda);

        // The answer forms in both, because both are reading the same chat as it happens.
        hub.Agent.Said(question, "She wrote the first program.");

        await Expect(Turn(Page, question).Locator(".answer")).ToHaveTextAsync("She wrote the first program.");
        await Expect(Turn(second, question).Locator(".answer")).ToHaveTextAsync("She wrote the first program.");
    }

    [Fact]
    [Trait("req", "ACCESS-010")]
    [Trait("req", "QUERY-005")]
    public async Task NewChat_EmptiesEveryBrowserReadingIt()
    {
        // Why a browser (III.4): two readers — a new chat started in one browser empties both.
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var question = await hub.AskAsync(AboutAda, token);

        await Page.GotoAsync($"{hub.Address}/chat.html");
        var second = await SecondBrowserAsync(hub);

        await Expect(Turn(Page, question)).ToBeVisibleAsync();
        await Expect(Turn(second, question)).ToBeVisibleAsync();

        hub.Agent.Said(question, "She wrote the first program.");
        hub.Agent.End(question, RunOutcome.Done);

        // Started from the first tab — and **both** are emptied, because there is one chat and they
        // both read it. No modal asks first: `docs/ux.md` rules them out, and a chat is kept nowhere.
        await Page.Locator("#new-chat").ClickAsync();

        await Expect(Page.Locator("#chat li")).ToHaveCountAsync(0);
        await Expect(second.Locator("#chat li")).ToHaveCountAsync(0);

        // And nothing of the old one is reachable: the total goes with it.
        await Expect(Page.Locator("#total")).ToHaveTextAsync("0");
        await Expect(second.Locator("#total")).ToHaveTextAsync("0");
    }

    [Fact]
    [Trait("req", "ACCESS-007")]
    [Trait("req", "RUNS-002")]
    public async Task Question_SaysItIsWaitingItsTurn_WhileASubmissionsRunIsUnderWay()
    {
        // Why a browser (III.4): live push — the state changes on the open page when the run ahead
        // of it ends.
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        // A submission takes the one run slot (RUNS-002).
        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);
        hub.Agent.ReportIn(submission);

        var question = await hub.AskAsync(AboutAda, token);

        await Page.GotoAsync($"{hub.Address}/chat.html");

        // It was accepted, not refused — there is no refusal for a run being in progress — and it says
        // where it stands (ACCESS-007, QUERY-001).
        await Expect(Turn(Page, question).Locator(".state")).ToHaveTextAsync("waiting its turn");
        await Expect(Turn(Page, question).Locator(".cost")).ToHaveTextAsync(string.Empty);

        // The submission's run ends, and the question is answered after it — never beside it.
        hub.Agent.End(submission, RunOutcome.Done);

        await Expect(Turn(Page, question).Locator(".state")).ToHaveTextAsync("being answered");
    }

    [Fact]
    [Trait("req", "QUERY-006")]
    [Trait("req", "ACCESS-003")]
    [Trait("req", "RUNS-003")]
    [Trait("req", "ACCESS-007")]
    public async Task Question_OffersTheOneControl_AfterItsRunGotNoAnswer()
    {
        // Why a browser (III.4): JavaScript logic no other runner reaches — chat.js hides what a
        // failed run wrote and offers the control on that question only; live push carries the
        // queue moving on.
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var question = await hub.AskAsync(AboutAda, token);

        await Page.GotoAsync($"{hub.Address}/chat.html");
        await Expect(Turn(Page, question)).ToBeVisibleAsync();

        // The run says half a sentence and then dies.
        hub.Agent.Said(question, "She wrote the fir");
        hub.Agent.End(question, RunOutcome.Failed, RunEndedBecause.AgentProcessDied);

        // It says it got no answer **and why** (QUERY-006).
        await Expect(Turn(Page, question).Locator(".state")).ToHaveTextAsync("got no answer");
        await Expect(Turn(Page, question).Locator(".because")).ToContainTextAsync("agent stopped working");

        // **And the half sentence is not shown as its answer.** An answer that looks like an answer and
        // is not is the worst kind of wrong; what the run did is still under it, in the steps.
        await Expect(Turn(Page, question).Locator(".answer")).Not.ToBeVisibleAsync();

        // A question waiting behind the failure does not start: a failure holds the queue until the
        // user says they have seen it (RUNS-003).
        var behind = await hub.AskAsync("And who was her mother?", token);

        await Expect(Turn(Page, behind).Locator(".state")).ToHaveTextAsync("waiting its turn");

        // The one control, on the question whose failure it is.
        var acknowledge = Turn(Page, question).Locator(".acknowledge");
        await Expect(acknowledge).ToBeVisibleAsync();
        await acknowledge.ClickAsync();

        // Afterwards the queue moves, and the acknowledged question still reads that it got no answer —
        // acknowledging is not undoing.
        await Expect(Turn(Page, behind).Locator(".state")).ToHaveTextAsync("being answered");
        await Expect(Turn(Page, question).Locator(".state")).ToHaveTextAsync("got no answer");
        await Expect(Turn(Page, question).Locator(".acknowledge")).ToHaveCountAsync(0);
    }
}
