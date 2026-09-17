using Grimoire.Tests.Support;

namespace Grimoire.Tests.Dispatch;

/// <summary>
/// T090 / TS-14, serialisation half (FR-019). One run at a time: a submission accepted while a run
/// is executing becomes a <c>queued</c> task rather than a second run.
/// </summary>
/// <remarks>
/// Serialisation is a property of the product, not a limitation waiting to be lifted. Runs hold the
/// wiki working tree, so two at once would interleave their writes into one commit and "one commit
/// per run" — the thing that makes an ingest a single revertible unit — would stop being true.
///
/// The <c>slow-read-then-write</c> script keeps a run in flight long enough that the second
/// submission genuinely arrives mid-run, rather than the suite catching two quick runs in sequence
/// and proving nothing.
/// </remarks>
public sealed class QueueTests
{
    [Fact]
    public async Task ASubmissionAcceptedDuringARunBecomesAQueuedTask()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("slow-read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var first = await hub.SubmitText("first source", TestContext.Current.CancellationToken);
        await WaitForState(hub, first, "running", TestContext.Current.CancellationToken);

        // Accepted, not rejected and not made to wait: submitting is not the same as running.
        var second = await hub.SubmitText("second source", TestContext.Current.CancellationToken);
        var task = await hub.GetTask(second, TestContext.Current.CancellationToken);
        Assert.Equal("queued", task.GetProperty("state").GetString());
        Assert.True(task.GetProperty("startedAt").ValueKind is System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task NeverRunsTwoAtOnce()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("slow-read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var ids = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add(await hub.SubmitText($"source {i}", TestContext.Current.CancellationToken));
        }

        // Sampled while the queue drains: at no point is more than one task running.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);
        var sawRunning = false;
        while (DateTime.UtcNow < deadline)
        {
            var states = new List<string>();
            foreach (var id in ids)
            {
                var task = await hub.GetTask(id, TestContext.Current.CancellationToken);
                states.Add(task.GetProperty("state").GetString()!);
            }

            var running = states.Count(state => state is "running");
            Assert.True(running <= 1, $"Two runs were executing at once: {string.Join(", ", states)}");
            sawRunning |= running is 1;

            if (states.All(state => state is "completed" or "failed"))
            {
                break;
            }

            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        Assert.True(sawRunning, "No run was ever observed executing, so nothing was serialised.");
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
}
