using Grimoire.Agent;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Grimoire.E2E.Tests;

/// <summary>
/// The owner asks the wiki and reads the answer as it forms: the text arrives while the run is under
/// way, what it has spent stands beside it against its ceiling, and nothing arriving moves what they
/// are already reading (ACCESS-007, ACCESS-008).
/// </summary>
/// <remarks>
/// <para>
/// Only a real browser can settle any of this. "Content arriving must not move what the user is
/// already reading" is <b>geometry</b>, and a layout is something only a browser has (DEC-020); that a
/// figure rises without moving what is beside it is the same claim about a number. What the stream
/// carries is proven a level down, in the Fast suite's <c>ChatChangeTests</c> and
/// <c>QuestionCostTests</c>, and what <c>chat.js</c> makes of it is only observable here.
/// </para>
/// <para>
/// The agent is driven from outside, so nothing waits for a model: the moments are the ones a
/// transcript reports, delivered when this test says so.
/// </para>
/// </remarks>
[Trait("level", "e2e")]
public sealed class AskingTheWikiTests : PageTest
{
    private const string AboutAda = "What does the wiki say about Ada Lovelace?";

    private ILocator Turn(Guid question) => Page.Locator($"#chat li[data-id=\"{question}\"]");

    [Fact]
    [Trait("req", "ACCESS-007")]
    public async Task Answer_GrowsInPlace_WhileTheRunIsStillWriting()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var question = await hub.AskAsync(AboutAda, token);

        await Page.GotoAsync($"{hub.Address}/chat.html");
        await Expect(Turn(question)).ToBeVisibleAsync();

        // The question is on the page before any of the answer is, which is what "the user waits for no
        // part of the answer" looks like from the outside (QUERY-001).
        await Expect(Turn(question).Locator(".asked")).ToHaveTextAsync(AboutAda);
        await Expect(Turn(question).Locator(".answer")).ToHaveTextAsync(string.Empty);

        // Where the question and everything drawn with it sit, before a word of the answer exists.
        var askedBefore = await Turn(question).Locator(".asked").BoundingBoxAsync();

        // The agent writes, in pieces, as a stream delivers them — the run is still under way.
        hub.Agent.Said(question, "She wrote the first program, ");

        await Expect(Turn(question).Locator(".answer")).ToHaveTextAsync("She wrote the first program, ");

        hub.Agent.Said(question, "for Babbage's Analytical Engine.");

        // One piece of prose, in the order it arrived. The text appeared while the run was under way,
        // which is the whole of "an answer appears as the agent produces it".
        await Expect(Turn(question).Locator(".answer"))
            .ToHaveTextAsync("She wrote the first program, for Babbage's Analytical Engine.");

        // **And the question above it has not moved.** This is the assertion this test exists for: the
        // answer grows downwards into space of its own, and what the user was already reading stays
        // where they were reading it (ACCESS-007, docs/ux.md).
        var askedAfter = await Turn(question).Locator(".asked").BoundingBoxAsync();

