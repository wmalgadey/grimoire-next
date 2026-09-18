import { describe, expect, it } from "vitest";
import { createWiki, runAgent } from "./harness.js";

/**
 * A runner whose hub has died must not keep working.
 *
 * The hub holds the protocol stream open for the whole run, so EOF on stdin means the hub is gone.
 * Nothing else tells the runner that: a SIGKILL or an OOM kill reaches the hub and no one else, and
 * there is no portable parent-death signal. A runner that kept going would write into a wiki nobody
 * is supervising — and the next hub's startup recovery resets that working tree and dispatches the
 * next run, so the two would be writing into the same tree at once (FR-017, FR-028).
 */
describe("an orphaned runner", () => {
  it("exits when the hub closes the protocol stream mid-run", async () => {
    const wiki = createWiki();
    try {
      const result = await runAgent({
        wiki,
        // Keeps working long after the first tool call, so the run is genuinely in flight.
        script: "never-stopping",
        closeStdinAfterFirstToolCallMs: 100,
      });

      // It exited on its own, rather than having to be killed or running to its limit.
      expect(result.exitCode).toBe(1);
      expect(result.stderr).toContain("exiting rather than running unsupervised");
    } finally {
      wiki.dispose();
    }
  }, 60_000);
});
