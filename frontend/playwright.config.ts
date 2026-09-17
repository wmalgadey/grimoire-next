import { defineConfig } from "@playwright/test";

// TS-16 runs against the built SvelteKit app served by a real hub process
// over a real git repository and a real runner. The per-suite fixtures start it.
export default defineConfig({
  testDir: "../tests/surfaces",
  timeout: 120_000,
  fullyParallel: false,
  workers: 1,
  reporter: [["list"]],
  use: {
    trace: "retain-on-failure",
  },
});
