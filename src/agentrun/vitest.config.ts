import { fileURLToPath } from "node:url";
import { defineConfig } from "vitest/config";

// The runner suite lives in tests/agentrun/ because tests/ mirrors the slices rather than test
// kinds (plan "Project Structure"), so the project root is the repository root.
const repositoryRoot = fileURLToPath(new URL("../..", import.meta.url));

// Every test spawns a real runner process against the scripted model (tests/scripted-model/).
// No mocks: the LLM is the only double (constitution III.2).
export default defineConfig({
  root: repositoryRoot,
  // The suite imports from src/agentrun/ while living under tests/; both are inside the
  // repository, and Vite has to be told that explicitly.
  server: { fs: { allow: [repositoryRoot], strict: false } },
  test: {
    // The runner suite, plus the TypeScript half of the architecture gates (quality gate 1).
    include: ["tests/agentrun/**/*.test.ts", "tests/architecture/**/*.test.ts"],
    testTimeout: 120_000,
    hookTimeout: 120_000,
    fileParallelism: false,
  },
});
