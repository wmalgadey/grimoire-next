using Grimoire.Wiki.Adapters;
using Microsoft.Extensions.Logging;

namespace Grimoire.Wiki;

/// <summary>
/// The single wiki mutation path (constitution II.1, ADR-0005). Every change to wiki content in
/// the system goes through this type: one commit per run at run end, a working-tree reset on every
/// failure path, revert as a second commit, and the diff read back from history.
/// </summary>
/// <remarks>
/// It holds the single-writer lock. One run executes at a time (FR-019) and revert re-checks the
/// tip under the same lock, so a double-click or a second browser tab loses the race and is
/// refused rather than reverting twice (FR-024).
/// </remarks>
public sealed class WikiMutation(GitCli git, ILogger<WikiMutation> logger)
{
    private readonly SemaphoreSlim _singleWriter = new(1, 1);

    /// <summary>The message a commit gets when the agent's final message was empty.</summary>
    public static string FallbackMessage(string taskId) => $"ingest {taskId}";

    /// <summary>The wiki's current tip.</summary>
    public string Tip() => git.RevParseHead();

    /// <summary>
    /// Commits everything the run wrote as <b>exactly one</b> commit, or reports that it changed
    /// nothing (FR-015, FR-016).
    /// </summary>
    /// <param name="commitMessage">
    /// The run's final assistant message, verbatim — commit-message wording is judgment and lives
    /// in the instruction file (research R9). Empty falls back to the fixed constant.
    /// </param>
    public async Task<WikiCommit?> CommitRun(
        string taskId, string? commitMessage, CancellationToken cancellationToken)
    {
        await _singleWriter.WaitAsync(cancellationToken);
        try
        {
            // Verbatim, per the parameter's own contract: only the blank case is replaced.
            var message = string.IsNullOrWhiteSpace(commitMessage)
                ? FallbackMessage(taskId)
                : commitMessage;

            var commit = git.CommitAll(message);
            if (commit is null)
            {
                return null;
            }

            // What is left is what the commit did not take: files the wiki ignores. They are in no
            // commit and cannot be reverted, so the next run must not find them (FR-017).
            try
            {
                git.ResetWorkingTree();
            }
            catch (GitCommandFailedException exception)
            {
                // The commit is history now; failing here would record the run as failed with no
                // commit and leave one no task accounts for (FR-015, SC-003). The residue is not
                // lost track of either: every dispatch resets the tree before its run starts.
                logger.LogWarning(exception,
                    "The commit {CommitSha} for task {TaskId} stands, but the working tree could not be "
                    + "cleaned after it. The next run's reset will try again.",
                    commit.Sha, taskId);
            }

            return commit;
        }
        finally
        {
            _singleWriter.Release();
        }
    }

    /// <summary>
    /// Whether the working tree differs from the tip — ignored files aside, since no commit would
    /// take them. Decides whether a successful run changed the wiki at all (FR-015, FR-016).
    /// </summary>
    public async Task<bool> HasUncommittedChanges(CancellationToken cancellationToken)
    {
        await _singleWriter.WaitAsync(cancellationToken);
        try
        {
            return git.HasChanges();
        }
        finally
        {
            _singleWriter.Release();
        }
    }

    /// <summary>
    /// Discards whatever the working tree holds that the tip does not, so a failed, crashed,
    /// aborted, or limit-hit run commits nothing and leaves content byte-identical to the pre-run
    /// commit (FR-017). Run on the run-end failure path and again at startup.
    /// </summary>
    public async Task ResetWorkingTree(CancellationToken cancellationToken)
    {
        await _singleWriter.WaitAsync(cancellationToken);
        try
        {
            git.ResetWorkingTree();
        }
        finally
        {
            _singleWriter.Release();
        }
    }

    /// <summary>
    /// Restores the content a commit changed, as a new commit, under the same lock that guards
    /// eligibility — so the tip cannot move between the check and the restoration (FR-025).
    /// </summary>
    /// <param name="expectedTip">
    /// The commit that must still be the tip. A caller that lost the race gets <c>null</c> back
    /// rather than a revert of someone else's work.
    /// </param>
    public async Task<string?> RevertIfStillTip(
        string sha, string expectedTip, CancellationToken cancellationToken)
    {
        await _singleWriter.WaitAsync(cancellationToken);
        try
        {
            if (git.RevParseHead() != expectedTip)
            {
                return null;
            }

            try
            {
                return git.Revert(sha);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // `revert --no-commit` has already rewritten the tree and the index by the time
                // anything after it fails — building the commit, putting it on record, moving the
                // branch — and that half-done revert is exactly what the next run would otherwise
                // start from and sweep into its own commit (constitution II.1).
                git.ResetWorkingTree();

                // Under the lock only this revert moves the tip, so a moved tip means the restoring
                // commit landed and only what came after it failed. It is history now; say so.
                var tip = git.RevParseHead();
                if (tip != expectedTip)
                {
                    return tip;
                }

                throw new WikiRevertFailedException(
                    $"Reverting {sha} failed and the working tree was reset to {expectedTip}.", exception);
            }
        }
        finally
        {
            _singleWriter.Release();
        }
    }

    /// <summary>
    /// The commit exactly as it already exists in history — used only by startup HEAD
    /// reconciliation to attribute a commit it did not make (FR-028).
    /// </summary>
    public WikiCommit CommitAt(string sha) => git.CommitAt(sha);

    /// <summary>
    /// The per-file diff of a commit, derived from history on read and never stored, so the
    /// artifact cannot drift from what the wiki actually contains (FR-022).
    /// </summary>
    public IReadOnlyList<FileDiff> DiffOf(string sha) => git.DiffOf(sha);
}

/// <summary>
/// A revert that git could not complete. The working tree has been reset and the wiki is still at
/// the tip it had before the attempt, so nothing the revert began is left behind (FR-025).
/// </summary>
public sealed class WikiRevertFailedException(string message, Exception innerException)
    : Exception(message, innerException);
