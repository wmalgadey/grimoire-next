using Grimoire.Agent;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The fence rule of contracts/run-record.md, which is what makes a record segmentable whatever a
/// tool returned (RUNS-009).
/// </summary>
/// <remarks>
/// A class of its own because the rendering is a pure function and touches no disk: the shape the
/// browser depends on is provable here, without a filesystem and without a run.
/// </remarks>
[Trait("level", "fast")]
public sealed class RecordTextTests
{
    [Fact]
    [Trait("req", "RUNS-009")]
    public void Fence_IsOneBacktickLongerThanTheLongestRunInTheContent()
    {
        // Three fences inside the result, the longest of them five. CommonMark closes a fence only
        // on one at least as long, so six opens a block nothing inside it can close.
        var result = "```\ncode\n```\n\n`````\nmore\n`````\n";

        var fenced = RecordText.Fenced(result);
        var lines = fenced.Split('\n');

        Assert.Equal(new string('`', 6), lines[0]);
        Assert.Contains(new string('`', 6), lines[^2..]);

        // The same length at both ends, and nothing of the result altered.
        Assert.Equal(lines.First(l => l.Length > 0), lines.Last(l => l.Length > 0));
        Assert.Contains(result.TrimEnd('\n'), fenced, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("req", "RUNS-009")]
    public void Fence_IsThreeBackticks_WhenTheContentHasNone()
    {
        var fenced = RecordText.Fenced("1\t# Probe Notes\n2\tThe answer is forty-two.\n");

        Assert.StartsWith("```\n", fenced, StringComparison.Ordinal);
        Assert.EndsWith("\n```\n", fenced, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("req", "RUNS-009")]
    public void Result_IsNeitherCutNorEscaped()
    {
        // Backticks, a fence, a heading and a run of them — every character comes back as it went in.
        var result = "## Not a heading here\n`inline` and ``double``\n```\nfenced\n```\n";

        var rendered = RecordText.Moment(Returned(result));

        Assert.Contains(result, rendered, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("req", "RUNS-009")]
    public void Result_HoldsNoSegmentBoundary_WhenItStartsALineWithTwoHashes()
    {
        const string heading = "## A heading the tool returned";

        var lines = RecordText.Moment(Returned($"{heading}\ntext\n")).Split('\n');

        // The line is there, unaltered — nothing of a result is escaped. What makes it no boundary is
        // where it is: behind the opening fence and in front of the closing one, so a reader that
        // finds the fence first and skips to its close never sees it as a segment
        // (contracts/run-record.md, rule 4).
        var opening = Array.FindIndex(lines, l => l.StartsWith("```", StringComparison.Ordinal));
        var closing = Array.FindLastIndex(lines, l => l.StartsWith("```", StringComparison.Ordinal));
        var inside = Array.IndexOf(lines, heading);

        Assert.InRange(inside, opening + 1, closing - 1);

        // And the moment's own first line is the one boundary, outside the fence altogether.
        Assert.Equal(0, Array.FindIndex(lines, l => l.StartsWith("## ", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("req", "RUNS-008")]
    public void Head_StartsTheRecordAndOpensNoSegment()
    {
        var head = new RunFrameHead(
            Guid.NewGuid(),
            Guid.NewGuid(),
            FastHub.Model,
            ["list_pages", "read_page"],
            FastSuite.Start,
            Ceilings.Fixed,
            FastSuite.Start);

        var rendered = RecordText.Head(head);

        // Everything before the first `## ` line is the head, so the head may hold none of its own
        // (contracts/run-record.md, rule 1).
        Assert.DoesNotContain(
            rendered.Split('\n'),
            l => l.StartsWith("## ", StringComparison.Ordinal));
        Assert.StartsWith($"# Run {head.RunId}", rendered, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("req", "RUNS-008")]
    public void Tail_HoldsTheFourRawCountsBesideWhatTheyCost()
    {
        // The weighting cannot be undone, so the weighted figure alone would not say what the run
        // actually caused — and 2 000 000 is a placeholder the owner calibrates from exactly these
        // four after the acceptance run (GUARD-004, RUNS-008).
        var tail = new RunFrameTail(
            Guid.NewGuid(),
            FastSuite.Start,
            RunOutcome.Done,
            RunEndedBecause.StoppedWithItsLogEntry,
            TimeSpan.FromMinutes(3),
            CostSpent: 73_676,
            new ModelTokens(41_009, 3_202, 22_016, 7_228),
            Ceilings.Fixed,
            new Dictionary<string, ModelTokens>(StringComparer.Ordinal));

        var rendered = RecordText.Tail(tail);

        Assert.Contains("73,676 of 2,000,000", rendered, StringComparison.Ordinal);
        Assert.Contains("41,009", rendered, StringComparison.Ordinal);
        Assert.Contains("3,202", rendered, StringComparison.Ordinal);
        Assert.Contains("22,016", rendered, StringComparison.Ordinal);
        Assert.Contains("7,228", rendered, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("req", "RUNS-008")]
    public void Tail_HoldsWhatEachModelWasGivenAndProduced()
    {
        // One row per model, in the CLI's own four counts, ordered by name so that two records of
        // the same run read the same way. Found by the mutation measurement: the per-model row was
        // the one part of the tail no test rendered, and both the row and its ordering are
        // decisions this change made (RUNS-008, DEC-015).
        var tail = new RunFrameTail(
            Guid.NewGuid(),
            FastSuite.Start,
            RunOutcome.Done,
            RunEndedBecause.StoppedWithItsLogEntry,
            TimeSpan.FromMinutes(3),
            CostSpent: 73_676,
            new ModelTokens(41_009, 3_202, 22_016, 7_228),
            Ceilings.Fixed,
            new Dictionary<string, ModelTokens>(StringComparer.Ordinal)
            {
                ["claude-opus-4-5-20251101"] = new(40_112, 3_190, 22_016, 7_228),
                ["claude-haiku-4-5-20251001"] = new(897, 12, 0, 0),
            });

        var rendered = RecordText.Tail(tail);

        // The background call the run never asked for is a row of its own, with its own counts:
        // one number for both models would say the Opus row spent what the Haiku one did.
        Assert.Contains("| claude-haiku-4-5-20251001 | 897 in · 12 out ·", rendered, StringComparison.Ordinal);
        Assert.Contains("| claude-opus-4-5-20251101 | 40,112 in · 3,190 out ·", rendered, StringComparison.Ordinal);

        // Haiku before Opus, because the names order that way and not because that model came
        // first. A record ordered by what the dictionary happened to hold would read differently
        // from one written a moment later.
        Assert.True(
            rendered.IndexOf("claude-haiku", StringComparison.Ordinal)
                < rendered.IndexOf("claude-opus", StringComparison.Ordinal),
            "the model rows are not ordered by name");
    }

    [Fact]
    [Trait("req", "RUNS-008")]
    public void Tail_SaysTheTimeWasNotMeasured_WithoutOne()
    {
        // The one ending nobody timed: a run ended by the next start-up, which knows when it began
        // and not when it stopped. The row says so rather than standing a guessed span against the
        // ceiling — the cost beside it was measured and is still read against its own.
        var tail = new RunFrameTail(
            Guid.NewGuid(),
            FastSuite.Start,
            RunOutcome.Failed,
            RunEndedBecause.GrimoireStopped,
            Elapsed: null,
            CostSpent: 12_400,
            new ModelTokens(12_000, 400, 0, 0),
            Ceilings.Fixed,
            new Dictionary<string, ModelTokens>(StringComparer.Ordinal));

        var rendered = RecordText.Tail(tail);

        Assert.Contains("| Elapsed | not measured |", rendered, StringComparison.Ordinal);
        Assert.Contains("12,400", rendered, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("req", "RUNS-007")]
    public void LostEntries_AreOneSegmentOfTheirOwn()
    {
        var rendered = RecordText.EntriesLost(3, FastSuite.Start);

        Assert.StartsWith("## ", rendered, StringComparison.Ordinal);
        Assert.Contains("3", rendered, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "## ")]
    [InlineData(1, "### ")]
    [InlineData(2, "#### ")]
    [Trait("req", "RUNS-009")]
    public void Moment_OpensAtTheHeadingLevelOfItsDepth(int depth, string level)
    {
        var rendered = RecordText.Moment(
            new RunMoment(Guid.NewGuid(), FastSuite.Start, RunMomentKind.AgentSaid, Tool: null, "A word.", depth));

        // The depth is written as the heading level and nowhere else: it is what puts a call inside
        // the turn that explains it and an answer inside its call, in the file and therefore in an
        // editor, on GitHub and in the browser alike (contracts/run-record.md, US3).
        Assert.StartsWith(level, rendered, StringComparison.Ordinal);
        Assert.False(rendered.StartsWith(level + "#", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("req", "RUNS-009")]
    public void Result_NamesTheToolThatReturned()
    {
        var rendered = RecordText.Moment(Returned("what it held"));

        // A segment's first line says what it is, and for a result that is which call returned
        // (contracts/run-record.md, rule 2). `run.js` finds the segment by that shape.
        Assert.StartsWith("## ", rendered, StringComparison.Ordinal);
        Assert.Contains("read_page returned", rendered, StringComparison.Ordinal);
    }

    private static RunMoment Returned(string content) =>
        new(Guid.NewGuid(), FastSuite.Start, RunMomentKind.ToolReturned, "read_page", content);
}
