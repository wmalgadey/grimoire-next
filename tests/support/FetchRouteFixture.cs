using System.Net;

namespace Grimoire.Tests.Support;

/// <summary>
/// A real proxy speaking the egress proxy's fetch-route protocol — <c>GET /fetch?url=…</c> — but
/// with no destination policy (contracts/deployment.md Topology).
/// </summary>
/// <remarks>
/// Not a double of the proxy — it is a proxy; the double in this system is the LLM alone
/// (constitution III.2). It exists because both destination policies refuse loopback, the hub's
/// and the real proxy's (<c>tests/egress/</c>), so a test origin on loopback is unreachable through
/// either. That is the production arrangement: with <c>GRIMOIRE_FETCH_PROXY</c> set the hub connects
/// to the proxy and the proxy reaches the destination, which is why the policy is enforced in both
/// places (ADR-0010).
/// </remarks>
public sealed class FetchRouteFixture : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly HttpClient _client = new(new HttpClientHandler { AllowAutoRedirect = false });

    public FetchRouteFixture()
    {
        var port = FreePort();
        Url = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add($"{Url}/");
        _listener.Start();

        _ = Task.Run(Serve);
    }

    /// <summary>The proxy's base URL.</summary>
    public string Url { get; }

    /// <summary>What the hub is configured with as <c>GRIMOIRE_FETCH_PROXY</c>.</summary>
    public string FetchRoute => $"{Url}/fetch";

    /// <inheritdoc />
    public void Dispose()
    {
        _stopping.Cancel();
        _listener.Close();
        _client.Dispose();
        _stopping.Dispose();
    }

    private async Task Serve()
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

            try
            {
                await Forward(context);
            }
            catch (Exception)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                context.Response.Close();
            }
        }
    }

    private async Task Forward(HttpListenerContext context)
    {
        // The destination is named in the query, exactly as the egress proxy's fetch route takes it.
        var target = context.Request.Url?.AbsolutePath == "/fetch" ? context.Request.QueryString["url"] : null;

        if (target is null)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            context.Response.Close();
            return;
        }

        using var upstream = await _client.GetAsync(target, HttpCompletionOption.ResponseContentRead, _stopping.Token);

        context.Response.StatusCode = (int)upstream.StatusCode;
        if (upstream.Content.Headers.ContentType is { } contentType)
        {
            context.Response.ContentType = contentType.ToString();
        }

        var body = await upstream.Content.ReadAsByteArrayAsync(_stopping.Token);
        await context.Response.OutputStream.WriteAsync(body, _stopping.Token);
        context.Response.Close();
    }

    private static int FreePort()
    {
        using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
