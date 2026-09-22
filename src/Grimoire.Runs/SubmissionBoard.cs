namespace Grimoire.Runs;

/// <summary>
/// Which of the two texts every run receives are in place. Both are read from the paths the hub
/// was started with, and the absence of either refuses every submission (INGEST-003).
/// </summary>
public sealed record StartUpInputs(bool InstructionPresent, bool PurposeDescriptionPresent)
{
    public static readonly StartUpInputs BothPresent = new(InstructionPresent: true, PurposeDescriptionPresent: true);
}

/// <summary>Why a submission was refused. Each reason names exactly one thing that was wrong.</summary>
public enum Refusal
{
    InstructionMissing,
    PurposeDescriptionMissing,
    TextEmpty,
    RunInProgress,
}

/// <summary>The answer to a submission: it was accepted, or it was refused for one reason.</summary>
public sealed record SubmissionResult
{
    private SubmissionResult(Submission? accepted, Refusal? refused)
    {
        Accepted = accepted;
        Refused = refused;
    }

    public Submission? Accepted { get; }

    public Refusal? Refused { get; }

    public static SubmissionResult Of(Submission submission) => new(submission, null);

    public static SubmissionResult RefusedWith(Refusal refusal) => new(null, refusal);
}

/// <summary>
/// The submissions and their states, for as long as the process runs.
/// </summary>
/// <remarks>
/// A plain object: no interface, no port, no store. There is no second implementation and nothing
/// outside the process behind it (Constitution II.4), and a store would have no consumer here —
/// surviving a restart is RUNS-004, which the split moved to the follow-up feature (research.md
/// R-08). A stop loses everything here, which the spec says and the owner accepted.
/// </remarks>
public sealed class SubmissionBoard(TimeProvider clock)
{
    /// <summary>
    /// One lock for the board and every submission on it, so that a state cannot change while the
    /// single-run rule is being decided from those same states. <see cref="Submission"/> takes
    /// it at construction.
    /// </summary>
    private readonly Lock gate = new();
    private readonly List<Submission> submissions = [];

    /// <summary>Every submission the user made, newest first — the order the browser lists them in.</summary>
    public IReadOnlyList<Submission> All
    {
        get
        {
            lock (gate)
            {
                return [.. Enumerable.Reverse(submissions)];
            }
        }
    }

    /// <summary>
    /// Accept a text, or refuse it with the one reason that applies.
    /// </summary>
    /// <remarks>
    /// The order is the contract's: both start-up inputs before the text, and the instruction
    /// before the purpose description, so each refusal names exactly one missing file. The text
    /// is judged before the moment — a request that is wrong is told so, rather than being told
    /// to come back later and then refused again (contracts/hub-http-api.md).
    /// </remarks>
    public SubmissionResult Accept(string text, StartUpInputs inputs)
    {
        if (!inputs.InstructionPresent)
        {
            return SubmissionResult.RefusedWith(Refusal.InstructionMissing);
        }

        if (!inputs.PurposeDescriptionPresent)
        {
            return SubmissionResult.RefusedWith(Refusal.PurposeDescriptionMissing);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return SubmissionResult.RefusedWith(Refusal.TextEmpty);
        }

        lock (gate)
        {
            if (submissions.Exists(s => s.State is SubmissionState.Submitted or SubmissionState.Running))
            {
                return SubmissionResult.RefusedWith(Refusal.RunInProgress);
            }

            var submission = new Submission(Guid.NewGuid(), text, clock.GetUtcNow(), gate);
            submissions.Add(submission);
            return SubmissionResult.Of(submission);
        }
    }

    /// <summary>The submission with this id, or null where there is none.</summary>
    public Submission? Find(Guid id)
    {
        lock (gate)
        {
            return submissions.Find(s => s.Id == id);
        }
    }
}
