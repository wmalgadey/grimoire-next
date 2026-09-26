using Grimoire.Agent;
using Grimoire.Agent.Adapters;

namespace Grimoire.Fast.Tests;

/// <summary>
/// What the CLI's lines tell the hub, read against the lines the R-11 spike recorded.
/// </summary>
/// <remarks>
/// This sits at Fast rather than at Contract although the type lives in the agent's adapter
/// folder: it starts no process, opens no file and reads no clock — a line goes in and an event
/// comes out. What the real CLI actually emits is the Contract suite's business (T025); what the
/// hub makes of those lines, once emitted, is this one's.
/// </remarks>
[Trait("level", "fast")]
public sealed class AgentTranscriptTests
{
    private static AgentTranscript Transcript() => new(ToolGrant.Ingest(FastSuite.Clock()));

    [Fact]
    [Trait("req", "GUARD-001")]
    public void Init_ReportsTheAgentIn_WhenTheSurfaceIsTheGrant() =>
        Assert.Equal(TranscriptSays.AgentReportedIn, Transcript().Read(RecordedTranscript.Init).Says);

    [Fact]
    [Trait("req", "GUARD-001")]
    public void Init_ReportsTheAgentIn_WhenTheNamesComeBackWithoutThePrefix()
    {
        // The grant records bare names and the CLI reports prefixed ones; the surface is read out
        // of the line and mapped before it is compared, so either spelling is the same surface.
        var bare = RecordedTranscript.Init.Replace(AgentTranscript.McpPrefix, string.Empty, StringComparison.Ordinal);

        Assert.Equal(TranscriptSays.AgentReportedIn, Transcript().Read(bare).Says);
    }

    [Fact]
    [Trait("req", "GUARD-001")]
    public void Init_RefusesTheSurface_WhenAToolOutsideTheGrantIsReported() =>
        Assert.Equal(
            TranscriptSays.InitIsNotAcceptable,
            Transcript().Read(RecordedTranscript.InitWithAToolOutsideTheGrant).Says);

    [Fact]
    [Trait("req", "GUARD-001")]
    public void Init_RefusesTheSurface_WithoutOneOfTheGrantedNames() =>
        // The grant is the whole surface and not a ceiling on it, so a surface short of it is no
        // more this run's grant than one beyond it.
        Assert.Equal(
            TranscriptSays.InitIsNotAcceptable,
            Transcript().Read(RecordedTranscript.InitOfTheSpikesStub).Says);

    [Fact]
    [Trait("req", "GUARD-001")]
    public void Init_RefusesTheRun_WithoutTheWikiServerConnected()
    {
        Assert.Equal(
            TranscriptSays.InitIsNotAcceptable,
            Transcript().Read(RecordedTranscript.InitWithTheWikiServerDown).Says);

        // What is read is the wiki's own status, not that of every server the CLI lists. A second
        // server down is not this run's wiki down, and a run that refused on it would refuse for a
        // reason GUARD-001 does not give.
        Assert.Equal(
            TranscriptSays.AgentReportedIn,
            Transcript().Read(RecordedTranscript.InitWithASecondServerDown).Says);
    }

    [Fact]
    [Trait("req", "GUARD-001")]
    public void Init_RefusesTheSurface_WhenTheToolsAreNotAllNames()
    {
        // The granted names are all there, and something that is no name is there too. Dropping
        // what cannot be read would leave exactly the grant behind and let the run start on a
        // surface nobody could compare.
        Assert.Equal(
            TranscriptSays.InitIsNotAcceptable,
            Transcript().Read(RecordedTranscript.InitWithAMalformedToolsArray).Says);

        Assert.Equal(
            TranscriptSays.InitIsNotAcceptable,
            Transcript().Read(RecordedTranscript.InitWithAnObjectAmongTheTools).Says);
    }

    [Fact]
    [Trait("req", "GUARD-001")]
    public void Init_RefusesTheSurface_WithoutAToolsArrayAtAll() =>
        // No surface reported is not an empty surface; it is a surface that was never read.
        Assert.Equal(
            TranscriptSays.InitIsNotAcceptable,
            Transcript()
                .Read("""{"type":"system","subtype":"init","mcp_servers":[{"name":"wiki","status":"connected"}],"capabilities":["interrupt_receipt_v1"]}""")
                .Says);

    [Fact]
    [Trait("req", "GUARD-004")]
    public void Init_RefusesTheRun_WithoutAnInterruptToSend() =>
        // A run that cannot be interrupted cannot be stopped at a ceiling, so it does not start.
        Assert.Equal(
            TranscriptSays.InitIsNotAcceptable,
            Transcript().Read(RecordedTranscript.InitWithoutTheInterrupt).Says);

