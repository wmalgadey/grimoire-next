using Grimoire.Agent;

namespace Grimoire.Runs;

/// <summary>
/// Which of the texts a run receives are in place. All three are read from the paths the hub was
/// started with, per acceptance rather than once at start-up, because the requirements are about the
/// state of those paths when the text is submitted or the question asked.
/// </summary>
/// <param name="InstructionPresent">Grimoire's ingest instruction. Its absence refuses a submission (INGEST-003).</param>
/// <param name="QuestionInstructionPresent">Grimoire's question instruction. Its absence refuses a question (QUERY-003).</param>
/// <param name="PurposeDescriptionPresent">The user's. Its absence refuses both (INGEST-003, QUERY-003).</param>
public sealed record StartUpInputs(
    bool InstructionPresent,
    bool QuestionInstructionPresent,
    bool PurposeDescriptionPresent)
{
    public static readonly StartUpInputs BothPresent = new(
        InstructionPresent: true, QuestionInstructionPresent: true, PurposeDescriptionPresent: true);
}

/// <summary>Why a submission was refused. Each reason names exactly one thing that was wrong.</summary>
public enum Refusal
{
    InstructionMissing,
    PurposeDescriptionMissing,
    TextEmpty,
}

/// <summary>
/// Why a question was refused (QUERY-003). Three of its own rather than <see cref="Refusal"/>'s
/// three: a question is refused on the <em>question</em> instruction, and the browser is told
/// <c>question-empty</c> where a submission is told <c>text-empty</c> — one enum for both would be
/// one value meaning two different things to the page (contracts/hub-http-api.md).
/// </summary>
public enum QuestionRefusal
{
    QuestionInstructionMissing,
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

/// <summary>The answer to a question: it was accepted, or it was refused for one reason.</summary>
public sealed record QuestionResult
{
    private QuestionResult(Question? accepted, QuestionRefusal? refused)
    {
        Accepted = accepted;
        Refused = refused;
    }

    public Question? Accepted { get; }

    public QuestionRefusal? Refused { get; }

    public static QuestionResult Of(Question question) => new(question, null);

    public static QuestionResult RefusedWith(QuestionRefusal refusal) => new(null, refusal);
}

/// <summary>
/// Everything waiting for a run or being worked by one, and the whole of the queue rule (RUNS-002).
/// </summary>
/// <remarks>
/// <para>
/// A plain object rather than a port: there is no second implementation and nothing outside the
/// process behind it (Constitution II.4). What is outside the process is where the submissions are
/// kept, and that is a port of its own behind this one.
/// </para>
/// <para>
/// Every judgment about a run is made in this context (plan.md, Structure Decision), which is why
/// the queue rule is here and not in the hub: what dispatches asks and acts, and decides nothing
/// (research.md R-03).
/// </para>
/// <para>
/// <b>One ordered list of <see cref="Queued"/></b>, of which <see cref="Submission"/> and
/// <see cref="Question"/> are the two kinds. RUNS-002 orders waiting work by when it was made
/// <em>across</em> both kinds — a question asked after a submission waits behind it, and a
/// submission made after a question waits behind that — and one list carries that order
/// intrinsically. Two lists would need a sequence number of our own to order across them, a second
/// ordering mechanism beside the one the list already is; which is the same argument
/// <see cref="TakeNext"/> already makes for list position over a clock that is not monotonic.
/// </para>
/// <para>
/// It was <c>RunBoard</c> until <c>004-ask-the-wiki</c>. A board named for one of the two
/// things it holds would be a name that lies, in a tree whose comments carry the reasons.
/// </para>
/// </remarks>
public sealed class RunBoard(TimeProvider clock, ISubmissionStore store, RunBoard.Changed? changed = null)
{
    /// <summary>
    /// A run for this submission or question, made by whoever knows what a run is given — the hub.
    /// The board asks for one only once it has decided that this one may start, so that deciding,
    /// marking and recording are one step under one lock.
    /// </summary>
    /// <remarks>
    /// It is given the <see cref="Queued"/> itself and not only its identifier, because the grant and
    /// the endpoint that serves it follow from which kind it is — and the hub is where a grant is
    /// decided (GUARD-005, research.md R-06).
    /// </remarks>
    public delegate Run RunForQueued(Queued queued);

