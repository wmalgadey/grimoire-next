/**
 * The runner's entry point: the process the hub spawns, one per run, never reused
 * (contracts/runner-protocol.md).
 *
 * It does four things in a fixed order, and the order is the point:
 *
 * 1. Load the instruction file and announce its version.
 * 2. Announce the tool grant.
 * 3. **Block** until the hub says `proceed` — the hub has now durably persisted both, so
 *    FR-013 and FR-014's "before the first model call" is a property rather than a hope about
 *    scheduling.
 * 4. Run the loop, emitting one `tool_call` per call as it resolves, then `run_end`.
 *
 * Diagnostics go to stderr only, never as a source of task state: the hub reads stdout.
 */

import { createInterface } from "node:readline";
import { loadInstruction } from "../instruction/loader.js";
import { runModel } from "../model/adapter.js";
import { GRANTED_TOOLS, ToolGuard } from "../wiki-tools/guard.js";
import {
  parseHubMessage,
  ProtocolError,
  serialiseRunnerEvent,
  type DispatchMessage,
  type RunnerEvent,
} from "./protocol.js";

/** The instruction file to load, from the hub's own configuration. */
const DEFAULT_INSTRUCTION_PATH = "src/instructions/ingest.md";

function emit(event: RunnerEvent): void {
  process.stdout.write(`${serialiseRunnerEvent(event)}\n`);
}

function diagnostic(message: string): void {
  process.stderr.write(`${message}\n`);
}

/** Reads hub messages off stdin, one JSON object per line. */
function hubMessages(): {
  dispatch: Promise<DispatchMessage>;
  proceed: Promise<void>;
} {
  let resolveDispatch: (message: DispatchMessage) => void;
  let resolveProceed: () => void;
  const dispatch = new Promise<DispatchMessage>((resolve) => (resolveDispatch = resolve));
  const proceed = new Promise<void>((resolve) => (resolveProceed = resolve));

  const lines = createInterface({ input: process.stdin });
  lines.on("line", (line) => {
    const trimmed = line.trim();
    if (trimmed.length === 0) {
      return;
    }

    try {
      const message = parseHubMessage(trimmed);
      if (message.type === "dispatch") {
        resolveDispatch(message);
      } else {
        resolveProceed();
      }
    } catch (cause) {
      // A hub speaking an envelope this runner does not know is a failed run, not a quiet one.
      diagnostic(`protocol error: ${(cause as ProtocolError).message}`);
      process.exitCode = 1;
      lines.close();
    }
  });

  return { dispatch, proceed };
}

async function main(): Promise<void> {
  const { dispatch, proceed } = hubMessages();
  const instructionPath = process.env["GRIMOIRE_INSTRUCTION"] ?? DEFAULT_INSTRUCTION_PATH;

  const instruction = await loadInstruction(instructionPath);
  emit({
    type: "instruction_loaded",
    path: instruction.path,
    sha256: instruction.sha256,
    byteLength: instruction.byteLength,
  });

  emit({ type: "tool_grant", tools: [...GRANTED_TOOLS] });

  const dispatched = await dispatch;
  // The gate. Nothing has been asked of the model yet, and nothing will be until the hub has the
  // instruction version and the grant on the task.
  await proceed;

  const guard = new ToolGuard();
  guard.onRecorded = (call) =>
    emit({
      type: "tool_call",
      seq: call.seq,
      tool: call.tool,
      target: call.target,
      outcome: call.outcome,
      detail: call.detail,
      at: call.at,
    });

  const result = await runModel({
    systemPrompt: instruction.systemPrompt,
    sourceText: dispatched.sourceText,
    // cwd is the wiki working tree, pinned by the hub at spawn.
    repositoryRoot: process.cwd(),
    guard,
    maxToolCalls: dispatched.maxToolCalls,
  });

  // The tool-call ceiling is enforced here as well as by the hub's own kill, so a run that
  // overruns ends with a reason rather than a dead process (FR-009).
  const overran = guard.count > dispatched.maxToolCalls;
  const failureReason = overran
    ? `The run made ${guard.count} tool calls, past its ceiling of ${dispatched.maxToolCalls}.`
    : result.failureReason;

  emit({
    type: "run_end",
    outcome: failureReason === null ? "completed" : "failed",
    failureReason,
    commitMessage: result.finalMessage,
    toolCallCount: guard.count,
  });
}

main().then(
  () => process.exit(process.exitCode ?? 0),
  (cause: unknown) => {
    // A crash produces no run_end at all, which the hub treats identically to an abort: no commit
    // (FR-017). Saying why on stderr is a diagnostic, not task state.
    diagnostic(`run failed: ${(cause as Error).stack ?? String(cause)}`);
    process.exit(1);
  },
);
