using System.Globalization;
using System.Text;
using Grimoire.Agent;

namespace Grimoire.Runs;

/// <summary>
/// A record's head, one moment and its tail, rendered to Markdown.
/// </summary>
/// <remarks>
/// <para>
/// Pure: no filesystem, no clock, no run. That is what makes the shape the browser depends on
/// provable in the Fast suite — <c>run.js</c> segments the record by the rule written down in
/// contracts/run-record.md, and a shape nobody wrote down is a shape that changes by accident.
/// </para>
/// <para>
/// <b>The wording here is not tested</b> (Constitution III.8). What is tested is that each part is
/// there, in order, and whole, and that the fence rule holds for any content.
/// </para>
/// </remarks>
public static class RecordText
{
    /// <summary>
    /// The shortest fence CommonMark recognises. A fence is never shorter than this however little
    /// the content holds.
    /// </summary>
    private const int ShortestFence = 3;

    /// <summary>
    /// The head: everything known when the run begins, and <b>no line starting with <c>## </c></b>,
    /// because everything before the first such line is the head (contracts/run-record.md, rule 1).
    /// </summary>
    public static string Head(RunFrameHead head)
    {
        ArgumentNullException.ThrowIfNull(head);

        var text = new StringBuilder();

        text.Append(CultureInfo.InvariantCulture, $"# Run {head.RunId}\n\n");
        text.Append("| | |\n| --- | --- |\n");
        Row(text, "Submission", head.SubmissionId.ToString());
        Row(text, "Model", head.Model);
        Row(text, "Granted tools", string.Join(", ", head.GrantedTools));
        Row(text, "Grant recorded", Moment(head.GrantRecordedAt));
        Row(text, "Time ceiling", Duration(head.Ceilings.Elapsed));
        Row(text, "Cost ceiling", Tokens(head.Ceilings.Tokens));
        Row(text, "Started", Moment(head.StartedAt));

        return text.Append('\n').ToString();
    }

    /// <summary>
    /// One moment, as one segment: a first line that says what it is, and then either a fenced block
    /// or prose (RUNS-009).
    /// </summary>
    /// <remarks>
    /// A call's arguments and a call's result are fenced, because they are not prose and may hold
    /// anything at all. The agent's own text and what Grimoire said are prose and go in as they are:
    /// fencing them would make a record of a run read as a listing of one.
    /// </remarks>
    public static string Moment(RunMoment moment)
    {
        ArgumentNullException.ThrowIfNull(moment);

        var opening = moment.Kind switch
        {
            RunMomentKind.ToolCalled => $"called {moment.Tool}",
            RunMomentKind.ToolReturned => $"{moment.Tool} returned",
            RunMomentKind.AgentSaid => "the agent",
            RunMomentKind.GrimoireSaid => "Grimoire",
            _ => throw new ArgumentOutOfRangeException(nameof(moment), moment.Kind, "not one of the four kinds"),
        };

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"## {Moment(moment.At)} · {opening}\n\n");

        if (moment.Content is not { } content)
        {
            // Refused rather than read around: a result nobody could read is said to be one, and the
            // moment stays in the record (RUNS-009, data-model.md §RunMoment).
            return text.Append("The result could not be read.\n\n").ToString();
        }

