using Grimoire.Wiki;

namespace Grimoire.Fast.Tests;

/// <summary>
/// What a failed run leaves behind, and what Grimoire does not do to it (WIKI-003).
/// </summary>
/// <remarks>
/// What Grimoire does <em>not</em> do is observable at the port: there is nothing there to remove,
/// revert or commit with. That absence is the requirement, so it is what this test reads. That a
/// failed run's pages and log entry stay on disk is the real adapter's, and is proven against it
/// (<c>FileSystemWikiStoreTests</c>).
/// </remarks>
[Trait("level", "fast")]
public sealed class FailedRunTests
{
    [Fact]
    [Trait("req", "WIKI-003")]
    public void WikiPort_OffersNoWayToRemoveRevertOrCommit()
    {
        var offered = typeof(IWikiStore).GetMethods().Select(m => m.Name).Order(StringComparer.Ordinal);

        // Not a refusal at runtime but an absence at the port: undo belongs to the wiki's own
        // history, never to Grimoire (docs/product.md §4).
        Assert.Equal(["AppendLogAsync", "ListAsync", "ReadAsync", "WriteAsync"], offered);
    }
}
