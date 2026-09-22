using Grimoire.Wiki;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The names the wiki reserves, and which tool may write to them (WIKI-002, data-model.md).
/// </summary>
/// <remarks>
/// Pages, indexes and the log are all files at the store's port, and which of them carries a
/// generation record is decided above it. These are that decision, with no directory to read: a
/// page written over an index would be stamped where no record belongs, and an index written over
/// a page would create one that no run is recorded as having generated.
/// </remarks>
[Trait("level", "fast")]
[Trait("req", "WIKI-002")]
public sealed class WikiFileTests
{
    [Fact]
    public void Index_IsTheNameAtEveryDepth()
    {
        Assert.True(WikiFile.IsAnIndex("index.md"));
        Assert.True(WikiFile.IsAnIndex("recipes/index.md"));
        Assert.True(WikiFile.IsAnIndex("recipes/sourdough/index.md"));
    }

    [Fact]
    public void Index_IsTheWholeName_AndNotAnEndingOfIt()
    {
        // A page may well be called this; what reserves the name is the name, not a suffix of it.
        Assert.False(WikiFile.IsAnIndex("recipes/the-index.md"));
        Assert.False(WikiFile.IsAnIndex("index.md.md"));
        Assert.False(WikiFile.IsAnIndex("recipes/sourdough.md"));
    }

    [Fact]
    public void Page_MayNotBeWrittenOverAnIndexOrTheLog()
    {
        // Both have a tool of their own, and neither carries a generation record.
        Assert.True(WikiFile.IsReservedForSomethingOtherThanAPage("index.md"));
        Assert.True(WikiFile.IsReservedForSomethingOtherThanAPage("recipes/index.md"));
        Assert.True(WikiFile.IsReservedForSomethingOtherThanAPage("log.md"));
    }

    [Fact]
    public void Page_MayBeWrittenAnywhereElse()
    {
        Assert.False(WikiFile.IsReservedForSomethingOtherThanAPage("recipes/sourdough.md"));
        Assert.False(WikiFile.IsReservedForSomethingOtherThanAPage("catalogue.md"));
    }
}