    /// <summary>
    /// Something about a submission changed: a state, a figure, or an acknowledgement. The browser
    /// is sent the list again (ACCESS-005).
    /// </summary>
    /// <remarks>
    /// A delegate the composition root supplies, which is the precedent
    /// <c>RunConductor.NextRunMayStart</c> already sets: the RUNS context says something happened
    /// without knowing who listens. One mechanism, followed, rather than an event or an observer
    /// beside it (Constitution II.1, research.md R-05).
    /// <para>
    /// Raised <b>inside</b> the one lock, at every place that already changes something under it. A
    /// list read as one instant is the whole of ACCESS-005, and told outside the lock a subscriber
    /// could be woken by a change and then read a board another change had moved on — or, worse, be
    /// told of the earlier of two changes after the later one. What the delegate does must therefore
    /// not block and must not take the board again; the hub's <c>LiveUpdates.Changed</c> writes a
    /// byte to a channel per subscriber and does neither.
    /// </para>
    /// <para>
    /// It is raised for a question's changes too. The chat draws from the same facts and has to be
    /// sent them at the same moments; which stream a change matters to is the hub's to sort out, and
    /// the board does not know there are two.
    /// </para>
    /// <para>
    /// Optional, and null in the suites that do not read the streams. A board with nobody listening
    /// is what every test of the queue rule needs, and a delegate that has to be supplied would put
    /// a stub in every one of them.
    /// </para>
    /// </remarks>
    public delegate void Changed(Queued queued);

    /// <summary>
    /// One lock for the board and everything on it, so that a state cannot change while the
    /// single-run rule is being decided from those same states. <see cref="Queued"/> takes it at
    /// construction.
    /// </summary>
    private readonly Lock gate = new();
    private readonly List<Queued> queued = [];

    /// <summary>
    /// Every submission the user made, newest first — the order the browser lists them in
    /// (ACCESS-004).
    /// </summary>
    /// <remarks>
    /// <b>The submissions alone.</b> A question is not a submission: it puts nothing into the wiki and
    /// appears in no list of submissions, so the list this answers is exactly what it was before
    /// questions existed (research.md R-12). The chat is where a question is read.
    /// <para>
    /// The submissions themselves, for a caller that wants the objects. What the browser is told is
    /// built from <see cref="Snapshot"/> instead, because these are read one at a time afterwards and
    /// the list the browser reads has to be one instant (ACCESS-005).
    /// </para>
    /// </remarks>
    public IReadOnlyList<Submission> All
    {
        get
        {
            lock (gate)
            {
                return [.. queued.OfType<Submission>().Reverse()];
            }
        }
    }

    /// <summary>
    /// Every submission as it stands at <b>one</b> instant, newest first (ACCESS-005).
    /// </summary>
    /// <remarks>
    /// One pass of the one lock for the whole list, and not <see cref="All"/> followed by a reading of
    /// each submission: those readings are each atomic in themselves, but they are taken one after
    /// another, so a run ending between two of them would make a list that combines two instants —
    /// which is exactly what "read as one instant under the board's one lock" forbids and what the
    /// browser is sent. A delta would break it, and so does reading the whole list a row at a time.
    /// </remarks>
    public IReadOnlyList<SubmissionSnapshot> Snapshot()
    {
        lock (gate)
        {
            return [.. queued.OfType<Submission>().Reverse().Select(SubmissionSnapshot.Of)];
        }
    }

