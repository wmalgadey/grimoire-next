import { spawn, spawnSync, type ChildProcess } from "node:child_process";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { createServer } from "node:net";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { test as base } from "@playwright/test";
import { startScriptedModel, type ScriptedModel } from "../scripted-model/src/server.js";

/**
 * The surfaces are exercised against the real thing: the built SvelteKit app served by a real hub
 * process, over a real git repository, driving a real runner. Only the LLM is doubled, at the wire
 * (ADR-0004, constitution III.2).
 */

const repositoryRoot = resolve(fileURLToPath(new URL(".", import.meta.url)), "..");

/** A real wiki git repository with a seed commit. */
export interface Wiki {
  readonly path: string;
  head(): string;
  commitCount(): number;
  dispose(): void;
}

function createWiki(seed: Record<string, string> = { "index.md": "# Index\n" }): Wiki {
  const path = mkdtempSync(join(tmpdir(), "grimoire-surface-wiki-"));
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
    head: () => git("rev-parse", "HEAD").trim(),
    commitCount: () => Number(git("rev-list", "--count", "HEAD").trim()),
    dispose: () => rmSync(path, { recursive: true, force: true }),
  };
}

async function freePort(): Promise<number> {
  return new Promise((resolvePort) => {
    const probe = createServer();
    probe.listen(0, "127.0.0.1", () => {
      const { port } = probe.address() as { port: number };
      probe.close(() => resolvePort(port));
    });
  });
}

/** A running hub, serving the built frontend and the API on one origin. */
export interface Hub {
  readonly baseUrl: string;
  readonly wiki: Wiki;
  readonly model: ScriptedModel;
  dispose(): Promise<void>;
}

async function startHub(script: string, seed?: Record<string, string>): Promise<Hub> {
  const wiki = createWiki(seed);
  const model = await startScriptedModel(script);
  const port = await freePort();
  const stateDb = join(mkdtempSync(join(tmpdir(), "grimoire-surface-state-")), "grimoire.db");

  const child: ChildProcess = spawn(
    "dotnet",
    ["run", "--project", join(repositoryRoot, "src", "hub"), "--no-build"],
    {
      cwd: repositoryRoot,
      env: {
        ...process.env,
        ASPNETCORE_URLS: `http://127.0.0.1:${port}`,
        GRIMOIRE_WIKI_REPO: wiki.path,
        GRIMOIRE_STATE_DB: stateDb,
        GRIMOIRE_MODEL_BASE_URL: model.baseUrl,
        GRIMOIRE_MODEL_TOKEN: "an-opaque-internal-token",
        GRIMOIRE_INSTRUCTION: join(repositoryRoot, "src", "instructions", "ingest.md"),
        // The hub serves the built frontend from its own content root.
        ASPNETCORE_WEBROOT: join(repositoryRoot, "frontend", "build"),
      },
      stdio: ["ignore", "pipe", "pipe"],
    },
  );

  const baseUrl = `http://127.0.0.1:${port}`;
  const deadline = Date.now() + 90_000;
  while (Date.now() < deadline) {
    try {
      const response = await fetch(`${baseUrl}/healthz`);
      if (response.ok) {
        break;
      }
    } catch {
      // Not listening yet.
    }

    await new Promise((wait) => setTimeout(wait, 250));
  }

  return {
    baseUrl,
    wiki,
    model,
    dispose: async () => {
      child.kill("SIGKILL");
      await model.close();
      wiki.dispose();
      rmSync(dirname(stateDb), { recursive: true, force: true });
    },
  };
}

/** A Playwright fixture that gives each test its own hub, wiki and scripted model. */
export const test = base.extend<{ hub: Hub; script: string; seed: Record<string, string> }>({
  script: ["read-then-write", { option: true }],
  seed: [{ "index.md": "# Index\n" }, { option: true }],
  hub: async ({ script, seed }, use) => {
    const hub = await startHub(script, seed);
    await use(hub);
    await hub.dispose();
  },
});

export { expect } from "@playwright/test";
