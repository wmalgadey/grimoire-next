using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Grimoire.Tests.Support;

namespace Grimoire.Tests.Ingest;

/// <summary>
/// T042 / TS-03 (FR-003). URL retrieval happens before dispatch, against a <b>second real HTTP
/// listener</b>. When it fails the task ends <c>failed</c> with a human-readable reason,
/// <b>no run is dispatched</b>, and the wiki is byte-identical.
/// </summary>
/// <remarks>
/// The hub is configured with a fetch proxy here, as it is in a container: it connects to the
/// proxy and the proxy reaches the origin (ADR-0010). Without one the hub refuses a loopback
/// destination outright, which is what <see cref="UrlFetchPolicyTests"/> asserts — these two
/// suites exercise the two halves of that arrangement.
/// </remarks>
public sealed class UrlRetrievalTests
{
    [Fact]
    public async Task FailsTheTaskWithAReadableReasonWhenTheOriginAnswersWithAnError()
    {
        using var origin = new TestOrigin(_ => (HttpStatusCode.InternalServerError, "text/plain", "boom"));
        using var wiki = new WikiRepositoryFixture();
        using var proxy = new ForwardProxyFixture();
        using var hub = GrimoireHub.Start(wiki, extraEnvironment: Through(proxy));

        var id = await SubmitUrl(hub, origin.Url("/article"));
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        Assert.Equal("failed", task.GetProperty("state").GetString());
        var reason = task.GetProperty("failureReason").GetString();
        Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.Contains("500", reason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DispatchesNoRunWhenRetrievalFails()
    {
        using var origin = new TestOrigin(_ => (HttpStatusCode.InternalServerError, "text/plain", "boom"));
        using var wiki = new WikiRepositoryFixture();
        using var proxy = new ForwardProxyFixture();
        using var hub = GrimoireHub.Start(wiki, extraEnvironment: Through(proxy));

        var id = await SubmitUrl(hub, origin.Url("/article"));
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        // No run at all — not a run that failed. There was nothing to hand the agent.
        Assert.True(
            task.GetProperty("run").ValueKind is JsonValueKind.Null,
            "A run was dispatched for a task whose source could not be retrieved.");
    }

    [Fact]
    public async Task LeavesTheWikiByteIdenticalWhenRetrievalFails()
    {
        using var origin = new TestOrigin(_ => (HttpStatusCode.InternalServerError, "text/plain", "boom"));
        using var wiki = new WikiRepositoryFixture();
        var before = wiki.Snapshot();
        var tip = wiki.Head();
        using var proxy = new ForwardProxyFixture();
        using var hub = GrimoireHub.Start(wiki, extraEnvironment: Through(proxy));

        var id = await SubmitUrl(hub, origin.Url("/article"));
        await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        Assert.Equal(before, wiki.Snapshot());
        Assert.Equal(tip, wiki.Head());
        Assert.Equal(1, wiki.CommitCount());
    }

    [Fact]
    public async Task FailsTheTaskWhenTheOriginAnswersWithANonTextContentType()
    {
        using var origin = new TestOrigin(_ => (HttpStatusCode.OK, "image/png", "PNG"));
        using var wiki = new WikiRepositoryFixture();
        using var proxy = new ForwardProxyFixture();
        using var hub = GrimoireHub.Start(wiki, extraEnvironment: Through(proxy));

        var id = await SubmitUrl(hub, origin.Url("/picture.png"));
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        Assert.Equal("failed", task.GetProperty("state").GetString());
        Assert.Contains("image/png", task.GetProperty("failureReason").GetString()!, StringComparison.Ordinal);
        Assert.True(task.GetProperty("run").ValueKind is JsonValueKind.Null);
    }

    [Fact]
    public async Task FailsTheTaskWhenTheHostIsUnreachable()
    {
        using var wiki = new WikiRepositoryFixture();
        using var proxy = new ForwardProxyFixture();
        using var hub = GrimoireHub.Start(wiki, extraEnvironment: Through(proxy));

        var id = await SubmitUrl(hub, "http://no-such-host.invalid/article");
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        Assert.Equal("failed", task.GetProperty("state").GetString());
        Assert.False(string.IsNullOrWhiteSpace(task.GetProperty("failureReason").GetString()));
        Assert.True(task.GetProperty("run").ValueKind is JsonValueKind.Null);
    }

    [Fact]
    public async Task LeavesRetrievedTextNullPermanentlyWhenRetrievalFailed()
    {
        using var origin = new TestOrigin(_ => (HttpStatusCode.InternalServerError, "text/plain", "boom"));
        using var wiki = new WikiRepositoryFixture();
        using var proxy = new ForwardProxyFixture();
        using var hub = GrimoireHub.Start(wiki, extraEnvironment: Through(proxy));

        var id = await SubmitUrl(hub, origin.Url("/article"));
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        Assert.Equal(JsonValueKind.Null, task.GetProperty("source").GetProperty("retrievedText").ValueKind);
    }

    [Fact]
    public async Task AttachesTheRetrievedTextWhenTheOriginAnswersWithText()
    {
        const string body = "# Article\n\nEverything the article says.\n";
        using var origin = new TestOrigin(_ => (HttpStatusCode.OK, "text/plain; charset=utf-8", body));
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("no-op");
        using var proxy = new ForwardProxyFixture();
        using var hub = GrimoireHub.Start(wiki, model, extraEnvironment: Through(proxy));

        var id = await SubmitUrl(hub, origin.Url("/article"));
        await hub.WaitForEnd(id, TestContext.Current.CancellationToken);
        var task = await hub.GetTask(id, TestContext.Current.CancellationToken);

        var source = task.GetProperty("source");
        Assert.Equal("url", source.GetProperty("kind").GetString());
        Assert.Equal(body, source.GetProperty("retrievedText").GetString());
        Assert.Equal(Encoding.UTF8.GetByteCount(body), source.GetProperty("byteLength").GetInt32());
    }

    /// <summary>Routes the hub's retrieval through the proxy, as a container deployment does.</summary>
    private static Dictionary<string, string?> Through(ForwardProxyFixture proxy) =>
        new() { ["GRIMOIRE_FETCH_PROXY"] = proxy.Url };

    private static async Task<string> SubmitUrl(GrimoireHub hub, string url)
    {
        var response = await hub.Submit("url", url, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var task = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return task.GetProperty("id").GetString()!;
    }
}

/// <summary>
/// A second real HTTP listener — the origin a submitted URL points at. Real, because the LLM is
/// the only sanctioned double (constitution III.2).
/// </summary>
public sealed class TestOrigin : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stopping = new();

    public TestOrigin(Func<HttpListenerRequest, (HttpStatusCode Status, string ContentType, string Body)> respond)
    {
        var port = FreePort();
        Prefix = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(Prefix);
        _listener.Start();

        _ = Task.Run(async () =>
        {
            while (!_stopping.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception) when (_stopping.IsCancellationRequested || !_listener.IsListening)
                {
                    return;
                }

                var (status, contentType, body) = respond(context.Request);
                context.Response.StatusCode = (int)status;
                context.Response.ContentType = contentType;
                var bytes = Encoding.UTF8.GetBytes(body);
                await context.Response.OutputStream.WriteAsync(bytes, _stopping.Token);
                context.Response.Close();
            }
        });
    }

    /// <summary>The listener's root, e.g. <c>http://127.0.0.1:53211/</c>.</summary>
    public string Prefix { get; }

    /// <summary>A URL on this origin.</summary>
    public string Url(string path) => Prefix.TrimEnd('/') + path;

    /// <inheritdoc />
    public void Dispose()
    {
        _stopping.Cancel();
        _listener.Close();
        _stopping.Dispose();
    }

    private static int FreePort()
    {
        using var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
