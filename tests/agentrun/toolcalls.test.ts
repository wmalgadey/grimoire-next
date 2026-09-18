import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { createWiki, runAgent, type WikiRepository } from "./harness.js";

/**
 * T048 / TS-08 (FR-011, FR-021). Every call the agent made is recorded, in order, with its target
 * and outcome — **including refused ones**. A refusal is a recorded tool call, not an absence:
 * an operator has to be able to see what the agent reached for, not only what it got.
 */
describe("the tool-call record", () => {
  let wiki: WikiRepository;

  beforeEach(() => {
    wiki = createWiki({ "index.md": "# Index\n\nThe wiki starts here.\n" });
  });

  afterEach(() => {
    wiki.dispose();
  });

  it("records a read and a write in the order they were made, each with its target", async () => {
    const result = await runAgent({ wiki, script: "read-then-write" });

    expect(result.toolCalls.map((call) => [call.seq, call.tool, call.target, call.outcome])).toEqual([
      [1, "mcp__wiki__read_page", "index.md", "ok"],
      [2, "mcp__wiki__write_page", "topics/scripted.md", "ok"],
    ]);
  });

  it("records an attempt at a tool outside the granted set as refused, not as nothing", async () => {
    const result = await runAgent({ wiki, script: "escape-attempts" });

    const refused = result.toolCalls.filter((call) => call.outcome === "refused");
    expect(refused.length).toBeGreaterThan(0);

    // The tool name is recorded as the model named it, which is how a denied attempt reads.
    const names = result.toolCalls.map((call) => call.tool);
    for (const attempted of ["Bash", "Read", "WebFetch", "Task", "mcp__unknown__do_anything"]) {
      expect(names).toContain(attempted);
    }

    for (const call of refused) {
      expect(call.detail).toBeTruthy();
    }
  });

  it("records a write whose target resolves outside the wiki as refused, with a reason", async () => {
    const result = await runAgent({ wiki, script: "escape-through-write-paths" });

    const traversal = result.toolCalls.find((call) => call.target === "../escaped.md");
    expect(traversal).toBeDefined();
    expect(traversal?.outcome).toBe("refused");
    expect(traversal?.detail).toContain("outside the wiki repository");
  });

  it("keeps seq strictly increasing across a mix of granted and refused calls", async () => {
    const result = await runAgent({ wiki, script: "escape-attempts" });

    const sequence = result.toolCalls.map((call) => call.seq);
    expect(sequence).toEqual([...sequence].sort((a, b) => a - b));
    expect(new Set(sequence).size).toBe(sequence.length);
    expect(sequence[0]).toBe(1);
  });

  it("counts refused calls in the run's tool-call count", async () => {
    const result = await runAgent({ wiki, script: "escape-attempts" });

    const runEnd = result.events.find((event) => event.type === "run_end");
    expect(runEnd?.type === "run_end" && runEnd.toolCallCount).toBe(result.toolCalls.length);
  });

  it("reports a missing page as a result rather than an error, so the agent can act on it", async () => {
    // "Nothing here yet, so create it" is a decision the agent legitimately makes; turning a
    // missing page into a failure would push that decision into the hub
    // (contracts/wiki-tools.md).
    const result = await runAgent({ wiki, script: "escalation-1" });

    expect(result.toolCalls).toHaveLength(1);
    expect(result.toolCalls[0]?.tool).toBe("mcp__wiki__read_page");
    expect(result.toolCalls[0]?.target).toBe("step-1.md");
    expect(result.toolCalls[0]?.outcome).toBe("ok");
  });

  it("timestamps every call", async () => {
    const result = await runAgent({ wiki, script: "read-then-write" });

    for (const call of result.toolCalls) {
      expect(Number.isNaN(Date.parse(call.at))).toBe(false);
    }
  });
});

/**
 * The half of containment that a denylist cannot carry on its own (FR-010, SC-004). Naming every
 * built-in in `disallowedTools` removes it from the request — but only the ones that were named,
 * and the SDK grows tools between releases. Asserting the request itself is what turns a rotted
 * list into a failing build rather than a quietly wider grant.
 */
describe("what the model is offered", () => {
  let wiki: WikiRepository;

  beforeEach(() => {
    wiki = createWiki();
  });

  afterEach(() => {
    wiki.dispose();
  });

  it("offers the model exactly the two granted tools and nothing else", async () => {
    const result = await runAgent({ wiki, script: "read-then-write" });

    expect(result.toolNamesOffered.length).toBeGreaterThan(0);
    for (const offered of result.toolNamesOffered) {
      expect([...offered].sort()).toEqual(["mcp__wiki__read_page", "mcp__wiki__write_page"]);
    }
  });

  // Against the real API the SDK turns tool search on, defers every MCP tool behind it, and offers
  // its own ToolSearch to load them. ToolSearch is outside the grant, so the agent could reach
  // neither granted tool and every call it made was a refused search. The scripted model hides
  // tool search unless it is switched on, which is why this case has to ask for it.
  it("offers the two granted tools callable, not deferred, when tool search is on", async () => {
    const result = await runAgent({ wiki, script: "read-then-write", toolSearch: true });

    expect(result.toolNamesOffered.length).toBeGreaterThan(0);
    for (const offered of result.toolNamesOffered) {
      expect([...offered].sort()).toEqual(["mcp__wiki__read_page", "mcp__wiki__write_page"]);
    }
    for (const deferred of result.toolNamesDeferred) {
      expect(deferred).toEqual([]);
    }
    expect(result.toolCalls.map((call) => [call.tool, call.outcome])).toEqual([
      ["mcp__wiki__read_page", "ok"],
      ["mcp__wiki__write_page", "ok"],
    ]);
  });
});
