import { readdirSync, readFileSync, statSync } from "node:fs";
import { extname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

/**
 * Quality gate 1 / T051 / TS-17 (constitution I.2). Judgment lives in instruction files; control
 * lives in code. The mechanism that keeps those apart is a single module:
 * `src/agentrun/src/instruction/` is the only module that reads instruction files and the only
 * constructor of the `SystemPrompt` branded type the model port accepts.
 *
 * This is the gate that makes "no prompt text in harness code" checkable rather than a habit.
 */

const repositoryRoot = resolve(fileURLToPath(new URL(".", import.meta.url)), "../..");
const instructionModule = join("src", "agentrun", "src", "instruction");

/** Every source file the gate looks at: our code, never a dependency or a build output. */
function sourceFiles(): string[] {
  const skipped = new Set([
    "node_modules",
    "dist",
    "bin",
    "obj",
    ".git",
    ".svelte-kit",
    "build",
    "test-results",
    "playwright-report",
  ]);
  const extensions = new Set([".ts", ".tsx", ".js", ".mjs", ".cjs", ".svelte", ".cs"]);
  const found: string[] = [];

  const walk = (directory: string): void => {
    for (const entry of readdirSync(directory)) {
      if (skipped.has(entry) || entry.startsWith(".")) {
        continue;
      }

      const path = join(directory, entry);
      if (statSync(path).isDirectory()) {
        walk(path);
      } else if (extensions.has(extname(path))) {
        found.push(path);
      }
    }
  };

  for (const top of ["src", "frontend", "tests"]) {
    walk(join(repositoryRoot, top));
  }

  // The gate itself names the patterns it looks for, so it would otherwise report itself.
  return found.filter((path) => path !== fileURLToPath(import.meta.url));
}

const isInsideInstructionModule = (path: string): boolean =>
  relative(repositoryRoot, path).split(sep).join("/").startsWith(instructionModule.split(sep).join("/"));

describe("quality gate 1: the system prompt has exactly one constructor", () => {
  it("brands SystemPrompt so it cannot be produced by an ordinary string", () => {
    const source = readFileSync(
      join(repositoryRoot, instructionModule, "systemPrompt.ts"),
      "utf8",
    );

    // A branded type: `string` alone must not be assignable to SystemPrompt, or the gate below
    // would be a naming convention rather than a type-level guarantee.
    expect(source).toMatch(/unique symbol|declare const .*brand|readonly __brand/);
    expect(source).toMatch(/SystemPrompt/);
  });

  it("constructs SystemPrompt only inside src/agentrun/src/instruction/", () => {
    const constructors = sourceFiles()
      .filter((path) => !isInsideInstructionModule(path))
      .filter((path) => {
        const source = readFileSync(path, "utf8");
        // Every way to mint the branded type: the factory, and an assertion past the brand.
        return (
          /\bcreateSystemPrompt\s*\(/.test(source) ||
          /\bas\s+SystemPrompt\b/.test(source) ||
          /<\s*SystemPrompt\s*>/.test(source)
        );
      })
      .map((path) => relative(repositoryRoot, path));

    expect(constructors).toEqual([]);
  });

  it("reads instruction files only inside src/agentrun/src/instruction/", () => {
    const readers = sourceFiles()
      .filter((path) => path.endsWith(".ts") && !isInsideInstructionModule(path))
      .filter((path) => !relative(repositoryRoot, path).startsWith(join("tests", "architecture")))
      .filter((path) => {
        const source = readFileSync(path, "utf8");
        return /instructions?\/[A-Za-z0-9_-]+\.md/.test(source) && /readFile/.test(source);
      })
      .map((path) => relative(repositoryRoot, path));

    expect(readers).toEqual([]);
  });

  it("has no systemPrompt string literal anywhere outside the instruction module", () => {
    const offenders = sourceFiles()
      .filter((path) => !isInsideInstructionModule(path))
      .filter((path) => {
        const source = readFileSync(path, "utf8");
        // `systemPrompt:` assigned a literal is prompt text in harness code — exactly what
        // constitution I.2 forbids. Passing a SystemPrompt value through is fine.
        return /systemPrompt\s*:\s*["'`]/.test(source);
      })
      .map((path) => relative(repositoryRoot, path));

    expect(offenders).toEqual([]);
  });

  it("does not use the SDK's claude_code preset, which would reach the model as instruction", () => {
    const offenders = sourceFiles()
      .filter((path) => path.endsWith(".ts"))
      .filter((path) => /["']claude_code["']/.test(readFileSync(path, "utf8")))
      .filter((path) => !relative(repositoryRoot, path).startsWith(join("tests", "architecture")))
      .map((path) => relative(repositoryRoot, path));

    expect(offenders).toEqual([]);
  });
});
