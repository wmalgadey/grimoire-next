# Contracts — 003-live-run-record

Two documents. Everything not named here stands as the earlier features' `contracts/` wrote it.

| Document | Between | Status |
| --- | --- | --- |
| [hub-http-api.md](hub-http-api.md) | browser ↔ hub | **supersedes** the `002-ingest-queue` document of the same name |
| [run-record.md](run-record.md) | hub ↔ a run's record on disk | new — the port this feature adds, and the shape of the file it writes |

Unchanged and still in force:

- `specs/001-first-ingest/contracts/agent-cli-protocol.md` — hub ↔ the `claude` CLI. **Nothing in the
  protocol changes.** This feature reads two message kinds that document already specifies and that
  had no consumer until now: the `assistant` message's `tool_use` and `text` blocks, and the `user`
  message's `tool_result` blocks. What the spike measured about them is in
  [../research.md](../research.md) R-03, and the three lines it recorded go into
  `RecordedTranscript`. Nothing is written to the CLI that was not written before.
- `specs/001-first-ingest/contracts/mcp-wiki-tools.md` — agent ↔ hub, the five granted tools. The
  grant is unchanged; it is now also written into the record (GUARD-003).
- `specs/002-ingest-queue/contracts/submission-store.md` — hub ↔ the durable store. Still in force.
  This feature adds four columns to the `runs` table and one member to the port; the promise that
  every call returns only once the change is on disk is unchanged. The additions are in
  [../data-model.md](../data-model.md) §StoredRun.

A contract document of a closed feature is a change record and is not edited. Where this feature
changes a promise, the change and its reason are stated in the document that supersedes it.
