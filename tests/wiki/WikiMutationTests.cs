using System.Runtime.Versioning;
using Grimoire.Tests.Support;
using Grimoire.Wiki;
using Grimoire.Wiki.Adapters;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.Tests.Wiki;

/// <summary>
/// T146 (FR-015, SC-003). Once a run's commit is in history it is a fact nothing can undo, so
/// every step after it failing must still hand the commit back — otherwise the run is recorded as
/// failed with no commit, and the wiki holds a change no task accounts for.
/// </summary>
public sealed class WikiMutationTests : IDisposable
{
    private readonly WikiRepositoryFixture _wiki = new();

    public void Dispose() => _wiki.Dispose();

    [Fact]
    [UnsupportedOSPlatform("windows")] // file modes are how the clean is made to fail
    public async Task HandsBackTheCommitWhenTheCleanAfterItFails()
    {
        // An ignored file git cannot delete: the commit succeeds, the clean that follows it does not.
        _wiki.Write(".gitignore", "locked/\n");
        _wiki.CommitTracked("ignore locked/");
        var locked = Path.Combine(_wiki.Path, "locked");
        Directory.CreateDirectory(locked);
        File.WriteAllText(Path.Combine(locked, "residue.md"), "# Residue\n");
        File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        var parent = _wiki.Head();
        _wiki.Write("topics/kept.md", "# Kept\n");
        var mutation = new WikiMutation(new GitCli(_wiki.Path), NullLogger<WikiMutation>.Instance);

        WikiCommit? commit;
        try
        {
            commit = await mutation.CommitRun("task-1", "Add kept topic", _ => { }, TestContext.Current.CancellationToken);
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Assert.NotNull(commit);
        Assert.Equal(_wiki.Head(), commit.Sha);
        Assert.Equal(parent, commit.ParentSha);
        Assert.Equal("Add kept topic", commit.Message);
    }

    [Fact]
    public async Task RecordsTheCommitExactlyAsHistoryHoldsIt()
    {
        _wiki.Write("topics/kept.md", "# Kept\n");
        var mutation = new WikiMutation(new GitCli(_wiki.Path), NullLogger<WikiMutation>.Instance);

        var commit = await mutation.CommitRun("task-1", "Add kept topic", _ => { }, TestContext.Current.CancellationToken);

        Assert.NotNull(commit);
        Assert.Equal(_wiki.Head(), commit.Sha);
        Assert.Equal(2, _wiki.CommitCount());
        Assert.True(_wiki.IsClean());
        Assert.InRange(commit.CommittedAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task NeverMovesTheBranchWhenTheCommitCannotBePutOnRecord()
    {
        // The record comes first: a commit that could not be put on record must not reach history,
        // or a crash after it would leave a commit nothing accounts for (FR-028).
        var tip = _wiki.Head();
        _wiki.Write("topics/unrecorded.md", "# Unrecorded\n");
        var mutation = new WikiMutation(new GitCli(_wiki.Path), NullLogger<WikiMutation>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => mutation.CommitRun(
            "task-1", "Add unrecorded topic", _ => throw new InvalidOperationException("the store is down"),
            TestContext.Current.CancellationToken));

        Assert.Equal(tip, _wiki.Head());
        Assert.Equal(1, _wiki.CommitCount());
    }
}
