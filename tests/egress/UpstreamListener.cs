using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Net;
using System.Text;

namespace Grimoire.Tests.Egress;

/// <summary>One request an <see cref="UpstreamListener"/> received, headers as they arrived.</summary>
public sealed record ReceivedRequest(string Method, string PathAndQuery, NameValueCollection Headers, string Body);

/// <summary>
/// A real HTTP listener standing where the model upstream or a retrieval origin stands, recording
/// exactly what arrived — so an assertion about what the proxy forwards is an assertion about bytes
/// on a socket, not about a transform's return value.
/// </summary>
public sealed class UpstreamListener : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly Func<HttpListenerContext, Task> _respond;

    /// <summary>Starts a listener on loopback that answers every request with <paramref name="respond"/>.</summary>
    public UpstreamListener(Func<HttpListenerContext, Task>? respond = null)
    {
        _respond = respond ?? (context => Answer(context, HttpStatusCode.OK, "application/json", """{"ok":true}"""));

        var port = FreePort();
        Url = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add($"{Url}/");
        _listener.Start();
        _ = Task.Run(Serve);
    }

    /// <summary>The listener's base URL.</summary>
    public string Url { get; }

    /// <summary>Every request received so far, in arrival order.</summary>
    public ConcurrentQueue<ReceivedRequest> Received { get; } = new();

    /// <summary>Writes a complete response.</summary>
    public static async Task Answer(HttpListenerContext context, HttpStatusCode status, string contentType, string body)
    {
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = contentType;
        var bytes = Encoding.UTF8.GetBytes(body);
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stopping.Cancel();
        _listener.Close();
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

            _ = Task.Run(async () =>
            {
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                Received.Enqueue(new ReceivedRequest(
                    context.Request.HttpMethod,
                    context.Request.Url?.PathAndQuery ?? "",
                    new NameValueCollection(context.Request.Headers),
                    body));

                try
                {
                    await _respond(context);
                }
                catch (Exception) when (_stopping.IsCancellationRequested)
                {
                    // Torn down mid-response: the test is over.
                }
            });
        }
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
