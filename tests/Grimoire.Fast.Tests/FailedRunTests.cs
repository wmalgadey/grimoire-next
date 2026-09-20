using Grimoire.Wiki;

namespace Grimoire.Fast.Tests;

/// <summary>
/// What a failed run leaves behind, and what Grimoire does not do to it (WIKI-003).
/// </summary>
/// <remarks>
/// What Grimoire does <em>not</em> do is observable at the port: there is nothing there to remove,
/// revert or commit with. That absence is the requirement, so it is what these tests read.
/// </remarks>
[Trait("level", "fast")]
[Trait("req", "WIKI-003")]
public sealed class FailedRunTests
{
    private readonly InMemoryWikiStore wiki = new();

    private async Task ARunWrites()
    {
        await wiki.WriteAsync("recipes/sourdough.md", "---\ntype: Recipe\n---\n\nBody.\n", TestContext.Current.CancellationToken);
        await wiki.WriteAsync("recipes/index.md", "- sourdough\n", TestContext.Current.CancellationToken);
        await wiki.AppendLogAsync("Run 8f3c wrote one page.\n", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunFails_LeavesEveryPageItWroteInPlace()
    {
        await ARunWrites();

        // The run ends failed. Nothing in Grimoire reaches back into the wiki afterwards: the
        // failure is shown to the user, not recorded in the wiki or undone there.
        Assert.Equal(
            ["log.md", "recipes/index.md", "recipes/sourdough.md"],
            await wiki.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RunFails_LeavesTheLogEntryItWroteInPlace()
    {
        await ARunWrites();

        Assert.Equal("Run 8f3c wrote one page.\n", await wiki.ReadAsync("log.md", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void WikiPort_OffersNoWayToRemoveRevertOrCommit()
    {
        var offered = typeof(IWikiStore).GetMethods().Select(m => m.Name).Order(StringComparer.Ordinal);

        // Not a refusal at runtime but an absence at the port: undo belongs to the wiki's own
        // history, never to Grimoire (docs/product.md §4).
        Assert.Equal(["AppendLogAsync", "ListAsync", "ReadAsync", "WriteAsync"], offered);
    }
}
