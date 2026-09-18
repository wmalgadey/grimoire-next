using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Grimoire.Tests.Support;

namespace Grimoire.Tests.Egress;

/// <summary>
/// The egress proxy run as a <b>real operating-system process</b> on real Kestrel, configured the
/// only way production configures it — its environment (contracts/deployment.md, "Egress proxy").
/// </summary>
/// <remarks>
/// A process rather than an in-process host because the proxy is its own deployable: what is under
/// test is the built <c>Grimoire.Egress</c> application, not a composition a test assembled.
/// </remarks>
public sealed class EgressProcess : IDisposable
{
    /// <summary>The token a caller has to present on the model route.</summary>
    public const string InternalToken = "an-opaque-internal-token";

    /// <summary>The upstream credential only the proxy holds.</summary>
    public const string UpstreamCredential = "sk-ant-the-real-upstream-credential";

    // The suite and the proxy come out of the same `dotnet build`, so run the proxy built in this
    // suite's configuration — a Release-only CI has no Debug build.
    private const string Configuration =
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    private readonly Process _process;
    private readonly List<string> _output = [];
    private readonly object _lock = new();

    private EgressProcess(Process process, string baseAddress)
    {
        _process = process;
        BaseAddress = baseAddress;
    }

    /// <summary>Where the proxy listens — what the hub has as its model and fetch base.</summary>
    public string BaseAddress { get; }

    /// <summary>Everything the proxy has written so far, stdout and stderr interleaved.</summary>
    public IReadOnlyList<string> Output
    {
        get
        {
            lock (_lock)
            {
                return [.. _output];
            }
        }
    }

    /// <summary>Starts the proxy with <paramref name="modelUpstream"/> as its one allowlisted upstream.</summary>
    public static async Task<EgressProcess> Start(string modelUpstream, CancellationToken cancellationToken)
    {
        var port = FreePort();
        var assembly = Path.Combine(
            ScriptedModelFixture.RepositoryRoot, "src", "egress", "bin", Configuration, "net10.0", "Grimoire.Egress.dll");

        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add(assembly);

        // Replaced, not merged: nothing from the test process's environment reaches the proxy.
        info.Environment.Clear();
        info.Environment["PATH"] = Environment.GetEnvironmentVariable("PATH");
        info.Environment["HOME"] = Environment.GetEnvironmentVariable("HOME");
        info.Environment["DOTNET_ROOT"] = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        info.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        info.Environment["GRIMOIRE_EGRESS_MODEL_UPSTREAM"] = modelUpstream;
        info.Environment["GRIMOIRE_EGRESS_MODEL_CREDENTIAL"] = UpstreamCredential;
        info.Environment["GRIMOIRE_EGRESS_INTERNAL_TOKEN"] = InternalToken;

        var process = Process.Start(info)
            ?? throw new InvalidOperationException("The egress proxy could not be started.");
        var egress = new EgressProcess(process, $"http://127.0.0.1:{port}");
        process.OutputDataReceived += (_, args) => egress.Record(args.Data);
        process.ErrorDataReceived += (_, args) => egress.Record(args.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await egress.WaitUntilListening(port, cancellationToken);
            return egress;
        }
        catch
        {
            // Nobody owns a proxy that never became a fixture; left running it would outlive the suite.
            egress.Dispose();
            throw;
        }
    }

    /// <summary>A client whose requests go to the proxy, with no redirects followed.</summary>
    public HttpClient Client() =>
        new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false })
        {
            BaseAddress = new Uri(BaseAddress),
        };

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(10_000);
        }

        _process.Dispose();
    }

    private void Record(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_lock)
        {
            _output.Add(line);
        }
    }

    private async Task WaitUntilListening(int port, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    "The egress proxy exited before it listened:" + Environment.NewLine
                    + string.Join(Environment.NewLine, Output));
            }

            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        throw new TimeoutException("The egress proxy did not listen within 30 seconds.");
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