        Assert.Equal(askedBefore!.X, askedAfter!.X);
        Assert.Equal(askedBefore.Y, askedAfter.Y);
    }

    [Fact]
    [Trait("req", "ACCESS-007")]
    public async Task Answer_IsNotRedrawn_WhileItGrows()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var question = await hub.AskAsync(AboutAda, token);

        await Page.GotoAsync($"{hub.Address}/chat.html");
        await Expect(Turn(question)).ToBeVisibleAsync();

        hub.Agent.Said(question, "She wrote ");
        await Expect(Turn(question).Locator(".answer")).ToHaveTextAsync("She wrote ");

        // The very text node the first piece went into, marked so it can be recognised again. A page
        // that assigned `textContent` would replace this node on the next piece, and with it the user's
        // selection and their place in what they are reading.
        await Page.EvaluateAsync(
            """
            (id) => {
              const node = document.querySelector(`#chat li[data-id="${id}"] .answer`).firstChild;
              node.__thisOne = true;
            }
            """,
            question.ToString());

        hub.Agent.Said(question, "the first program.");
        await Expect(Turn(question).Locator(".answer")).ToHaveTextAsync("She wrote the first program.");

        var sameNode = await Page.EvaluateAsync<bool>(
            """
            (id) => document.querySelector(`#chat li[data-id="${id}"] .answer`).firstChild.__thisOne === true
            """,
            question.ToString());

        Assert.True(sameNode, "the answer's text node was replaced, so what the user was reading moved");
    }

    [Fact]
    [Trait("req", "ACCESS-009")]
    public async Task PageName_StandsInTheProseAsALink_WhenItArrivesMidSentence()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync("Notes", "wiki", token);

        var question = await hub.AskAsync(AboutAda, token);

        await Page.GotoAsync($"{hub.Address}/chat.html");
        await Expect(Turn(question)).ToBeVisibleAsync();

        hub.Agent.Said(question, "She wrote the first program (");
        await Expect(Turn(question).Locator(".answer")).ToHaveTextAsync("She wrote the first program (");

        // The text node the sentence began in, marked so it can be recognised again: the link that
        // arrives next must be added after it, not drawn by replacing it (ACCESS-007).
        await Page.EvaluateAsync(
            """
            (id) => { document.querySelector(`#chat li[data-id="${id}"] .answer`).firstChild.__thisOne = true; }
            """,
            question.ToString());

        // The link arrives in pieces, split mid-target, as a stream may deliver it — and the sentence
        // goes on after it.
        hub.Agent.Said(question, "[people/ada-lovelace.md](people/");
        hub.Agent.Said(question, "ada-lovelace.md)) for Babbage's engine.");

        // **The page's name stands in the prose, as a link**, where the agent put it (US1-AS4).
        await Expect(Turn(question).Locator(".answer"))
            .ToHaveTextAsync("She wrote the first program (people/ada-lovelace.md) for Babbage's engine.");
        await Expect(Turn(question).Locator(".answer a")).ToHaveTextAsync("people/ada-lovelace.md");

        // **And no line beside it**: the turn is its question, the line about it, and the answer —
        // nothing else holds the page's name.
        await Expect(Turn(question).Locator(":scope > *")).ToHaveCountAsync(3);

        var sameNode = await Page.EvaluateAsync<bool>(
            """
            (id) => document.querySelector(`#chat li[data-id="${id}"] .answer`).firstChild.__thisOne === true
            """,
            question.ToString());

        Assert.True(sameNode, "the prose before the link was redrawn to make room for it");
    }

    [Fact]
    [Trait("req", "ACCESS-008")]
    public async Task Cost_RisesBesideTheQuestionAgainstItsCeiling_WhileTheRunIsUnderWay()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var question = await hub.AskAsync(AboutAda, token);

        await Page.GotoAsync($"{hub.Address}/chat.html");
        await Expect(Turn(question)).ToBeVisibleAsync();

        hub.Agent.Said(question, "She wrote the first program.");
        hub.Agent.Spend(question, 12_000);

        // Written against the ceiling it is held to, which is what makes the bare figure mean anything:
        // 12 000 of 2 000 000 has spent almost nothing, and 12 000 alone says neither that nor the
        // opposite (ACCESS-008, GUARD-004).
        var cost = Turn(question).Locator(".cost");

        await Expect(cost).ToContainTextAsync("12");
        await Expect(cost).ToContainTextAsync("2");
        await Expect(cost).ToContainTextAsync("/");

        var answerBefore = await Turn(question).Locator(".answer").BoundingBoxAsync();
        var costBefore = await cost.BoundingBoxAsync();

        // The figure follows the run.
        hub.Agent.Spend(question, 1_204_118);

        await Expect(cost).ToContainTextAsync("204");

        // **And nothing moved.** The figure has an element of its own with a reserved width, so a
        // number gaining digits changes a number and moves neither the answer nor the cell itself
        // (ACCESS-008, docs/ux.md: live content grows in place).
        var answerAfter = await Turn(question).Locator(".answer").BoundingBoxAsync();
        var costAfter = await cost.BoundingBoxAsync();

        Assert.Equal(answerBefore!.Y, answerAfter!.Y);
        Assert.Equal(costBefore!.X, costAfter!.X);
        Assert.Equal(costBefore.Width, costAfter.Width);
    }

    [Fact]
    [Trait("req", "ACCESS-008")]
    public async Task Total_StandsWithNoCeilingBesideIt_AndNoCurrencyAnywhere()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var first = await hub.AskAsync(AboutAda, token);

        await Page.GotoAsync($"{hub.Address}/chat.html");
        await Expect(Turn(first)).ToBeVisibleAsync();

        hub.Agent.Spend(first, 12_000);
        await Expect(Turn(first).Locator(".cost")).ToContainTextAsync("12");

        // What the chat has spent altogether. **No ceiling beside it**: every question carries its own,
        // and `x / y` here would invent one that does not exist (ACCESS-008).
        var total = Page.Locator("#total");

        await Expect(total).ToContainTextAsync("12");
        await Expect(total).Not.ToContainTextAsync("/");

        // And no currency anywhere on the page. What a run costs is counted in input-token
        // equivalents, which are not money and have no unit of their own (DEC-015, GUARD-004).
        var shown = await Page.Locator("body").TextContentAsync();

        foreach (var currency in new[] { "$", "€", "£", "USD", "EUR" })
        {
            Assert.DoesNotContain(currency, shown, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("req", "ACCESS-007")]
    public async Task Chat_StandsAsTheHubHoldsIt_AfterTheConnectionWasLostAndRestored()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var question = await hub.AskAsync(AboutAda, token);

        await Page.GotoAsync($"{hub.Address}/chat.html");
        await Expect(Turn(question)).ToBeVisibleAsync();

        // Every time the page's stream opens, counted — so that the test can tell a page that
        // reconnected from one that simply never lost anything.
        await Page.EvaluateAsync("() => { window.__opened = 0; events.addEventListener('open', () => window.__opened++); }");

        hub.Agent.Said(question, "She wrote ");
        await Expect(Turn(question).Locator(".answer")).ToHaveTextAsync("She wrote ");

        // The browser loses its connection, and the run goes on: more of the answer, a step, a figure
        // and a second question all reach the hub while nothing reaches the page.
        hub.ChatStreamLost();

        hub.Agent.Said(question, "the first program.");
        hub.Agent.Called(question, "read_page", """{"path":"people/ada-lovelace.md"}""");
        hub.Agent.Spend(question, 12_000);
        var waiting = await hub.AskAsync("And who was her mother?", token);

        // Nothing of it reached the page while the line was down.
        await Expect(Turn(question).Locator(".answer")).ToHaveTextAsync("She wrote ");
        await Expect(Turn(waiting)).ToHaveCountAsync(0);

        hub.ChatStreamRestored();

        // `EventSource` makes the connection again on its own and is answered with a fresh snapshot,
        // which chat.js merges into a page that is already drawn (ACCESS-007's last clause).
        await Expect(Page.Locator("#chat li")).ToHaveCountAsync(2, new() { Timeout = 30_000 });
        await Expect(Turn(question).Locator(".answer")).ToHaveTextAsync("She wrote the first program.");

        Assert.True(await Page.EvaluateAsync<int>("() => window.__opened") >= 1, "the stream never opened again");

        // **Every turn exactly once, nothing missing, the answer whole** — the first part not doubled by
        // the snapshot that carried it again, and what arrived while the page was away all there.
        await Expect(Turn(question)).ToHaveCountAsync(1);
        await Expect(Turn(waiting)).ToHaveCountAsync(1);
        await Expect(Turn(question).Locator("details.steps > details.step")).ToHaveCountAsync(1);
        await Expect(Turn(question).Locator(".cost")).ToContainTextAsync("12");
        await Expect(Page.Locator("#total")).ToContainTextAsync("12");
    }
}

