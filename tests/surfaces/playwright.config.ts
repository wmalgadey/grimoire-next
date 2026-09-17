import { defineConfig } from "@playwright/test";

// TS-16 runs against the built SvelteKit app served by a real hub process
// over a real git repository and a real runner. The per-suite fixtures start it.
// Run through frontend/ (`npm --prefix frontend run test:e2e`), which builds the app first.
export default defineConfig({
  testDir: ".",
  timeout: 120_000,
  fullyParallel: false,
  workers: 1,
  reporter: [["list"]],
  use: {
    trace: "retain-on-failure",
  },
});
