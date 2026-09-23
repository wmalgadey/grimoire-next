using System.Diagnostics;
using Grimoire.Agent;
using Grimoire.Agent.Adapters;

namespace Grimoire.Contract.Tests;

/// <summary>
/// Ending an agent that outlived a stop Grimoire could not act on, against a real process
/// (RUNS-006).
/// </summary>
/// <remarks>
/// <para>
/// Terminating a process is an act on the operating system, and no in-memory adapter can make it
/// true — which is what puts this at the Contract level (Constitution III.4). The child is an
/// ordinary long-lived process this suite starts, not <c>claude</c>: nothing here is about the
/// CLI's protocol, so <b>no sign-in is needed and this runs in CI</b>, unlike the three tests
/// DEC-021 excludes.
/// </para>
/// <para>
/// The third test is the one that protects something outside Grimoire. A recorded identifier is
/// not an identity — those numbers are reused, and after a reboot one almost certainly belongs to
/// another program on the owner's machine — so a live process whose number matches but whose start
/// time does not is left alone (research.md R-11).
/// </para>
/// </remarks>
[Trait("level", "contract")]
[Trait("req", "RUNS-006")]
public sealed class AgentProcessTests
{
    private readonly HarnessProcess harness = new(HarnessSettings.Default(new Uri("http://127.0.0.1:1")));

    /// <summary>
    /// A child that stays alive until it is ended, the way an agent mid-run does. `sleep` is on
    /// both the developer's macOS and CI's ubuntu-latest.
    /// </summary>
    private static Process ALongLivedChild()
    {
        var child = Process.Start(new ProcessStartInfo("/bin/sleep", "600") { UseShellExecute = false })
            ?? throw new InvalidOperationException("the child did not start");

        return child;
    }

    private static AgentProcessIdentity IdentityOf(Process child) =>
        new(child.Id, new DateTimeOffset(child.StartTime).ToUniversalTime());

    [Fact]
    public async Task Terminate_EndsTheProcess_WhenTheRecordedIdentityIsStillThatAgent()
    {
        using var child = ALongLivedChild();
        var identity = IdentityOf(child);

        harness.Terminate(identity);

        await child.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.True(child.HasExited);
    }

    [Fact]
    public async Task Terminate_DoesNothing_WhenTheProcessIsAlreadyGone()
    {
        using var child = ALongLivedChild();
        var identity = IdentityOf(child);
        child.Kill();
        await child.WaitForExitAsync(TestContext.Current.CancellationToken);

        // The usual case after a clean stop: the agent went with Grimoire, and its run reads failed
        // as it would have anyway. Recorded identities are never cleared, so this is not an error.
        harness.Terminate(identity);
    }

    [Fact]
    public async Task Terminate_LeavesTheProcessAlone_WhenOnlyTheIdentifierMatches()
    {
        using var unrelated = ALongLivedChild();

        // The same number, another moment — which is what a reused identifier looks like, and what
        // every identifier looks like after a reboot. Ending this would kill a program that has
        // nothing to do with Grimoire.
        var reused = IdentityOf(unrelated) with { StartedAt = IdentityOf(unrelated).StartedAt.AddHours(-1) };

        harness.Terminate(reused);

        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        Assert.False(unrelated.HasExited);

        unrelated.Kill();
    }
}
