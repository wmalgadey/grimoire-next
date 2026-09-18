using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Grimoire.Wiki.Adapters;

/// <summary>
/// The wiki repository (ADR-0005). This file is the only place in the repository that invokes
/// <c>git</c>, and — with <c>src/dispatch/adapters/</c> — one of only two that use
/// <see cref="Process"/>. Adapter confinement, asserted by
/// <c>tests/architecture/AdapterConfinementTests.cs</c> (constitution V.3).
/// </summary>
/// <remarks>
/// Every wiki mutation in the system goes through this type: one commit per run at run end,
/// a working-tree reset on every failure path, and revert as a second commit (constitution II.1).
/// </remarks>
public sealed class GitCli(string repositoryPath)
{
    /// <summary>The working tree the agent writes into and the hub commits from.</summary>
    public string RepositoryPath { get; } = repositoryPath;

    /// <summary>The current tip — the pre-run commit a failed run leaves the wiki at (FR-017).</summary>
    public string RevParseHead() => Execute("rev-parse", "HEAD").Trim();

    /// <summary>Whether the working tree differs from the tip, tracked files and untracked alike.</summary>
    public bool HasChanges() => Execute("status", "--porcelain").Length > 0;

    /// <summary>
    /// Stages everything and produces <b>exactly one</b> commit (FR-015). Returns <c>null</c>
    /// without committing when the working tree is clean, so a run that changed nothing leaves
    /// no commit at all rather than an empty one (FR-016).
    /// </summary>
    /// <param name="message">
    /// The run's final assistant message, verbatim (research R9). Passed as an argument, never
    /// interpolated into a shell — there is no shell.
    /// </param>
    public WikiCommit? CommitAll(string message)
    {
        Execute("add", "-A");

        if (!HasChanges())
        {
            return null;
        }

        // Built from plumbing so that everything the task records about the commit is known
        // before the branch moves: the moment it moves, the commit is a fact of history, and a
        // read-back that failed after that point would leave a commit no task accounts for. The
        // ref update is the last step, and a failure there means no commit exists at all.
        var parent = RevParseHead();
        var tree = Execute("write-tree").Trim();
        var committedAt = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var date = $"@{committedAt.ToUnixTimeSeconds()} +0000";
        var sha = Execute(
            new Dictionary<string, string> { ["GIT_AUTHOR_DATE"] = date, ["GIT_COMMITTER_DATE"] = date },
            "-c", $"user.name={CommitterName}", "-c", $"user.email={CommitterEmail}",
            "commit-tree", tree, "-p", parent, "-m", message).Trim();

        // Compare-and-swap against the parent, as `git commit` does: the tip cannot have moved
        // under the single-writer lock, and if it somehow had, this refuses rather than orphaning it.
        Execute("update-ref", "-m", "commit", "HEAD", sha, parent);
        return new WikiCommit(sha, parent, message, committedAt);
    }

    /// <summary>
    /// Discards everything the working tree holds that the tip does not — a modification, a new
    /// file, a new directory, or a deletion — leaving content byte-identical to the pre-run commit.
    /// Run on every failure path and again at startup (FR-017, contracts/deployment.md Lifecycle).
    /// </summary>
    public void ResetWorkingTree()
    {
        // A revert the process was interrupted in, or whose commit failed, leaves its state behind
        // (REVERT_HEAD, MERGE_MSG) for the next commit to pick up. --quit forgets it, and is a
        // no-op when nothing is in progress.
        Execute("revert", "--quit");
        // The index and the tree back to the tip, without writing any ref: `reset --hard` rewrites
        // the branch ref even when it does not move it, so a ref that cannot be locked — the very
        // thing that makes a commit fail — would make the reset after it fail too, and leave the
        // half-done change in place.
        Execute("read-tree", "--reset", "-u", "HEAD");
        // -x as well as -d and -f: wiki content is exactly what a run wrote, and that can include
        // a .gitignore (write_page accepts any repository-relative path). Without -x, a stray file
        // matching a rule the wiki content itself introduced would survive a reset, breaking the
        // byte-identical-to-the-pre-run-commit guarantee for reasons found nowhere in the diff.
        Execute("clean", "-fdx");
    }

    /// <summary>
    /// Restores the content a commit changed as a <b>new</b> commit. The original stays in
    /// history: nothing is rewritten or discarded (FR-025).
    /// </summary>
    /// <returns>The identity of the restoring commit.</returns>
    public string Revert(string sha)
    {
        Execute("-c", $"user.name={CommitterName}", "-c", $"user.email={CommitterEmail}",
            "revert", "--no-edit", "--no-commit", sha);
        Execute("-c", $"user.name={CommitterName}", "-c", $"user.email={CommitterEmail}",
            "commit", "--no-verify", "--message", $"Revert \"{MessageOf(sha)}\"");
        return RevParseHead();
    }

    /// <summary>
    /// The per-file share of a commit, derived from the commit on read and never stored, so the
    /// artifact cannot drift from history (FR-022).
    /// </summary>
    public IReadOnlyList<FileDiff> DiffOf(string sha)
    {
        // --root so the first commit in a repository diffs against the empty tree rather than failing.
        var status = Execute("show", "--no-color", "--root", "--name-status", "--format=", sha);
        var diffs = new List<FileDiff>();

        foreach (var line in status.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var changeKind = parts[0][0] switch
            {
                'A' => ChangeKind.Added,
                'D' => ChangeKind.Removed,
                _ => ChangeKind.Modified,
            };

            // A rename arrives as "R100\told\tnew"; the new path is the page the operator reads.
            var path = parts[^1];
            var patch = Execute("show", "--no-color", "--root", "--format=", sha, "--", path);
            diffs.Add(new FileDiff(path, changeKind, patch));
        }

        return diffs;
    }

    /// <summary>How many commits the wiki's history holds. Used by tests to assert "exactly one".</summary>
    public int CountCommits() =>
        int.Parse(Execute("rev-list", "--count", "HEAD").Trim(), CultureInfo.InvariantCulture);

    /// <summary>Every commit identity in history, newest first.</summary>
    public IReadOnlyList<string> CommitShas() =>
        Execute("rev-list", "HEAD").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private string MessageOf(string sha) => Execute("show", "--no-patch", "--format=%s", sha).Trim();

    // The hub is the committer of every wiki commit, whatever wrote the files. Identity is passed
    // per invocation so the deployment needs no git configuration on a read-only root filesystem.
    private const string CommitterName = "Grimoire";
    private const string CommitterEmail = "grimoire@localhost";

    private string Execute(params string[] args) => Execute(new Dictionary<string, string>(), args);

    private string Execute(IReadOnlyDictionary<string, string> environment, params string[] args)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = RepositoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var (name, value) in environment)
        {
            info.Environment[name] = value;
        }

        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("git could not be started.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode is not 0)
        {
            throw new GitCommandFailedException(
                $"git {string.Join(' ', args)} exited {process.ExitCode}: {stderr.Trim()}");
        }

        return stdout;
    }
}

/// <summary>A git invocation that did not succeed. Surfaced as a task failure reason (FR-018).</summary>
public sealed class GitCommandFailedException(string message) : Exception(message);