    [Fact]
    [Trait("req", "GUARD-004")]
    public void StreamedUsage_CountsTheHighestTurnTotal_WhenTheUsageGrows()
    {
        var transcript = Transcript();

        var counted = RecordedTranscript.StreamedUsage.Select(line => transcript.Read(line).TokensUsed).ToList();

        // Cumulative within the response: the figure grows to the last line's total and stops
        // there. Summing the three would count the first line's tokens three times over.
        Assert.Equal(RecordedTranscript.StreamedUsageTotals, counted);
    }

    [Fact]
    [Trait("req", "GUARD-004")]
    public void StreamedUsage_CountsTheFourTokenFields()
    {
        const string line =
            """
            {"type":"stream_event","event":{"type":"message_delta","usage":{"input_tokens":1,"output_tokens":20,"cache_read_input_tokens":300,"cache_creation_input_tokens":4000}}}
            """;

        Assert.Equal(4321, Transcript().Read(line).TokensUsed);
    }

    [Fact]
    [Trait("req", "GUARD-004")]
    public void StreamedUsage_CountsNothing_WithoutAUsageOnTheEvent() =>
        Assert.Equal(
            0,
            Transcript().Read("""{"type":"stream_event","event":{"type":"message_start"}}""").TokensUsed);

    [Fact]
    [Trait("req", "GUARD-004")]
    public void Result_CountsEveryModelTheRunTouched() =>
        // The second entry is a call of the CLI's own. A hub counting only the pinned model would
        // have missed 909 tokens the run caused.
        Assert.Equal(
            RecordedTranscript.PinnedModelTotal + RecordedTranscript.BackgroundCallTotal,
            Transcript().Read(RecordedTranscript.Result).TokensUsed);

    [Fact]
    [Trait("req", "GUARD-004")]
    public void Result_CountsThinkingTokensWithinTheOutputTokens() =>
        Assert.Equal(
            RecordedTranscript.OneTurnProbeTotal,
            Transcript().Read(RecordedTranscript.ResultOfTheOneTurnProbe).TokensUsed);

    [Fact]
    [Trait("req", "GUARD-004")]
    public void Result_CountsTheReconciledTotal_AfterTheStreamedFloor()
    {
        var transcript = Transcript();

        foreach (var line in RecordedTranscript.StreamedUsage)
        {
            transcript.Read(line);
        }

        // The streamed figure was a floor for the main model's response alone; the result's
        // modelUsage is the authority and replaces it, background call included.
        Assert.Equal(
            RecordedTranscript.PinnedModelTotal + RecordedTranscript.BackgroundCallTotal,
            transcript.Read(RecordedTranscript.Result).TokensUsed);
    }

    [Fact]
    [Trait("req", "GUARD-004")]
    public void StreamedUsage_CountsNoLessThanBefore_WhenALaterLineReportsLess()
    {
        var transcript = Transcript();

        foreach (var line in RecordedTranscript.StreamedUsage)
        {
            transcript.Read(line);
        }

        // The next response starts its own counter from the bottom. What the run has cost does
        // not go back down with it.
        Assert.Equal(
            RecordedTranscript.StreamedUsageTotals[^1],
            transcript.Read(RecordedTranscript.StreamedUsage[0]).TokensUsed);
    }

    [Fact]
    [Trait("req", "GUARD-004")]
    public void Result_CountsTheSessionTotalAndNotTheSumOfTheTurns()
    {
        var transcript = Transcript();
        transcript.Read(RecordedTranscript.ResultOfTheFirstNudgedTurn);

        // modelUsage is cumulative across the session: the second result carries the whole run,
        // the first turn included. Summing the two results would count the first turn twice —
        // 160 083 for a run that caused 108 989.
        Assert.Equal(
            RecordedTranscript.NudgedSessionTotal,
            transcript.Read(RecordedTranscript.ResultOfTheSecondNudgedTurn).TokensUsed);
    }

    [Fact]
    [Trait("req", "GUARD-004")]
    public void StreamedUsage_CountsTheTurnsBefore_WhenANudgedTurnStreamsFromNothing()
    {
        var transcript = Transcript();
        transcript.Read(RecordedTranscript.ResultOfTheFirstNudgedTurn);

        // The streamed figure is this response's alone and starts at the bottom, so on its own it
        // says 57 895 for a run that has by then caused 108 989. Read as a bare maximum the
        // ceiling would have been blind to a whole turn's worth of tokens until the next result.
        Assert.Equal(
            RecordedTranscript.NudgedSessionTotal,
            transcript.Read(RecordedTranscript.StreamedUsageOfTheSecondNudgedTurn).TokensUsed);
    }

