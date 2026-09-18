using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Tests.Support;

namespace Grimoire.Tests.Hub;

/// <summary>
/// Quality gate 6 / T055 / TS-13, US1 rows. Observability is the control loop, not diagnostics
/// (constitution IV): every declared signal is emitted through the <b>production composition
/// root</b> <i>and</i> appears on the response field the surface renders.
/// </summary>
/// <remarks>
/// Asserting only the emission would let a signal exist that nobody can read; asserting only the
/// response field would let a surface show something the logs never said. Both halves, or the
/// signal is not declared.
/// </remarks>
[Collection("observability")]
public sealed class ObservabilityTests
{
    [Fact]
    public async Task TaskCreatedReachesTheTaskListRow()
    {
        using var wiki = new WikiRepositoryFixture();
        using var hub = GrimoireHub.Start(wiki);

        var id = await hub.SubmitText("notes worth keeping", TestContext.Current.CancellationToken);

        // Surface: task list. Operator decision: did my submission become exactly one task?
        var list = await hub.Client.GetFromJsonAsync<JsonElement>(
            "/api/tasks", TestContext.Current.CancellationToken);
        var row = list.GetProperty("tasks").EnumerateArray().Single();
        Assert.Equal(id, row.GetProperty("id").GetString());
        Assert.True(row.TryGetProperty("submittedAt", out _));
        Assert.True(row.GetProperty("source").TryGetProperty("preview", out _));
    }

