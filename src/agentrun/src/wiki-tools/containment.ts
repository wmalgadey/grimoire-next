/**
 * Path containment for both wiki tools (FR-011, contracts/wiki-tools.md).
 *
 * Every path either tool is given is wiki-relative and resolved against the run's repository root
 * with `realpath`. That single mechanism covers all three escapes at once — `../` traversal, an
 * absolute path, and a symlink an earlier run may have committed into wiki content — because it
 * asks where the path *lands*, not what it looks like. A denylist of suspicious strings would
 * catch the first two and miss the third.
 */

import { realpath } from "node:fs/promises";
import { dirname, isAbsolute, resolve, sep } from "node:path";

/** The reason a path was refused, in the words the tool-call record carries. */
export const OUTSIDE_THE_WIKI = "target resolves outside the wiki repository";

/** Where a requested path landed, or why it was refused. */
export type ContainmentResult =
  | { readonly contained: true; readonly absolutePath: string }
  | { readonly contained: false; readonly detail: string };

/**
 * Resolves a wiki-relative path against the repository root and refuses anything landing outside
 * it.
 *
 * The root itself is resolved through `realpath` first, so a repository that is reached by way of
 * a symlink — as it is on macOS, where `/tmp` is a link to `/private/tmp` — does not make every
 * path look like an escape.
 *
 * For a path that does not exist yet, the nearest existing ancestor is resolved instead: a new
 * page cannot be `realpath`ed, but the directory it would be created in can, and that is what
 * decides whether the write lands inside.
 */
export async function containedPath(
  repositoryRoot: string,
  requestedPath: string,
): Promise<ContainmentResult> {
  if (requestedPath.length === 0) {
    return { contained: false, detail: "no page path was given" };
  }

  // An absolute path is never wiki-relative, whatever it points at.
  if (isAbsolute(requestedPath)) {
    return { contained: false, detail: OUTSIDE_THE_WIKI };
  }

  let root: string;
  try {
    root = await realpath(repositoryRoot);
  } catch (cause) {
    return {
      contained: false,
      detail: `the wiki repository could not be resolved: ${(cause as Error).message}`,
    };
  }

  const candidate = resolve(root, requestedPath);
  const resolved = await resolveThroughSymlinks(candidate);

  if (resolved === null) {
    return { contained: false, detail: OUTSIDE_THE_WIKI };
  }

  if (resolved !== root && !resolved.startsWith(root + sep)) {
    return { contained: false, detail: OUTSIDE_THE_WIKI };
  }

  // `.git` is not wiki content. The runner never commits and does not know the wiki is a git
  // repository (contracts/runner-protocol.md); letting it write into `.git` would make that
  // untrue in the worst possible way.
  const relative = resolved.slice(root.length + 1);
  if (relative === ".git" || relative.startsWith(`.git${sep}`)) {
    return { contained: false, detail: OUTSIDE_THE_WIKI };
  }

  return { contained: true, absolutePath: resolved };
}

/**
 * Resolves a path through every symlink on it, falling back to the nearest existing ancestor for
 * a path that does not exist yet.
 */
async function resolveThroughSymlinks(candidate: string): Promise<string | null> {
  try {
    return await realpath(candidate);
  } catch {
    // Does not exist. Resolve the deepest ancestor that does, then re-append the rest: a write to
    // `out/new.md` where `out` is a symlink out of the repository must still be refused.
    const parent = dirname(candidate);
    if (parent === candidate) {
      return null;
    }

    const resolvedParent = await resolveThroughSymlinks(parent);
    if (resolvedParent === null) {
      return null;
    }

    return resolve(resolvedParent, candidate.slice(parent.length + 1));
  }
}
