/**
 * `mcp__wiki__read_page` — read what the wiki already contains, so the agent can decide in light
 * of it (contracts/wiki-tools.md).
 *
 * Whether to read, and what, is judgment and lives in the instruction file. The tool imposes no
 * reading and no ordering; it answers questions.
 */

import { readdir, readFile, stat } from "node:fs/promises";
import { z } from "zod";
import { containedPath } from "./containment.js";

/** The shape the model sees. */
export const readPageSchema = {
  path: z
    .string()
    .describe("Wiki-relative page path, e.g. topics/kafka.md. A directory path lists its entries."),
};

/** What a read produced, and what the tool-call record should say about it. */
export interface ReadPageResult {
  readonly text: string;
  readonly refusedDetail?: string;
}

/**
 * Reads a page, lists a directory, or reports that there is no such page.
 *
 * A missing page is an explicit result and **not an error**: "nothing here yet, so create it" is a
 * decision the agent legitimately makes, and turning it into a failure would move that decision
 * out of the instruction file and into the harness (constitution I.1).
 */
export async function readPage(repositoryRoot: string, requestedPath: string): Promise<ReadPageResult> {
  const contained = await containedPath(repositoryRoot, requestedPath);
  if (!contained.contained) {
    return { text: `Refused: ${contained.detail}.`, refusedDetail: contained.detail };
  }

  let entry;
  try {
    entry = await stat(contained.absolutePath);
  } catch {
    return { text: `No such page: ${requestedPath}` };
  }

  if (entry.isDirectory()) {
    const entries = await readdir(contained.absolutePath, { withFileTypes: true });
    const listing = entries
      .filter((child) => child.name !== ".git")
      .map((child) => (child.isDirectory() ? `${child.name}/` : child.name))
      .sort();

    return {
      text:
        listing.length === 0
          ? `${requestedPath} is an empty directory.`
          : `${requestedPath} contains:\n${listing.map((name) => `- ${name}`).join("\n")}`,
    };
  }

  return { text: await readFile(contained.absolutePath, "utf8") };
}
