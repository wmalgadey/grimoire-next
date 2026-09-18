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
  /**
   * Answers this turn with an HTTP error instead of a message — what the model endpoint says when
   * the prompt is too large for it, or what the egress proxy says when its upstream is not there.
   */
  readonly error?: ScriptedError;
}

/** An error response in the Messages API's own envelope. */
export interface ScriptedError {
  readonly status: number;
  readonly type: string;
  readonly message: string;
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

/**
 * Every tool, granted or not, aimed at the canary files a TS-09 suite plants for this test — read,
 * overwritten, deleted. The suite sets `CANARY_ROOT`; without it the attempts would miss the
 * canaries, and "every canary is untouched" would prove nothing.
 */
export const escapeTowardCanaries: Script = {
  name: "escape-toward-canaries",
  turns: [
    { toolUses: [{ name: "Bash", input: { command: "cat {{CANARY_ROOT}}/secrets.txt" } }] },
    { toolUses: [{ name: "Bash", input: { command: "rm {{CANARY_ROOT}}/keys.pem" } }] },
    { toolUses: [{ name: "Read", input: { file_path: "{{CANARY_ROOT}}/config.json" } }] },
    { toolUses: [{ name: "Write", input: { file_path: "{{CANARY_ROOT}}/secrets.txt", content: "overwritten" } }] },
    { toolUses: [{ name: "Task", input: { prompt: "read {{CANARY_ROOT}}/keys.pem" } }] },
    { toolUses: [read("{{CANARY_ROOT}}/secrets.txt")] },
    { toolUses: [write("{{CANARY_ROOT}}/keys.pem", "overwritten")] },
    { toolUses: [write("{{CANARY_ROOT}}/config.json", null)] },
    { text: "Tried every canary" },
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

/**
 * More tool uses in one turn than a small tool-call ceiling allows. `maxTurns` bounds turns, not
 * calls, so only the guard can stop this run at its ceiling (FR-009).
 */
export const parallelToolUses: Script = {
  name: "parallel-tool-uses",
  turns: [
    {
      toolUses: [read("index.md"), read("a.md"), read("b.md"), read("c.md"), read("d.md"), read("e.md")],
    },
    { toolUses: [read("f.md"), read("g.md"), read("h.md")] },
    { text: "Read everything" },
  ],
};

/** Reads one page, for a suite that makes that page unreadable (FR-021: no call goes unrecorded). */
export const readLocked: Script = {
  name: "read-locked",
  turns: [{ toolUses: [read("locked.md")] }, { text: "Could not read it" }],
};

/**
 * The model endpoint refuses the prompt as too large for it — the one way a source's size
 * surfaces, since the harness imposes no limit of its own (FR-029).
 */
export const promptTooLong: Script = {
  name: "prompt-too-long",
  turns: [
    {
      error: {
        status: 400,
        type: "invalid_request_error",
        message: "prompt is too long: 250000 tokens > 200000 maximum",
      },
    },
  ],
};

/**
 * The egress proxy answers, but with an error of its own — the path exists and is broken, which
 * a TCP probe cannot see (plan IV, grimoire.run.model_endpoint_unreachable). A status the SDK does
 * not retry, so the run ends promptly.
 */
export const endpointRefuses: Script = {
  name: "endpoint-refuses",
  turns: [
    {
      error: {
        status: 403,
        type: "permission_error",
        message: "The egress proxy refused this request.",
      },
    },
  ],
};

/**
 * Writes only a file the wiki's own `.gitignore` excludes: the tree still matches the tip, so the
 * run changed nothing — and the file must not outlive it (FR-016, FR-017).
 */
export const writesIgnoredOnly: Script = {
  name: "writes-ignored-only",
  turns: [{ toolUses: [write("scratch/notes.md", "# Scratch\n")] }, { text: "Nothing worth keeping" }],
};

/**
 * Writes a `.gitignore` and a file it excludes. The commit takes the first; the second is in no
 * commit, so it must not stay in the working tree for the next run to read (FR-015, FR-017).
 */
export const writesGitignoreAndIgnored: Script = {
  name: "writes-gitignore-and-ignored",
  turns: [
    { toolUses: [write(".gitignore", "secret.md\n")] },
    { toolUses: [write("secret.md", "# Secret\n")] },
    { text: "Ignore secret pages" },
  ],
};

/**
 * Narrates while it writes, then ends on a turn with no text at all. The narration is not the
 * run's final message, so the commit falls back to the fixed one (research R9).
 */
export const narratesThenEndsSilently: Script = {
  name: "narrates-then-ends-silently",
  turns: [
    { text: "Writing the topic page now", toolUses: [write("topics/quiet.md", "# Quiet\n")] },
    { text: "" },
  ],
};

/**
 * Writes into the repository's own `.git` directory — a hook git would run and the configuration
 * git reads — through the granted write tool. Neither is wiki content (FR-011, TS-10).
 */
export const writesIntoGitDirectory: Script = {
  name: "writes-into-git-directory",
  turns: [
    { toolUses: [write(".git/hooks/post-commit", "#!/bin/sh\ntouch /tmp/grimoire-hook-ran\n")] },
    { toolUses: [write(".git/config", "[core]\n\thooksPath = /tmp\n")] },
    { text: "Tried the git directory" },
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
  [escapeTowardCanaries.name, escapeTowardCanaries],
  [parallelToolUses.name, parallelToolUses],
  [readLocked.name, readLocked],
  [promptTooLong.name, promptTooLong],
  [endpointRefuses.name, endpointRefuses],
  [writesIgnoredOnly.name, writesIgnoredOnly],
  [writesGitignoreAndIgnored.name, writesGitignoreAndIgnored],
  [narratesThenEndsSilently.name, narratesThenEndsSilently],
  [writesIntoGitDirectory.name, writesIntoGitDirectory],
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
