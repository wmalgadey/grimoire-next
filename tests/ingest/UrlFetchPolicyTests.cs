using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Tests.Support;

namespace Grimoire.Tests.Ingest;

/// <summary>
/// T043 / TS-20 (FR-003, research R16). URL retrieval is the one place a user's input becomes an
/// outbound request, so it is also the one place server-side request forgery has to be refused.
/// A refusal is recorded as the task's failure reason and dispatches no run.
/// </summary>
/// <remarks>
/// This guard lives in <c>src/ingest/adapters/UrlFetch.cs</c>. The egress proxy enforces the same
/// policy independently on its fetch route (TS-21) — two checks, because the in-process one gives
/// the operator a readable reason and the network one makes the posture real.
/// </remarks>
public sealed class UrlFetchPolicyTests
{
    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.test/article")]
    [InlineData("gopher://example.test/1")]
    [InlineData("data:text/plain,inline")]
    [InlineData("jar:http://example.test/!/x")]
    public async Task RefusesASchemeThatIsNotHttpOrHttps(string url)
    {
        using var wiki = new WikiRepositoryFixture();
        using var hub = GrimoireHub.Start(wiki);

        var task = await SubmitAndWait(hub, url);

        Assert.Equal("failed", task.GetProperty("state").GetString());
        Assert.True(task.GetProperty("run").ValueKind is JsonValueKind.Null,
            $"A run was dispatched for the refused URL '{url}'.");
    }

    [Theory]
    [InlineData("http://127.0.0.1/secret")]
    [InlineData("http://localhost/secret")]
    [InlineData("http://[::1]/secret")]
    [InlineData("http://10.0.0.1/secret")]
    [InlineData("http://192.168.1.1/secret")]
    [InlineData("http://172.16.0.1/secret")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    public async Task RefusesADestinationResolvingToLoopbackLinkLocalOrAPrivateRange(string url)
    {
        using var wiki = new WikiRepositoryFixture();
        using var hub = GrimoireHub.Start(wiki);

        var task = await SubmitAndWait(hub, url);

        Assert.Equal("failed", task.GetProperty("state").GetString());
        Assert.True(task.GetProperty("run").ValueKind is JsonValueKind.Null);
    }

    [Fact]
    public async Task RecordsTheRefusalAsTheTasksFailureReasonInWordsAnOperatorCanActOn()
    {
        using var wiki = new WikiRepositoryFixture();
        using var hub = GrimoireHub.Start(wiki);

        var task = await SubmitAndWait(hub, "http://169.254.169.254/latest/meta-data/");

        var reason = task.GetProperty("failureReason").GetString();
        Assert.False(string.IsNullOrWhiteSpace(reason));
        // The reason names the destination, so the operator can tell a refusal from an outage.
        Assert.Contains("169.254.169.254", reason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesEvenWhenSomethingIsActuallyListeningOnLoopback()
    {
        // A refusal has to be a policy decision, not a connection failure: a live listener on
        // loopback is exactly the case a naive implementation would happily fetch.
        using var origin = new TestOrigin(_ => (HttpStatusCode.OK, "text/plain", "internal secrets"));
        using var wiki = new WikiRepositoryFixture();
        using var hub = GrimoireHub.Start(wiki);

        var task = await SubmitAndWait(hub, origin.Url("/secret"));

        Assert.Equal("failed", task.GetProperty("state").GetString());
        Assert.True(task.GetProperty("run").ValueKind is JsonValueKind.Null);
    }

    [Fact]
    public async Task LeavesTheWikiUntouchedWhenARetrievalIsRefused()
    {
        using var wiki = new WikiRepositoryFixture();
        var before = wiki.Snapshot();
        var tip = wiki.Head();
        using var hub = GrimoireHub.Start(wiki);

        await SubmitAndWait(hub, "http://10.0.0.1/secret");

        Assert.Equal(before, wiki.Snapshot());
        Assert.Equal(tip, wiki.Head());
    }

    [Fact]
    public async Task RefusesAUrlThatIsNotAUrlAtAll()
    {
        using var wiki = new WikiRepositoryFixture();
        using var hub = GrimoireHub.Start(wiki);

        var task = await SubmitAndWait(hub, "not a url at all");

        Assert.Equal("failed", task.GetProperty("state").GetString());
        Assert.True(task.GetProperty("run").ValueKind is JsonValueKind.Null);
    }

    private static async Task<JsonElement> SubmitAndWait(GrimoireHub hub, string url)
    {
        var response = await hub.Submit("url", url, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return await hub.WaitForEnd(created.GetProperty("id").GetString()!, TestContext.Current.CancellationToken);
    }
}
