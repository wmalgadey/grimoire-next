# Contracts — 002-ingest-queue

Two documents. Everything not named here stands as `specs/001-first-ingest/contracts/` wrote it.

| Document | Between | Status |
| --- | --- | --- |
| [hub-http-api.md](hub-http-api.md) | browser ↔ hub | **supersedes** the `001-first-ingest` document of the same name |
| [submission-store.md](submission-store.md) | hub ↔ the durable store | new — the port this feature adds |

Unchanged and still in force, in `specs/001-first-ingest/contracts/`:

- `agent-cli-protocol.md` — hub ↔ the `claude` CLI. RUNS-006 stops a run with the interrupt this
  document already specifies (DEC-016); nothing in the protocol changes.
- `mcp-wiki-tools.md` — agent ↔ hub, the five granted tools. The queue writes nothing into the wiki
  and reads nothing in it.

A contract document of a closed feature is a change record and is not edited. Where this feature
changes a promise, the change and its reason are stated here.