    [Fact]
    [Trait("req", "GUARD-004")]
    public void Result_CountsNoLessThanTheTurnBefore_WhenALaterResultReportsLess()
    {
        var transcript = Transcript();
        transcript.Read(RecordedTranscript.Result);

        // Not a shape the CLI produces in one session — modelUsage only grows — but the counter
        // is what a ceiling rests on, and it does not go back down for any reason.
        Assert.Equal(
            RecordedTranscript.PinnedModelTotal + RecordedTranscript.BackgroundCallTotal,
            transcript.Read(RecordedTranscript.ResultOfTheOneTurnProbe).TokensUsed);
    }

    [Fact]
    [Trait("req", "RUNS-005")]
    public void Result_SaysTheAgentStoppedOfItsOwnAccord_WhenTheTurnSucceeded()
    {
        var said = Transcript().Read(RecordedTranscript.Result);

        Assert.Equal(TranscriptSays.AgentStopped, said.Says);
        Assert.False(said.EndedAbnormally);
    }

    [Fact]
    [Trait("req", "GUARD-004")]
    public void Result_SaysTheAgentDidNotStopOfItsOwnAccord_WhenTheTurnWasInterrupted()
    {
        var said = Transcript().Read(RecordedTranscript.ResultOfAnInterruptedTurn);

        Assert.Equal(TranscriptSays.AgentStopped, said.Says);
        Assert.True(said.EndedAbnormally);
    }

    [Fact]
    [Trait("req", "GUARD-004")]
    public void Result_SaysTheAgentDidNotStopOfItsOwnAccord_WhenTheStreamWasAborted() =>
        // The interrupt is how a ceiling stops a run, and the turn it cut short is not a turn the
        // agent finished — whatever the subtype beside it says. Read the subtype alone and an
        // interrupted run could end done, which GUARD-004 does not allow.
        Assert.True(
            Transcript()
                .Read("""{"type":"result","subtype":"success","terminal_reason":"aborted_streaming"}""")
                .EndedAbnormally);

    [Fact]
    [Trait("req", "GUARD-004")]
    public void Result_SaysTheAgentDidNotStopOfItsOwnAccord_WhenTheSubtypeIsNotSuccess() =>
        Assert.True(
            Transcript()
                .Read("""{"type":"result","subtype":"error_max_turns","terminal_reason":"completed"}""")
                .EndedAbnormally);

    [Fact]
    [Trait("req", "GUARD-004")]
    public void Result_SaysTheAgentDidNotStopOfItsOwnAccord_WhenATerminalFieldCannotBeRead()
    {
        // Present and not a name. Read as absent, this result says the agent finished cleanly,
        // and a run with a log entry and a zero exit would then be done on the strength of a
        // field nobody could read. Absent and unreadable are not the same thing.
        Assert.True(
            Transcript()
                .Read("""{"type":"result","subtype":5,"terminal_reason":"completed"}""")
                .EndedAbnormally);

        Assert.True(
            Transcript()
                .Read("""{"type":"result","subtype":"success","terminal_reason":{"was":"aborted"}}""")
                .EndedAbnormally);
    }

    [Fact]
    [Trait("req", "GUARD-001")]
    public void Init_RefusesTheRun_WhenTheSystemMessageCannotBeToldFromAnInit() =>
        // It may be the init, and there is no way to tell. Passed over, the tool surface is never
        // compared against the grant and the run proceeds unchecked.
        Assert.Equal(
            TranscriptSays.InitIsNotAcceptable,
            Transcript().Read("""{"type":"system","subtype":7}""").Says);

    [Fact]
    public void Line_SaysNothing_WhenItIsNotJson() =>
        Assert.Equal(TranscriptSays.Nothing, Transcript().Read(RecordedTranscript.NotJson).Says);

    [Fact]
    public void Line_SaysNothing_WhenTheHubReadsNothingFromIt() =>
        // A complete `assistant` message whose one block is `thinking`. Measured, it carries an empty
        // string and a signature blob, and there is nothing in it a person reads — so it is no moment
        // of the run and the hub reads nothing from the line at all (research.md R-05).
        Assert.Equal(TranscriptSays.Nothing, Transcript().Read(RecordedTranscript.Thinking).Says);

    [Fact]
    public void Line_SaysNothing_WhenTheSystemMessageIsNotAnInit() =>
        Assert.Equal(
            TranscriptSays.Nothing,
            Transcript().Read("""{"type":"system","subtype":"compact_boundary"}""").Says);
}