    /// <summary>
    /// Accept a text, or refuse it with the one reason that applies.
    /// </summary>
    /// <remarks>
    /// The order is the contract's: both start-up inputs before the text, and the instruction
    /// before the purpose description, so each refusal names exactly one missing file. What is
    /// under way does not enter into it: a text is accepted whatever else is running and waits its
    /// turn (RUNS-002). Refusing one for the moment was INGEST-005, retired with that feature.
    /// </remarks>
    public SubmissionResult Accept(string text, StartUpInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

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

            queued.Add(submission);
            changed?.Invoke(submission);
            return SubmissionResult.Of(submission);
        }
    }

    /// <summary>
    /// Accept a question, or refuse it with the one reason that applies (QUERY-001, QUERY-003).
    /// </summary>
    /// <remarks>
    /// The same order and the same shape as <see cref="Accept"/>, with the <em>question</em>
    /// instruction in place of the ingest one. <b>Nothing is written to any store</b>: a question's
    /// text, its answer and its steps are kept nowhere (QUERY-005). Its run's row is written when the
    /// board hands it out, which is <see cref="TakeNext"/>.
    /// <para>
    /// There is no refusal for a run being in progress: a question asked while something else runs is
    /// accepted and waits its turn (RUNS-002, ACCESS-007).
    /// </para>
    /// </remarks>
    public QuestionResult Ask(string text, StartUpInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (!inputs.QuestionInstructionPresent)
        {
            return QuestionResult.RefusedWith(QuestionRefusal.QuestionInstructionMissing);
        }

        if (!inputs.PurposeDescriptionPresent)
        {
            return QuestionResult.RefusedWith(QuestionRefusal.PurposeDescriptionMissing);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return QuestionResult.RefusedWith(QuestionRefusal.TextEmpty);
        }

        lock (gate)
        {
            var question = new Question(Guid.NewGuid(), text, clock.GetUtcNow(), gate);

            queued.Add(question);
            changed?.Invoke(question);
            return QuestionResult.Of(question);
        }
    }

    /// <summary>
    /// The queue rule, whole: the next thing to run, marked with the run it is being given, or null
    /// where none may start (RUNS-002).
    /// </summary>
    /// <remarks>
    /// Decided under the one lock, which is what makes "at most one run in progress" true against
    /// a race rather than by luck: two callers asking at the same moment — an accepted submission
    /// and a run that has just ended — cannot both be handed one, because the first marks what it
    /// took before the second reads (research.md R-03).
    /// <para>
    /// It reads the one list, so a question and a submission queue together and neither kind can jump
    /// the other.
    /// </para>
    /// </remarks>
    public Run? TakeNext(RunForQueued newRun)
    {
        ArgumentNullException.ThrowIfNull(newRun);

        lock (gate)
        {
            if (queued.Exists(q => q.IsUnderWay))
            {
                return null;
            }

            // A failure holds the queue until the user says they have seen it, so that the run
            // behind it does not work on a wiki the failed run left half-written (RUNS-003). A failed
            // question holds it exactly as a failed submission does: the reason the block exists is
            // that the user should see what happened before more work is done, and that is as true of
            // a question that got no answer (QUERY-006, research.md R-12).
            if (queued.Exists(q => q.IsUnacknowledgedFailure))
            {
                return null;
            }

            // The first waiting one in the list, which is the order they were accepted in and so
            // the order the user made them in, across both kinds (RUNS-002). Deliberately not the
            // earliest `SubmittedAt` or `AskedAt`: the clock those come from is not monotonic, and a
            // correction — an NTP step, or the owner putting the machine's time back — would give a
            // later one an earlier stamp and let it jump the queue. The list is appended to under this
            // same lock, so its order is the acceptance order and nothing can reorder it.
            if (queued.Find(q => q.IsWaiting) is not { } next)
            {
                return null;
            }

            var run = newRun(next);

            // On disk before it is marked, for the reason `Accept` writes before it adds: a write
            // that fails must leave the queue as it was. Marked first, a failed write would leave
            // something that every later `TakeNext` reads as under way and no dispatch ever ends —
            // the queue stranded on a run that does not exist.
            //
            // Which of the two writes depends on whether there is a submission row to hang the run
            // on. A question has none — nothing of it is on disk — so its run is written on its own,
            // with a null where the submission would be (RUNS-006, QUERY-005, research.md R-04).
            if (next is Submission)
            {
                store.AssignRun(next.Id, StoredRun.Of(run));
            }
            else
            {
                store.AddRun(StoredRun.Of(run));
            }

            next.HandedTo(run.Id, run.Model);
            changed?.Invoke(next);
            return run;
        }
    }

