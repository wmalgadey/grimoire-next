/**
 * The tool-grant guardrail (ADR-0009, FR-010, FR-011, FR-021).
 *
 Four mechanisms enforce one rule, deliberately overlapping, because each fails differently:
 *
 * 1. Every built-in is named in `disallowedTools`, so its definition never reaches the model
 *    (`src/model/adapter.ts`).
 * 2. Every `tool_use` block the model port sees is checked against the grant, so an attempt at a
 *    tool the SDK rejects as unknown is still *recorded*.
 * 3. This `PreToolUse` hook records and decides every call that does reach a permission step.
 * 4. `realpath` containment inside each handler refuses a target outside the repository, so a
 *    granted tool cannot be used to reach past the boundary (`containment.ts`).
 *
 * The first and last make the boundary effective; the middle two make it *observable*, which is a
 * separate requirement. An operator needs to see what the agent reached for, not only what it got:
 * a refusal is a recorded tool call, not an absence.
 */

import type { ToolCallOutcome } from "../run/protocol.js";

/** The set of actions the agent may take. Exactly two, in 100% of runs (FR-010, SC-004). */
export const GRANTED_TOOLS = ["mcp__wiki__read_page", "mcp__wiki__write_page"] as const;

/** The reason a call outside the grant is refused, in the words the record carries. */
export const NOT_GRANTED = "tool not granted";

/** The reason a granted call past the run's tool-call ceiling is refused (FR-009). */
export const CEILING_REACHED = "the run's tool-call ceiling was reached";

/** One entry in the run's tool-call record. */
export interface RecordedCall {
  readonly seq: number;
  readonly tool: string;
  readonly target: string | null;
  readonly outcome: ToolCallOutcome;
  readonly detail: string | null;
  readonly at: string;
}

/** What the hook decided about a call. */
export interface GuardDecision {
  readonly allow: boolean;
  readonly reason?: string;
}

/**
 * The run's ordered, append-only tool-call record, and the decision point every call passes
 * through.
 */
export class ToolGuard {
  private readonly calls: RecordedCall[] = [];
  private readonly recordedAttempts = new Set<string>();
  private nextSeq = 1;
  private ceilingSignalled = false;

  /**
   * @param maxToolCalls The tool-call half of the run limit (FR-009). Calls up to it act; the next
   * one is refused and recorded, and {@link onCeiling} fires so the run can be stopped. Enforced
   * here, per call, because the SDK's turn limit bounds turns — and one turn can carry any number
   * of tool uses.
   */
  constructor(private readonly maxToolCalls: number = Number.POSITIVE_INFINITY) {}

  /** Called once, when the record first goes past the ceiling. */
  onCeiling: (() => void) | undefined;

  /** Whether the run went past its tool-call ceiling. */
  get overran(): boolean {
    return this.calls.length > this.maxToolCalls;
  }

  /** Whether a tool name is inside the grant. Deny-by-default: unknown means denied. */
  static isGranted(tool: string): boolean {
    return (GRANTED_TOOLS as readonly string[]).includes(tool);
  }

  /**
   * Decides a call before it acts, and records the attempt when it is denied.
   *
   * A denied call is recorded here because it will never reach a handler; a permitted call is
   * recorded by {@link recordResolved} once it has an outcome, so the record says what happened
   * rather than what was attempted.
   */
  decide(tool: string, input: unknown, toolUseId?: string): GuardDecision {
    if (!ToolGuard.isGranted(tool)) {
      this.recordRefusedAttempt(tool, input, toolUseId);
      return { allow: false, reason: NOT_GRANTED };
    }

    if (this.calls.length >= this.maxToolCalls) {
      this.recordRefusedAttempt(tool, input, toolUseId, CEILING_REACHED);
      return { allow: false, reason: CEILING_REACHED };
    }

    return { allow: true };
  }

  /**
   * Records an attempt at a tool outside the grant.
   *
   * Called from two places on purpose. Naming every built-in in `disallowedTools` removes its
   * definition from the request, and a tool the model names anyway is rejected by the SDK as
   * unknown — *before* any permission step, so the `PreToolUse` hook never sees it. Left there,
   * the most clearly hostile attempts would be the ones that left no trace, which is the opposite
   * of what FR-011 and FR-021 ask for. So the model port also reports every `tool_use` block it
   * sees, and this method is idempotent per tool-use id so the two paths cannot double-count.
   */
  recordRefusedAttempt(tool: string, input: unknown, toolUseId?: string, detail: string = NOT_GRANTED): void {
    const key = toolUseId ?? `${tool}:${this.nextSeq}`;
    if (this.recordedAttempts.has(key)) {
      return;
    }

    this.recordedAttempts.add(key);
    this.append({
      seq: this.nextSeq++,
      tool,
      target: describeTarget(input),
      outcome: "refused",
      detail,
      at: new Date().toISOString(),
    });
  }

  /** Records a granted call once it has resolved, with its target and outcome. */
  recordResolved(tool: string, target: string | null, outcome: ToolCallOutcome, detail: string | null): RecordedCall {
    const call: RecordedCall = {
      seq: this.nextSeq++,
      tool,
      target,
      outcome,
      detail,
      at: new Date().toISOString(),
    };

    this.append(call);
    return call;
  }

  /** How many calls the run has made, refusals included — the run limit counts attempts. */
  get count(): number {
    return this.calls.length;
  }

  /** The record so far, in the order the calls were made. */
  get record(): readonly RecordedCall[] {
    return this.calls;
  }

  /** Called with each call as it is recorded, so the runner can emit it immediately. */
  onRecorded: ((call: RecordedCall) => void) | undefined;

  private append(call: RecordedCall): void {
    this.calls.push(call);
    this.onRecorded?.(call);

    if (this.overran && !this.ceilingSignalled) {
      this.ceilingSignalled = true;
      this.onCeiling?.();
    }
  }
}

/**
 * The raw requested target for a refused call — what was asked for, not what it would have become.
 * An operator reading the record needs to see the attempt as the model made it.
 */
function describeTarget(input: unknown): string | null {
  if (input === null || typeof input !== "object") {
    return null;
  }

  const record = input as Record<string, unknown>;
  for (const key of ["path", "file_path", "filePath", "command", "url", "pattern", "prompt"]) {
    const value = record[key];
    if (typeof value === "string") {
      return value;
    }
  }

  const rendered = JSON.stringify(input);
  return rendered === "{}" ? null : rendered;
}
