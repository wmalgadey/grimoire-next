using Grimoire.Agent;

namespace Grimoire.Runs;

/// <summary>
/// Everything known when a run begins, written as the record's head (RUNS-008).
/// </summary>
/// <remarks>
/// Written at <see cref="IRunRecord.Begin"/>, which the conductor reaches before the agent's
/// process exists — so a dispatch that never started one still leaves a record
/// (contracts/run-record.md).
/// </remarks>
public sealed record RunFrameHead(
    Guid RunId,
    Guid SubmissionId,
    string Model,
    IReadOnlyList<string> GrantedTools,
    DateTimeOffset GrantRecordedAt,
    Ceilings Ceilings,
    DateTimeOffset StartedAt);

/// <summary>
/// What only the ending knows, written as the record's tail (RUNS-008).
/// </summary>
/// <param name="Ceilings">
/// Both ceilings, so that <see cref="Elapsed"/> and <see cref="TokensUsed"/> are read against what
/// they were held to. A figure without the ceiling beside it says nothing about how close the run
/// came to it.
/// </param>
/// <param name="TokensPerModel">
/// Every entry of the run's <c>modelUsage</c>, so that the user can see <em>which</em> model spent
/// them (DEC-015). Empty for a run that never reached a model call.
/// </param>
public sealed record RunFrameTail(
    Guid RunId,
    DateTimeOffset EndedAt,
    RunOutcome Outcome,
    RunEndedBecause EndedBecause,
    TimeSpan Elapsed,
    long TokensUsed,
    Ceilings Ceilings,
    IReadOnlyDictionary<string, ModelTokens> TokensPerModel);

/// <summary>
/// One thing that happened, at the moment the hub read it (RUNS-009).
/// </summary>
/// <remarks>
/// <see cref="TranscriptMoment"/> is what the adapter saw; this is that moment with the run it
/// belongs to and the instant the hub's clock read. The time is stamped here, by the hub, and not in
/// the adapter: the clock is the hub's (DEC-018), and an adapter that read one for itself would put
/// a record's times outside what the Fast suite can drive.
/// </remarks>
/// <param name="Depth">
/// How deep this moment sits in the record: <c>0</c> opens a section of its own, <c>1</c> sits inside
/// the one above it, <c>2</c> inside that. It is written as the heading level — <c>##</c>, <c>###</c>,
/// <c>####</c> — so the nesting is in the file and therefore in an editor, on GitHub and in the
/// browser alike, rather than being built when the page is drawn (US3).
/// </param>
public sealed record RunMoment(
    Guid RunId,
    DateTimeOffset At,
    RunMomentKind Kind,
    string? Tool,
    string? Content,
    int Depth = 0)
{
    public static RunMoment Of(Guid runId, DateTimeOffset at, TranscriptMoment moment, int depth = 0)
    {
        ArgumentNullException.ThrowIfNull(moment);

        return new RunMoment(runId, at, moment.Kind, moment.Tool, moment.Content, depth);
    }

    /// <summary>What Grimoire told the agent — today only the nudge (RUNS-005, DEC-017).</summary>
    public static RunMoment GrimoireSaid(Guid runId, DateTimeOffset at, string words) =>
        new(runId, at, RunMomentKind.GrimoireSaid, Tool: null, words);
}

/// <summary>
/// Where a run's record is written. The RUNS context's second port, after
/// <see cref="ISubmissionStore"/>; its one adapter is <c>MarkdownRunRecord</c>, and the Fast suite
/// has an in-memory one at the same port (Constitution II.4, III.9, V.2).
/// </summary>
/// <remarks>
/// <para>
/// A new port, <b>not</b> a new external system: the filesystem is already reached by
/// <see cref="Grimoire.Wiki.IWikiStore"/>'s adapter and by the state directory of DEC-023, which is
/// what keeps this feature to one slice addition (Constitution I.6).
/// </para>
/// <para>
/// <b>No delete, no rewrite, no move.</b> The same shape the other two ports have and for the same
/// reason: RUNS-007 says a record is never rewritten or removed, so no member exists that could.
/// </para>
/// <para>
/// <b>There is one read</b>, <see cref="Read"/>, which contracts/run-record.md left off this port on
/// the reasoning that the browser reads the file. It has to be here: the filesystem is an external
/// system and appears only inside an adapter (Constitution V.2), so the endpoint that serves the
/// record cannot open the file itself. Reading is not what RUNS-007 forbids — that sentence was
/// reasoned from a record never changing, which a read does not touch — and the member has a consumer
/// in this same feature, the record endpoint of ACCESS-006 (II.1).
/// </para>
/// <para>
/// <b>Nothing here throws.</b> An IO failure — an unwritable directory, a full disk — is caught by
/// the adapter and counted; <see cref="EntriesLost"/> is how the count is read back for the run's
/// figures. A throw would end the run, which is the opposite of what was decided (RUNS-007,
/// research.md R-10).
/// </para>
/// <para>
/// <b>Synchronous</b>, for the reason DEC-023 gave <see cref="ISubmissionStore"/>: the calls come
/// from the conductor on whatever thread the harness reads on, and the writes underneath are
/// synchronous, so an async signature would promise a yielding call that never yields. Every call
/// returns only once the change is on disk — RUNS-004 covers a stop that gives Grimoire no chance
/// to act, so nothing may be waiting to be written.
/// </para>
/// </remarks>
public interface IRunRecord
{
    /// <summary>The head, once, as the run begins.</summary>
    void Begin(RunFrameHead head);

    /// <summary>One moment, appended after everything already there.</summary>
    void Append(RunMoment moment);

    /// <summary>
    /// The tail, once, where the run ends. A run ends once, so a second call appends nothing.
    /// </summary>
    /// <remarks>
    /// Named for the event rather than the act — <c>End</c> is a keyword in languages this member would
    /// have to be implemented in (CA1716, an error here), and <c>Ended</c> also reads as the board's
    /// own <c>Ended</c> does.
    /// </remarks>
    void Ended(RunFrameTail tail);

    /// <summary>How many of the calls above could not be written for that run.</summary>
    int EntriesLost(Guid runId);

    /// <summary>
    /// The record as it stands, byte for byte, or null where that run has no record at all.
    /// </summary>
    /// <remarks>
    /// The bytes and not a rendering of them: the browser gets exactly what the owner's editor would
    /// get, which is what keeps it a window onto the record rather than a second place the run lives
    /// (ACCESS-006, US3). A run still under way answers with the record as far as it goes — the head
    /// and however many moments have happened, and no tail.
    /// </remarks>
    byte[]? Read(Guid runId);
}
