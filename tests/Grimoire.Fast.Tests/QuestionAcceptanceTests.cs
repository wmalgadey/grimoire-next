using Grimoire.Agent;
using Grimoire.Hub.Api;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// A question is accepted and the user is answered with the turn straight away — before its run has
/// produced any part of the answer — and a question asked while something else runs is accepted too
/// (QUERY-001).
/// </summary>
/// <remarks>
/// <para>
/// The answer to asking is <see cref="ChatTurnView"/>, the same shape the chat stream's snapshot
/// carries, which is what <c>contracts/hub-http-api.md</c> promises the body of an accepted question
/// is. It is built here from the accepted question's own turn exactly as the endpoint builds it, with
/// no server in the way: what is being proven is that a turn exists to hand back and that nothing of
/// an answer is in it, not that ASP.NET can serialise it (Constitution III.8).
/// </para>
/// <para>
/// There is no refusal for a run being in progress, which is the second half of this file: a question
/// asked while something else runs waits its turn rather than being turned away (RUNS-002).
/// </para>
/// </remarks>
[Trait("level", "fast")]
public sealed class QuestionAcceptanceTests
{
    private const string Text = "What does the wiki say about Ada Lovelace?";

    private readonly FastHub hub = new();

    [Fact]
    [Trait("req", "QUERY-001")]
    public async Task Ask_IsAnsweredWithTheTurn_WhileItsRunHasProducedNothing()
    {
        var question = await hub.AskedAsync(Text);

        var answered = Handed(question);

        // What was asked, and when — the question's own, because the turn the user is handed is the
        // turn every browser reading the chat has (ACCESS-007).
        Assert.Equal(question.Id.ToString(), answered.Id);
        Assert.Equal(Text, answered.Text);
        Assert.Equal(question.AskedAt, answered.AskedAt);

        // Under way or waiting its turn, and nothing else: the two states a question that has just been
        // accepted can be in.
        Assert.Contains(answered.State, (string[])["waiting", "answering"]);

        // And nothing of an answer. Not a reason either, which would be a reason for nothing, and no
        // acknowledgement to offer.
        Assert.Equal(string.Empty, answered.Answer);
        Assert.Empty(answered.Steps);
        Assert.Null(answered.Because);
        Assert.Null(answered.AwaitingAcknowledgement);
    }

    [Fact]
    [Trait("req", "QUERY-001")]
    public async Task Ask_WaitsForNoPartOfTheAnswer_WhileTheRunProducesIt()
    {
        var question = await hub.AskedAsync(Text);

        // The user has their turn already, and the run that will answer is only now under way.
        var answered = Handed(question);

        Assert.Contains(question.RunId!.Value, hub.Harness.Dispatched.Select(dispatch => dispatch.RunId));

        hub.Harness.Did(question.Id, new TranscriptMoment(RunMomentKind.AgentSaid, null, "She wrote the first program."));
        hub.Harness.Called(question.Id, "read_page", """{"path":"people/ada-lovelace.md"}""");

        // The answer forms in the chat afterwards, and none of it was waited for: what the user was
        // given holds no part of it (QUERY-001).
        Assert.Equal(string.Empty, answered.Answer);
        Assert.Empty(answered.Steps);
        Assert.Equal("She wrote the first program.", Assert.Single(hub.Chat.Turns).Answer);
    }

    [Fact]
    [Trait("req", "QUERY-001")]
    public async Task Ask_IsAccepted_WhileSomethingElseRuns()
    {
        await hub.AcceptedAsync("Ada Lovelace wrote the first program.");

        var result = await hub.AskAsync(Text);

        // **Accepted, not refused.** A run in progress is not a reason to turn a question away
        // (contracts/hub-http-api.md, RUNS-002).
        Assert.Null(result.Refused);
        Assert.NotNull(result.Accepted);
        Assert.Equal(Text, Assert.Single(hub.Chat.Turns).Question.Text);
    }

    [Fact]
    [Trait("req", "RUNS-002")]
    public async Task Ask_WaitsItsTurn_WhileSomethingElseRuns()
    {
        var submission = await hub.AcceptedAsync("Ada Lovelace wrote the first program.");
        var question = await hub.AskedAsync(Text);

        // Waiting, with no run of its own: the one under way keeps its place, and nothing was
        // dispatched for the question (RUNS-002).
        Assert.Equal("waiting", Handed(question).State);
        Assert.Null(question.RunId);
        Assert.Equal(
            [submission.RunId!.Value],
            hub.Harness.Dispatched.Select(dispatch => dispatch.RunId));
    }

    [Fact]
    [Trait("req", "QUERY-005")]
    public async Task Ask_StoresNothingOfTheQuestion()
    {
        var question = await hub.AskedAsync(Text);

        // An accepted question is held in the chat and written down nowhere — there is no row of its
        // own in the store to find it by (QUERY-005 holds the whole of that; here it is what accepting
        // a question does and does not do).
        Assert.DoesNotContain(question.Text, hub.Store.Load().Select(stored => stored.Text));
        Assert.Empty(hub.Store.Load());
    }

    /// <summary>
    /// The turn the user is answered with, built from this question's own turn the way
    /// <c>ChatEndpoints</c> builds the body of an accepted question.
    /// </summary>
    private ChatTurnView Handed(Question question) =>
        ChatTurnView.Of(hub.Chat.Turns.Single(turn => turn.Question.Id == question.Id));
}
