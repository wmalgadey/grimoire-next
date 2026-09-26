using Grimoire.Agent;

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
public sealed class SubmissionBoard(TimeProvider clock, ISubmissionStore store)
{
    /// <summary>
    /// A run for this submission, made by whoever knows what a run is given — the hub. The board
    /// asks for one only once it has decided that this submission may start, so that deciding,
    /// marking and recording are one step under one lock.
    /// </summary>
    public delegate Run RunForSubmission(Guid submissionId);

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

            // On disk before it is on the board, and so before the user is answered: a user told
            // their text was accepted and then losing power finds it after the restart (RUNS-004).
            // The order matters the other way too — a write that fails must leave no submission
            // behind that a pump could hand out and a restart would not find.
            store.Add(new StoredSubmission(
                submission.Id,
                submission.Text,
                submission.SubmittedAt,
                SubmissionState.Submitted,
                Run: null,
                AcknowledgedAt: null));

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
    public Run? TakeNext(RunForSubmission newRun)
    {
        ArgumentNullException.ThrowIfNull(newRun);

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
            if (submissions.Find(s => s.IsWaiting) is not { } next)
            {
                return null;
            }

            var run = newRun(next.Id);

            // On disk before the submission is marked, for the reason `Accept` writes before it
            // adds: a write that fails must leave the queue as it was. Marked first, a failed
            // write would leave a submission that every later `TakeNext` reads as under way and
            // no dispatch ever ends — the queue stranded on a run that does not exist.
            store.AssignRun(next.Id, StoredRun.Of(run));

            next.HandedTo(run.Id, run.Model);
            return run;
        }
    }

    /// <summary>The text this run is to be given, or null where the submission is gone.</summary>
    public string? TextOf(Guid submissionId)
    {
        lock (gate)
        {
            return Located(submissionId)?.Text;
        }
    }

    /// <summary>
    /// Which process this submission's agent is (RUNS-006). Recorded against the run, because that
    /// is what a start-up reads it back for.
    /// </summary>
    public void AgentProcessIs(Guid submissionId, AgentProcessIdentity identity)
    {
        lock (gate)
        {
            if (Located(submissionId)?.RunId is { } run)
            {
                store.RecordAgentProcess(run, identity);
            }
        }
    }

    /// <summary>
    /// What the store held, made into submissions and their states again (RUNS-004).
    /// </summary>
    /// <remarks>
    /// A submission with a run and a state that is not terminal was in progress when Grimoire
    /// stopped, and reads <c>failed</c> — which then holds the queue until the user acknowledges
    /// it, exactly as a failure that happened while Grimoire was running does (RUNS-003). Nothing
    /// is resumed and nothing is retried, and everything such a run had already written stays in
    /// the wiki (WIKI-003).
    /// <para>
    /// The agents of those runs are terminated <b>before</b> this is called: the browser must never
    /// show failed while the agent is still at work, and no second run may begin beside a first
    /// that is still writing (RUNS-006, research.md R-11).
    /// </para>
    /// </remarks>
    public void Restore(IReadOnlyList<StoredSubmission> stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        lock (gate)
        {
            // In the order the store hands them back, which the port promises is the order they
            // were accepted (ISubmissionStore.Load). Sorting them again here by `SubmittedAt`
            // would throw exactly that away: the clock is not monotonic, and a correction between
            // two submissions would rebuild the queue in an order the last Grimoire never had.
            foreach (var held in stored)
            {
                var submission = new Submission(held.Id, held.Text, held.SubmittedAt, gate);
                submission.Restored(held);
                submissions.Add(submission);

                if (held.WasUnderWay)
                {
                    submission.Ended(SubmissionState.Failed);
                    store.SetState(held.Id, SubmissionState.Failed);
                }
            }
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
                var at = clock.GetUtcNow();
                failure.Acknowledged(at);

                // On disk before the queue moves, so that a restart does not re-block a queue the
                // user has already cleared (RUNS-003, RUNS-004).
                store.Acknowledge(submissionId, at);
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
            if (Located(submissionId) is not { } submission)
            {
                return;
            }

            submission.ReportedIn();
            store.SetState(submissionId, SubmissionState.Running);
        }
    }

    /// <summary>This submission's run has ended, done or failed (RUNS-001).</summary>
    public void Ended(Guid submissionId, SubmissionState terminal)
    {
        lock (gate)
        {
            if (Located(submissionId) is not { } submission)
            {
                return;
            }

            submission.Ended(terminal);
            store.SetState(submissionId, terminal);
        }
    }

    /// <summary>
    /// The figures of this submission's run as they now stand (RUNS-010).
    /// </summary>
    /// <remarks>
    /// Written under the one lock, as every other change to a submission is, and <b>only where one of
    /// them has actually risen</b>: the cost is reported on every streamed line, most of which change
    /// nothing, and a store written sixty times a turn to record the same three numbers would be sixty
    /// writes with no reader (research.md R-06).
    /// </remarks>
    public void RunFiguresAre(Guid submissionId, long tokensUsed, int toolCalls, int entriesLost)
    {
        lock (gate)
        {
            if (Located(submissionId) is not { RunId: { } run } submission)
            {
                return;
            }

            if (submission.FiguresAre(tokensUsed, toolCalls, entriesLost))
            {
                store.RecordFigures(run, tokensUsed, toolCalls, entriesLost);
            }
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
