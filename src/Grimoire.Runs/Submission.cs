namespace Grimoire.Runs;

/// <summary>
/// The four states a submission can be in, and no more (RUNS-001). <c>Done</c> and <c>Failed</c>
/// are terminal; there is no transition out of <c>Failed</c> in this feature, because
/// acknowledgement is RUNS-003 and the split holds it back.
/// </summary>
public enum SubmissionState
{
    Submitted,
    Running,
    Done,
    Failed,
}

/// <summary>
/// A text the user handed to Grimoire and that was <em>accepted</em>, together with its state.
/// A refused text never becomes one: nothing about it is stored and it carries no state
/// (INGEST-003, INGEST-004).
/// </summary>
public sealed class Submission
{
    /// <summary>
    /// The board's lock, shared with every submission on it. One lock rather than one per object:
    /// the single-run rule is decided by reading the states of all of them together
    /// (RUNS-002), so a state that could change while that read is under way would let two runs
    /// start at once. The harness reports from whatever thread its adapter reads on, which is why
    /// this is not theoretical.
    /// </summary>
    private readonly Lock gate;

    internal Submission(Guid id, string text, DateTimeOffset submittedAt, Lock gate)
    {
        Id = id;
        Text = text;
        SubmittedAt = submittedAt;
        this.gate = gate;
    }

    public Guid Id { get; }

    /// <summary>
    /// The text as the user gave it. INGEST-004 refuses one that is empty after trimming, but what
    /// survives that is kept whole: the agent receives what the user pasted, not a tidied version
    /// of it.
    /// </summary>
    public string Text { get; }

    public DateTimeOffset SubmittedAt { get; }

    /// <summary>Exactly one state at a time (RUNS-001).</summary>
    public SubmissionState State
    {
        get
        {
            lock (gate)
            {
                return state;
            }
        }
    }

    private SubmissionState state = SubmissionState.Submitted;

    /// <summary>
    /// The agent has reported in — the <c>system/init</c> event of
    /// <c>contracts/agent-cli-protocol.md</c>. This is the boundary the spec draws: a submission
    /// reads <c>Submitted</c> from acceptance until here and <c>Running</c> from here on.
    /// </summary>
    public void AgentReportedIn()
    {
        lock (gate)
        {
            if (state != SubmissionState.Submitted)
            {
                throw new InvalidOperationException($"a submission reading {state} cannot start running");
            }

            state = SubmissionState.Running;
        }
    }

    /// <summary>
    /// The run ended. A run can end before the agent ever reports in — that is where the grant is
    /// checked, and a surface that is not the grant ends the run failed there (GUARD-001).
    /// </summary>
    public void Ended(SubmissionState terminal)
    {
        if (terminal is not (SubmissionState.Done or SubmissionState.Failed))
        {
            throw new ArgumentOutOfRangeException(nameof(terminal), terminal, "a run ends done or failed");
        }

        lock (gate)
        {
            // Guard and write under the one lock: read separately, a report-in and an end racing
            // on a submission still reading Submitted would both pass, and the later write could
            // put a terminal submission back to Running.
            if (state is SubmissionState.Done or SubmissionState.Failed)
            {
                throw new InvalidOperationException($"{state} is terminal; a submission does not leave it");
            }

            state = terminal;
        }
    }
}
