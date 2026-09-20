using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Grimoire.E2E.Tests;

/// <summary>
/// A person entering and submitting a text in a browser, against the running hub (ACCESS-001).
/// </summary>
[Trait("level", "e2e")]
public sealed class SubmitTextTests : PageTest
{
    [Fact]
    [Trait("req", "ACCESS-001")]
    public async Task APersonPastesATextSubmitsItAndThePageReportsItAccepted()
    {
        await using var hub = await HubUnderTest.StartAsync(TestContext.Current.CancellationToken);

        await Page.GotoAsync(hub.Address);
        await Page.FillAsync("#text", "Ada Lovelace wrote the first program.");
        await Page.ClickAsync("#submit");

        await Expect(Page.Locator("#message")).ToHaveTextAsync("Submission accepted. A run is under way.");
    }
}
