using Grimoire.Agent;
using Grimoire.Agent.Adapters;
using Grimoire.Hub.Mcp;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The grant for a question's run: reading anything in the wiki and nothing else, and a door that
/// serves exactly that and nothing else (GUARD-005).
/// </summary>
/// <remarks>
/// <para>
/// The hub's own half of the grant, which is the level GUARD-002's Fast test already sits at
/// (Constitution III.6). That a real run then reports exactly this surface is GUARD-001's existing
/// proof, and that the wiki is byte for byte unchanged after a real question is this feature's E2E
/// half — no new <c>requires=signin</c> test, because R-06 chose the design that needs no fresh
/// evidence from the real CLI (DEC-021).
/// </para>
/// <para>
/// What is asserted is <b>equality</b>, never containment. A surface that is not the grant ends the
/// run failed before its first model call, so "these two and no others" is the claim and a subset
/// check would let a write tool through.
/// </para>
/// <para>
/// <b>What a type declares is not what a route serves</b>, and the difference is not academic: a hub
/// that served all five tools at the question door passed every test in this file, because mapping a
/// second route does not give a second catalogue. What the route serves is read over a real MCP
/// session, which costs seconds and so sits in the Contract suite — <c>WikiToolDoorTests</c>. These
/// stay here because they are the hub's own half of the grant, as GUARD-002's Fast test is.
/// </para>
/// </remarks>
[Trait("level", "fast")]
public sealed class QuestionGrantTests
{
    /// <summary>The three an ingest run has and a question's run must not (GUARD-002, GUARD-005).</summary>
    private static readonly string[] TheWriteTools = ["write_page", "write_index", "append_log"];

    [Fact]
    [Trait("req", "GUARD-005")]
    public void Grant_IsTheTwoReadToolsAndNothingElse()
    {
        var grant = ToolGrant.Question(FastSuite.Clock());

        // Exactly two, in the order the grant records them. Nothing written, nothing deleted, nothing
        // moved — and the port has no call that could delete or move in any case (IWikiStore).
        Assert.Equal(["list_pages", "read_page"], grant.ToolNames);

        foreach (var write in TheWriteTools)
        {
            Assert.DoesNotContain(write, grant.ToolNames);
        }
    }

    [Fact]
    public void Grant_CarriesTheDoorThatServesIt()
    {
        var question = ToolGrant.Question(FastSuite.Clock());
        var ingest = ToolGrant.Ingest(FastSuite.Clock());

        // The endpoint segment sits on the grant beside the names, so the grant and the door that
        // serves it are one value and cannot disagree: a run dispatched at the wrong door would be
        // served a surface that is not its grant, and there would be no single place to read what it
        // should have been (GUARD-001, research.md R-06).
        Assert.Equal("questions", question.Endpoint);
        Assert.Equal("runs", ingest.Endpoint);
    }

    [Fact]
    public void Grant_IsWhatTheQuestionDoorServes()
    {
        // The two halves held against each other: what the grant records, and what the tool type
        // behind `/mcp/questions/{runId}` actually exposes. Drifted apart, a question's run would
        // report a surface that is not its grant and end failed before its first model call — which is
        // GUARD-001 catching our own mistake rather than a promise being kept.
        Assert.Equal(
            ToolGrant.Question(FastSuite.Clock()).ToolNames.Order(),
            WikiReadToolsServer.ServedNames.Order());
    }

    [Fact]
    public void QuestionDoor_ServesNoToolThatWrites()
    {
        // **Not registered at all**, which is the whole of GUARD-005's mechanism: there is no flag
        // that would turn one on and no name that would reach one, because the methods are not on this
        // type. An allow-list of permitted names over a five-tool surface is what DEC-011 rejected,
        // and this is the alternative it asked for (research.md R-06).
        foreach (var write in TheWriteTools)
        {
            Assert.DoesNotContain(write, WikiReadToolsServer.ServedNames);
        }

        // And the ingest door does serve them, so the assertion above is about this type and not about
        // a name nothing in the tree has.
        Assert.Equal(TheWriteTools.Order(), WikiToolsServer.ServedNames.Intersect(TheWriteTools).Order());
    }

    [Fact]
    [Trait("req", "GUARD-005")]
    [Trait("req", "GUARD-001")]
    public void Dispatch_PointsAQuestionsRunAtTheDoorItsGrantNames()
    {
        var question = new AgentDispatch(
            Guid.NewGuid(), Guid.NewGuid(), "a prompt", ToolGrant.Question(FastSuite.Clock()), FastHub.Model);

        var ingest = new AgentDispatch(
            Guid.NewGuid(), Guid.NewGuid(), "a prompt", ToolGrant.Ingest(FastSuite.Clock()), FastHub.Model);

        var somewhere = new Uri("http://127.0.0.1:5057");

        // The address the agent is told to reach its tools at comes **off the grant**. Built from a
        // literal instead, every question's run would connect to the ingest door — served a surface
        // that is not its grant, and so failed before its first model call, or worse reaching tools it
        // was never granted (GUARD-001, GUARD-005).
        Assert.Contains(
            $"/mcp/questions/{question.RunId}",
            string.Join(' ', HarnessProcess.ArgumentsFor(question, somewhere)),
            StringComparison.Ordinal);

        Assert.Contains(
            $"/mcp/runs/{ingest.RunId}",
            string.Join(' ', HarnessProcess.ArgumentsFor(ingest, somewhere)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void QuestionDoor_ServesTheSameTwoReadsTheIngestDoorDoes()
    {
        // Same names, and — because both wrappers call one implementation — same arguments and same
        // answers. Nothing about reading the wiki is different for a question; what is different is
        // that these are all there is (contracts/question-run.md §1).
        Assert.Equal(
            WikiReadToolsServer.ServedNames.Order(),
            WikiToolsServer.ServedNames.Intersect(WikiReadToolsServer.ServedNames).Order());
    }
}
