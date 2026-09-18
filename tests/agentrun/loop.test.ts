import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { createWiki, runAgent, type WikiRepository } from "./harness.js";

/**
 * T047 / TS-06 (FR-007, FR-008, SC-007). The run is a real tool-use loop, not a single request:
 * every tool result is fed back so the next call can be decided in light of it.
 *
 * Parameterised N = 1…8 against a real spawned runner and the real SDK loop. All N calls happen
 * **within a single run** — the thing that makes this an agent rather than a template filler.
 */
describe("the agent loop", () => {
  let wiki: WikiRepository;

  beforeEach(() => {
    wiki = createWiki();
  });

  afterEach(() => {
    wiki.dispose();
  });

  it.each([1, 2, 3, 4, 5, 6, 7, 8])(
    "executes and records all %i tool call(s) within a single run",
    async (n) => {
      const result = await runAgent({ wiki, script: `escalation-${n}`, maxToolCalls: 40 });

      expect(result.toolCalls).toHaveLength(n);
      // One run: exactly one run_end, never one per iteration.
      expect(result.events.filter((event) => event.type === "run_end")).toHaveLength(1);
    },
  );

  it("numbers tool calls 1-based and strictly increasing, in the order they were made", async () => {
    const result = await runAgent({ wiki, script: "escalation-8" });

    expect(result.toolCalls.map((call) => call.seq)).toEqual([1, 2, 3, 4, 5, 6, 7, 8]);
  });

  it("feeds each result back, so a later call can depend on an earlier one", async () => {
    // The escalation script alternates read and write over step-N pages: a loop that did not feed
    // results back would stop after the first call rather than reaching step 8.
    const result = await runAgent({ wiki, script: "escalation-8" });

    const targets = result.toolCalls.map((call) => call.target);
    expect(targets).toEqual([
      "step-1.md",
      "step-2.md",
      "step-3.md",
      "step-4.md",
      "step-5.md",
      "step-6.md",
      "step-7.md",
      "step-8.md",
    ]);
  });

  it("hands each call's result to the model in the very next request", async () => {
    // The targets above could be reproduced by a loop that dropped results, since the script picks
    // its turn from the conversation. This asserts the other half: request k+1 carries the result
    // of call k, with its content, answering that call's own tool_use id (SC-007).
    const result = await runAgent({ wiki, script: "escalation-8" });

    type Block = { type?: string; id?: string; tool_use_id?: string; content?: unknown };
    type Message = { role?: string; content?: string | Block[] };
    const conversations = result.requestBodies
      .map((body) => ((body as { messages?: Message[] } | null)?.messages ?? []))
      .filter((messages) => messages.length > 0);

    for (let k = 1; k <= 8; k++) {
      const next = conversations.find(
        (messages) => messages.filter((message) => message.role === "assistant").length === k,
      );
      expect(next, `no request followed call ${k}`).toBeDefined();

      const blocks = (messages: Message[], role: string) =>
        messages
          .filter((message) => message.role === role)
          .flatMap((message) => (Array.isArray(message.content) ? message.content : []));
      const toolUse = blocks(next!, "assistant").filter((block) => block.type === "tool_use").at(-1);
      const toolResult = blocks(next!, "user").find(
        (block) => block.type === "tool_result" && block.tool_use_id === toolUse?.id,
      );

      expect(toolResult, `request ${k + 1} does not answer call ${k}`).toBeDefined();
      expect(JSON.stringify(toolResult?.content)).toContain(`step-${k}.md`);
    }
  });

  it("ends the run with the agent's own final message, verbatim", async () => {
    const result = await runAgent({ wiki, script: "escalation-3" });

    const runEnd = result.events.find((event) => event.type === "run_end");
    expect(runEnd).toBeDefined();
    if (runEnd?.type === "run_end") {
      // Commit-message wording is judgment, taken verbatim from the agent (research R9).
      expect(runEnd.commitMessage).toBe("Completed 3 steps");
      expect(runEnd.outcome).toBe("completed");
      expect(runEnd.toolCallCount).toBe(3);
    }
  });

  it("emits instruction_loaded and tool_grant before any tool call", async () => {
    const result = await runAgent({ wiki, script: "escalation-2" });

    const types = result.events.map((event) => event.type);
    expect(types[0]).toBe("instruction_loaded");
    expect(types[1]).toBe("tool_grant");
    expect(types.indexOf("tool_call")).toBeGreaterThan(types.indexOf("tool_grant"));
  });

  it("runs a no-op script to completion without touching the wiki", async () => {
    const before = wiki.head();
    const result = await runAgent({ wiki, script: "no-op" });

    expect(result.toolCalls).toHaveLength(0);
    expect(wiki.head()).toBe(before);
    const runEnd = result.events.find((event) => event.type === "run_end");
    expect(runEnd?.type === "run_end" && runEnd.outcome).toBe("completed");
  });

  it("writes into the working tree but never commits — committing is the hub's alone", async () => {
    const before = wiki.commitCount();

    await runAgent({ wiki, script: "write-only" });

    // The write landed in the working tree (FR-012)…
    expect(wiki.read("topics/blind.md")).toContain("# Blind");
    // …and nothing was committed. The runner does not know the wiki is a git repository
    // (contracts/runner-protocol.md "What the runner may not do", constitution II.2).
    expect(wiki.commitCount()).toBe(before);
  });

  it("exits zero when the run ends normally", async () => {
    const result = await runAgent({ wiki, script: "read-then-write" });

    expect(result.exitCode).toBe(0);
  });
});