    /// <summary>The text this run is to be given, or null where the queued thing is gone.</summary>
    public string? TextOf(Guid queuedId)
    {
        lock (gate)
        {
            return Located(queuedId) switch
            {
                Submission submission => submission.Text,
                Question question => question.Text,
                _ => null,
            };
        }
    }

    /// <summary>
    /// Which process this run's agent is (RUNS-006). Recorded against the run, because that is what a
    /// start-up reads it back for — and it is recorded for a question's run too, which is the whole
    /// reason that run has a row at all (research.md R-04).
    /// </summary>
    public void AgentProcessIs(Guid queuedId, AgentProcessIdentity identity)
    {
        lock (gate)
        {
            if (Located(queuedId)?.RunId is { } run)
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
    /// <para>
    /// <b>No question comes back.</b> A chat does not survive Grimoire stopping (QUERY-005), so there
    /// is nothing to restore one into; what a question's run leaves on disk is dealt with by the
    /// composition root, which ends it failed without putting anything on the board.
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
                queued.Add(submission);

                if (held.WasUnderWay)
                {
                    submission.Ended(RunOutcome.Failed, RunEndedBecause.GrimoireStopped);
                    store.SetState(held.Id, SubmissionState.Failed);
                }

                // Said per submission rather than once at the end, so that the delegate is always
                // given the thing that changed and never a stand-in for "the board". Nobody is
                // listening yet in any case: this runs before the hub is serving anything, and a
                // stream that opens afterwards opens with the whole list (research.md R-01).
                changed?.Invoke(submission);
            }
        }
    }

    /// <summary>
    /// The user has acknowledged this failed run, which is the only thing that lifts the block a
    /// failure puts on the queue (RUNS-003).
    /// </summary>
    /// <remarks>
    /// Something that is not an unacknowledged failure changes nothing — already acknowledged, never
    /// failed, or not one this Grimoire knows. A page loaded before the last run failed can send
    /// exactly that, and the answer to it is that nothing happens (contracts/hub-http-api.md).
    /// </remarks>
    public void Acknowledge(Guid queuedId)
    {
        lock (gate)
        {
            if (Located(queuedId) is { IsUnacknowledgedFailure: true } failure)
            {
                var at = clock.GetUtcNow();
                failure.Acknowledged(at);

                // On disk before the queue moves, so that a restart does not re-block a queue the
                // user has already cleared (RUNS-003, RUNS-004). A question has no row to write it
                // to, and needs none: nothing of a chat survives a stop, so there is no block for a
                // restart to rebuild (QUERY-005).
                if (failure is Submission)
                {
                    store.Acknowledge(queuedId, at);
                }

                changed?.Invoke(failure);
            }
        }
    }

    /// <summary>
    /// The agent of this submission's run has reported in: submitted becomes running (RUNS-001).
    /// </summary>
    /// <remarks>
    /// A submission's only. A question shows that its answer is forming from the agent's first word
    /// rather than from <c>system/init</c>, so there is no state for this to move (ACCESS-007).
    /// </remarks>
    public void ReportedIn(Guid queuedId)
    {
        lock (gate)
        {
            if (Located(queuedId) is not Submission submission)
            {
                return;
            }

            submission.ReportedIn();
            store.SetState(queuedId, SubmissionState.Running);
            changed?.Invoke(submission);
        }
    }

    /// <summary>
    /// This run has ended, done or failed, with the reason and the figures it ended on (RUNS-001,
    /// RUNS-008, RUNS-010, QUERY-006).
    /// </summary>
    /// <remarks>
    /// The terminal state and the final figures are written in <b>one</b> pass of the one lock. Written
    /// as two — the figures, then the state — the list would be sent twice, once with <c>running</c>
    /// beside a final figure, and a tail whose write failed would raise the count of lost entries on a
    /// row still reading <c>running</c>. ACCESS-005 has the state and the figures read as one instant,
    /// and a reading is only as atomic as the writing behind it.
    /// </remarks>
    public void Ended(
        Guid queuedId,
        RunOutcome outcome,
        RunEndedBecause because,
        long costSpent,
        ModelTokens tokens,
        int toolCalls,
        int entriesLost)
    {
        lock (gate)
        {
            if (Located(queuedId) is not { } ending)
            {
                return;
            }

            // The ending first, because it is the one that refuses: something already done or failed
            // throws here, and it must throw before anything else about it has been changed.
            ending.Ended(outcome, because);
            ending.FiguresAre(costSpent, tokens, toolCalls, entriesLost);

            var terminal = outcome == RunOutcome.Done ? SubmissionState.Done : SubmissionState.Failed;

            // One change on disk, too. Written as a state and then a figure, a stop between the two
            // would leave something reading done or failed beside the figures it had one moment
            // earlier — and RUNS-010 has the final figures survive exactly that stop.
            if (ending.RunId is { } run)
            {
                // A question has no submission state to set, so its run's figures are written keyed by
                // the run itself. The row still has to be written: RUNS-010 keeps a run's final
                // figures whatever caused it, and the chat reads its cost from there (DEC-030).
                if (ending is Submission)
                {
                    store.Ended(queuedId, terminal, run, costSpent, tokens, toolCalls, entriesLost);
                }
                else
                {
                    store.RunEnded(run, costSpent, tokens, toolCalls, entriesLost);
                }
            }
            else if (ending is Submission)
            {
                // Something that ends without ever having had a run has no figures to write. Nothing
                // reaches this today — the board only ends what it handed out — and the state still has
                // to be recorded if anything ever does.
                store.SetState(queuedId, terminal);
            }

            changed?.Invoke(ending);
        }
    }

    /// <summary>
    /// The figures of this run as they now stand (RUNS-010).
    /// </summary>
    /// <remarks>
    /// Written under the one lock, as every other change is, and <b>only where one of them has actually
    /// risen</b>: the cost is reported on every streamed line, most of which change nothing, and a
    /// store written sixty times a turn to record the same three numbers would be sixty writes with no
    /// reader (research.md R-06).
    /// </remarks>
    public void RunFiguresAre(Guid queuedId, long costSpent, ModelTokens tokens, int toolCalls, int entriesLost)
    {
        lock (gate)
        {
            // Nothing after the ending. The conductor reads a run and then reports on it in two steps,
            // so a tool call racing a stop can arrive here after the final figures were published —
            // and it would raise the count past the run's last snapshot, to a figure that is in no
            // record, because the moment behind it was dropped for arriving after the tail. RUNS-010
            // has the figures stand as the run's final ones once it has ended, and this is where a
            // terminal one is known.
            if (Located(queuedId) is not { IsUnderWay: true, RunId: { } run } working)
            {
                return;
            }

            if (working.FiguresAre(costSpent, tokens, toolCalls, entriesLost))
            {
                store.RecordFigures(run, costSpent, tokens, toolCalls, entriesLost);
                changed?.Invoke(working);
            }
        }
    }

    /// <summary>The submission or question with this id, or null where there is none.</summary>
    public Queued? Find(Guid id)
    {
        lock (gate)
        {
            return Located(id);
        }
    }

    /// <summary>Assumes the lock: every caller here is already inside it.</summary>
    private Queued? Located(Guid id) => queued.Find(q => q.Id == id);
}
