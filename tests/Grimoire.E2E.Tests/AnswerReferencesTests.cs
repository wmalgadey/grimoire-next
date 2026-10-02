using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Grimoire.E2E.Tests;

/// <summary>
/// What an answer rests on, and how the user checks it: the steps folded under it, opened one at a
/// time, and a page the answer names opened in the editor the wiki is read in (ACCESS-007,
/// ACCESS-006, ACCESS-009).
/// </summary>
/// <remarks>
/// <para>
/// All of this is the browser's. A thing that is <em>shut</em> exists only where something can open
/// it; that a further step appears below what is there without disturbing the scroll or closing what
/// the user opened is geometry; and a link is followed rather than read. None of the three is a claim
/// a unit can make (DEC-020).
/// </para>
/// <para>
/// What the stream carries is proven a level down, in the Fast suite's <c>StepTests</c> and
/// <c>VaultTests</c>. What <c>chat.js</c> makes of it is here.
/// </para>
/// </remarks>
[Trait("level", "e2e")]
public sealed class AnswerReferencesTests : PageTest
{
    private const string AboutAda = "What does the wiki say about Ada Lovelace?";
    private const string Vault = "Notes";
    private const string WikiInTheVault = "wiki";

    private ILocator Turn(Guid question) => Page.Locator($"#chat li[data-id=\"{question}\"]");

    [Fact]
    [Trait("req", "ACCESS-007")]
    public async Task Steps_AreShutUntilTheUserOpensOne()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var question = await hub.AskAsync(AboutAda, token);

        await Page.GotoAsync($"{hub.Address}/chat.html");
        await Expect(Turn(question)).ToBeVisibleAsync();

        hub.Agent.Said(question, "The wiki has a page for her.");
        hub.Agent.Called(question, "read_page", """{"path":"people/ada-lovelace.md"}""");
        hub.Agent.Returned(question, "read_page", "# Ada Lovelace\n\nShe wrote the first program.");

        var steps = Turn(question).Locator("details.steps");
        await Expect(steps).ToBeVisibleAsync();

        // **Shut.** What the agent did is under the answer for the user who wants it, not in front of
        // the user who does not (ACCESS-007, and the shape a run's record is read in — ACCESS-006).
        Assert.False(await steps.Locator(":scope > details.step").First.EvaluateAsync<bool>("d => d.open"));

        // The fold says how many there are without being opened, which is what lets the user see that
        // something happened at all.
        await Expect(steps.Locator(":scope > summary")).ToContainTextAsync("2 steps");

        // Opened **a step at a time**: opening one leaves the others as they were.
        await steps.EvaluateAsync("d => d.open = true");
        await steps.Locator(":scope > details.step").First.Locator("summary").ClickAsync();

        var opened = steps.Locator(":scope > details.step");

        Assert.True(await opened.Nth(0).EvaluateAsync<bool>("d => d.open"));
        Assert.False(await opened.Nth(1).EvaluateAsync<bool>("d => d.open"));

