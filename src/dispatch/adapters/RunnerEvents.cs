using System.Text.Json;
using System.Text.Json.Serialization;
using Grimoire.Tasks;

namespace Grimoire.Dispatch.Adapters;

/// <summary>
/// The hub's half of the runner protocol envelope (contracts/runner-protocol.md): one JSON object
/// per line, no embedded newlines, UTF-8.
/// </summary>
/// <remarks>
/// These shapes are the port-boundary translation of <see cref="InstructionVersion"/>,
/// <see cref="ToolGrant"/>, <see cref="ToolCall"/> and the run outcome across a process boundary —
/// not a parallel model of them (constitution VII.2). They stay free of file descriptors and of
/// paths outside the wiki repository, so moving the runner into its own container is a transport
/// change rather than a redesign.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(InstructionLoadedEvent), "instruction_loaded")]
[JsonDerivedType(typeof(ToolGrantEvent), "tool_grant")]
[JsonDerivedType(typeof(ToolCallEvent), "tool_call")]
[JsonDerivedType(typeof(RunEndEvent), "run_end")]
public abstract record RunnerEvent;

/// <summary>
/// Emitted first, always, before anything else — and before any model call (FR-014).
/// </summary>
public sealed record InstructionLoadedEvent(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("byteLength")] int ByteLength) : RunnerEvent
{
    /// <summary>The artifact's own representation of the same fact.</summary>
    public InstructionVersion ToInstructionVersion() => new(Path, Sha256, ByteLength);
}

/// <summary>
/// The set of actions the agent may take. Exactly two, in 100% of runs (FR-010, SC-004). The hub
/// records what it is told <b>and</b> asserts it equals the grant it configured.
/// </summary>
public sealed record ToolGrantEvent(
    [property: JsonPropertyName("tools")] IReadOnlyList<string> Tools) : RunnerEvent;

/// <summary>
/// One call the agent made, emitted as it resolves, in the order made. Refused and failed calls
/// are emitted the same way as successful ones — a refusal is a recorded call, not an absence
/// (FR-011, FR-021).
/// </summary>
public sealed record ToolCallEvent(
    [property: JsonPropertyName("seq")] int Seq,
    [property: JsonPropertyName("tool")] string Tool,
    [property: JsonPropertyName("target")] string? Target,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("detail")] string? Detail,
    [property: JsonPropertyName("at")] DateTimeOffset At) : RunnerEvent
{
    /// <summary>The artifact's own representation of the same call.</summary>
    public ToolCall ToToolCall() => new(
        Seq,
        Tool,
        Target,
        Outcome switch
        {
            "ok" => ToolCallOutcome.Ok,
            "failed" => ToolCallOutcome.Failed,
            "refused" => ToolCallOutcome.Refused,
            _ => throw new RunnerProtocolException($"Unknown tool-call outcome '{Outcome}'."),
        },
        Detail,
        At);
}

/// <summary>
/// How the run ended. A crash produces no <c>run_end</c> at all, which the hub treats identically:
/// no commit (FR-017).
/// </summary>
/// <param name="CommitMessage">
/// The run's final assistant message text, verbatim — commit-message wording is judgment and lives
/// in the instruction file (research R9). Empty means the hub commits under its fixed constant.
/// </param>
public sealed record RunEndEvent(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("failureReason")] string? FailureReason,
    [property: JsonPropertyName("commitMessage")] string? CommitMessage,
    [property: JsonPropertyName("toolCallCount")] int ToolCallCount) : RunnerEvent
{
    /// <summary>Whether the agent stopped on its own or reported a failure.</summary>
    public RunOutcomeKind ToOutcome() => Outcome switch
    {
        "completed" => RunOutcomeKind.Completed,
        "failed" => RunOutcomeKind.Failed,
        _ => throw new RunnerProtocolException($"Unknown run outcome '{Outcome}'."),
    };
}

/// <summary>Hub → runner. One JSON object per line on the runner's stdin.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(DispatchMessage), "dispatch")]
[JsonDerivedType(typeof(ProceedMessage), "proceed")]
public abstract record RunnerMessage;

/// <summary>
/// The run's input.
/// </summary>
/// <param name="SourceText">
/// The source <b>whole</b> — no size limit, no truncation, no summarisation (FR-029).
/// </param>
public sealed record DispatchMessage(
    [property: JsonPropertyName("taskId")] string TaskId,
    [property: JsonPropertyName("sourceText")] string SourceText,
    [property: JsonPropertyName("maxToolCalls")] int MaxToolCalls,
    [property: JsonPropertyName("maxElapsedMs")] int MaxElapsedMs) : RunnerMessage;

/// <summary>
/// The gate. Sent only after <c>instruction_loaded</c> and <c>tool_grant</c> are durably persisted
/// on the task, which is what makes FR-013 and FR-014's "before the first model call" a property a
/// test can assert rather than a hope about scheduling.
/// </summary>
public sealed record ProceedMessage : RunnerMessage;

/// <summary>
/// A line on the runner's stdout that is not a message this protocol defines. Rejected rather than
/// ignored: a runner speaking an envelope the hub does not know is a failed run, not a quiet one.
/// </summary>
public sealed class RunnerProtocolException(string message) : Exception(message);

/// <summary>Reads and writes the envelope.</summary>
public static class RunnerProtocol
{
    /// <summary>
    /// Serialisation settings shared by both directions. Timestamps are ISO-8601; unknown members
    /// are an error, so a protocol change cannot pass silently.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Parses one NDJSON line from the runner.</summary>
    /// <exception cref="RunnerProtocolException">The line is not a known event.</exception>
    public static RunnerEvent ParseEvent(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<RunnerEvent>(line, Json)
                ?? throw new RunnerProtocolException("The runner emitted a null event.");
        }
        catch (JsonException exception)
        {
            throw new RunnerProtocolException(
                $"The runner emitted a line this protocol does not define: {exception.Message}");
        }
    }

    /// <summary>Renders one NDJSON line for the runner's stdin.</summary>
    public static string Serialise(RunnerMessage message) =>
        JsonSerializer.Serialize(message, Json);
}
