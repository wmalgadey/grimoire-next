using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Grimoire.Tests.Egress;

/// <summary>
/// T101 / TS-21, the model route (ADR-0010, ADR-0012). Against the real proxy process and a real
/// upstream listener asserting the headers it received: the caller's opaque internal token never
/// leaves, the upstream credential only the proxy holds is attached, and nothing reaches any
/// upstream but the one configured.
/// </summary>
public sealed class ModelRouteTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StripsTheInternalTokenAndInjectsTheUpstreamCredential()
    {
        using var upstream = new UpstreamListener();
        using var egress = await EgressProcess.Start(upstream.Url, Cancel);
        using var client = egress.Client();

        using var request = ModelRequest(EgressProcess.InternalToken);
        using var response = await client.SendAsync(request, Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = Assert.Single(upstream.Received);
        Assert.Equal("POST", received.Method);
        Assert.Equal("/v1/messages?beta=true", received.PathAndQuery);
        Assert.Equal(EgressProcess.UpstreamCredential, received.Headers["x-api-key"]);
        Assert.Null(received.Headers["Authorization"]);
        Assert.Null(received.Headers["X-Grimoire-Run"]);

        // Not under any header name: the internal token is meaningless outside the deployment and
        // has no business reaching the upstream at all.
        foreach (var name in received.Headers.AllKeys)
        {
            Assert.DoesNotContain(EgressProcess.InternalToken, received.Headers[name] ?? "", StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ReplacesACredentialTheCallerTriedToSupplyItself()
    {
        using var upstream = new UpstreamListener();
        using var egress = await EgressProcess.Start(upstream.Url, Cancel);
        using var client = egress.Client();

        using var request = ModelRequest(EgressProcess.InternalToken);
        request.Headers.Add("x-api-key", "sk-ant-a-key-the-caller-should-not-have");
        using var response = await client.SendAsync(request, Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = Assert.Single(upstream.Received);
        Assert.Equal(EgressProcess.UpstreamCredential, received.Headers["x-api-key"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-the-internal-token")]
    public async Task RefusesACallerWithoutTheInternalToken(string? token)
    {
        using var upstream = new UpstreamListener();
        using var egress = await EgressProcess.Start(upstream.Url, Cancel);
        using var client = egress.Client();

        using var request = ModelRequest(token);
        using var response = await client.SendAsync(request, Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(upstream.Received);
    }

    [Fact]
    public async Task RefusesToCarryARequestToAnUpstreamThatIsNotAllowlisted()
    {
        using var allowlisted = new UpstreamListener();
        using var elsewhere = new UpstreamListener();
        using var egress = await EgressProcess.Start(allowlisted.Url, Cancel);

        // Used as a forward proxy, the way a client honouring HTTP_PROXY would: the request names
        // its own destination. The proxy is not a forward proxy for the model route, and a request
        // that names a destination is not re-aimed at the allowlisted one either.
        using var client = new HttpClient(new SocketsHttpHandler
        {
            Proxy = new WebProxy(egress.BaseAddress),
            UseProxy = true,
        });
        using var request = ModelRequest(EgressProcess.InternalToken, $"{elsewhere.Url}/v1/messages");
        using var response = await client.SendAsync(request, Cancel);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(elsewhere.Received);
        Assert.Empty(allowlisted.Received);
    }

    [Fact]
    public async Task ForwardsNothingOutsideTheModelApi()
    {
        using var upstream = new UpstreamListener();
        using var egress = await EgressProcess.Start(upstream.Url, Cancel);
        using var client = egress.Client();

        using var request = ModelRequest(EgressProcess.InternalToken, "/admin/keys");
        using var response = await client.SendAsync(request, Cancel);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(upstream.Received);
    }

    [Fact]
    public async Task PassesAStreamedResponseThroughAsItArrives()
    {
        // The upstream sends one event, then holds the response open. A proxy that buffered would
        // deliver nothing until the whole body existed — which here is never, until released.
        var release = new TaskCompletionSource();
        using var upstream = new UpstreamListener(async context =>
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/event-stream";
            context.Response.SendChunked = true;
            var first = Encoding.UTF8.GetBytes("event: message_start\ndata: {}\n\n");
            await context.Response.OutputStream.WriteAsync(first);
            await context.Response.OutputStream.FlushAsync();
            await release.Task;
            context.Response.Close();
        });
        using var egress = await EgressProcess.Start(upstream.Url, Cancel);
        using var client = egress.Client();

        using var request = ModelRequest(EgressProcess.InternalToken);
        var clock = Stopwatch.StartNew();
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Cancel);
        await using var body = await response.Content.ReadAsStreamAsync(Cancel);

        var buffer = new byte[64];
        using var firstEventWindow = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        firstEventWindow.CancelAfter(TimeSpan.FromSeconds(10));
        var read = await body.ReadAsync(buffer, firstEventWindow.Token);

        Assert.True(read > 0, "The first streamed event never arrived.");
        Assert.StartsWith("event: message_start", Encoding.UTF8.GetString(buffer, 0, read), StringComparison.Ordinal);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10));
        release.SetResult();
    }

    private static HttpRequestMessage ModelRequest(string? token, string path = "/v1/messages?beta=true")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent("""{"model":"claude","messages":[]}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Headers.Add("X-Grimoire-Run", "run-under-test");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }
}
