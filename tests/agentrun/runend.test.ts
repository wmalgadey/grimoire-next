import { chmodSync } from "node:fs";
import { join } from "node:path";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { createWiki, runAgent, type WikiRepository } from "./harness.js";

/**
 * How a run ends when something other than the agent's own decision ends it: the tool-call
 * ceiling (FR-009), a model endpoint that answers with an error (plan IV), and a read that fails
 * (FR-021). Each one ends with a reason an operator can act on, and none of them leaves a call
 * unrecorded.
 */
describe("how a run ends", () => {
  let wiki: WikiRepository;

  beforeEach(() => {
    wiki = createWiki();
  });

  afterEach(() => {
    wiki.dispose();
  });

  it("stops at the tool-call ceiling even when one turn asks for more calls than it allows", async () => {
    // Six reads in one turn against a ceiling of three: maxTurns bounds turns, not calls, so only
    // the guard can stop this run where the limit says (FR-009).
    const result = await runAgent({ wiki, script: "parallel-tool-uses", maxToolCalls: 3 });

    const runEnd = result.events.find((event) => event.type === "run_end");
    expect(runEnd?.type === "run_end" && runEnd.outcome).toBe("failed");
    expect(runEnd?.type === "run_end" && runEnd.failureReason).toMatch(/ceiling of 3/);

    // At most three calls acted; everything past the ceiling is on the record as refused, never
    // an absence, and the model was not asked for another turn once the ceiling was reached.
    const acted = result.toolCalls.filter((call) => call.outcome !== "refused");
    expect(acted.length).toBeLessThanOrEqual(3);
    expect(result.toolCalls.some((call) => call.outcome === "refused")).toBe(true);
    expect(result.toolCalls.map((call) => call.target)).not.toContain("f.md");
  });

  it("records a read that fails as a failed call rather than losing it", async () => {
    wiki.write("locked.md", "# Locked\n");
    chmodSync(join(wiki.path, "locked.md"), 0o000);

    try {
      const result = await runAgent({ wiki, script: "read-locked" });

      const call = result.toolCalls.find((recorded) => recorded.target === "locked.md");
      expect(call?.outcome).toBe("failed");
      expect(call?.detail).toBeTruthy();
    } finally {
      chmodSync(join(wiki.path, "locked.md"), 0o644);
    }
  });

  it("reports the status the model endpoint answered with when it ends the run", async () => {
    const result = await runAgent({ wiki, script: "endpoint-refuses" });

    const runEnd = result.events.find((event) => event.type === "run_end");
    expect(runEnd?.type === "run_end" && runEnd.outcome).toBe("failed");
    expect(runEnd?.type === "run_end" && runEnd.modelEndpointStatus).toBe("403");
    expect(runEnd?.type === "run_end" && runEnd.failureReason).toMatch(/403/);
    // Not the agent's final message dressed up as a commit message.
    expect(runEnd?.type === "run_end" && runEnd.commitMessage).toBeFalsy();
  });

  it("ends a completed run with no model endpoint status", async () => {
    const result = await runAgent({ wiki, script: "read-then-write" });

    const runEnd = result.events.find((event) => event.type === "run_end");
    expect(runEnd?.type === "run_end" && runEnd.outcome).toBe("completed");
    expect(runEnd?.type === "run_end" && runEnd.modelEndpointStatus).toBeNull();
  });

  it("takes the commit message from the final turn only, never from earlier narration", async () => {
    // The last turn has no text. An earlier turn's narration is not the agent's final message,
    // so run_end carries none and the hub falls back to `ingest <taskId>` (research R9).
    const result = await runAgent({ wiki, script: "narrates-then-ends-silently" });

    const runEnd = result.events.find((event) => event.type === "run_end");
    expect(runEnd?.type === "run_end" && runEnd.outcome).toBe("completed");
    expect(runEnd?.type === "run_end" && runEnd.commitMessage).not.toContain("Writing the topic page");
    expect(runEnd?.type === "run_end" && (runEnd.commitMessage ?? "").trim()).toBe("");
  });
});
