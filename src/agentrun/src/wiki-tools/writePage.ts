/**
 * `mcp__wiki__write_page` — create, update, or delete one wiki page (contracts/wiki-tools.md).
 * The only way anything reaches the wiki.
 *
 * The write lands in the wiki repository's working tree and is **not committed**. It is not in the
 * wiki — not in any diff, not in any revert, not on any surface — until the hub commits the whole
 * run as one commit (FR-012, FR-015). A run that fails, crashes, is aborted, or hits its limit has
 * that working tree reset, so as far as history and every surface are concerned its writes never
 * existed (FR-017).
 */

import { mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { dirname } from "node:path";
import { z } from "zod";
import { containedPath } from "./containment.js";

/** The shape the model sees. */
export const writePageSchema = {
  path: z.string().describe("Wiki-relative page path. Parent directories are created as needed."),
  content: z
    .string()
    .nullable()
    .describe("Full page content. null deletes the page."),
};

/** What the write did to the page. */
export type ChangeKind = "added" | "modified" | "removed" | "unchanged";

/** What a write produced, and what the tool-call record should say about it. */
export interface WritePageResult {
  readonly text: string;
  readonly changeKind?: ChangeKind;
  readonly refusedDetail?: string;
  readonly failedDetail?: string;
}

/** Creates, updates, or deletes one page, and reports which of those it did. */
export async function writePage(
  repositoryRoot: string,
  requestedPath: string,
  content: string | null,
): Promise<WritePageResult> {
  const contained = await containedPath(repositoryRoot, requestedPath);
  if (!contained.contained) {
    return { text: `Refused: ${contained.detail}.`, refusedDetail: contained.detail };
  }

  const existing = await currentContent(contained.absolutePath);

  try {
    if (content === null) {
      if (existing === null) {
        return { text: `No such page: ${requestedPath}. Nothing was deleted.`, changeKind: "unchanged" };
      }

      await rm(contained.absolutePath);
      return { text: `Removed ${requestedPath}.`, changeKind: "removed" };
    }

    if (existing === content) {
      // Writing a page's own content back is not a change. Saying so keeps "the run changed
      // nothing" honest when that is what happened (FR-016).
      return { text: `${requestedPath} already had this content.`, changeKind: "unchanged" };
    }

    await mkdir(dirname(contained.absolutePath), { recursive: true });
    await writeFile(contained.absolutePath, content, "utf8");

    const changeKind: ChangeKind = existing === null ? "added" : "modified";
    return { text: `${changeKind === "added" ? "Added" : "Updated"} ${requestedPath}.`, changeKind };
  } catch (cause) {
    const detail = (cause as Error).message;
    return { text: `Could not write ${requestedPath}: ${detail}`, failedDetail: detail };
  }
}

async function currentContent(absolutePath: string): Promise<string | null> {
  try {
    return await readFile(absolutePath, "utf8");
  } catch {
    return null;
  }
}
