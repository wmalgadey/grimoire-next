/**
 * Shared harness for the runner suite: a real wiki git repository, the scripted model on a real
 * socket, and a real spawned runner process driven over the real NDJSON protocol.
 *
 * Nothing here is a mock. The LLM is the only sanctioned double (constitution III.2), and it is
 * replaced at the wire through `ANTHROPIC_BASE_URL` so the SDK's own agent loop executes
 * (ADR-0004).
 */

import { spawn, spawnSync, type ChildProcessWithoutNullStreams } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { startScriptedModel, type ScriptedModel } from "../scripted-model/src/server.js";
import {
  parseRunnerEvent,
  serialiseHubMessage,
  type RunnerEvent,
  type ToolCallEvent,
} from "../../src/agentrun/src/run/protocol.js";

export const repositoryRoot = resolve(fileURLToPath(new URL(".", import.meta.url)), "../..");

/** A real wiki git repository with a seed commit. */
export interface WikiRepository {
  readonly path: string;
  read(page: string): string | null;
  write(page: string, content: string): void;
  head(): string;
  commitCount(): number;
  dispose(): void;
}

/** Creates a wiki repository seeded with the given pages. */
export function createWiki(seed: Record<string, string> = { "index.md": "# Index\n" }): WikiRepository {
  const path = mkdtempSync(join(tmpdir(), "grimoire-wiki-"));
  const git = (...args: string[]) => {
    const result = spawnSync("git", args, { cwd: path, encoding: "utf8" });
    if (result.status !== 0) {
      throw new Error(`git ${args.join(" ")} failed: ${result.stderr}`);
    }

    return result.stdout;
  };

  git("init", "--initial-branch=main");
  git("config", "user.email", "grimoire@test.invalid");
  git("config", "user.name", "Grimoire");

  for (const [page, content] of Object.entries(seed)) {
    mkdirSync(dirname(join(path, page)), { recursive: true });
    writeFileSync(join(path, page), content);
  }

  git("add", "-A");
  git("commit", "-m", "seed");

  return {
    path,
    read: (page) => {
      try {
        return readFileSync(join(path, page), "utf8");
      } catch {
        return null;
      }
    },
    write: (page, content) => {
      mkdirSync(dirname(join(path, page)), { recursive: true });
      writeFileSync(join(path, page), content);
    },
    head: () => git("rev-parse", "HEAD").trim(),
    commitCount: () => Number(git("rev-list", "--count", "HEAD").trim()),
    dispose: () => rmSync(path, { recursive: true, force: true }),
  };
}

/** Everything one run produced. */
export interface RunResult {
  readonly events: readonly RunnerEvent[];
  readonly toolCalls: readonly ToolCallEvent[];
  readonly exitCode: number | null;
  readonly stderr: string;
  /**
   * The tool names carried in each request the model received. Asserted against the granted pair
   * so a built-in the SDK grows later cannot widen the agent's reach unnoticed (FR-010).
   */
  readonly toolNamesOffered: readonly (readonly string[])[];
  /**
   * The tool names each request marked `defer_loading` — offered by name only, with no schema the
   * model can call until it loads one through a tool search the grant does not include.
   */
  readonly toolNamesDeferred: readonly (readonly string[])[];
}

/** Options for driving one run. */
export interface RunOptions {
  readonly wiki: WikiRepository;
  readonly script: string;
  readonly sourceText?: string;
  readonly maxToolCalls?: number;
  readonly maxElapsedMs?: number;
  /** Overrides the instruction file, for the adversarial-instruction containment gate (TS-09). */
  readonly instructionPath?: string;
  /** Kills the runner this many milliseconds after the first tool call, for TS-12. */
  readonly killAfterFirstToolCallMs?: number;
  /**
   * Closes the runner's stdin this many milliseconds after the first tool call — what an
   * orphaned runner sees when the hub supervising it dies.
   */
  readonly closeStdinAfterFirstToolCallMs?: number;
  /**
   * Runs with the SDK's tool search asked for, as it is by default against the real API. The
   * default environment disables experimental betas for the scripted model, which also hides tool
   * search — so without this, no test would see the request production sends.
   */
  readonly toolSearch?: boolean;
}

/**
 * Spawns a real runner against a real wiki and the scripted model, drives the full protocol —
 * including the `proceed` gate — and returns everything the run emitted.
 */
