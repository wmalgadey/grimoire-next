using Grimoire.Wiki.Adapters;

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
public sealed class WikiMutation(GitCli git)
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
    public async Task<WikiCommit?> CommitRun(string taskId, string? commitMessage, CancellationToken cancellationToken)
    {
        await _singleWriter.WaitAsync(cancellationToken);
        try
        {
            var message = string.IsNullOrWhiteSpace(commitMessage)
                ? FallbackMessage(taskId)
                : commitMessage.Trim();

            return git.CommitAll(message);
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
    public async Task<string?> RevertIfStillTip(string sha, string expectedTip, CancellationToken cancellationToken)
    {
        await _singleWriter.WaitAsync(cancellationToken);
        try
        {
            return git.RevParseHead() != expectedTip ? null : git.Revert(sha);
        }
        finally
        {
            _singleWriter.Release();
        }
    }

    /// <summary>
    /// The per-file diff of a commit, derived from history on read and never stored, so the
    /// artifact cannot drift from what the wiki actually contains (FR-022).
    /// </summary>
    public IReadOnlyList<FileDiff> DiffOf(string sha) => git.DiffOf(sha);
}
