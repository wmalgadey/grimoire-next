using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Tests.Support;

namespace Grimoire.Tests.Ingest;

/// <summary>
/// T040 / TS-01 (FR-001). An empty or whitespace-only submission is rejected and <b>no task is
/// created</b>. Not "a task that immediately fails": the submission never became work.
/// </summary>
public sealed class SubmissionTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   \t  ")]
    [InlineData("\n\n")]
    [InlineData("\r\n \t")]
    public async Task RejectsAnEmptyOrWhitespaceOnlySubmissionWithProblemDetails(string value)
    {
        using var wiki = new WikiRepositoryFixture();
        using var hub = GrimoireHub.Start(wiki);

        var response = await hub.Submit("text", value, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreatesNoTaskForARejectedSubmission(string value)
    {
        using var wiki = new WikiRepositoryFixture();
        using var hub = GrimoireHub.Start(wiki);

        await hub.Submit("text", value, TestContext.Current.CancellationToken);

        var list = await hub.Client.GetFromJsonAsync<JsonElement>(
            "/api/tasks", TestContext.Current.CancellationToken);
        Assert.Empty(list.GetProperty("tasks").EnumerateArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \t ")]
    public async Task RejectsAnEmptyOrWhitespaceOnlyUrlTheSameWay(string value)
    {
        using var wiki = new WikiRepositoryFixture();
        using var hub = GrimoireHub.Start(wiki);

        var response = await hub.Submit("url", value, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LeavesTheWikiUntouchedWhenASubmissionIsRejected()
    {
        using var wiki = new WikiRepositoryFixture();
        var before = wiki.Snapshot();
        var tip = wiki.Head();
        using var hub = GrimoireHub.Start(wiki);

        await hub.Submit("text", "   ", TestContext.Current.CancellationToken);

        Assert.Equal(before, wiki.Snapshot());
        Assert.Equal(tip, wiki.Head());
    }

    [Fact]
    public async Task AcceptsASubmissionThatIsOnlyNonEmptyAfterTrimmingIsConsidered()
    {
        using var wiki = new WikiRepositoryFixture();
        using var hub = GrimoireHub.Start(wiki);

        // Padding is not emptiness: the rule is "empty after trimming", not "contains no whitespace".
        var response = await hub.Submit("text", "  something worth keeping  ", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
