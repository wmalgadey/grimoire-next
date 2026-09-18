/**
 * The runner's half of the hub↔runner envelope (contracts/runner-protocol.md): one JSON object per
 * line, no embedded newlines, UTF-8.
 *
 * These shapes are the port-boundary translation of the artifact's own concepts across a process
 * boundary, not a parallel model of them (constitution VII.2). They carry no file descriptors and
 * no paths outside the wiki repository, so moving the runner into its own container is a transport
 * change rather than a redesign.
 */

import { z } from "zod";

/**
 * A line that is not a message this protocol defines. Rejected rather than ignored: a peer
 * speaking an envelope the other does not know is a failed run, not a quiet one.
 */
export class ProtocolError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "ProtocolError";
  }
}

/** Emitted first, always, before anything else — and before any model call (FR-014). */
const instructionLoaded = z.object({
  type: z.literal("instruction_loaded"),
  path: z.string(),
  sha256: z.string().regex(/^[0-9a-f]{64}$/, "sha256 must be 64 lowercase hex characters"),
  byteLength: z.number().int().nonnegative(),
});

/** The set of actions the agent may take. Exactly two, in 100% of runs (FR-010, SC-004). */
const toolGrant = z.object({
  type: z.literal("tool_grant"),
  tools: z.array(z.string()),
});

/**
 * One call the agent made, emitted as it resolves, in the order made. Refused and failed calls are
 * emitted the same way as successful ones — a refusal is a recorded call, not an absence
 * (FR-011, FR-021).
 */
const toolCall = z.object({
  type: z.literal("tool_call"),
  /** 1-based, strictly increasing, the order the calls were made. */
  seq: z.number().int().min(1),
  /** The tool name as the model named it, including a name outside the granted set. */
  tool: z.string(),
  /** The page path for a granted call; the raw requested target for a refused one. */
  target: z.string().nullable(),
  outcome: z.enum(["ok", "failed", "refused"]),
  /** Why it failed or was refused. */
  detail: z.string().nullable(),
  at: z.string(),
});

/**
 * How the run ended. A crash produces no `run_end` at all, which the hub treats identically:
 * no commit (FR-017).
 */
const runEnd = z.object({
  type: z.literal("run_end"),
  outcome: z.enum(["completed", "failed"]),
  failureReason: z.string().nullable(),
  /**
   * The run's final assistant message text, verbatim — commit-message wording is judgment and
   * lives in the instruction file (research R9). Empty means the hub commits under its constant.
   */
  commitMessage: z.string().nullable(),
  toolCallCount: z.number().int().nonnegative(),
  /**
   * Set when the run ended because the model endpoint answered with an error rather than a
   * message: the HTTP status, or `no-response` when there was none. Lets the hub say "this is the
   * egress path, not the agent" (plan IV, grimoire.run.model_endpoint_unreachable). A prompt too
   * large for the model is the source's size, not the endpoint, and leaves this null (FR-029).
   */
  modelEndpointStatus: z.string().nullable(),
});

const runnerEvent = z.discriminatedUnion("type", [instructionLoaded, toolGrant, toolCall, runEnd]);

/** The run's input. `sourceText` is the source whole — no size limit, no truncation (FR-029). */
const dispatch = z.object({
  type: z.literal("dispatch"),
  taskId: z.string(),
  sourceText: z.string(),
  maxToolCalls: z.number().int().min(1),
  maxElapsedMs: z.number().int().min(1),
});

/**
 * The gate. Sent only after `instruction_loaded` and `tool_grant` are durably persisted on the
 * task, which is what makes "before the first model call" a property a test can assert rather
 * than a hope about scheduling (FR-013, FR-014).
 */
const proceed = z.object({ type: z.literal("proceed") });

const hubMessage = z.discriminatedUnion("type", [dispatch, proceed]);

export type InstructionLoadedEvent = z.infer<typeof instructionLoaded>;
export type ToolGrantEvent = z.infer<typeof toolGrant>;
export type ToolCallEvent = z.infer<typeof toolCall>;
export type RunEndEvent = z.infer<typeof runEnd>;
export type RunnerEvent = z.infer<typeof runnerEvent>;
export type DispatchMessage = z.infer<typeof dispatch>;
export type ProceedMessage = z.infer<typeof proceed>;
export type HubMessage = z.infer<typeof hubMessage>;

/** The outcome of one tool call, as the record reports it. */
export type ToolCallOutcome = ToolCallEvent["outcome"];

/**
 * Renders one event as an NDJSON line. `JSON.stringify` escapes embedded newlines, so a commit
 * message spanning several lines still travels as exactly one line.
 */
export function serialiseRunnerEvent(event: RunnerEvent): string {
  return JSON.stringify(runnerEvent.parse(event));
}

/** Renders one hub message as an NDJSON line. */
export function serialiseHubMessage(message: HubMessage): string {
  return JSON.stringify(hubMessage.parse(message));
}

/** Parses one NDJSON line from the runner. */
export function parseRunnerEvent(line: string): RunnerEvent {
  return parse(runnerEvent, line, "runner event");
}

/** Parses one NDJSON line from the hub. */
export function parseHubMessage(line: string): HubMessage {
  return parse(hubMessage, line, "hub message");
}

function parse<T>(schema: z.ZodType<T>, line: string, what: string): T {
  let json: unknown;
  try {
    json = JSON.parse(line);
  } catch {
    throw new ProtocolError(`Not a ${what}: the line is not JSON.`);
  }

  const result = schema.safeParse(json);
  if (!result.success) {
    throw new ProtocolError(
      `Not a ${what}: ${result.error.issues.map((issue) => `${issue.path.join(".") || "(root)"} ${issue.message}`).join("; ")}`,
    );
  }

  return result.data;
}