    [Fact]
    public async Task TaskStateChangedReachesTheStateFieldOnBothSurfaces()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        // Surface: task list and task view — the state field. "Is anything stuck?" (SC-006).
        Assert.Equal("completed", task.GetProperty("state").GetString());
        var list = await hub.Client.GetFromJsonAsync<JsonElement>(
            "/api/tasks", TestContext.Current.CancellationToken);
        Assert.Equal("completed", list.GetProperty("tasks").EnumerateArray()
            .Single(row => row.GetProperty("id").GetString() == id).GetProperty("state").GetString());
    }

    [Fact]
    public async Task RunDispatchedReachesTheInstructionVersionAndGrantedToolSet()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        // Surface: task view. Decisions: which instruction revision is this attributable to
        // (SC-009), and was the grant deny-by-default (SC-004)?
        var run = task.GetProperty("run");
        Assert.Matches("^[0-9a-f]{64}$",
            run.GetProperty("instructionVersion").GetProperty("sha256").GetString()!);
        Assert.Equal(2, run.GetProperty("toolGrant").GetProperty("tools").EnumerateArray().Count());
        Assert.True(run.GetProperty("toolGrant").TryGetProperty("recordedAt", out _));
    }

    [Fact]
    public async Task RunToolCallReachesTheOrderedToolCallRecordWithTargetAndOutcome()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        // Surface: task view. Decisions: did the run consult the wiki before writing (SC-010),
        // what did it touch, what was refused?
        var calls = task.GetProperty("run").GetProperty("toolCalls").EnumerateArray().ToList();
        Assert.NotEmpty(calls);
        foreach (var call in calls)
        {
            Assert.True(call.GetProperty("seq").GetInt32() >= 1);
            Assert.False(string.IsNullOrWhiteSpace(call.GetProperty("tool").GetString()));
            Assert.Contains(call.GetProperty("outcome").GetString(), new[] { "ok", "failed", "refused" });
            Assert.True(call.TryGetProperty("target", out _));
            Assert.True(call.TryGetProperty("at", out _));
        }
    }

    [Fact]
    public async Task RunEndedReachesTheOutcomeCountAndDuration()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        // Surface: task view — state and failure reason. "Did it finish, and is the limit too
        // tight?" (SC-011).
        var run = task.GetProperty("run");
        Assert.Equal("completed", run.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, run.GetProperty("failureReason").ValueKind);
        Assert.Equal(2, run.GetProperty("toolCallCount").GetInt32());
        Assert.True(run.GetProperty("durationMs").GetInt32() >= 0);
    }

    [Fact]
    public async Task RunEndedCarriesAFailureReasonWhenTheRunFailed()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("never-stopping");
        using var hub = GrimoireHub.Start(wiki, model, extraEnvironment: new Dictionary<string, string?>
        {
            ["GRIMOIRE_RUN_MAX_TOOL_CALLS"] = "3",
        });

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken, TimeSpan.FromSeconds(90));

        Assert.Equal("failed", task.GetProperty("run").GetProperty("outcome").GetString());
        Assert.False(string.IsNullOrWhiteSpace(task.GetProperty("run").GetProperty("failureReason").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(task.GetProperty("failureReason").GetString()));
    }

    [Fact]
    public async Task WikiCommittedReachesTheCommitIdentityAndDiff()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        // Surface: task view — commit identity and diff. "Were the wiki changes appropriate?"
        // (SC-002, SC-008).
        var commit = task.GetProperty("run").GetProperty("commit");
        Assert.False(string.IsNullOrWhiteSpace(commit.GetProperty("sha").GetString()));
        Assert.NotEmpty(commit.GetProperty("fileDiffs").EnumerateArray());
    }

    [Fact]
    public async Task ModelEndpointUnreachableIsDistinguishableFromTheAgentFailing()
    {
        // "Is the run failing on its own account, or because the egress path is broken?" is a
        // different operator decision, so it is a different signal with its own reason text.
        using var wiki = new WikiRepositoryFixture();
        using var hub = GrimoireHub.Start(wiki); // No model: GRIMOIRE_MODEL_BASE_URL points at a dead port.

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken, TimeSpan.FromSeconds(90));

        Assert.Equal("failed", task.GetProperty("state").GetString());
        var reason = task.GetProperty("failureReason").GetString();
        Assert.False(string.IsNullOrWhiteSpace(reason));

        // The reason names the endpoint, so an operator can tell this from the agent failing.
        Assert.Contains("egress path", reason!, StringComparison.OrdinalIgnoreCase);

        // Surface: /readyz says the same thing. 503 is the point — a replica whose one permitted
        // destination is unreachable should not be taking traffic.
        using var readyz = await hub.Client.GetAsync("/readyz", TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, readyz.StatusCode);
        var readiness = await readyz.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("failed", readiness.GetProperty("checks").GetProperty("egress").GetString());
    }

    [Fact]
    public async Task ReadinessIsEmittedWithEachCheckAndReachesTheReadyzBody()
    {
        // grimoire.hub.readiness: "should this replica be taking traffic?" Emitted from the
        // readiness path itself, so the log line and the /readyz body are the same answer. No model
        // is started, so the egress check fails and the other two pass — every field has to carry
        // its own value, not a shared one.
        using var wiki = new WikiRepositoryFixture();
        using var hub = await HubProcess.Start(wiki, cancellationToken: TestContext.Current.CancellationToken);

        using var readyz = await hub.Client.GetAsync("/readyz", TestContext.Current.CancellationToken);
        var body = await readyz.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var checks = body.GetProperty("checks");
        Assert.Equal("ok", checks.GetProperty("wikiRepo").GetString());
        Assert.Equal("ok", checks.GetProperty("stateDb").GetString());
        Assert.Equal("failed", checks.GetProperty("egress").GetString());

        await Task.Delay(500, TestContext.Current.CancellationToken);
        var line = Assert.Single(hub.Stdout, line => line.Contains("grimoire.hub.readiness", StringComparison.Ordinal));
        var state = JsonDocument.Parse(line).RootElement.GetProperty("State");
        Assert.Equal("ok", state.GetProperty("WikiRepo").GetString());
        Assert.Equal("ok", state.GetProperty("StateDb").GetString());
        Assert.Equal("failed", state.GetProperty("Egress").GetString());
        Assert.Equal("not-ready", state.GetProperty("Status").GetString());
    }

    [Fact]
    public async Task EmitsEveryUs1SignalAsStructuredJsonOnStdout()
    {
        // The transport half of the gate: structured JSON on stdout, one event per line — the
        // container convention (contracts/deployment.md "Logging"). Asserted against a hub run as
        // a real process, because in-process hosting captures logs differently than production.
        var logs = await HubProcess.RunOneIngest("read-then-write", TestContext.Current.CancellationToken);

        // The declared fields, not just the name: a row without them answers no question (plan IV).
        var created = State(logs, "grimoire.task.created").Single();
        Assert.False(string.IsNullOrEmpty(created.GetProperty("TaskId").GetString()));
        Assert.Equal("text", created.GetProperty("Kind").GetString());

        Assert.Contains(State(logs, "grimoire.task.state_changed"),
            state => state.GetProperty("State").GetString() == "completed");

        var dispatched = State(logs, "grimoire.run.dispatched").Single();
        Assert.Matches("^[0-9a-f]{64}$", dispatched.GetProperty("InstructionVersion").GetString()!);
        Assert.Equal("mcp__wiki__read_page,mcp__wiki__write_page", dispatched.GetProperty("ToolGrant").GetString());

        var calls = State(logs, "grimoire.run.tool_call").ToList();
        Assert.Equal([1, 2], calls.Select(call => call.GetProperty("Seq").GetInt32()));
        Assert.Equal(["mcp__wiki__read_page", "mcp__wiki__write_page"], calls.Select(call => call.GetProperty("Tool").GetString()));
        Assert.Equal(["index.md", "topics/scripted.md"], calls.Select(call => call.GetProperty("Target").GetString()));
        Assert.All(calls, call => Assert.Equal("ok", call.GetProperty("Outcome").GetString()));

        // Exactly once per run: a second event would make "did it finish?" ambiguous.
        var ended = State(logs, "grimoire.run.ended").Single();
        Assert.Equal("completed", ended.GetProperty("Outcome").GetString());
        Assert.Equal(JsonValueKind.Null, ended.GetProperty("FailureReason").ValueKind);
        Assert.Equal(2, ended.GetProperty("ToolCallCount").GetInt32());
        Assert.True(ended.GetProperty("DurationMs").GetInt32() > 0);

        var committed = State(logs, "grimoire.wiki.committed").Single();
        Assert.Matches("^[0-9a-f]{40}$", committed.GetProperty("CommitSha").GetString()!);
        Assert.Equal(1, committed.GetProperty("FilesChanged").GetInt32());

        // One event per line, every line is JSON, and every event says when it happened — in UTC,
        // as ISO 8601, so lines from different sources order and join without a timezone guess.
        foreach (var line in logs.Where(line => line.Contains("grimoire.", StringComparison.Ordinal)))
        {
            using var document = JsonDocument.Parse(line);
            Assert.True(document.RootElement.TryGetProperty("Timestamp", out var timestamp), line);
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", timestamp.GetString()!);
        }
    }

    [Fact]
    public async Task ModelEndpointErrorsCarryTheStatusTheEndpointAnswered()
    {
        // The proxy is reachable and answers with an error of its own — the case a TCP probe cannot
        // see. The signal says which status, and the task says it is the egress path, not the agent.
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("endpoint-refuses");
        using var hub = await HubProcess.Start(wiki, model, cancellationToken: TestContext.Current.CancellationToken);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        Assert.Equal("failed", task.GetProperty("state").GetString());
        Assert.Contains("HTTP 403", task.GetProperty("failureReason").GetString()!, StringComparison.Ordinal);

        await Task.Delay(500, TestContext.Current.CancellationToken);
        var signal = State(hub.Stdout, "grimoire.run.model_endpoint_unreachable").Single();
        Assert.Equal(id, signal.GetProperty("TaskId").GetString());
        Assert.Equal("403", signal.GetProperty("Status").GetString());
        Assert.Equal(model.BaseUrl, signal.GetProperty("Endpoint").GetString());
    }

    /// <summary>The structured state of every stdout event carrying a signal, in emission order.</summary>
    private static IEnumerable<JsonElement> State(IEnumerable<string> logs, string signal) =>
        logs.Where(line => line.Contains(signal, StringComparison.Ordinal))
            .Select(line => JsonDocument.Parse(line).RootElement)
            .Where(root => root.GetProperty("State").GetProperty("{OriginalFormat}").GetString()!
                .StartsWith(signal + " ", StringComparison.Ordinal))
            .Select(root => root.GetProperty("State").Clone());
}

/// <summary>Groups the observability suites so they do not contend for the same environment.</summary>
[CollectionDefinition("observability", DisableParallelization = true)]
public sealed class ObservabilityCollection;
