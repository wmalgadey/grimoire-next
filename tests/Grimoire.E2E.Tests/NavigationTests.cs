using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Grimoire.E2E.Tests;

/// <summary>
/// The three jobs reach each other: submitting a source, reading a submission's run, and asking the
/// wiki (ACCESS-010).
/// </summary>
/// <remarks>
/// <para>
/// A link the user follows between three served pages exists only in a browser: whether an anchor is
/// on the page, whether it points somewhere that answers, and whether following it arrives are three
/// claims a unit cannot make at all.
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
    public async Task Chat_IsReachedFromTheList()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        await Page.GotoAsync(hub.Address);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Ask the wiki" }).ClickAsync();

        // Arrived, and it is the chat: the thing the page is for is on it.
        await Expect(Page.Locator("#text")).ToBeVisibleAsync();
        await Expect(Page.Locator("h1")).ToContainTextAsync("Ask the wiki");
    }

    [Fact]
    public async Task List_IsReachedFromTheChat()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        await Page.GotoAsync($"{hub.Address}/chat.html");
        await Page.Locator(".jobs a").First.ClickAsync();

        // The list, which is where a source is submitted **and** where a submission's run is opened
        // from — a run's page addresses one submission, so there is no address for the chat to link to.
        //
        // Asserted on what is actually drawn: an empty list has no height, so a browser calls it hidden
        // and its presence says nothing about having arrived.
        await Expect(Page.Locator("h1")).ToContainTextAsync("Submit a text");
        await Expect(Page.Locator("#text")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "What became of each submission" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task ChatAndTheList_AreBothReachedFromARunsPage()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = await HubUnderTest.StartAsync(token);

        // A run to have a page for. A submission's run is reached from the row that names it, which is
        // the third job, and this is the page it leads to (ACCESS-006).
        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.", token);
        hub.Agent.ReportIn(submission);

        await Page.GotoAsync(hub.Address);
        await Page.Locator($"#submissions li[data-id=\"{submission}\"] .open-run").ClickAsync();

        await Expect(Page.Locator("h1")).ToContainTextAsync("what this run did");

        // From here the other two. Both, because this page is the one that has room for both links.
        await Page.GetByRole(AriaRole.Link, new() { Name = "Ask the wiki" }).ClickAsync();
        await Expect(Page.Locator("#text")).ToBeVisibleAsync();
        await Expect(Page.Locator("h1")).ToContainTextAsync("Ask the wiki");

        await Page.GoBackAsync();
        await Page.Locator(".jobs a").First.ClickAsync();
        await Expect(Page.Locator("h1")).ToContainTextAsync("Submit a text");
    }
}
