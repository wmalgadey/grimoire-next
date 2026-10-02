# Contracts — 004-ask-the-wiki

Two documents. Everything not named here stands as the earlier features' `contracts/` wrote it.

| Document | Between | Status |
| --- | --- | --- |
| [hub-http-api.md](hub-http-api.md) | browser ↔ hub | **supersedes** the `003-live-run-record` document of the same name |
| [question-run.md](question-run.md) | hub ↔ a question's run | new — the read-only grant, the endpoint that serves it, and what the run is given |

Unchanged and still in force:

- `specs/001-first-ingest/contracts/agent-cli-protocol.md` — hub ↔ the `claude` CLI. **Nothing in
  the protocol changes.** A question's run is dispatched with the same argv, differing in two
  values: the model's grant is `list_pages` and `read_page`, and `--mcp-config` points at
  `/mcp/questions/{runId}` instead of `/mcp/runs/{runId}`. Both are covered in
  [question-run.md](question-run.md). The three message kinds a chat shows — `tool_use`,
  `tool_result` and `text` — are the ones `003-live-run-record` already reads (DEC-028); nothing new
  is parsed.
- `specs/001-first-ingest/contracts/mcp-wiki-tools.md` — agent ↔ hub, the granted tools. The five
  tools and their shapes are unchanged. What this feature adds is a second endpoint at which only
  two of them exist; the tools themselves are the same tools, serving the same `IWikiStore`.
- `specs/002-ingest-queue/contracts/submission-store.md` — hub ↔ the durable store. Still in force,
  including the promise that every call returns only once the change is on disk. This feature adds
  two members to the port and lets `submission_id` be null where a question caused the run; the
  additions are in [../data-model.md](../data-model.md) §`StoredRun` and §`ISubmissionStore`.
- `specs/003-live-run-record/contracts/run-record.md` — hub ↔ a run's record on disk. Unchanged, and
  now stated of **a run a submission causes**: RUNS-007 as this feature rewords it gives a record to
  those runs and not to every run. A question's run writes no record, so nothing in that document
  applies to one.

A contract document of a closed feature is a change record and is not edited. Where this feature
changes a promise, the change and its reason are stated in the document that supersedes it.
