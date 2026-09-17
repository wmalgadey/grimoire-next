/**
 * The named scripts the Test Strategy requires (plan.md, Test Strategy column
 * "Scripted LLM responses"). Each script is a sequence of assistant turns the server replays in
 * order: turn N is the response to the Nth request of a run.
 *
 * A script says what the *model* does, never what the wiki should contain — no test asserts model
 * output (constitution III.4), and nothing here is a judgment the instruction file owns.
 */

/** One tool call the scripted assistant makes in a turn. */
export interface ScriptedToolUse {
  readonly name: string;
  readonly input: Record<string, unknown>;
}

/**
 * One assistant turn. `text` is the assistant's message; when a turn carries tool uses the server
 * ends it with `stop_reason: "tool_use"`, otherwise with `end_turn` — which is what ends the run
 * and makes the final text the commit message (research R9).
 */
export interface ScriptedTurn {
  readonly text?: string;
  readonly toolUses?: readonly ScriptedToolUse[];
  /** Milliseconds to wait before answering, for the slow and hanging scripts. */
  readonly delayMs?: number;
}

/** A named sequence of turns. */
export interface Script {
  readonly name: string;
  readonly turns: readonly ScriptedTurn[];
  /**
   * When set, the server keeps answering after `turns` is exhausted by repeating the last turn —
   * this is what makes the never-stopping script never stop.
   */
  readonly repeatLastTurn?: boolean;
}

const read = (path: string): ScriptedToolUse => ({ name: "mcp__wiki__read_page", input: { path } });
const write = (path: string, content: string | null): ScriptedToolUse => ({
  name: "mcp__wiki__write_page",
  input: { path, content },
});

/**
 * The canonical run: consult the wiki, then write in light of what was there. Distinguishes a run
 * that read before writing from one that wrote blind (SC-010).
 */
export const readThenWrite: Script = {
  name: "read-then-write",
  turns: [
    { toolUses: [read("index.md")] },
    { toolUses: [write("topics/scripted.md", "# Scripted\n\nWritten by the scripted model.\n")] },
    { text: "Add scripted topic page" },
  ],
};

/** Writes without reading first. */
export const writeOnly: Script = {
  name: "write-only",
  turns: [
    { toolUses: [write("topics/blind.md", "# Blind\n\nWritten without reading.\n")] },
    { text: "Add blind topic page" },
  ],
};

/** Reads and then deliberately changes nothing (FR-016, SC-011). */
export const readOnly: Script = {
  name: "read-only",
  turns: [{ toolUses: [read("index.md")] }, { text: "The wiki already says this" }],
};

/** Stops immediately, touching nothing at all. */
export const noOp: Script = {
  name: "no-op",
  turns: [{ text: "Nothing to add" }],
};

/**
 * N tool calls across N iterations of one run, each informed by the previous result — the escalation
 * SC-007 is about. Parameterised N = 1…8 by TS-06.
 */
export function escalation(n: number): Script {
  const turns: ScriptedTurn[] = [];
  for (let i = 1; i <= n; i++) {
    turns.push({ toolUses: [i % 2 === 1 ? read(`step-${i}.md`) : write(`step-${i}.md`, `# Step ${i}\n`)] });
  }

  turns.push({ text: `Completed ${n} step${n === 1 ? "" : "s"}` });
  return { name: `escalation-${n}`, turns };
}

/** Never reaches a final turn, so only the run limit can end it (FR-009). */
export const neverStopping: Script = {
  name: "never-stopping",
  turns: [{ toolUses: [write("churn.md", "# Churn\n")] }],
  repeatLastTurn: true,
};

/** Writes, then stops answering — the runner is killed mid-write (TS-12). */
export const writeThenHang: Script = {
  name: "write-then-hang",
  turns: [
    { toolUses: [write("half-written.md", "# Half written\n")] },
    { delayMs: 3_600_000, text: "never delivered" },
  ],
};

/** A slow but ordinary run, so a second submission arrives while this one is executing (TS-14). */
export const slowReadThenWrite: Script = {
  name: "slow-read-then-write",
  turns: [
    { delayMs: 1_500, toolUses: [read("index.md")] },
    { delayMs: 1_500, toolUses: [write("topics/slow.md", "# Slow\n")] },
    { text: "Add slow topic page" },
  ],
};

/**
 * Reports the byte length of the prompt it received, so TS-04 can assert the source reached the
 * run whole: no size limit, no truncation, no summarisation (FR-029).
 */
export const echoLength: Script = {
  name: "echo-length",
  turns: [{ text: "__ECHO_PROMPT_BYTES__" }],
};

/**
 * Every escape the containment gates must refuse (TS-09, TS-10): built-in tools whose definitions
 * were removed from the request, an MCP tool that does not exist, and wiki paths that resolve
 * outside the repository.
 */
export const escapeAttempts: Script = {
  name: "escape-attempts",
  turns: [
    { toolUses: [{ name: "Bash", input: { command: "cat /etc/passwd" } }] },
    { toolUses: [{ name: "Read", input: { file_path: "/etc/passwd" } }] },
    { toolUses: [{ name: "WebFetch", input: { url: "http://example.invalid/" } }] },
    { toolUses: [{ name: "Task", input: { prompt: "escape" } }] },
    { toolUses: [{ name: "mcp__unknown__do_anything", input: {} }] },
    { text: "Tried everything" },
  ],
};

/** Traversal, absolute paths and a planted symlink, all through the granted write tool (TS-10). */
export const escapeThroughWritePaths: Script = {
  name: "escape-through-write-paths",
  turns: [
    { toolUses: [write("../escaped.md", "# Escaped\n")] },
    { toolUses: [write("/tmp/escaped.md", "# Escaped\n")] },
    { toolUses: [write("out/escaped.md", "# Escaped\n")] },
    { toolUses: [read("../canary.md")] },
    { text: "Tried every path" },
  ],
};

/** Every script the suites can ask for, by name. */
export const scripts: ReadonlyMap<string, Script> = new Map<string, Script>([
  [readThenWrite.name, readThenWrite],
  [writeOnly.name, writeOnly],
  [readOnly.name, readOnly],
  [noOp.name, noOp],
  [neverStopping.name, neverStopping],
  [writeThenHang.name, writeThenHang],
  [slowReadThenWrite.name, slowReadThenWrite],
  [echoLength.name, echoLength],
  [escapeAttempts.name, escapeAttempts],
  [escapeThroughWritePaths.name, escapeThroughWritePaths],
  ...Array.from({ length: 8 }, (_, index) => {
    const script = escalation(index + 1);
    return [script.name, script] as const;
  }),
]);

/** Looks a script up by name, failing loudly rather than silently answering with something else. */
export function scriptByName(name: string): Script {
  const script = scripts.get(name);
  if (!script) {
    throw new Error(
      `No scripted response named '${name}'. Known scripts: ${[...scripts.keys()].sort().join(", ")}`,
    );
  }

  return script;
}
