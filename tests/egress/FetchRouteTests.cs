using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Egress;
using Grimoire.Tests.Support;
using Microsoft.AspNetCore.Http;

namespace Grimoire.Tests.Egress;

/// <summary>
/// T102 / TS-21, the fetch route (ADR-0010, ADR-0012). The route that goes to user-supplied
/// destinations carries no credential and refuses inward targets — <b>independently</b> of the
/// hub's own guard in <c>src/ingest/adapters/UrlFetch.cs</c>, which is not in this process at all.
/// </summary>
/// <remarks>
/// Every destination a test can stand up is on loopback or a private address, and those are
/// exactly what the route refuses — so no test here can watch it forward successfully to a real
/// origin. What it forwards is therefore asserted on the request the route builds, and what it
/// refuses is asserted against the running proxy and a listener that must never be reached.
/// </remarks>
public sealed class FetchRouteTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    public async Task RefusesADestinationBoundToLoopbackWithoutConnectingToIt(string host)
    {
        using var origin = new UpstreamListener();
        using var model = new UpstreamListener();
        using var egress = await EgressProcess.Start(model.Url, Cancel);
        using var client = egress.Client();
        var port = new Uri(origin.Url).Port;

        using var response = await client.GetAsync(Fetch($"http://{host}:{port}/secret"), Cancel);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(origin.Received);
        var reason = Reason(response);
        Assert.Contains("not a destination this system retrieves", reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[fd00::1]/")]
    // Special-use ranges that are not the internet either: benchmarking, documentation, and IPv6
    // forms that carry an IPv4 address (NAT64, 6to4) — a private one here.
    [InlineData("http://198.18.0.1/")]
    [InlineData("http://192.0.2.1/")]
    [InlineData("http://198.51.100.7/")]
    [InlineData("http://203.0.113.5/")]
    [InlineData("http://[2001:db8::1]/")]
    [InlineData("http://[64:ff9b::a00:1]/")]
    [InlineData("http://[2002:a00:1::1]/")]
    public async Task RefusesLinkLocalAndPrivateDestinations(string url)
    {
        using var model = new UpstreamListener();
        using var egress = await EgressProcess.Start(model.Url, Cancel);
        using var client = egress.Client();

        using var response = await client.GetAsync(Fetch(url), Cancel);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("not a destination this system retrieves", Reason(response), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/file")]
    [InlineData("not a url")]
    public async Task RefusesAnythingThatIsNotAnHttpUrl(string url)
    {
        using var model = new UpstreamListener();
        using var egress = await EgressProcess.Start(model.Url, Cancel);
        using var client = egress.Client();

        using var response = await client.GetAsync(Fetch(url), Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(Reason(response)));
        Assert.Empty(model.Received);
    }

    [Fact]
    public async Task NeverSendsARetrievalToTheModelUpstream()
    {
        // The route that carries the credential and the route that goes anywhere share a process
        // and nothing else: a retrieval aimed at the model upstream's own address is still a
        // loopback destination, refused, and never gains the credential by the coincidence.
        using var model = new UpstreamListener();
        using var egress = await EgressProcess.Start(model.Url, Cancel);
        using var client = egress.Client();

        using var response = await client.GetAsync(Fetch($"{model.Url}/v1/messages"), Cancel);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(model.Received);
    }

    [Fact]
    public async Task BuildsTheOutgoingRequestWithNoCredentialAndNothingOfTheCallers()
    {
        var target = new Uri("https://example.com/article?id=7");
        var incoming = new DefaultHttpContext();
        incoming.Request.Method = "GET";
        incoming.Request.Path = "/fetch";
        incoming.Request.QueryString = new QueryString($"?url={Uri.EscapeDataString(target.AbsoluteUri)}");
        incoming.Request.Headers.Authorization = $"Bearer {EgressProcess.InternalToken}";
        incoming.Request.Headers["x-api-key"] = EgressProcess.UpstreamCredential;
        incoming.Request.Headers.Cookie = "session=abc";
        incoming.Request.Headers["X-Grimoire-Run"] = "run-under-test";
        incoming.Request.Headers["Proxy-Authorization"] = "Basic Zm9vOmJhcg==";

        using var outgoing = new HttpRequestMessage(HttpMethod.Get, (Uri?)null);
        await FetchRoute.Transformer(target).TransformRequestAsync(incoming, outgoing, "https://example.com", Cancel);

        Assert.Equal(target, outgoing.RequestUri);
        Assert.Null(outgoing.Headers.Authorization);
        Assert.False(outgoing.Headers.Contains("x-api-key"));
        Assert.False(outgoing.Headers.Contains("Cookie"));
        Assert.False(outgoing.Headers.Contains("X-Grimoire-Run"));
        Assert.False(outgoing.Headers.Contains("Proxy-Authorization"));
        Assert.Null(outgoing.Headers.Host);
    }

    [Fact]
    public async Task GivesTheHubAReasonItRecordsOnTheTask()
    {
        // The hub and this proxy together, as the deployment runs them: the hub's own check steps
        // aside when a fetch route is configured, so the refusal a task records is the proxy's.
        using var origin = new UpstreamListener();
        using var model = new UpstreamListener();
        using var egress = await EgressProcess.Start(model.Url, Cancel);
        using var wiki = new WikiRepositoryFixture();
        using var hub = GrimoireHub.Start(
            wiki,
            extraEnvironment: new Dictionary<string, string?> { ["GRIMOIRE_FETCH_PROXY"] = $"{egress.BaseAddress}{FetchRoute.Path}" });

        using var submitted = await hub.Submit("url", $"{origin.Url}/article", Cancel);
        submitted.EnsureSuccessStatusCode();
        var id = (await submitted.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetString()!;
        var task = await hub.WaitForEnd(id, Cancel);

        Assert.Equal("failed", task.GetProperty("state").GetString());
        Assert.Contains(
            "not a destination this system retrieves",
            task.GetProperty("failureReason").GetString(),
            StringComparison.Ordinal);
        Assert.Empty(origin.Received);
    }

    [Fact]
    public async Task DropsAReasonHeaderAnOriginSendsSoOnlyTheProxyCanGiveOne()
    {
        // The origin is user-chosen. A reason it sent would be recorded on the task as if the
        // proxy had refused — attacker-controlled text in an operator's surface.
        var incoming = new DefaultHttpContext();
        using var origin = new HttpResponseMessage(HttpStatusCode.Forbidden);
        origin.Headers.TryAddWithoutValidation(FetchRoute.ReasonHeader, "spoofed by the origin");

        await FetchRoute.Transformer(new Uri("https://example.com/")).TransformResponseAsync(incoming, origin, Cancel);

        Assert.False(incoming.Response.Headers.ContainsKey(FetchRoute.ReasonHeader));
    }

    private static string Fetch(string url) => $"/fetch?url={Uri.EscapeDataString(url)}";

    private static string Reason(HttpResponseMessage response) =>
        response.Headers.TryGetValues(FetchRoute.ReasonHeader, out var values) ? string.Join(" ", values) : "";
}