        return text
            .Append(moment.Kind is RunMomentKind.ToolCalled or RunMomentKind.ToolReturned
                ? Fenced(content)
                : Prose(content))
            .Append('\n')
            .ToString();
    }

    /// <summary>
    /// The tail: when the run ended, done or failed, why, where it stood against both ceilings, and
    /// the tokens of every model it touched (RUNS-008, DEC-015).
    /// </summary>
    public static string Tail(RunFrameTail tail)
    {
        ArgumentNullException.ThrowIfNull(tail);

        var text = new StringBuilder();
        var outcome = tail.Outcome == RunOutcome.Done ? "done" : "failed";

        text.Append(CultureInfo.InvariantCulture, $"## {Moment(tail.EndedAt)} · ended {outcome} — {Because(tail.EndedBecause)}\n\n");
        text.Append("| | |\n| --- | --- |\n");
        Row(text, "Ended", Moment(tail.EndedAt));
        Row(text, "Elapsed", $"{Duration(tail.Elapsed)} of {Duration(tail.Ceilings.Elapsed)}");
        Row(text, "Tokens", $"{Tokens(tail.TokensUsed)} of {Tokens(tail.Ceilings.Tokens)}");

        // Which model spent them, so that a figure on the row can be read against the models behind
        // it. Ordered by name, so that two records of the same run read the same way.
        foreach (var (model, spent) in tail.TokensPerModel.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            Row(text, model, Tokens(spent.Total));
        }

        return text.Append('\n').ToString();
    }

    /// <summary>
    /// The one line a record gets once a write succeeds again, saying how many entries were lost
    /// before it (RUNS-007). A segment of its own, so that the browser can say lines are missing
    /// where they went missing.
    /// </summary>
    /// <remarks>
    /// Stamped with the time of the write that finally got through, which is the only time the
    /// adapter has: it holds no clock, and the gap is announced where it ends rather than where it
    /// began — the moment the record could speak again.
    /// </remarks>
    public static string EntriesLost(int count, DateTimeOffset at) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"## {Moment(at)} · {count} entries of this run could not be written\n\n");

    /// <summary>
    /// Content inside a fence a run of backticks <b>one longer than the longest run of backticks in
    /// it</b>, and at least three, closed by a run of the same length at column one.
    /// </summary>
    /// <remarks>
    /// CommonMark's own rule: a fence closes only on one at least as long, so a fence longer than
    /// anything inside cannot be closed early. That is what keeps a result containing a fence — which
    /// a run reading wiki pages full of code will produce — unambiguous without altering a byte of it
    /// (research.md R-04). Escaping was the alternative, and it changes what the tool returned, which
    /// is the one thing a record must not do.
    /// </remarks>
    public static string Fenced(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var fence = new string('`', Math.Max(ShortestFence, LongestBacktickRun(content) + 1));

        // A content that does not end in a newline would put the closing fence on its last line,
        // where it is no fence at all.
        var body = content.EndsWith('\n') ? content : content + "\n";

        return $"{fence}\n{body}{fence}\n";
    }

    private static int LongestBacktickRun(string content)
    {
        var longest = 0;
        var run = 0;

        foreach (var character in content)
        {
            run = character == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        return longest;
    }

    /// <summary>Prose, ended with a newline so the next segment starts on its own line.</summary>
    private static string Prose(string content) => content.EndsWith('\n') ? content : content + "\n";

    private static void Row(StringBuilder text, string name, string value) =>
        text.Append(CultureInfo.InvariantCulture, $"| {name} | {value} |\n");

    /// <summary>
    /// UTC to the second, the way the browser's list and the wiki's own <c>generated.at</c> already
    /// read. Nothing in Grimoire needs an offset applied before two times can be compared.
    /// </summary>
    private static string Moment(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    private static string Duration(TimeSpan elapsed) =>
        elapsed.TotalMinutes >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalMinutes} min {elapsed.Seconds} s")
            : string.Create(CultureInfo.InvariantCulture, $"{elapsed.Seconds} s");

    /// <summary>Tokens, grouped, because the ceiling is seven digits long (DEC-015).</summary>
    private static string Tokens(long tokens) => tokens.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>
    /// Why the run ended, as a sentence. Seven values of one requirement, and each is a state the
    /// code already reaches (RUNS-008, Constitution IV.7).
    /// </summary>
    private static string Because(RunEndedBecause because) => because switch
    {
        RunEndedBecause.StoppedWithItsLogEntry => "the agent stopped inside both ceilings, and the log holds its entry",
        RunEndedBecause.StoppedWithoutItsLogEntry => "the agent stopped without its log entry, after being told once",
        RunEndedBecause.TimeCeiling => "the time ceiling was reached",
        RunEndedBecause.CostCeiling => "the cost ceiling was reached",
        RunEndedBecause.ToolsWereNotTheGrant => "the tools the agent reported were not the ones it was granted",
        RunEndedBecause.AgentProcessDied => "the agent's process died",
        RunEndedBecause.GrimoireStopped => "Grimoire was stopped while the run was in progress",
        _ => throw new ArgumentOutOfRangeException(nameof(because), because, "not one of the seven reasons"),
    };
}
