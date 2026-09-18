using System.Net;
using System.Net.Http.Headers;
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
        using var proxy = new FetchRouteFixture();
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
        using var proxy = new FetchRouteFixture();
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
        using var proxy = new FetchRouteFixture();
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
        using var proxy = new FetchRouteFixture();
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
        using var proxy = new FetchRouteFixture();
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
        using var proxy = new FetchRouteFixture();
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
        using var proxy = new FetchRouteFixture();
        using var hub = GrimoireHub.Start(wiki, model, extraEnvironment: Through(proxy));

        var id = await SubmitUrl(hub, origin.Url("/article"));
        await hub.WaitForEnd(id, TestContext.Current.CancellationToken);
        var task = await hub.GetTask(id, TestContext.Current.CancellationToken);

        var source = task.GetProperty("source");
        Assert.Equal("url", source.GetProperty("kind").GetString());
        Assert.Equal(body, source.GetProperty("retrievedText").GetString());
        Assert.Equal(Encoding.UTF8.GetByteCount(body), source.GetProperty("byteLength").GetInt32());
    }

    [Fact]
    public async Task ReChecksEveryRedirectHopRatherThanFollowingItBlindly()
    {
        // The origin answers with a redirect to a destination the policy refuses. The hub follows
        // redirects by hand precisely so each hop passes the same check as the first (TS-20):
        // here the scheme, which the hub checks itself on every hop. A redirect to a private
        // address is refused on the same hop by the proxy's connect step — tests/egress covers that.
        using var origin = new TestOrigin(
            _ => (HttpStatusCode.Found, "text/plain", ""),
            headers: new Dictionary<string, string> { ["Location"] = "file:///etc/passwd" });
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("no-op");
        using var proxy = new FetchRouteFixture();
        using var hub = GrimoireHub.Start(wiki, model, extraEnvironment: Through(proxy));

        var id = await SubmitUrl(hub, origin.Url("/article"));
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        Assert.Equal("failed", task.GetProperty("state").GetString());
        Assert.Contains("file", task.GetProperty("failureReason").GetString()!, StringComparison.Ordinal);
        Assert.True(task.GetProperty("run").ValueKind is JsonValueKind.Null, "A run was dispatched after a refused hop.");
        Assert.Empty(await model.Requests(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AUrlSubmissionIsVisibleAtOnceHoweverSlowTheOrigin()
    {
        // SC-001 holds for URLs as well as text: retrieval is the first step of dispatch, not part
        // of answering the submission.
        using var origin = new TestOrigin(
            _ => (HttpStatusCode.OK, "text/plain", "A slow article."), delay: TimeSpan.FromSeconds(6));
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("no-op");
        using var proxy = new FetchRouteFixture();
        using var hub = GrimoireHub.Start(wiki, model, extraEnvironment: Through(proxy));

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var response = await hub.Submit("url", origin.Url("/article"), TestContext.Current.CancellationToken);
        clock.Stop();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"The task took {clock.Elapsed} to appear.");
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("queued", created.GetProperty("state").GetString());

        var task = await hub.WaitForEnd(created.GetProperty("id").GetString()!, TestContext.Current.CancellationToken);
        Assert.Equal("completed", task.GetProperty("state").GetString());
        Assert.Equal("A slow article.", task.GetProperty("source").GetProperty("retrievedText").GetString());
    }

    [Fact]
    public async Task AUrlTaskWhoseRetrievalAStoppedHubInterruptedIsNotStranded()
    {
        // The hub dies while fetching. The task is still queued with nothing retrieved; the next
        // start retrieves it and the task reaches an end, rather than sitting queued forever with no
        // source to run (FR-003, SC-006).
        using var origin = new TestOrigin(
            _ => (HttpStatusCode.OK, "text/plain", "An article worth keeping."), delay: TimeSpan.FromSeconds(4));
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("no-op");
        using var proxy = new FetchRouteFixture();
        var state = Path.Combine(Path.GetTempPath(), $"grimoire-state-{Guid.NewGuid():N}.db");

        string id;
        using (var first = await HubProcess.Start(
                   wiki, model, state, Through(proxy), TestContext.Current.CancellationToken))
        {
            var response = await first.Client.PostAsJsonAsync(
                "/api/tasks", new { kind = "url", value = origin.Url("/article") }, TestContext.Current.CancellationToken);
            response.EnsureSuccessStatusCode();
            id = (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
                .GetProperty("id").GetString()!;

            await Task.Delay(1_000, TestContext.Current.CancellationToken);
            first.KillUngracefully();
        }

        using var second = await HubProcess.Start(
            wiki, model, state, Through(proxy), TestContext.Current.CancellationToken);
        var task = await second.WaitForEnd(id, TestContext.Current.CancellationToken);

        Assert.Equal("completed", task.GetProperty("state").GetString());
        Assert.Equal("An article worth keeping.", task.GetProperty("source").GetProperty("retrievedText").GetString());
    }

    [Fact]
    public async Task DecodesTheRetrievedBodyByTheCharsetTheOriginDeclares()
    {
        // Sent as ISO-8859-1, where "ü" and "ß" are single bytes that are not valid UTF-8. Decoded
        // as UTF-8 regardless, the run would be handed replacement characters, not the source
        // (FR-029: the text is passed whole and unaltered).
        const string body = "Grüße aus Köln.";
        using var origin = new TestOrigin(_ => (HttpStatusCode.OK, "text/plain; charset=iso-8859-1", body));
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("no-op");
        using var proxy = new FetchRouteFixture();
        using var hub = GrimoireHub.Start(wiki, model, extraEnvironment: Through(proxy));

        var id = await SubmitUrl(hub, origin.Url("/article"));
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        Assert.Equal("completed", task.GetProperty("state").GetString());
        Assert.Equal(body, task.GetProperty("source").GetProperty("retrievedText").GetString());
    }

    [Fact]
    public async Task FailsTheTaskWhenTheOriginDeclaresACharsetThatCannotBeDecoded()
    {
        using var origin = new TestOrigin(_ => (HttpStatusCode.OK, "text/plain; charset=no-such-charset", "text"));
        using var wiki = new WikiRepositoryFixture();
        using var proxy = new FetchRouteFixture();
        using var hub = GrimoireHub.Start(wiki, extraEnvironment: Through(proxy));

        var id = await SubmitUrl(hub, origin.Url("/article"));
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        Assert.Equal("failed", task.GetProperty("state").GetString());
        Assert.Contains("no-such-charset", task.GetProperty("failureReason").GetString()!, StringComparison.Ordinal);
        Assert.True(task.GetProperty("run").ValueKind is JsonValueKind.Null);
    }

    [Fact]
    public async Task FailsTheTaskWhenTheOriginTricklesItsBodyPastTheRetrievalDeadline()
    {
        // The headers arrive at once; the body never finishes. A timeout that covers only the
        // wait for headers would leave this task retrieving for as long as the origin likes to
        // take — and every task behind it waiting (FR-003, FR-019).
        using var origin = new TestOrigin(
            _ => (HttpStatusCode.OK, "text/plain", "An article that takes a minute to arrive."),
            trickle: TimeSpan.FromSeconds(1));
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("no-op");
        using var proxy = new FetchRouteFixture();
        var environment = Through(proxy);
        environment["GRIMOIRE_FETCH_DEADLINE_MS"] = "2000";
        using var hub = GrimoireHub.Start(wiki, model, extraEnvironment: environment);

        var id = await SubmitUrl(hub, origin.Url("/article"));
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken, TimeSpan.FromSeconds(20));

        Assert.Equal("failed", task.GetProperty("state").GetString());
        Assert.Contains("2 seconds", task.GetProperty("failureReason").GetString()!, StringComparison.Ordinal);
        Assert.True(task.GetProperty("run").ValueKind is JsonValueKind.Null, "A run was dispatched with a partial source.");
        Assert.Empty(await model.Requests(TestContext.Current.CancellationToken));
    }

    /// <summary>Routes the hub's retrieval through the proxy, as a container deployment does.</summary>
    private static Dictionary<string, string?> Through(FetchRouteFixture proxy) =>
        new() { ["GRIMOIRE_FETCH_PROXY"] = proxy.FetchRoute };

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

    /// <param name="respond">What the origin answers.</param>
    /// <param name="headers">Extra response headers — a <c>Location</c> for a redirect.</param>
    /// <param name="delay">How long the origin takes before it answers, for a slow origin.</param>
    /// <param name="trickle">
    /// When set, the headers go out at once and the body follows one byte per interval — an origin
    /// that is answering, just never finishing.
    /// </param>
    public TestOrigin(
        Func<HttpListenerRequest, (HttpStatusCode Status, string ContentType, string Body)> respond,
        IReadOnlyDictionary<string, string>? headers = null,
        TimeSpan? delay = null,
        TimeSpan? trickle = null)
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

                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (delay is { } wait)
                        {
                            await Task.Delay(wait, _stopping.Token);
                        }

                        var (status, contentType, body) = respond(context.Request);
                        context.Response.StatusCode = (int)status;
                        context.Response.ContentType = contentType;
                        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
                        {
                            if (name is "Location")
                            {
                                context.Response.RedirectLocation = value;
                            }
                            else
                            {
                                context.Response.Headers[name] = value;
                            }
                        }

                        // Encoded as the content type says, so an origin can declare a charset
                        // other than UTF-8 and mean it.
                        var charset = MediaTypeHeaderValue.TryParse(contentType, out var parsed)
                            ? parsed.CharSet?.Trim('"')
                            : null;
                        var bytes = EncodingFor(charset).GetBytes(body);

                        if (trickle is { } interval)
                        {
                            context.Response.SendChunked = true;
                            foreach (var single in bytes)
                            {
                                await context.Response.OutputStream.WriteAsync(new[] { single }, _stopping.Token);
                                await context.Response.OutputStream.FlushAsync(_stopping.Token);
                                await Task.Delay(interval, _stopping.Token);
                            }
                        }
                        else
                        {
                            await context.Response.OutputStream.WriteAsync(bytes, _stopping.Token);
                        }

                        context.Response.Close();
                    }
                    catch (Exception) when (_stopping.IsCancellationRequested)
                    {
                        // Stopped while answering.
                    }
                    catch (HttpListenerException)
                    {
                        // The caller went away while this origin was still answering.
                    }
                });
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

    // A charset nobody knows is the origin misdeclaring, which is a case worth serving too.
    private static Encoding EncodingFor(string? charset)
    {
        try
        {
            return charset is null ? Encoding.UTF8 : Encoding.GetEncoding(charset);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
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
