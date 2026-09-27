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
/// carries is proven a level down, in the Fast suite's <c>ChatStreamTests</c>, and what <c>chat.js</c>
/// makes of it is only observable here.
/// </para>
/// <para>
/// The agent is driven from outside, so nothing waits for a model: the moments are the ones a
/// transcript reports, delivered when this test says so.
/// </para>
/// </remarks>
[Trait("level", "e2e")]
[Trait("req", "ACCESS-007")]
public sealed class AskingTheWikiTests : PageTest
{
    private const string AboutAda = "What does the wiki say about Ada Lovelace?";

    private ILocator Turn(Guid question) => Page.Locator($"#chat li[data-id=\"{question}\"]");

    [Fact]
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
    [Trait("req", "GUARD-005")]
    public async Task Wiki_IsByteForByteWhatItWas_AfterAQuestionWasAnswered()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        // A wiki that already holds something, so that "unchanged" is a claim about files and not about
        // an empty directory.
        Directory.CreateDirectory(Path.Combine(hub.WikiDirectory, "people"));
        await File.WriteAllTextAsync(
            Path.Combine(hub.WikiDirectory, "people", "ada-lovelace.md"),
            "---\ntype: person\n---\n\n# Ada Lovelace\n",
            token);
        await File.WriteAllTextAsync(
            Path.Combine(hub.WikiDirectory, "index.md"), "---\nokf_version: \"0.2\"\n---\n", token);
        await File.WriteAllTextAsync(Path.Combine(hub.WikiDirectory, "log.md"), "# Log\n", token);

        var before = hub.WikiAsItStands();

        var question = await hub.AskAsync(AboutAda, token);

        await Page.GotoAsync($"{hub.Address}/chat.html");
        await Expect(Turn(question)).ToBeVisibleAsync();

        // A run that reads, says something, and stops of its own accord inside both ceilings.
        hub.Agent.Called(question, "list_pages", "{}");
        hub.Agent.Returned(question, "list_pages", """{"paths":["people/ada-lovelace.md"]}""");
        hub.Agent.Called(question, "read_page", """{"path":"people/ada-lovelace.md"}""");
        hub.Agent.Returned(question, "read_page", "# Ada Lovelace");
        hub.Agent.Said(question, "The wiki has a page for her ([people/ada-lovelace.md](people/ada-lovelace.md)).");

        // Ended done. `Exit` alone would not do it: the verdict at a process's exit also needs the
        // `result` that says the agent stopped of its own accord, and this harness reports no result —
        // so the ending is given directly, which is the path a run with no result takes anyway
        // (RUNS-005, contracts/agent-cli-protocol.md).
        hub.Agent.End(question, RunOutcome.Done);

        await Expect(Turn(question).Locator(".state")).ToHaveTextAsync("answered");

        // **Byte for byte.** No page written, no index touched, no log entry — by construction and not
        // because the instruction asked nicely: the tools that could have done any of it are not
        // served at that run's endpoint at all (GUARD-005, contracts/question-run.md §1).
        var after = hub.WikiAsItStands();

        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));

        foreach (var (path, held) in before)
        {
            Assert.Equal(held, after[path]);
        }
    }
}
