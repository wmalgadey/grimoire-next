using Grimoire.Agent;
using Grimoire.Agent.Adapters;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// What a run did, in the order it happened: each tool call with its arguments, what it returned
/// whole, the agent's own text, and what Grimoire said (RUNS-009).
/// </summary>
/// <remarks>
/// Driven from the lines the spike recorded, so no process and no sign-in is needed — which is why no
/// test of this feature carries <c>requires=signin</c> and DEC-021's budget of three is untouched
/// (research.md R-03, R-11).
/// </remarks>
[Trait("level", "fast")]
[Trait("req", "RUNS-009")]
public sealed class RunNarrativeTests
{
    private readonly FastHub hub = new();

    private static AgentTranscript Transcript() => new(ToolGrant.Ingest(FastSuite.Clock()));

    [Fact]
    public void ToolCall_IsReadWithItsNameAndItsArguments()
    {
        var read = Transcript().Read(RecordedTranscript.ToolCall);

        Assert.Equal(TranscriptSays.MomentsHappened, read.Says);

        var moment = Assert.Single(read.Moments);
        Assert.Equal(RunMomentKind.ToolCalled, moment.Kind);
        Assert.Equal("Read", moment.Tool);
        Assert.Contains("notes.md", moment.Content!, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolResult_IsReadWhole()
    {
        var transcript = Transcript();
        transcript.Read(RecordedTranscript.ToolCall);

        var moment = Assert.Single(transcript.Read(RecordedTranscript.ToolResult).Moments);

        Assert.Equal(RunMomentKind.ToolReturned, moment.Kind);

        // Byte for byte, tabs and newlines included. Nothing is cut and nothing is escaped (RUNS-009,
        // the owner's decision in the spec's Clarifications).
        Assert.Equal(RecordedTranscript.ToolResultContent, moment.Content);

        // And it says which call returned, from the call before it. A run makes one call at a time in
        // the order the stream reports it, so no identifier has to be in the record.
        Assert.Equal("Read", moment.Tool);
    }

    [Fact]
    public void AgentText_IsReadAsTheAgentsOwn()
    {
        var moment = Assert.Single(Transcript().Read(RecordedTranscript.AgentText).Moments);

        Assert.Equal(RunMomentKind.AgentSaid, moment.Kind);
        Assert.Equal(RecordedTranscript.AgentTextContent, moment.Content);
    }

    [Fact]
    public void Blocks_AreEachAMomentInTheOrderTheyArrived()
    {
        // `message.content` is an array and the API allows several blocks. Each is its own moment, and
        // the order is the order they came in.
        var read = Transcript().Read(RecordedTranscript.TextThenToolCall);

        Assert.Equal(
            [RunMomentKind.AgentSaid, RunMomentKind.ToolCalled],
            read.Moments.Select(m => m.Kind));
    }

    [Fact]
    public void ToolResult_IsTheTextOfItsBlocks_WhenTheContentIsAnArray()
    {
        var moment = Assert.Single(Transcript().Read(RecordedTranscript.ToolResultInBlocks).Moments);

        Assert.Equal("# Probe Notes\nforty-two", moment.Content);
    }

    [Fact]
    public void ToolResult_IsRecordedAsUnreadable_WhenItIsNeitherAStringNorBlocks()
    {
        var moment = Assert.Single(Transcript().Read(RecordedTranscript.ToolResultThatCannotBeRead).Moments);

        // Refused rather than read around, and never dropped: a result nobody could read must not pass
        // for a call that returned nothing (GUARD-001's precedent).
        Assert.Equal(RunMomentKind.ToolReturned, moment.Kind);
        Assert.Null(moment.Content);
        Assert.Contains("could not be read", RecordText.Moment(AtNoon(moment)), StringComparison.Ordinal);
    }

    [Fact]
    public void Thinking_IsNotAMoment() =>
        // Measured: an empty `thinking` and a signature blob. RUNS-009 asks for the agent's own text,
        // and a signature is not text (research.md R-05).
        Assert.Empty(Transcript().Read(RecordedTranscript.Thinking).Moments);

    [Fact]
    [Trait("req", "RUNS-005")]
    public async Task Record_HoldsWhatTheRunDid_InTheOrderItHappened()
    {
        var submission = await hub.AcceptedAsync();
        var run = hub.Conductor.Of(submission.Id)!;

        hub.Harness.ReportIn(submission.Id);

        foreach (var line in new[]
            {
                RecordedTranscript.ToolCall,
                RecordedTranscript.ToolResult,
                RecordedTranscript.AgentText,
            })
        {
            foreach (var moment in Transcript().Read(line).Moments)
            {
                hub.Harness.Did(submission.Id, moment);
            }
        }

        // The agent stops with no log entry, so Grimoire tells it once — and what Grimoire said is in
        // the record too, appended by the hub that knows it nudged (RUNS-005, DEC-017).
        await hub.Harness.StoppedAsync(submission.Id);

        var moments = hub.Record.MomentsOf(run.Id);

        Assert.Equal(
            [RunMomentKind.ToolCalled, RunMomentKind.ToolReturned, RunMomentKind.AgentSaid, RunMomentKind.GrimoireSaid],
            moments.Select(m => m.Kind));

        Assert.Equal(RecordedTranscript.ToolResultContent, moments[1].Content);
        Assert.Equal(IAgentHarness.LogEntryMissing, moments[3].Content);

        // Once, and only once. The CLI does not echo what Grimoire writes to its stdin, so nothing
        // reads the nudge back (research.md R-03).
        Assert.Single(moments, m => m.Kind == RunMomentKind.GrimoireSaid);
    }

    private static RunMoment AtNoon(TranscriptMoment moment) =>
        RunMoment.Of(Guid.NewGuid(), FastSuite.Start, moment);
}
