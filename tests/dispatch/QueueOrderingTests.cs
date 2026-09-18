using System.Globalization;
using System.Text.Json;
using Grimoire.Tests.Support;

namespace Grimoire.Tests.Dispatch;

/// <summary>
/// FR-019: the hub dispatches queued tasks in <c>submittedAt</c> order, one at a time.
/// </summary>
/// <remarks>
/// The scripted model's <c>slow-read-then-write</c> script keeps a run in flight for a few
/// seconds — long enough that later submissions reach <see cref="RunQueue"/> before the first run
/// finishes, exercising the actual queuing rather than a coincidence of quick sequential runs.
/// </remarks>
public sealed class QueueOrderingTests
{
    [Fact]
    public async Task RunsQueuedTasksInTheOrderTheyWereSubmitted()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("slow-read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var first = await hub.SubmitText("first source", TestContext.Current.CancellationToken);
        var second = await hub.SubmitText("second source", TestContext.Current.CancellationToken);
        var third = await hub.SubmitText("third source", TestContext.Current.CancellationToken);

        var firstTask = await hub.WaitForEnd(first, TestContext.Current.CancellationToken);
        var secondTask = await hub.WaitForEnd(second, TestContext.Current.CancellationToken);
        var thirdTask = await hub.WaitForEnd(third, TestContext.Current.CancellationToken);

        var firstStarted = StartedAt(firstTask);
        var secondStarted = StartedAt(secondTask);
        var thirdStarted = StartedAt(thirdTask);

        Assert.True(firstStarted < secondStarted,
            $"'first source' started at {firstStarted:O} but 'second source' started at {secondStarted:O}: "
            + "dispatch did not honour submission order (FR-019).");
        Assert.True(secondStarted < thirdStarted,
            $"'second source' started at {secondStarted:O} but 'third source' started at {thirdStarted:O}: "
            + "dispatch did not honour submission order (FR-019).");
    }

    private static DateTimeOffset StartedAt(JsonElement task) =>
        DateTimeOffset.Parse(
            task.GetProperty("startedAt").GetString()!,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}
