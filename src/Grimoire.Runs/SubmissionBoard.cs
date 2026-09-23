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
/// The submissions and their states, and the whole of the queue rule (RUNS-002).
/// </summary>
/// <remarks>
/// A plain object rather than a port: there is no second implementation and nothing outside the
/// process behind it (Constitution II.4). What is outside the process is where the submissions are
/// kept, and that is a port of its own behind this one.
/// <para>
/// Every judgment about a run is made in this context (plan.md, Structure Decision), which is why
/// the queue rule is here and not in the hub: what dispatches asks and acts, and decides nothing
/// (research.md R-03).
/// </para>
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
    /// before the purpose description, so each refusal names exactly one missing file. What is
    /// under way does not enter into it: a text is accepted whatever else is running and waits its
    /// turn (RUNS-002). Refusing one for the moment was INGEST-005, retired with this feature.
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
            var submission = new Submission(Guid.NewGuid(), text, clock.GetUtcNow(), gate);
            submissions.Add(submission);
            return SubmissionResult.Of(submission);
        }
    }

    /// <summary>
    /// The queue rule, whole: the next submission to run, marked with the run it is being given,
    /// or null where none may start (RUNS-002).
    /// </summary>
    /// <remarks>
    /// Decided under the one lock, which is what makes "at most one run in progress" true against
    /// a race rather than by luck: two callers asking at the same moment — an accepted submission
    /// and a run that has just ended — cannot both be handed one, because the first marks what it
    /// took before the second reads (research.md R-03).
    /// </remarks>
    public Submission? TakeNext(Guid runId)
    {
        lock (gate)
        {
            if (submissions.Exists(s => s.IsUnderWay))
            {
                return null;
            }

            // A failure holds the queue until the user says they have seen it, so that the run
            // behind it does not work on a wiki the failed run left half-written (RUNS-003).
            if (submissions.Exists(s => s.IsUnacknowledgedFailure))
            {
                return null;
            }

            // The first waiting one in the list, which is the order they were accepted in and so
            // the order the user made them in (RUNS-002). Deliberately not the earliest
            // `SubmittedAt`: the clock those come from is not monotonic, and a correction — an NTP
            // step, or the owner putting the machine's time back — would give a later submission
            // an earlier stamp and let it jump the queue. The list is appended to under this same
            // lock, so its order is the acceptance order and nothing can reorder it.
            var next = submissions.Find(s => s.IsWaiting);
            next?.HandedTo(runId);
            return next;
        }
    }

    /// <summary>
    /// The user has acknowledged this submission's failed run, which is the only thing that lifts
    /// the block a failure puts on the queue (RUNS-003).
    /// </summary>
    /// <remarks>
    /// A submission that is not an unacknowledged failure changes nothing — already acknowledged,
    /// never failed, or not one this Grimoire knows. A page loaded before the last run failed can
    /// send exactly that, and the answer to it is that nothing happens (contracts/hub-http-api.md).
    /// </remarks>
    public void Acknowledge(Guid submissionId)
    {
        lock (gate)
        {
            if (Located(submissionId) is { IsUnacknowledgedFailure: true } failure)
            {
                failure.Acknowledged(clock.GetUtcNow());
            }
        }
    }

    /// <summary>
    /// The agent of this submission's run has reported in: submitted becomes running (RUNS-001).
    /// </summary>
    public void ReportedIn(Guid submissionId)
    {
        lock (gate)
        {
            Located(submissionId)?.ReportedIn();
        }
    }

    /// <summary>This submission's run has ended, done or failed (RUNS-001).</summary>
    public void Ended(Guid submissionId, SubmissionState terminal)
    {
        lock (gate)
        {
            Located(submissionId)?.Ended(terminal);
        }
    }

    /// <summary>The submission with this id, or null where there is none.</summary>
    public Submission? Find(Guid id)
    {
        lock (gate)
        {
            return Located(id);
        }
    }

    /// <summary>Assumes the lock: every caller here is already inside it.</summary>
    private Submission? Located(Guid id) => submissions.Find(s => s.Id == id);
}
