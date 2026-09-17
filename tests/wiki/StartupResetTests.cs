using Grimoire.Tests.Support;

namespace Grimoire.Tests.Wiki;

/// <summary>
/// T092 / TS-12, startup half (FR-017, SC-003). A hub killed mid-write leaves a dirty working
/// tree; the next start resets it, reaching content byte-identical to the pre-run commit with no
/// commit created.
/// </summary>
/// <remarks>
/// This is the containment guarantee at its weakest moment. The run-end failure path resets the
/// tree, but a process that is killed never reaches its own failure path — so the wiki is left
/// holding half of what an agent wrote, with nothing to clean it up but the next boot. Without
/// this, the first commit after a crash would silently carry the dead run's leftovers.
/// </remarks>
public sealed class StartupResetTests
{
    [Fact]
    public async Task ResetsWhatAKilledRunLeftBehind()
    {
        using var wiki = new WikiRepositoryFixture();
        var contentBefore = wiki.Snapshot();
        var tipBefore = wiki.Head();
        var commitsBefore = wiki.CommitCount();
        using var model = ScriptedModelFixture.Start("write-then-hang");
        var stateDb = Path.Combine(Path.GetTempPath(), $"grimoire-reset-{Guid.NewGuid():N}.db");

        using (var hub = await HubProcess.Start(
            wiki, model, stateDb, cancellationToken: TestContext.Current.CancellationToken))
        {
            var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
            await hub.WaitForState(id, "running", TestContext.Current.CancellationToken);
            // Wait for the write to land in the tree, so there is something to reset.
            await WaitUntilDirty(wiki, TestContext.Current.CancellationToken);
            hub.KillUngracefully();
        }

        // The premise: the kill really did leave residue behind.
        Assert.False(wiki.IsClean(), "The killed run left nothing behind, so this proves nothing.");

        using var restarted = await HubProcess.Start(
            wiki, model, stateDb, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(wiki.IsClean(), "Startup did not reset the working tree.");
        Assert.Equal(contentBefore, wiki.Snapshot());
        // Reset, not committed: a crashed run contributes nothing to history (FR-017).
        Assert.Equal(tipBefore, wiki.Head());
        Assert.Equal(commitsBefore, wiki.CommitCount());
    }

    private static async Task WaitUntilDirty(WikiRepositoryFixture wiki, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (!wiki.IsClean())
            {
                return;
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException("The run never wrote anything into the working tree.");
    }
}
