using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Tests.Support;

namespace Grimoire.Tests.Hub;

/// <summary>
/// T082 (FR-026). <c>POST /api/tasks/{taskId}/revert</c> answers exactly as
/// <c>contracts/hub-api.openapi.yaml</c> describes: <c>200</c> with the updated task view,
/// <c>404</c> for a task that does not exist, and <c>409</c> whose <c>detail</c> names which
/// condition failed.
/// </summary>
/// <remarks>
/// The refusals carry the weight. A second click, a second browser tab and a stale page all reach
/// this endpoint with the same request, and the difference between "already reverted" and
/// "superseded" is what the person reading the answer needs in order to know whether their undo
/// happened or someone else's change did.
/// </remarks>
public sealed class RevertEndpointTests
{
    [Fact]
    public async Task RevertsAndReturnsTheUpdatedTaskDetail()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes worth keeping", TestContext.Current.CancellationToken);
        await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        using var response = await hub.Revert(id, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(id, body.GetProperty("id").GetString());
        Assert.Equal("reverted", body.GetProperty("state").GetString());
        Assert.Equal(wiki.Head(), body.GetProperty("revert").GetProperty("revertCommitSha").GetString());
        // The body is the task view's body, so the page that issued the revert can render the
        // answer without a second request.
        Assert.False(body.GetProperty("revertEligibility").GetProperty("eligible").GetBoolean());
        Assert.Equal("already-reverted", body.GetProperty("revertEligibility").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task AnswersNotFoundForATaskThatDoesNotExist()
    {
        using var wiki = new WikiRepositoryFixture();
        using var hub = GrimoireHub.Start(wiki);

        using var response = await hub.Revert("no-such-task", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task RefusesASecondAttemptAndSaysItIsAlreadyReverted()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        using var first = await hub.Revert(id, TestContext.Current.CancellationToken);
        first.EnsureSuccessStatusCode();
        var headAfterFirst = wiki.Head();
        var countAfterFirst = wiki.CommitCount();

        using var second = await hub.Revert(id, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Contains("already", problem.GetProperty("detail").GetString()!, StringComparison.OrdinalIgnoreCase);
        // Refused, not repeated: the wiki did not move.
        Assert.Equal(headAfterFirst, wiki.Head());
        Assert.Equal(countAfterFirst, wiki.CommitCount());
    }

    [Fact]
    public async Task RefusesASupersededTaskAndSaysSo()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("first source", TestContext.Current.CancellationToken);
        await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        // Any later wiki commit supersedes the ingest, whoever made it (FR-027).
        wiki.Write("topics/edited-by-hand.md", "# Edited\n");
        wiki.Commit("a later change to the wiki");

        using var response = await hub.Revert(id, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Contains("superseded", problem.GetProperty("detail").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefusesATaskWhoseRunProducedNoCommit()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-only");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("something the wiki already covers", TestContext.Current.CancellationToken);
        await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        using var response = await hub.Revert(id, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Contains("no commit", problem.GetProperty("detail").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefusesWhileARunIsInFlight()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("slow-read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var first = await hub.SubmitText("first source", TestContext.Current.CancellationToken);
        var completed = await hub.WaitForEnd(first, TestContext.Current.CancellationToken);
        Assert.True(completed.GetProperty("revertEligibility").GetProperty("eligible").GetBoolean());

        // A second run is now writing into the same working tree. The first task's commit is still
        // the tip, so eligibility alone would say yes.
        var second = await hub.SubmitText("second source", TestContext.Current.CancellationToken);
        await WaitForState(hub, second, "running", TestContext.Current.CancellationToken);

        using var response = await hub.Revert(first, TestContext.Current.CancellationToken);

        // Refused, because a revert resolved against a tree a live agent is writing into would
        // carry that agent's half-finished work in the revert commit (FR-015).
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Contains("run is in progress", problem.GetProperty("detail").GetString()!, StringComparison.OrdinalIgnoreCase);

        // And once the run has ended it is offered again: this is a "not now", not a "never". The
        // second run wrote the same page with the same content, so it committed nothing and the
        // first task's commit is still the tip — the refusal above was about the live tree alone.
        await hub.WaitForEnd(second, TestContext.Current.CancellationToken);
        var later = await hub.GetTask(first, TestContext.Current.CancellationToken);
        Assert.True(later.GetProperty("revertEligibility").GetProperty("eligible").GetBoolean());
        using var afterwards = await hub.Revert(first, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, afterwards.StatusCode);
    }

    private static async Task WaitForState(
        GrimoireHub hub, string taskId, string state, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var task = await hub.GetTask(taskId, cancellationToken);
            if (task.GetProperty("state").GetString() == state)
            {
                return;
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException($"Task {taskId} never reached '{state}'.");
    }

    [Fact]
    public async Task TwoConcurrentAttemptsRevertExactlyOnce()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        await hub.WaitForEnd(id, TestContext.Current.CancellationToken);
        var countBefore = wiki.CommitCount();

        // A double-click, or the same task open in two tabs. Eligibility is re-checked under the
        // single-writer lock, so one wins and the other is refused rather than reverting twice.
        var attempts = await System.Threading.Tasks.Task.WhenAll(
            hub.Revert(id, TestContext.Current.CancellationToken),
            hub.Revert(id, TestContext.Current.CancellationToken));

        try
        {
            Assert.Equal(1, attempts.Count(attempt => attempt.StatusCode is HttpStatusCode.OK));
            Assert.Equal(1, attempts.Count(attempt => attempt.StatusCode is HttpStatusCode.Conflict));
            Assert.Equal(countBefore + 1, wiki.CommitCount());
        }
        finally
        {
            foreach (var attempt in attempts)
            {
                attempt.Dispose();
            }
        }
    }
}