        // And an opened step shows that call and what it returned, in the shape a record's segment is
        // read in — "called read_page", then the call whole.
        await Expect(opened.Nth(0).Locator("summary")).ToContainTextAsync("called read_page");
        await Expect(opened.Nth(0).Locator("pre")).ToContainTextAsync("people/ada-lovelace.md");
    }

    [Fact]
    [Trait("req", "ACCESS-007")]
    public async Task Step_AppearsBelowWhatIsThere_WhileAStepTheUserOpenedStaysOpen()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        var question = await hub.AskAsync(AboutAda, token);

        await Page.GotoAsync($"{hub.Address}/chat.html");
        await Expect(Turn(question)).ToBeVisibleAsync();

        hub.Agent.Said(question, new string('x', 4_000));
        hub.Agent.Called(question, "list_pages", "{}");

        var steps = Turn(question).Locator("details.steps");
        await Expect(steps.Locator(":scope > details.step")).ToHaveCountAsync(1);

        await steps.EvaluateAsync("d => d.open = true");
        await steps.Locator(":scope > details.step").First.EvaluateAsync("d => d.open = true");

        // The user has scrolled and opened a step. Both are things a redraw would take away.
        var scrolledTo = await Page.EvaluateAsync<double>(
            "() => { window.scrollTo(0, Math.floor(document.body.scrollHeight / 2)); return window.scrollY; }");

        Assert.True(scrolledTo > 0, "the page did not scroll, so there is no scroll position to keep");

        var firstBefore = await steps.Locator(":scope > details.step").First.BoundingBoxAsync();

        // The run is still under way, and does more.
        hub.Agent.Returned(question, "list_pages", """{"paths":["people/ada-lovelace.md"]}""");

        await Expect(steps.Locator(":scope > details.step")).ToHaveCountAsync(2);

        // **Below** what was already there.
        await Expect(steps.Locator(":scope > details.step").Nth(1).Locator("summary"))
            .ToContainTextAsync("list_pages returned");

        // The scroll is where the user left it, and the step they opened is still open. This is the
        // assertion this test exists for (ACCESS-007, docs/ux.md: live content grows in place).
        Assert.Equal(scrolledTo, await Page.EvaluateAsync<double>("() => window.scrollY"));
        Assert.True(
            await steps.Locator(":scope > details.step").First.EvaluateAsync<bool>("d => d.open"),
            "a step the user had opened was closed by one arriving after it");

        var firstAfter = await steps.Locator(":scope > details.step").First.BoundingBoxAsync();

        Assert.Equal(firstBefore!.Y, firstAfter!.Y);
    }

    [Fact]
    [Trait("req", "ACCESS-009")]
    public async Task Reference_OpensThatPageInTheOwnersOwnWiki()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(Vault, WikiInTheVault, token);

        var question = await hub.AskAsync(AboutAda, token);

        await Page.GotoAsync($"{hub.Address}/chat.html");
        await Expect(Turn(question)).ToBeVisibleAsync();

        // The agent names the page inside its prose, as a link in the wiki's own form, with the target
        // relative to the wiki's root (QUERY-004, WIKI-001, OKF §6.1).
        hub.Agent.Said(
            question,
            "She wrote the first program ([people/ada-lovelace.md](people/ada-lovelace.md)).");

        var reference = Turn(question).Locator(".references a").First;
        await Expect(reference).ToBeVisibleAsync();

        // **The page in the user's own wiki**: the vault they read it in, and the wiki's path inside
        // that vault joined to the target the answer gave (ACCESS-009).
        Assert.Equal(
            $"obsidian://open?vault={Vault}&file={WikiInTheVault}%2Fpeople%2Fada-lovelace.md",
            await reference.GetAttributeAsync("href"));

        // The agent's own words are untouched: the link is drawn beside the prose, not into it, so
        // nothing the user is reading was replaced to make it (ACCESS-007).
        await Expect(Turn(question).Locator(".answer"))
            .ToHaveTextAsync("She wrote the first program ([people/ada-lovelace.md](people/ada-lovelace.md)).");
    }

    [Fact]
    [Trait("req", "ACCESS-009")]
    public async Task Reference_IsPlainText_WhereItsTargetLeavesTheWiki()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(Vault, WikiInTheVault, token);

        var question = await hub.AskAsync(AboutAda, token);

        await Page.GotoAsync($"{hub.Address}/chat.html");
        await Expect(Turn(question)).ToBeVisibleAsync();

        // A target that climbs out of the wiki names something a reference has no business addressing,
        // however the answer came by it. A reference's target is a path relative to the wiki's root —
        // that is what the contract fixes, and `FileSystemWikiStore` refuses the same shape one layer
        // down. ACCESS-009's clause "a reference that leaves the wiki is shown as text and opens
        // nothing" is what this proves.
        hub.Agent.Said(
            question,
            "See ([../outside.md](../outside.md)), ([..\\outside.md](..\\outside.md)) "
            + "and ([people/ada-lovelace.md](people/ada-lovelace.md)).");

        // The page of this wiki is linked; the one that climbs out is not, and keeps its place in the
        // prose as plain text. Both are still readable — nothing of the agent's words is removed.
        var references = Turn(question).Locator(".references a");

        await Expect(references).ToHaveCountAsync(1);
        await Expect(references.First).ToHaveTextAsync("people/ada-lovelace.md");
        await Expect(Turn(question).Locator(".answer")).ToContainTextAsync("../outside.md");

        // A backslash is a path separator too, where the owner's Obsidian may be running — so a check
        // that knew only one would be a door left open on the platform it was not written on.
        await Expect(Turn(question).Locator(".answer")).ToContainTextAsync("..\\outside.md");
    }

    [Fact]
    [Trait("req", "ACCESS-009")]
    public async Task Reference_OpensThatPage_WhereTheWikiIsTheVault()
    {
        var token = TestContext.Current.CancellationToken;

        // The owner opens the wiki directly in Obsidian, so it has no path *inside* the vault — it is
        // the vault. An ordinary setup, and one an empty path is the honest answer for.
        await using var hub = await HubUnderTest.StartAsync(Vault, string.Empty, token);

        var question = await hub.AskAsync(AboutAda, token);

        await Page.GotoAsync($"{hub.Address}/chat.html");
        await Expect(Turn(question)).ToBeVisibleAsync();

        hub.Agent.Said(question, "She wrote it ([people/ada-lovelace.md](people/ada-lovelace.md)).");

        var reference = Turn(question).Locator(".references a").First;
        await Expect(reference).ToBeVisibleAsync();

        // The target stands alone: nothing is prefixed, and no empty segment is invented in front of
        // it. Treated as "not given", this setup would have drawn no link at all and said opening was
        // not set up (ACCESS-009).
        Assert.Equal(
            $"obsidian://open?vault={Vault}&file=people%2Fada-lovelace.md",
            await reference.GetAttributeAsync("href"));

        await Expect(Page.Locator("#opening")).ToHaveTextAsync(string.Empty);
    }

    [Fact]
    [Trait("req", "ACCESS-009")]
    public async Task Reference_IsPlainTextAndSaysOpeningIsNotSetUp_WithoutTheVault()
    {
        var token = TestContext.Current.CancellationToken;

        // Neither vault input given, which is how Grimoire starts until the owner says otherwise.
        await using var hub = await HubUnderTest.StartAsync(token);

        var question = await hub.AskAsync(AboutAda, token);

        await Page.GotoAsync($"{hub.Address}/chat.html");

        // **The question is not refused.** That is for a missing question instruction or purpose
        // description (QUERY-003); this is a convenience, and the answer is still an answer without it.
        await Expect(Turn(question)).ToBeVisibleAsync();
        await Expect(Turn(question).Locator(".state")).Not.ToHaveTextAsync("got no answer");

        hub.Agent.Said(
            question,
            "She wrote the first program ([people/ada-lovelace.md](people/ada-lovelace.md)).");

        // The answer arrives as normal and the page's name is readable in the prose, as plain text.
        await Expect(Turn(question).Locator(".answer"))
            .ToContainTextAsync("people/ada-lovelace.md");

        await Expect(Turn(question).Locator(".references a")).ToHaveCountAsync(0);

        // And the chat says **once** that opening a page is not set up — so a reader can tell a setting
        // that is absent from a page that is simply not linkable (ACCESS-009).
        await Expect(Page.Locator("#opening")).ToContainTextAsync("not set up");
        await Expect(Page.Locator("#opening")).ToHaveCountAsync(1);
    }
}
