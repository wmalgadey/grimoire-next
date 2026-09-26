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
    [Trait("req", "RUNS-007")]
    public void LostEntries_AreOneSegmentOfTheirOwn()
    {
        var rendered = RecordText.EntriesLost(3, FastSuite.Start);

        Assert.StartsWith("## ", rendered, StringComparison.Ordinal);
        Assert.Contains("3", rendered, StringComparison.Ordinal);
    }

    private static RunMoment Returned(string content) =>
        new(Guid.NewGuid(), FastSuite.Start, RunMomentKind.ToolReturned, "read_page", content);
}
