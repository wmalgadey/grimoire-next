using Grimoire.Wiki;
using Grimoire.Wiki.Adapters;

namespace Grimoire.Tests.Wiki;

/// <summary>
/// T023 / ADR-0005. Against a real git repository with a real working tree — the only sanctioned
/// double in this system is the LLM (constitution III.2), so git is the real thing here.
/// </summary>
public sealed class GitCliTests : IDisposable
{
    private readonly string _repo =
        Path.Combine(Path.GetTempPath(), $"grimoire-wiki-{Guid.NewGuid():N}");

    public GitCliTests()
    {
        Directory.CreateDirectory(_repo);
        Run("init", "--initial-branch=main");
        Run("config", "user.email", "grimoire@test.invalid");
        Run("config", "user.name", "Grimoire");
        File.WriteAllText(Path.Combine(_repo, "index.md"), "# Index\n\nThe wiki starts here.\n");
        Run("add", "-A");
        Run("commit", "-m", "seed");
    }

    public void Dispose()
    {
        if (Directory.Exists(_repo))
        {
            // git marks objects read-only; clear that before deleting.
            foreach (var file in Directory.EnumerateFiles(_repo, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_repo, recursive: true);
        }
    }

    private GitCli Git => new(_repo);

    [Fact]
    public void ReadsTheCurrentTip()
    {
        var head = Git.RevParseHead();

        Assert.Equal(40, head.Length);
        Assert.Matches("^[0-9a-f]{40}$", head);
    }

    [Fact]
    public void CommitsTheWholeWorkingTreeAsExactlyOneCommit()
    {
        var parent = Git.RevParseHead();
        File.WriteAllText(Path.Combine(_repo, "topic.md"), "# Topic\n");
        File.WriteAllText(Path.Combine(_repo, "index.md"), "# Index\n\nSee topic.\n");

        var commit = Git.CommitAll("record what the run wrote");

        Assert.NotNull(commit);
        Assert.Equal(parent, commit.ParentSha);
        Assert.Equal("record what the run wrote", commit.Message);
        Assert.Equal(commit.Sha, Git.RevParseHead());
        // Exactly one commit: the tip moved by one, never two (FR-015, SC-002).
        Assert.Equal(2, Git.CountCommits());
    }

    [Fact]
    public void CommitsNothingWhenTheWorkingTreeIsClean()
    {
        var before = Git.RevParseHead();

        var commit = Git.CommitAll("nothing to say");

        // A run that changed nothing produces no commit at all (FR-016), not an empty one.
        Assert.Null(commit);
        Assert.Equal(before, Git.RevParseHead());
        Assert.Equal(1, Git.CountCommits());
    }

    [Fact]
    public void CommitsADeletionAsPartOfTheSameCommit()
    {
        File.WriteAllText(Path.Combine(_repo, "doomed.md"), "# Doomed\n");
        Git.CommitAll("add a page to delete");
        File.Delete(Path.Combine(_repo, "doomed.md"));

        var commit = Git.CommitAll("remove the page");

        Assert.NotNull(commit);
        var diffs = Git.DiffOf(commit.Sha);
        Assert.Equal([("doomed.md", ChangeKind.Removed)], diffs.Select(d => (d.Path, d.ChangeKind)));
    }

    [Fact]
    public void ResetsArbitraryWorkingTreeChangesToByteIdenticalContent()
    {
        var before = SnapshotWorkingTree();
        var tip = Git.RevParseHead();

        // Everything a crashed run could have left behind: a modification, a new file,
        // a new directory, and a deletion.
        File.WriteAllText(Path.Combine(_repo, "index.md"), "clobbered");
        File.WriteAllText(Path.Combine(_repo, "stray.md"), "written mid-run");
        Directory.CreateDirectory(Path.Combine(_repo, "notes"));
        File.WriteAllText(Path.Combine(_repo, "notes", "scratch.md"), "scratch");

        Git.ResetWorkingTree();

        Assert.Equal(before, SnapshotWorkingTree());
        Assert.Equal(tip, Git.RevParseHead());
        Assert.False(Directory.Exists(Path.Combine(_repo, "notes")));
        Assert.Equal(1, Git.CountCommits());
    }

    [Fact]
    public void ResetsARestoredDeletion()
    {
        var before = SnapshotWorkingTree();
        File.Delete(Path.Combine(_repo, "index.md"));

        Git.ResetWorkingTree();

        Assert.Equal(before, SnapshotWorkingTree());
    }

    [Fact]
    public void ReportsWhetherTheWorkingTreeIsDirty()
    {
        Assert.False(Git.HasChanges());

        File.WriteAllText(Path.Combine(_repo, "new.md"), "# New\n");

        Assert.True(Git.HasChanges());
    }

    [Fact]
    public void ReportsPerFileDiffsOfACommit()
    {
        File.WriteAllText(Path.Combine(_repo, "added.md"), "# Added\n");
        File.WriteAllText(Path.Combine(_repo, "index.md"), "# Index\n\nChanged.\n");
        var commit = Git.CommitAll("one added, one modified");

        Assert.NotNull(commit);
        var diffs = Git.DiffOf(commit.Sha).OrderBy(d => d.Path).ToList();

        Assert.Equal(2, diffs.Count);
        Assert.Equal("added.md", diffs[0].Path);
        Assert.Equal(ChangeKind.Added, diffs[0].ChangeKind);
        Assert.Contains("# Added", diffs[0].Patch);
        Assert.Equal("index.md", diffs[1].Path);
        Assert.Equal(ChangeKind.Modified, diffs[1].ChangeKind);
        Assert.Contains("Changed.", diffs[1].Patch);
    }

    [Fact]
    public void RevertsACommitAsANewCommitLeavingHistoryIntact()
    {
        var beforeContent = SnapshotWorkingTree();
        File.WriteAllText(Path.Combine(_repo, "topic.md"), "# Topic\n");
        var commit = Git.CommitAll("add topic");
        Assert.NotNull(commit);

        var revertSha = Git.Revert(commit.Sha);

        // Content is restored byte-identically, as a new commit. The original stays in history:
        // nothing is rewritten or discarded (FR-025, SC-005).
        Assert.Equal(beforeContent, SnapshotWorkingTree());
        Assert.Equal(revertSha, Git.RevParseHead());
        Assert.Equal(3, Git.CountCommits());
        Assert.NotEqual(commit.Sha, revertSha);
        Assert.Contains(commit.Sha, Git.CommitShas());
    }

    private Dictionary<string, string> SnapshotWorkingTree()
    {
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(_repo, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(_repo, file);
            if (relative.StartsWith(".git", StringComparison.Ordinal))
            {
                continue;
            }

            snapshot[relative] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
        }

        return snapshot;
    }

    private void Run(params string[] args)
    {
        var info = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = _repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = System.Diagnostics.Process.Start(info)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
