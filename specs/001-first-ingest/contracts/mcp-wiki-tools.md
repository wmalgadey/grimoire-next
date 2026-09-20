# Contract: MCP wiki tools (agent ↔ hub)

The hub serves these as an MCP server over streamable HTTP at `/mcp/runs/{runId}`, bound to
loopback. The agent reaches them as `mcp__wiki__<tool>`.

**No authentication on the endpoint.** `docs/product.md` §2 puts Grimoire inside a network the user
trusts and gives it no access control of its own; the run identifier in the path is addressing, not
authorisation. Recorded as a decision in the plan, and the first feature that puts Grimoire on an
untrusted network (OUT-10, OUT-12) has to revisit it.

**This tool list is the grant, and it is the agent's entire tool surface.** The run is started with
every built-in tool switched off (`--tools ""`), so a tool outside this list does not exist for the
run — it has no handler here and no definition there (GUARD-001; research.md R-03, R-11). The grant
is recorded with the run (GUARD-003).

Reading, creating and changing only. **No delete, no move** — GUARD-002 does not grant them, and a
first ingest does not need them.

---

## `list_pages`

Read. Every path in the wiki. No arguments.

```json
{ "paths": ["index.md", "log.md", "recipes/index.md", "recipes/sourdough.md"] }
```

## `read_page`

Read. One file's full text, frontmatter included.

```json
{ "path": "recipes/sourdough.md" }
```

Errors: `not-found`, `outside-wiki`.

## `write_page`

Create or replace a page. The tool that WIKI-002 stamps.

```json
{ "path": "recipes/sourdough.md", "content": "---\ntype: Recipe\n…\n---\n\n…body…" }
```

Before the write, the hub:

1. Parses the YAML frontmatter — **and nothing else about the page**.
2. If there is no `generated` key, adds one. If there is one, in any form, replaces it.
   Both values are Grimoire's: `by` names Grimoire and the model, `at` is the write time. On an
   update the record names the run that updated it.
3. If the frontmatter does not parse, or `generated` is present but is not a mapping, the write
   **fails** and the agent is told why.

```json
{ "written": "recipes/sourdough.md", "generated": { "by": "…", "at": "2026-09-20T10:06:31Z" } }
```

Error: `frontmatter-unreadable`, with a message the agent can act on. Nothing else about the page
is judged (WIKI-002).

## `write_index`

Create or replace a section index or the root `index.md`. Same path rules; **no generation record**
— indexes are not pages (data-model.md).

## `append_log`

Append an entry to `log.md`. No generation record. The run's entry is what RUNS-005 reads to decide
whether the run ends done.

---

## Path rules

Every `path` is relative to the wiki root. A path escaping it — absolute, or containing `..` —
fails with `outside-wiki`. This is why GUARD-001's edge case holds: the agent cannot read or write
outside the wiki because no granted tool reaches there, and there is no other tool. The run's
working directory is one Grimoire owns, not the wiki, so the wiki is reachable only through these
five tools.

## What the hub never does through these tools

Remove, revert or commit anything (WIKI-003, `docs/product.md` §4). A failed run's writes stay
exactly where the agent left them.
