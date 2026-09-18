using System.Diagnostics;
using System.Text;

namespace Grimoire.Tests.Deployment;

/// <summary>What one container CLI invocation produced.</summary>
public sealed record CommandResult(int ExitCode, string Stdout, string Stderr)
{
    /// <summary>Whether the command succeeded.</summary>
    public bool Succeeded => ExitCode is 0;
}

/// <summary>
/// The container runtime's CLI — <c>docker</c>, or whatever <c>GRIMOIRE_CONTAINER_CLI</c> names
/// (Podman's <c>docker</c>-compatible CLI works). Real containers, real networks, real images: the
/// deployment suite asserts the built image, not a description of it.
/// </summary>
/// <remarks>
/// TS-18 needs a container runtime. Where there is none it skips with a reason that says so — a
/// developer convenience, not a disabled gate (plan, Test Strategy): under CI (<c>CI</c> set) a
/// missing runtime is a failure, because the gate runs there on every PR.
/// </remarks>
public static class ContainerRuntime
{
    private static readonly Lazy<string?> Unavailability = new(Probe);

    /// <summary>The CLI invoked.</summary>
    public static string Cli { get; } =
        Environment.GetEnvironmentVariable("GRIMOIRE_CONTAINER_CLI") is { Length: > 0 } cli ? cli : "docker";

    /// <summary>Why there is no usable container runtime here, or <c>null</c> when there is one.</summary>
    public static string? UnavailableBecause => Unavailability.Value;

    /// <summary>Skips the calling test, with the reason, when there is no usable container runtime.</summary>
    public static void RequireOrSkip()
    {
        if (Unavailability.Value is not { } reason)
        {
            return;
        }

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
        {
            throw new InvalidOperationException(
                $"TS-18 is a CI gate and cannot run: {reason} CI must provide a container runtime.");
        }

        Assert.Skip($"TS-18 needs a container runtime and this machine has none usable: {reason} It runs in CI on every PR.");
    }

    /// <summary>Runs the CLI and returns what happened, whatever happened.</summary>
    public static async Task<CommandResult> Run(IEnumerable<string> arguments, TimeSpan? timeout = null)
    {
        var info = new ProcessStartInfo(Cli)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"'{Cli}' could not be started.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"'{Cli} {string.Join(' ', info.ArgumentList)}' did not finish in time.");
        }

        return new CommandResult(process.ExitCode, await stdout, await stderr);
    }

    /// <summary>Runs the CLI and returns its stdout, failing loudly when it fails.</summary>
    public static async Task<string> Must(IEnumerable<string> arguments, TimeSpan? timeout = null)
    {
        var list = arguments.ToList();
        var result = await Run(list, timeout);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                new StringBuilder()
                    .AppendLine($"'{Cli} {string.Join(' ', list)}' exited {result.ExitCode}.")
                    .AppendLine(result.Stderr)
                    .Append(result.Stdout)
                    .ToString());
        }

        return result.Stdout.Trim();
    }

    private static string? Probe()
    {
        try
        {
            var result = Run(["info"], TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
            return result.Succeeded ? null : $"'{Cli} info' exited {result.ExitCode}: {FirstLine(result.Stderr)}.";
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or TimeoutException or InvalidOperationException)
        {
            return $"'{Cli}' could not be run: {exception.Message}.";
        }
    }

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
}
