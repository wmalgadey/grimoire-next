using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Tests.Support;

namespace Grimoire.Tests.Hub;

/// <summary>
/// Quality gate 6 / T083 / TS-13, US2 rows. The two revert signals are emitted through the
/// <b>production composition root</b> <i>and</i> appear on the task view (constitution IV).
/// </summary>
/// <remarks>
/// Both halves, or the signal is not declared. A row nobody can read is diagnostics; a surface
/// field the logs never mention cannot be corroborated when someone asks what happened to their
/// wiki.
/// </remarks>
[Collection("observability")]
public sealed class ObservabilityRevertTests
{
    [Fact]
    public async Task RevertEligibilityReachesTheRevertSectionOfTheTaskView()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("first source", TestContext.Current.CancellationToken);
        await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        // Offered, with no reason to give: the action itself is the answer.
        var offered = await hub.GetTask(id, TestContext.Current.CancellationToken);
        Assert.True(offered.GetProperty("revertEligibility").GetProperty("eligible").GetBoolean());
        Assert.True(offered.GetProperty("revertEligibility").GetProperty("reason").ValueKind is JsonValueKind.Null);

        wiki.Write("topics/edited-by-hand.md", "# Edited\n");
        wiki.Commit("a later change to the wiki");

        // Surface: task view. Operator decision: why is revert not offered here? (SC-005, FR-027)
        var superseded = await hub.GetTask(id, TestContext.Current.CancellationToken);
        var eligibility = superseded.GetProperty("revertEligibility");
        Assert.False(eligibility.GetProperty("eligible").GetBoolean());
        Assert.Equal("superseded", eligibility.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task RevertedReachesTheRevertCommitIdentityAndTheState()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        await hub.WaitForEnd(id, TestContext.Current.CancellationToken);
        using var response = await hub.Revert(id, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        // Surface: task view. Operator decision: did my undo actually land? (SC-005)
        var task = await hub.GetTask(id, TestContext.Current.CancellationToken);
        Assert.Equal("reverted", task.GetProperty("state").GetString());
        Assert.Equal(wiki.Head(), task.GetProperty("revert").GetProperty("revertCommitSha").GetString());
    }

    [Fact]
    public async Task EmitsBothRevertSignalsAsStructuredJsonOnStdout()
    {
        // The transport half of the gate: structured JSON on stdout, one event per line
        // (contracts/deployment.md "Logging"). Asserted against a hub run as a real process,
        // because in-process hosting captures logs differently than production.
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = await HubProcess.Start(
            wiki, model, cancellationToken: TestContext.Current.CancellationToken);

        using var created = await hub.Client.PostAsJsonAsync(
            "/api/tasks",
            new { kind = "text", value = "notes worth keeping" },
            TestContext.Current.CancellationToken);
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("id").GetString()!;
        await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        using var reverted = await hub.Client.PostAsync(
            $"/api/tasks/{id}/revert", content: null, TestContext.Current.CancellationToken);
        reverted.EnsureSuccessStatusCode();

        // Let the logger flush the last line before the process is torn down.
        await System.Threading.Tasks.Task.Delay(500, TestContext.Current.CancellationToken);
        var logs = hub.Stdout;

        foreach (var signal in new[] { "grimoire.wiki.revert_eligibility", "grimoire.wiki.reverted" })
        {
            Assert.Contains(logs, line => line.Contains(signal, StringComparison.Ordinal));
        }

        // The declared fields, not just the name: a row without them answers no question. The task
        // view is read more than once here, so these are "some line", not "the line".
        Assert.Contains(logs, line =>
            line.Contains("grimoire.wiki.revert_eligibility", StringComparison.Ordinal)
            && line.Contains("\"Eligible\":true", StringComparison.Ordinal));
        Assert.Contains(logs, line =>
            line.Contains("grimoire.wiki.reverted", StringComparison.Ordinal)
            && line.Contains(wiki.Head(), StringComparison.Ordinal));

        foreach (var line in logs.Where(line => line.Contains("grimoire.wiki.revert", StringComparison.Ordinal)))
        {
            var exception = Record.Exception(() => JsonDocument.Parse(line));
            Assert.Null(exception);
        }
    }
}
