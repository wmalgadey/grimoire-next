/**
 * The complete tool surface of an ingest run: two in-process MCP tools, and nothing else (FR-010,
 * contracts/wiki-tools.md).
 *
 * Registered under the server name `wiki`, so the model sees them as `mcp__wiki__read_page` and
 * `mcp__wiki__write_page`. Adding a third tool, or widening either of these, is an agent autonomy
 * change: it requires its own ADR and must not be bundled with any other change (constitution
 * VI.4). This file is where such a change would have to appear, which is why it holds exactly one
 * list.
 */

import { createSdkMcpServer, tool } from "@anthropic-ai/claude-agent-sdk";
import { readPage, readPageSchema } from "./readPage.js";
import { writePage, writePageSchema } from "./writePage.js";
import type { ToolGuard } from "./guard.js";

/**
 * Builds the wiki tool server for one run.
 *
 * @param repositoryRoot The run's `cwd` — the agent's entire filesystem world (ADR-0005).
 * @param guard Records each granted call once it resolves (FR-021).
 */
export function createWikiToolServer(repositoryRoot: string, guard: ToolGuard) {
  const read = tool(
    "read_page",
    "Read a wiki page, or list the entries of a wiki directory. A page that does not exist "
      + "comes back as an explicit 'no such page' result rather than an error.",
    readPageSchema,
    async (args) => {
      const result = await readPage(repositoryRoot, args.path);
      guard.recordResolved(
        "mcp__wiki__read_page",
        args.path,
        result.refusedDetail ? "refused" : "ok",
        result.refusedDetail ?? null,
      );

      return {
        content: [{ type: "text" as const, text: result.text }],
        ...(result.refusedDetail ? { isError: true } : {}),
      };
    },
  );

  const write = tool(
    "write_page",
    "Create, update, or delete one wiki page. Pass null content to delete. Parent directories "
      + "are created as needed. Nothing is committed: the hub commits the whole run at its end.",
    writePageSchema,
    async (args) => {
      const result = await writePage(repositoryRoot, args.path, args.content);
      const outcome = result.refusedDetail ? "refused" : result.failedDetail ? "failed" : "ok";
      guard.recordResolved(
        "mcp__wiki__write_page",
        args.path,
        outcome,
        result.refusedDetail ?? result.failedDetail ?? null,
      );

      return {
        content: [{ type: "text" as const, text: result.text }],
        ...(outcome === "ok" ? {} : { isError: true }),
      };
    },
  );

  // Always loaded: against the real API the SDK otherwise defers MCP tools behind its own tool
  // search, which is outside the grant — the model would see neither tool's schema and every call
  // it made would be a refused search.
  return createSdkMcpServer({ name: "wiki", version: "1.0.0", tools: [read, write], alwaysLoad: true });
}
