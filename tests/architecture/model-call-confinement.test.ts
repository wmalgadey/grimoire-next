import { readdirSync, readFileSync, statSync } from "node:fs";
import { extname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

/**
 * Quality gate 2, TypeScript half (constitution V.3). The model is called in exactly one module:
 * `src/agentrun/src/model/`, the model port. The SDK package itself is also imported by
 * `src/agentrun/src/wiki-tools/`, because `createSdkMcpServer` and `tool` are how the grant is
 * declared (ADR-0009) — so a package-level rule cannot say "only model/ calls the model".
 * `dependency-cruiser` confines the package; this names the call.
 */

const repositoryRoot = resolve(fileURLToPath(new URL(".", import.meta.url)), "../..");
const runnerSources = join(repositoryRoot, "src", "agentrun", "src");
const modelPort = "src/agentrun/src/model/";
const sdk = "@anthropic-ai/claude-agent-sdk";

function typeScriptFiles(directory: string): string[] {
  return readdirSync(directory).flatMap((entry) => {
    const path = join(directory, entry);
    if (statSync(path).isDirectory()) {
      return typeScriptFiles(path);
    }

    return extname(path) === ".ts" ? [path] : [];
  });
}

const relativePath = (path: string): string => relative(repositoryRoot, path).split(sep).join("/");

/** Every import from the SDK in a file: the named bindings, or `*` for a namespace or default import. */
function sdkImports(source: string): string[] {
  const escaped = sdk.replace(/[/.-]/g, "\\$&");
  const pattern = new RegExp(`import\\s+([^;]*?)\\s+from\\s+["']${escaped}["']`, "gs");
  const dynamic = new RegExp(`import\\(\\s*["']${escaped}["']\\s*\\)|require\\(\\s*["']${escaped}["']\\s*\\)`);

  const bindings = [...source.matchAll(pattern)].flatMap((match) => {
    const clause = match[1]!.replace(/^type\s+/, "");
    const named = /\{([^}]*)\}/.exec(clause);
    const names = named
      ? named[1]!.split(",").map((part) => part.trim().replace(/^type\s+/, "").split(/\s+as\s+/)[0]!.trim())
      : [];
    // Anything but a pure named-import clause (a namespace, a default) reaches every export.
    const reachesEverything = clause.replace(/\{[^}]*\}/, "").replace(/,/g, "").trim().length > 0;
    return [...names.filter((name) => name.length > 0), ...(reachesEverything ? ["*"] : [])];
  });

  return dynamic.test(source) ? [...bindings, "*"] : bindings;
}

describe("quality gate 2: only the model port calls the model", () => {
  it("imports query() from the SDK only under src/agentrun/src/model/", () => {
    const offenders = typeScriptFiles(runnerSources)
      .filter((path) => !relativePath(path).startsWith(modelPort))
      .filter((path) => {
        const imported = sdkImports(readFileSync(path, "utf8"));
        return imported.includes("query") || imported.includes("*");
      })
      .map(relativePath);

    expect(offenders).toEqual([]);
  });

  it("still sees the one module that does call it, so the check above is not vacuous", () => {
    const adapter = readFileSync(join(runnerSources, "model", "adapter.ts"), "utf8");
    expect(sdkImports(adapter)).toContain("query");

    // And recognises the forms it has to refuse.
    expect(sdkImports(`import * as agent from "${sdk}";`)).toContain("*");
    expect(sdkImports(`import agent from "${sdk}";`)).toContain("*");
    expect(sdkImports(`import { tool, query as ask } from "${sdk}";`)).toContain("query");
    expect(sdkImports(`const agent = await import("${sdk}");`)).toContain("*");
    expect(sdkImports(`import { createSdkMcpServer, tool } from "${sdk}";`)).toEqual(["createSdkMcpServer", "tool"]);
  });
});
