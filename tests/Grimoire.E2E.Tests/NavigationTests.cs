using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Grimoire.E2E.Tests;

/// <summary>
/// The three jobs reach each other: submitting a source, reading a submission's run, and asking the
/// wiki (ACCESS-010).
/// </summary>
/// <remarks>
/// <para>
/// One pass over all three pages, following every link there is between them (Constitution III.4:
/// tests with the same setup that differ only in their assertion are one test).
/// </para>
/// <para>
/// <c>docs/ux.md</c> withholds navigation chrome "until a second job exists", and a third exists now —
/// so it stays what the pages already are: a line of links, no bar and no menu (research.md R-14).
/// </para>
/// </remarks>
[Trait("level", "e2e")]
[Trait("req", "ACCESS-010")]
public sealed class NavigationTests : PageTest
{
    [Fact]
    public async Task Links_ReachEachJobFromTheOtherTwo()
    {
        // Why a browser (III.4): JavaScript logic no other runner reaches — a run's page is reached
        // only through the link app.js draws on that submission's row. The line of links on each page
        // is static and rides along: one pass, every page, every link followed.
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        // A run to have a page for (ACCESS-006).
        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);
        hub.Agent.ReportIn(submission);

        // The list → a run's page, from the row that names it.
        await Page.GotoAsync(hub.Address);
        await Page.Locator($"#submissions li[data-id=\"{submission}\"] .open-run").ClickAsync();
        await Expect(Page.Locator("h1")).ToContainTextAsync("what this run did");

        // A run's page → the list.
        await Page.Locator(".jobs a").First.ClickAsync();
        await ExpectTheList();

        // The list → the chat.
        await Page.GetByRole(AriaRole.Link, new() { Name = "Ask the wiki" }).ClickAsync();
        await ExpectTheChat();

        // The chat → the list, which is where a source is submitted **and** where a submission's run is
        // opened from — a run's page addresses one submission, so there is no address for the chat to
        // link to.
        await Page.Locator(".jobs a").First.ClickAsync();
        await ExpectTheList();

        // And a run's page → the chat.
        await Page.Locator($"#submissions li[data-id=\"{submission}\"] .open-run").ClickAsync();
        await Expect(Page.Locator("h1")).ToContainTextAsync("what this run did");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Ask the wiki" }).ClickAsync();
        await ExpectTheChat();
    }

    /// <summary>
    /// Arrived at the chat: the thing the page is for is on it.
    /// </summary>
    private async Task ExpectTheChat()
    {
        await Expect(Page.Locator("#text")).ToBeVisibleAsync();
        await Expect(Page.Locator("h1")).ToContainTextAsync("Ask the wiki");
    }

    /// <summary>
    /// Arrived at the list. Asserted on what is actually drawn: an empty list has no height, so a
    /// browser calls it hidden and its presence says nothing about having arrived.
    /// </summary>
    private async Task ExpectTheList()
    {
        await Expect(Page.Locator("h1")).ToContainTextAsync("Submit a text");
        await Expect(Page.Locator("#text")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "What became of each submission" }))
            .ToBeVisibleAsync();
    }
}
