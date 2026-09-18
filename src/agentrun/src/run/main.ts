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

/**
 * Writes one event and waits for it to actually leave the process. `main()` calls
 * `process.exit()` right after its last event to close the stdin readline interface (otherwise
 * nothing ends the event loop); without waiting here first, that exit can happen before an
 * asynchronous stdout write — routine when stdout is a pipe, as it is to the hub — has flushed,
 * so the hub observes a crash with no `run_end` and resets a run that actually completed.
 */
function emit(event: RunnerEvent): Promise<void> {
  return new Promise((resolve, reject) => {
    process.stdout.write(`${serialiseRunnerEvent(event)}\n`, (error) => {
      if (error) {
        reject(error);
      } else {
        resolve();
      }
    });
  });
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
  let rejectDispatch: (cause: Error) => void;
  let resolveProceed: () => void;
  let rejectProceed: (cause: Error) => void;
  const dispatch = new Promise<DispatchMessage>((resolve, reject) => {
    resolveDispatch = resolve;
    rejectDispatch = reject;
  });
  const proceed = new Promise<void>((resolve, reject) => {
    resolveProceed = resolve;
    rejectProceed = reject;
  });
  // A promise a caller never attached a rejection handler to (because it awaits the *other* one
  // first) would otherwise be an unhandled rejection the moment the catch below fires.
  dispatch.catch(() => {});
  proceed.catch(() => {});

  const lines = createInterface({ input: process.stdin });

  // The hub holds this pipe open for the whole run, so EOF means the hub is gone — killed, OOMed,
  // or the node it was on went away. A runner that kept going would write into a wiki nobody is
  // supervising, and the next hub's startup recovery would reset that working tree and dispatch
  // the next run while this process was still writing into it (FR-017, FR-028).
  //
  // This is the parent-death mechanism both platforms have. `PR_SET_PDEATHSIG` is Linux-only, and
  // a process group only helps when whoever kills the hub signals the group — an OOM kill does
  // not. Exiting on EOF needs nothing from the killer.
  process.stdin.on("end", () => {
    diagnostic("the hub closed the protocol stream; exiting rather than running unsupervised");
    process.exit(1);
  });

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
      // Rejecting whichever of dispatch/proceed main() is still waiting on is what makes that
      // true promptly — settling a promise that already settled is a documented no-op, so
      // rejecting both unconditionally is safe whichever stage this arrives at.
      const error = new Error(`protocol error: ${(cause as ProtocolError).message}`);
      diagnostic(error.message);
      rejectDispatch(error);
      rejectProceed(error);
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
  await emit({
    type: "instruction_loaded",
    path: instruction.path,
    sha256: instruction.sha256,
    byteLength: instruction.byteLength,
  });

  await emit({ type: "tool_grant", tools: [...GRANTED_TOOLS] });

  const dispatched = await dispatch;
  // The gate. Nothing has been asked of the model yet, and nothing will be until the hub has the
  // instruction version and the grant on the task.
  await proceed;

  const guard = new ToolGuard(dispatched.maxToolCalls);
  guard.onRecorded = (call) => {
    // Not awaited: the callback is synchronous (ToolGuard does not await it) and Node's stdout
    // is one ordered stream, so these writes still complete, in order, before the final `emit`
    // below is awaited — which is the one that has to happen before `process.exit()`.
    void emit({
      type: "tool_call",
      seq: call.seq,
      tool: call.tool,
      target: call.target,
      outcome: call.outcome,
      detail: call.detail,
      at: call.at,
    });
  };

  const result = await runModel({
    systemPrompt: instruction.systemPrompt,
    sourceText: dispatched.sourceText,
    // cwd is the wiki working tree, pinned by the hub at spawn.
    repositoryRoot: process.cwd(),
    guard,
    maxToolCalls: dispatched.maxToolCalls,
  });

  // The guard stopped the run at its tool-call ceiling; the hub checks the count again at run end,
  // so a runner that failed to stop still cannot have its work committed (FR-009).
  const failureReason = guard.overran
    ? `The run reached its ceiling of ${dispatched.maxToolCalls} tool calls and was stopped; `
      + `${guard.count - dispatched.maxToolCalls} further call(s) were refused. `
      + "Raise GRIMOIRE_RUN_MAX_TOOL_CALLS if the ceiling is too tight."
    : result.promptTooLong
      ? `The source is ${Buffer.byteLength(dispatched.sourceText, "utf8").toLocaleString("en-US")} bytes, `
        + "more than the model can take in at once, so the run could not proceed. The source was "
        + "passed whole; splitting it and submitting the parts is left to you."
      : result.failureReason;

  await emit({
    type: "run_end",
    outcome: failureReason === null ? "completed" : "failed",
    failureReason,
    commitMessage: result.finalMessage,
    toolCallCount: guard.count,
    modelEndpointStatus: guard.overran ? null : result.modelEndpointStatus,
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
