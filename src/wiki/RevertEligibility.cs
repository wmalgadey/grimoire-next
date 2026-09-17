namespace Grimoire.Wiki;

/// <summary>
/// Whether revert is offered on a task, and — when it is not — why (FR-024, FR-027).
/// </summary>
/// <remarks>
/// The reason is as much the point as the answer. A disabled control tells the user nothing; "this
/// was superseded by a later wiki commit" tells them why undo reaches one ingest back and not
/// further.
///
/// The rule is stated over the two facts it actually needs rather than over the task artifact, so
/// it lives in the slice that owns wiki history without that slice having to know what a task is.
/// </remarks>
public static class RevertEligibility
{
    /// <summary>Whether revert is offered, and the reason it is not.</summary>
    /// <param name="Eligible">Whether the task view offers the action.</param>
    /// <param name="Reason">
    /// <see cref="NoCommit"/>, <see cref="Superseded"/>, or <see cref="AlreadyReverted"/>;
    /// <c>null</c> when the action is offered.
    /// </param>
    public sealed record Verdict(bool Eligible, string? Reason);

    /// <summary>The task's run produced no commit — it failed, or it changed nothing.</summary>
    public const string NoCommit = "no-commit";

    /// <summary>Something has committed on top of this task's commit.</summary>
    public const string Superseded = "superseded";

    /// <summary>This task has already been reverted; a second attempt is refused (FR-026).</summary>
    public const string AlreadyReverted = "already-reverted";

    /// <summary>
    /// Revert is offered <b>exactly when</b> the run produced a commit, that commit is the wiki's
    /// current tip, and the task is not already reverted.
    /// </summary>
    /// <param name="runCommitSha">The commit the task's run produced, or <c>null</c> if it made none.</param>
    /// <param name="alreadyReverted">Whether the task is already in the reverted state.</param>
    /// <param name="tip">
    /// The live wiki tip. Checked again at revert time under the single-writer lock, so a
    /// double-click or a second browser tab loses the race and is refused rather than reverting
    /// twice.
    /// </param>
    public static Verdict For(string? runCommitSha, bool alreadyReverted, string tip)
    {
        if (alreadyReverted)
        {
            return new Verdict(false, AlreadyReverted);
        }

        if (runCommitSha is null)
        {
            return new Verdict(false, NoCommit);
        }

        // Because revert is itself a commit, immediately after one no task's commit is the tip:
        // undo reaches one ingest back, not further (data-model "Revert eligibility").
        return runCommitSha == tip
            ? new Verdict(true, null)
            : new Verdict(false, Superseded);
    }
}
