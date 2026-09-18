#!/usr/bin/env node
// Quality gate 8: the per-method complexity regression gate (constitution VII.4, VII.5).
//
//   node .github/complexity/gate.mjs --threshold 15            # check, as CI runs it
//   node .github/complexity/gate.mjs --threshold 15 --update   # accept the current state
//
// Measures every method in production code — C# under src/ through the SDK's own CA1502, and
// TypeScript/Svelte in the npm workspaces below through ESLint's `complexity` rule — and compares
// what is over the threshold with the committed baseline. Only regressions fail: a method that is
// newly over the threshold, or one already over it that got worse. A method that was already over
// and did not get worse passes, so the gate tightens with the work actually done rather than
// blocking unrelated changes. The threshold is CI configuration (.github/workflows/ci.yml), not a
// constant here and not a line in the constitution.

import { execFileSync } from "node:child_process";
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const baselinePath = join(root, ".github/complexity/baseline.json");

// Production code only. Each workspace is linted with its own ESLint configuration.
const TYPESCRIPT_WORKSPACES = [
  { directory: "src/agentrun", sources: ["src"] },
  { directory: "frontend", sources: ["src"] },
];

const args = process.argv.slice(2);
const update = args.includes("--update");
const thresholdArgument = args[args.indexOf("--threshold") + 1] ?? process.env.COMPLEXITY_THRESHOLD;
const threshold = Number(thresholdArgument);
if (!args.includes("--threshold") && !process.env.COMPLEXITY_THRESHOLD) {
  fail("No threshold. Pass --threshold N (CI sets it in .github/workflows/ci.yml).");
}
if (!Number.isInteger(threshold) || threshold < 1) {
  fail(`The threshold must be a positive integer; it is '${thresholdArgument}'.`);
}

const measured = new Map([...csharp(threshold), ...typescript(threshold)]);

if (update) {
  const methods = Object.fromEntries([...measured].sort(([a], [b]) => a.localeCompare(b)));
  writeFileSync(baselinePath, `${JSON.stringify({ threshold, methods }, null, 2)}\n`);
  console.log(`Baseline written: ${measured.size} method(s) over ${threshold}.`);
  process.exit(0);
}

const baseline = existsSync(baselinePath) ? JSON.parse(readFileSync(baselinePath, "utf8")) : { methods: {} };
const regressions = [];
for (const [method, complexities] of measured) {
  const before = baseline.methods[method] ?? [];
  complexities.forEach((complexity, index) => {
    if (index >= before.length) {
      regressions.push(`new:      ${method} has complexity ${complexity} (threshold ${threshold})`);
    } else if (complexity > before[index]) {
      regressions.push(`worsened: ${method} went from ${before[index]} to ${complexity} (threshold ${threshold})`);
    }
  });
}

const improved = Object.keys(baseline.methods).filter((method) => {
  const now = measured.get(method) ?? [];
  const before = baseline.methods[method];
  return now.length < before.length || now.some((complexity, index) => complexity < before[index]);
});

console.log(`${measured.size} method(s) over complexity ${threshold}; ${Object.keys(baseline.methods).length} in the baseline.`);
if (improved.length > 0) {
  console.log(`${improved.length} baselined method(s) improved or went away. Tighten the ratchet with --update:`);
  for (const method of improved) console.log(`  ${method}`);
}
if (regressions.length > 0) {
  console.error(`\nComplexity regressions (${regressions.length}):`);
  for (const regression of regressions) console.error(`  ${regression}`);
  console.error("\nSimplify the method. The gate fails only on new or worsened violations (constitution VII.5).");
  process.exit(1);
}
console.log("No complexity regression.");

// ---------------------------------------------------------------------------------------------

function csharp(max) {
  // CA1502 reports every method whose cyclomatic complexity exceeds the threshold written to
  // CodeMetricsConfig.txt; Directory.Build.props wires both files in only when this property is set.
  const directory = mkdtempSync(join(tmpdir(), "grimoire-complexity-"));
  try {
    writeFileSync(join(directory, "CodeMetricsConfig.txt"), `CA1502: ${max}\n`);
    writeFileSync(join(directory, "complexity.globalconfig"), "is_global = true\ndotnet_diagnostic.CA1502.severity = warning\n");
    const output = run("dotnet", [
      "build", join(root, "Grimoire.sln"), "--no-incremental", "-nologo", "-clp:NoSummary",
      "-p:TreatWarningsAsErrors=false", `-p:GrimoireComplexityDir=${directory}`,
    ], { DOTNET_CLI_UI_LANGUAGE: "en" });

    const found = new Map();
    const reported = new Set();
    const pattern = /^(.+?)\((\d+,\d+)\): warning CA1502: '(.+?)' has a cyclomatic complexity of '(\d+)'/;
    for (const line of output.split("\n")) {
      const match = pattern.exec(line.trim());
      if (!match) continue;
      const file = relative(root, match[1]);
      if (!file.startsWith("src/")) continue;
      // MSBuild repeats a warning for every project that compiles the file's project; one method is
      // one position, counted once.
      const position = `${file}(${match[2]})`;
      if (reported.has(position)) continue;
      reported.add(position);
      record(found, `${file}::${match[3]}`, Number(match[4]));
    }
    return found;
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}

function typescript(max) {
  const found = new Map();
  for (const workspace of TYPESCRIPT_WORKSPACES) {
    const cwd = join(root, workspace.directory);
    const output = run("npx", [
      "--no-install", "eslint", "--format", "json",
      "--rule", JSON.stringify({ complexity: ["error", { max }] }),
      ...workspace.sources,
    ], {}, cwd);

    for (const file of JSON.parse(output)) {
      // A file ESLint could not parse is a file it did not measure; passing on it would let any
      // complexity in it through unseen.
      const fatal = file.messages.find((message) => message.fatal);
      if (fatal) {
        fail(`ESLint could not measure ${relative(root, file.filePath)}: ${fatal.message}`);
      }
      for (const message of file.messages) {
        if (message.ruleId !== "complexity") continue;
        const match = /^(.+?) has a complexity of (\d+)\./.exec(message.message);
        if (!match) continue;
        record(found, `${relative(root, file.filePath)}::${match[1]}`, Number(match[2]));
      }
    }
  }
  return found;
}

// Methods are identified by file and name, which survives edits elsewhere in the file; a line
// number or an ordinal would not. Several methods can share a name — overloads, anonymous
// functions — so each name holds the complexities of all of them, largest first, and they are
// compared position by position.
function record(found, name, complexity) {
  const values = found.get(name) ?? [];
  values.push(complexity);
  values.sort((a, b) => b - a);
  found.set(name, values);
}

function run(command, commandArgs, extraEnvironment = {}, cwd = root) {
  try {
    return execFileSync(command, commandArgs, {
      cwd,
      env: { ...process.env, ...extraEnvironment },
      encoding: "utf8",
      maxBuffer: 256 * 1024 * 1024,
      stdio: ["ignore", "pipe", "pipe"],
    });
  } catch (error) {
    // ESLint exits 1 when it reports violations, which is exactly the output wanted; a build that
    // fails to compile is not a measurement, and the gate says so rather than passing on nothing.
    if (command === "npx" && error.status === 1 && error.stdout) return error.stdout;
    fail(`'${command} ${commandArgs.join(" ")}' failed in ${relative(root, cwd) || "."}:\n${error.stdout ?? ""}${error.stderr ?? ""}`);
  }
}

function fail(message) {
  console.error(message);
  process.exit(2);
}