export async function runAgent(options: RunOptions): Promise<RunResult> {
  const model: ScriptedModel = await startScriptedModel(options.script);
  const home = mkdtempSync(join(tmpdir(), "grimoire-run-home-"));
  const events: RunnerEvent[] = [];
  let stderr = "";

  const child: ChildProcessWithoutNullStreams = spawn(
    process.execPath,
    [join(repositoryRoot, "src", "agentrun", "dist", "main.js")],
    {
      // cwd is the agent's entire filesystem world (ADR-0005, ADR-0007).
      cwd: options.wiki.path,
      // env is replaced, not merged: credential and host-environment scrubbing is a property of
      // the spawn (contracts/deployment.md "Runner").
      env: {
        PATH: process.env["PATH"] ?? "",
        HOME: home,
        ANTHROPIC_BASE_URL: model.baseUrl,
        ANTHROPIC_AUTH_TOKEN: "an-opaque-internal-token",
        ANTHROPIC_CUSTOM_HEADERS: "X-Grimoire-Run: test-run",
        CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC: "1",
        ...(options.toolSearch
          ? { ENABLE_TOOL_SEARCH: "true" }
          : { CLAUDE_CODE_DISABLE_EXPERIMENTAL_BETAS: "1" }),
        GRIMOIRE_INSTRUCTION:
          options.instructionPath ?? join(repositoryRoot, "src", "instructions", "ingest.md"),
      },
      stdio: ["pipe", "pipe", "pipe"],
    },
  );

  child.stderr.setEncoding("utf8");
  child.stderr.on("data", (chunk: string) => {
    stderr += chunk;
  });

  let sawFirstToolCall = false;
  let killTimer: NodeJS.Timeout | undefined;

  const exitCode = await new Promise<number | null>((resolveExit, rejectExit) => {
    let buffered = "";
    let proceeded = false;

    child.stdout.setEncoding("utf8");
    child.stdout.on("data", (chunk: string) => {
      buffered += chunk;
      let newline = buffered.indexOf("\n");
      while (newline >= 0) {
        const line = buffered.slice(0, newline).trim();
        buffered = buffered.slice(newline + 1);
        newline = buffered.indexOf("\n");
        if (!line) {
          continue;
        }

        const event = parseRunnerEvent(line);
        events.push(event);

        // The hub persists instruction_loaded and tool_grant, then opens the gate.
        if (event.type === "tool_grant" && !proceeded) {
          proceeded = true;
          child.stdin.write(`${serialiseHubMessage({ type: "proceed" })}\n`);
        }

        if (event.type === "tool_call" && !sawFirstToolCall && options.killAfterFirstToolCallMs !== undefined) {
          sawFirstToolCall = true;
          killTimer = setTimeout(() => child.kill("SIGKILL"), options.killAfterFirstToolCallMs);
        }

        if (
          event.type === "tool_call"
          && !sawFirstToolCall
          && options.closeStdinAfterFirstToolCallMs !== undefined
        ) {
          sawFirstToolCall = true;
          // EOF, not a signal: the runner is left alive and unsupervised, exactly as it would be
          // after its hub was killed.
          killTimer = setTimeout(() => child.stdin.end(), options.closeStdinAfterFirstToolCallMs);
        }
      }
    });

    child.on("error", rejectExit);
    child.on("close", (code) => resolveExit(code));

    child.stdin.write(
      `${serialiseHubMessage({
        type: "dispatch",
        taskId: "test-task",
        sourceText: options.sourceText ?? "A source worth keeping.",
        maxToolCalls: options.maxToolCalls ?? 40,
        maxElapsedMs: options.maxElapsedMs ?? 60_000,
      })}\n`,
    );
  });

  if (killTimer) {
    clearTimeout(killTimer);
  }

  const offeredTools = model.requests
    .filter((request) => request.url.includes("/messages"))
    .map((request) => {
      const body = request.body as { tools?: { name?: string; defer_loading?: boolean }[] } | null;
      return (body?.tools ?? []).filter((tool) => (tool.name ?? "").length > 0);
    });
  const toolNamesOffered = offeredTools.map((tools) => tools.map((tool) => tool.name ?? ""));
  const toolNamesDeferred = offeredTools.map((tools) =>
    tools.filter((tool) => tool.defer_loading === true).map((tool) => tool.name ?? ""));

  await model.close();
  rmSync(home, { recursive: true, force: true });

  return {
    events,
    toolCalls: events.filter((event): event is ToolCallEvent => event.type === "tool_call"),
    exitCode,
    stderr,
    toolNamesOffered,
    toolNamesDeferred,
  };
}

/** The sha256 the instruction loader is expected to report for a file. */
export function sha256Of(path: string): string {
  return createHash("sha256").update(readFileSync(path)).digest("hex");
}
