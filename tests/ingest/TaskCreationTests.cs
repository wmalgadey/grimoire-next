using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Tests.Support;

namespace Grimoire.Tests.Ingest;

/// <summary>
/// T041 / TS-02 (FR-002, SC-001). An accepted submission becomes <b>exactly one</b> task, which is
/// openable from the moment it exists — not once a run has succeeded.
/// </summary>
public sealed class TaskCreationTests
{
    [Fact]
    public async Task AcceptsPastedTextLargerThanTheServersDefaultRequestBodyLimit()
    {
        // Kestrel refuses a request body over ~30 MB with 413 unless told otherwise, which would be
        // a size limit on sources the spec says the harness must not impose (FR-029). Only a real
        // Kestrel has that limit — the in-process test server does not — so this needs the hub as
        // a process.
        using var wiki = new WikiRepositoryFixture();
        using var hub = await HubProcess.Start(wiki, cancellationToken: TestContext.Current.CancellationToken);
        var text = new string('x', 31_000_000);

        var response = await hub.Client.PostAsJsonAsync(
            "/api/tasks", new { kind = "text", value = text }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var task = await hub.GetTask(created.GetProperty("id").GetString()!, TestContext.Current.CancellationToken);
        Assert.Equal(31_000_000, task.GetProperty("source").GetProperty("byteLength").GetInt32());
    }

    [Fact]
    public async Task CreatesExactlyOneTaskPerAcceptedSubmission()
    {
        using var wiki = new WikiRepositoryFixture();
        using var hub = GrimoireHub.Start(wiki);

        var response = await hub.Submit("text", "notes worth keeping", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var list = await hub.Client.GetFromJsonAsync<JsonElement>(
            "/api/tasks", TestContext.Current.CancellationToken);
        Assert.Single(list.GetProperty("tasks").EnumerateArray());
    }

    [Fact]
    public async Task ReturnsATaskWithAStableIdentifierAndTheQueuedState()
    {
        using var wiki = new WikiRepositoryFixture();
        using var hub = GrimoireHub.Start(wiki);

        var response = await hub.Submit("text", "notes", TestContext.Current.CancellationToken);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        var id = created.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.Contains(created.GetProperty("state").GetString(), new[] { "queued", "running" });
        Assert.True(created.TryGetProperty("submittedAt", out _));

        // Stable for the task's life: the same identifier still addresses it afterwards (FR-002).
        var reopened = await hub.GetTask(id!, TestContext.Current.CancellationToken);
        Assert.Equal(id, reopened.GetProperty("id").GetString());
    }

    [Fact]
    public async Task MakesTheTaskOpenableAsSoonAsItIsCreated()
    {
        using var wiki = new WikiRepositoryFixture();
        using var hub = GrimoireHub.Start(wiki);

        // Opened straight after the submission answers, with no run ended yet. SC-001 names no
        // latency; what it requires is that the task is openable from the moment it exists.
        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.GetTask(id, TestContext.Current.CancellationToken);

        Assert.Equal(id, task.GetProperty("id").GetString());
    }

    [Fact]
    public async Task RetainsTheSourceOnTheTask()
    {
        using var wiki = new WikiRepositoryFixture();
        using var hub = GrimoireHub.Start(wiki);
        const string submitted = "the exact text that was pasted";

        var id = await hub.SubmitText(submitted, TestContext.Current.CancellationToken);
        var task = await hub.GetTask(id, TestContext.Current.CancellationToken);

        // The source is retained as long as the task is (FR-004).
        var source = task.GetProperty("source");
        Assert.Equal("text", source.GetProperty("kind").GetString());
        Assert.Equal(submitted, source.GetProperty("submittedValue").GetString());
    }

    [Fact]
    public async Task GivesEachSubmissionItsOwnTask()
    {
        using var wiki = new WikiRepositoryFixture();
        using var hub = GrimoireHub.Start(wiki);

        var first = await hub.SubmitText("first", TestContext.Current.CancellationToken);
        var second = await hub.SubmitText("second", TestContext.Current.CancellationToken);

        Assert.NotEqual(first, second);
        var list = await hub.Client.GetFromJsonAsync<JsonElement>(
            "/api/tasks", TestContext.Current.CancellationToken);
        Assert.Equal(2, list.GetProperty("tasks").EnumerateArray().Count());
    }

    [Fact]
    public async Task Returns404ForATaskThatDoesNotExist()
    {
        using var wiki = new WikiRepositoryFixture();
        using var hub = GrimoireHub.Start(wiki);

        var response = await hub.Client.GetAsync(
            "/api/tasks/no-such-task", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
