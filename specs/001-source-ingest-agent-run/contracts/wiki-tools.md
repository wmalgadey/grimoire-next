# Contract: The Granted Wiki Tools

**Internal.** The complete tool surface of an ingest run: two in-process MCP tools,
and nothing else (FR-010). Registered with `createSdkMcpServer({ name: "wiki", … })` in
`src/agentrun/wiki-tools/`, so the model sees them as `mcp__wiki__read_page` and
`mcp__wiki__write_page`.

Every path in both tools is **wiki-relative**. Both resolve the requested path against the run's
repository root with `realpath` and refuse anything landing outside it — which covers `../`
traversal, absolute paths, and symlinks that an earlier run may have committed into wiki content.
A refusal returns an error result to the model *and* is recorded as a tool call with
`outcome: "refused"` (FR-011); the run continues, and what it does next is the agent's decision.

---

## `mcp__wiki__read_page`

Read what the wiki already contains, so the agent can decide in light of it. Whether to read, and
what, is judgment and lives in the instruction file — the tool imposes no reading.

**Input**

| Field | Type | Notes |
|-------|------|-------|
| `path` | string | Wiki-relative page path, e.g. `topics/kafka.md`. A directory path lists its entries instead of returning content. |

**Result**

- Page content as text, or
- an entry listing for a directory path, or
- an explicit "no such page" result — *not* an error. A missing page is information the agent
  legitimately acts on ("nothing here yet, so create it"), and turning it into a failure would
  push that decision into the hub.

**Recorded as**: `tool: "mcp__wiki__read_page"`, `target: <path>`, `outcome: ok | refused`.

---

## `mcp__wiki__write_page`

Create, update, or delete one wiki page. The only way anything reaches the wiki.

**Input**

| Field | Type | Notes |
|-------|------|-------|
| `path` | string | Wiki-relative page path. Parent directories are created as needed. |
| `content` | string \| null | Full page content. `null` deletes the page. |

**Result**: confirmation with the resulting change kind (`added` / `modified` / `removed` / `unchanged`).

**Recorded as**: `tool: "mcp__wiki__write_page"`, `target: <path>`, `outcome: ok | failed | refused`.

**Visibility**: the write lands in the wiki repository's working tree and is **not committed**. It
is not in the wiki — not in any diff, not in any revert, not on any surface — until the hub commits
the whole run as one commit (FR-012, FR-015). A run that fails, crashes, is aborted, or hits its
limit has that working tree reset and commits nothing, so as far as wiki history and every
user-facing surface are concerned its writes never existed (FR-017).

---

## Not granted

No network, no shell, no filesystem outside the wiki repository, no subagents, no task tracking, no user
questions. Every built-in SDK tool is named in `disallowedTools`, which removes its definition from
the request, so the model never sees it. Anything that still reaches the permission layer is denied
by a `PreToolUse` hook — which runs before every other permission step — and the attempt is
recorded as a refused tool call, so an operator can see what the agent tried to reach for.

Adding a third tool, or widening either of these, is an agent autonomy change: it requires its own
ADR and must not be bundled with any other change (constitution VI.4, Governance). This grant's
record is [ADR-0009](../../../docs/adr/0009-tool-grant-enforcement.md).
