using Grimoire.Wiki;
using Grimoire.Wiki.Adapters;

namespace Grimoire.Contract.Tests;

/// <summary>
/// The wiki adapter against a real directory on disk — the external thing at this port
/// (Constitution III.4). What lands on disk, and how a path resolves, is the filesystem's answer
/// and not ours, which is why these cannot be proven a level down.
/// </summary>
[Trait("level", "contract")]
public sealed class FileSystemWikiStoreTests : IDisposable
{
    private static readonly GenerationRecord Record = new(
        By: "Grimoire, claude-opus-4-5-20251101, run 8f3c1d4e",
        At: new DateTimeOffset(2026, 9, 20, 10, 6, 31, TimeSpan.Zero));

    private readonly string root = Directory.CreateTempSubdirectory("grimoire-wiki-").FullName;
    private readonly FileSystemWikiStore wiki;

    public FileSystemWikiStoreTests() => wiki = new FileSystemWikiStore(root);

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    [Trait("req", "WIKI-002")]
    public async Task WritePage_LandsOnDiskWithTheRecord()
    {
        var stamped = ProvenanceStamp.Apply("---\ntype: Recipe\n---\n\nBody.\n", Record);

        await wiki.WriteAsync("recipes/sourdough.md", stamped.Page!, TestContext.Current.CancellationToken);

        var onDisk = await File.ReadAllTextAsync(
            Path.Combine(root, "recipes", "sourdough.md"),
            TestContext.Current.CancellationToken);
        Assert.Contains("generated:", onDisk, StringComparison.Ordinal);
        Assert.Contains("  at: 2026-09-20T10:06:31Z", onDisk, StringComparison.Ordinal);
        Assert.Contains("type: Recipe", onDisk, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("req", "WIKI-002")]
    public async Task WritePage_IsRefused_WhenTheFrontmatterCannotBeRead()
    {
        var stamped = ProvenanceStamp.Apply("---\ngenerated: a plain string\n---\n\nBody.\n", Record);

        Assert.Null(stamped.Page);
        Assert.NotNull(stamped.Error);

        // The write fails, so nothing reaches the disk at all.
        Assert.Empty(await wiki.ListAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("../escaped.md")]
    [InlineData("recipes/../../escaped.md")]
    [InlineData("/etc/passwd")]
    [InlineData("recipes/./../../escaped.md")]
    [Trait("req", "GUARD-001")]
    public async Task WritePage_IsRefused_WhenThePathLeavesTheWiki(string path)
    {
        var outside = await Assert.ThrowsAsync<OutsideWikiException>(
            () => wiki.WriteAsync(path, "anything", TestContext.Current.CancellationToken));

        Assert.Equal(path, outside.Path);
        Assert.Empty(await wiki.ListAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("../escaped.md")]
    [InlineData("/etc/passwd")]
    [Trait("req", "GUARD-001")]
    public async Task ReadPage_IsRefused_WhenThePathLeavesTheWiki(string path) =>
        await Assert.ThrowsAsync<OutsideWikiException>(
            () => wiki.ReadAsync(path, TestContext.Current.CancellationToken));

    [Fact]
    [Trait("req", "WIKI-003")]
    public async Task RunFails_LeavesEveryFileItWroteOnDisk()
    {
        await wiki.WriteAsync("recipes/sourdough.md", "---\ntype: Recipe\n---\n\nBody.\n", TestContext.Current.CancellationToken);
        await wiki.WriteAsync("recipes/index.md", "- sourdough\n", TestContext.Current.CancellationToken);
        await wiki.AppendLogAsync("Run 8f3c wrote one page.\n", TestContext.Current.CancellationToken);

        // Nothing in the adapter reaches back: there is no call here that could.
        Assert.Equal(
            ["log.md", "recipes/index.md", "recipes/sourdough.md"],
            await wiki.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("req", "WIKI-003")]
    public async Task AppendLog_KeepsWhatEarlierRunsWrote()
    {
        await wiki.AppendLogAsync("First run.\n", TestContext.Current.CancellationToken);
        await wiki.AppendLogAsync("Second run.\n", TestContext.Current.CancellationToken);

        Assert.Equal(
            "First run.\nSecond run.\n",
            await wiki.ReadAsync("log.md", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadPage_FindsNothing_WhenTheFileIsNotThere()
        => Assert.Null(await wiki.ReadAsync("nothing.md", TestContext.Current.CancellationToken));

    [Fact]
    [Trait("req", "GUARD-001")]
    public async Task WritePage_IsRefused_WhenThePathNamesTheWikisOwnGitDirectory()
    {
        // scripts/run-hub.sh puts the wiki under git, so .git sits inside the very directory the
        // granted tools write to. The containment check alone lets it through: ".git/HEAD" climbs
        // nowhere and is not absolute. It is the wiki's undo, and a run must not reach it.
        await Assert.ThrowsAsync<OutsideWikiException>(
            () => wiki.WriteAsync(".git/HEAD", "ref: refs/heads/nowhere\n", TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<OutsideWikiException>(
            () => wiki.WriteAsync("notes/.git/config", "[core]\n", TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("req", "GUARD-001")]
    public async Task ReadPage_IsRefused_WhenThePathNamesADottedFile()
    {
        await File.WriteAllTextAsync(
            Path.Combine(root, ".env"), "SECRET=1\n", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<OutsideWikiException>(
            () => wiki.ReadAsync(".env", TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("req", "GUARD-001")]
    public async Task List_LeavesOutTheWikisOwnBookkeeping()
    {
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        await File.WriteAllTextAsync(
            Path.Combine(root, ".git", "HEAD"), "ref: refs/heads/main\n", TestContext.Current.CancellationToken);
        await wiki.WriteAsync("recipes/sourdough.md", "Body.\n", TestContext.Current.CancellationToken);

        // A run asking what is in the wiki is asking for its pages, not for git's objects.
        Assert.Equal(["recipes/sourdough.md"], await wiki.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("req", "GUARD-001")]
    public async Task WritePage_IsRefused_WhenALinkInTheWikiLeavesIt()
    {
        var outside = Directory.CreateTempSubdirectory("grimoire-outside-").FullName;

        try
        {
            Directory.CreateSymbolicLink(Path.Combine(root, "elsewhere"), outside);

            // GetFullPath is textual and follows no link, so "elsewhere/page.md" passes the
            // containment check while landing outside the wiki altogether.
            await Assert.ThrowsAsync<OutsideWikiException>(
                () => wiki.WriteAsync("elsewhere/page.md", "Body.\n", TestContext.Current.CancellationToken));

            Assert.Empty(Directory.GetFiles(outside));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    [Trait("req", "GUARD-001")]
    public async Task List_LeavesOutWhatALinkReachesOutsideTheWiki()
    {
        var outside = Directory.CreateTempSubdirectory("grimoire-outside-").FullName;

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(outside, "secret.md"), "Not the wiki's.\n", TestContext.Current.CancellationToken);
            Directory.CreateSymbolicLink(Path.Combine(root, "elsewhere"), outside);
            await wiki.WriteAsync("recipes/sourdough.md", "Body.\n", TestContext.Current.CancellationToken);

            // Refusing to read or write through the link is not enough on its own: an enumeration
            // that walks into it answers list_pages with files from outside the wiki, which the
            // run may then ask for by name.
            Assert.Equal(["recipes/sourdough.md"], await wiki.ListAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    [Trait("req", "GUARD-001")]
    public async Task ReadPage_IsAllowed_WhenALinkStaysInsideTheWiki()
    {
        await wiki.WriteAsync("recipes/sourdough.md", "Body.\n", TestContext.Current.CancellationToken);
        Directory.CreateSymbolicLink(Path.Combine(root, "shortcut"), Path.Combine(root, "recipes"));

        // A link is not the offence; leaving the wiki is. One that stays inside reads as itself.
        Assert.Equal("Body.\n", await wiki.ReadAsync("shortcut/sourdough.md", TestContext.Current.CancellationToken));
    }
}
