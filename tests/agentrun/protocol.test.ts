import { describe, expect, it } from "vitest";
import {
  parseHubMessage,
  parseRunnerEvent,
  ProtocolError,
  serialiseRunnerEvent,
} from "../../src/agentrun/src/run/protocol.js";

/**
 * T027 / contracts/runner-protocol.md. The envelope is "one JSON object per line, no embedded
 * newlines, UTF-8", and an unknown event type is rejected rather than ignored — a runner or a hub
 * speaking an envelope the other does not know is a failed run, not a quiet one.
 */
describe("the NDJSON envelope", () => {
  it("renders every event on a single line with no embedded newline", () => {
    const line = serialiseRunnerEvent({
      type: "run_end",
      outcome: "completed",
      failureReason: null,
      // A commit message is the agent's final text, verbatim — and may well contain newlines.
      commitMessage: "Add kafka topic page\n\nLinked from the streaming index.",
      toolCallCount: 4,
      modelEndpointStatus: null,
    });

    expect(line).not.toContain("\n");
    expect(JSON.parse(line)).toMatchObject({ type: "run_end", toolCallCount: 4 });
  });

  it("round-trips a multi-byte payload as UTF-8", () => {
    const commitMessage = "Add „Straße“ page — 日本語 also";
    const parsed = parseRunnerEvent(
      serialiseRunnerEvent({
        type: "run_end",
        outcome: "completed",
        failureReason: null,
        commitMessage,
        toolCallCount: 1,
        modelEndpointStatus: null,
      }),
    );

    expect(parsed.type).toBe("run_end");
    if (parsed.type === "run_end") {
      expect(parsed.commitMessage).toBe(commitMessage);
    }
  });
});

describe("runner → hub events", () => {
  it("accepts instruction_loaded with its full field set", () => {
    const event = parseRunnerEvent(
      JSON.stringify({
        type: "instruction_loaded",
        path: "src/instructions/ingest.md",
        sha256: "a".repeat(64),
        byteLength: 4213,
      }),
    );

    expect(event).toEqual({
      type: "instruction_loaded",
      path: "src/instructions/ingest.md",
      sha256: "a".repeat(64),
      byteLength: 4213,
    });
  });

  it("accepts tool_grant with exactly the granted pair", () => {
    const event = parseRunnerEvent(
      JSON.stringify({
        type: "tool_grant",
        tools: ["mcp__wiki__read_page", "mcp__wiki__write_page"],
      }),
    );

    expect(event.type).toBe("tool_grant");
  });

  it("accepts a refused tool_call, because a refusal is a recorded call and not an absence", () => {
    const event = parseRunnerEvent(
      JSON.stringify({
        type: "tool_call",
        seq: 2,
        tool: "Bash",
        target: "cat /etc/passwd",
        outcome: "refused",
        detail: "tool not granted",
        at: "2026-09-16T10:31:02.441Z",
      }),
    );

    expect(event).toMatchObject({ seq: 2, tool: "Bash", outcome: "refused" });
  });

  it.each(["ok", "failed", "refused"])("accepts the '%s' tool-call outcome", (outcome) => {
    expect(() =>
      parseRunnerEvent(
        JSON.stringify({
          type: "tool_call",
          seq: 1,
          tool: "mcp__wiki__read_page",
          target: "index.md",
          outcome,
          detail: null,
          at: "2026-09-16T10:31:02.441Z",
        }),
      ),
    ).not.toThrow();
  });
});

describe("what the envelope rejects rather than ignores", () => {
  it("rejects an unknown event type", () => {
    expect(() => parseRunnerEvent(JSON.stringify({ type: "definitely_not_an_event" }))).toThrow(
      ProtocolError,
    );
  });

  it("rejects an event with no type at all", () => {
    expect(() => parseRunnerEvent(JSON.stringify({ seq: 1 }))).toThrow(ProtocolError);
  });

  it("rejects a known event missing a required field", () => {
    expect(() =>
      parseRunnerEvent(JSON.stringify({ type: "instruction_loaded", path: "x" })),
    ).toThrow(ProtocolError);
  });

  it("rejects an unknown tool-call outcome", () => {
    expect(() =>
      parseRunnerEvent(
        JSON.stringify({
          type: "tool_call",
          seq: 1,
          tool: "mcp__wiki__read_page",
          target: "index.md",
          outcome: "maybe",
          detail: null,
          at: "2026-09-16T10:31:02.441Z",
        }),
      ),
    ).toThrow(ProtocolError);
  });

  it("rejects a line that is not JSON", () => {
    expect(() => parseRunnerEvent("this is a diagnostic, not an event")).toThrow(ProtocolError);
  });

  it("rejects a seq that is not 1-based", () => {
    expect(() =>
      parseRunnerEvent(
        JSON.stringify({
          type: "tool_call",
          seq: 0,
          tool: "mcp__wiki__read_page",
          target: "index.md",
          outcome: "ok",
          detail: null,
          at: "2026-09-16T10:31:02.441Z",
        }),
      ),
    ).toThrow(ProtocolError);
  });
});

describe("hub → runner messages", () => {
  it("accepts dispatch with the source whole", () => {
    // No size limit, no truncation, no summarisation (FR-029).
    const sourceText = "x".repeat(5_000_000);
    const message = parseHubMessage(
      JSON.stringify({ type: "dispatch", taskId: "t-1", sourceText, maxToolCalls: 40, maxElapsedMs: 600000 }),
    );

    expect(message.type).toBe("dispatch");
    if (message.type === "dispatch") {
      expect(message.sourceText).toHaveLength(5_000_000);
    }
  });

  it("accepts proceed", () => {
    expect(parseHubMessage(JSON.stringify({ type: "proceed" }))).toEqual({ type: "proceed" });
  });

  it("rejects an unknown hub message", () => {
    expect(() => parseHubMessage(JSON.stringify({ type: "abort_now" }))).toThrow(ProtocolError);
  });
});
