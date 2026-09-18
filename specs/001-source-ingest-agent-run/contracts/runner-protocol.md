# Contract: Hub ↔ Agent Runner Process Protocol

**Internal.** Not exposed to users and not part of the frontend↔hub contract
(constitution V.5 governs that one, `hub-api.openapi.yaml`). Documented here because it is the
boundary across which `src/dispatch/` and `src/agentrun/` are separately reviewable, and because
the ordering it enforces is what makes FR-013 and FR-014 provable.

## Invocation

```text
node src/agentrun/dist/main.js
```

Spawned by `src/dispatch/adapters/RunnerProcess.cs`, one process per run, never reused.

| Aspect | Value | Why |
|--------|-------|-----|
| `cwd` | the wiki repository's working tree | The agent's entire filesystem world (ADR-0005, ADR-0007) |
| `env` | **replaced**, not merged. Exactly: `PATH`, `HOME` (per-run `tmpfs` dir), `ANTHROPIC_BASE_URL` (the proxy), `ANTHROPIC_AUTH_TOKEN` (an **opaque internal token**, never an Anthropic credential), `ANTHROPIC_CUSTOM_HEADERS: X-Grimoire-Run: <runId>`, `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1`, `GRIMOIRE_INSTRUCTION` (the instruction file's absolute path); tests add `CLAUDE_CODE_DISABLE_EXPERIMENTAL_BETAS=1`. Full table in [deployment.md](./deployment.md) | The SDK's `env` option replaces `process.env`, so credential and host-environment scrubbing is a property of the spawn (ADR-0007, ADR-0010) |
| `stdin` | NDJSON, hub → runner | Carries the dispatch payload and the `proceed` gate |
| `stdout` | NDJSON, runner → hub | The run's event stream |
| `stderr` | free text | Diagnostics only; never parsed, never a source of task state |
| exit code | `0` run ended normally (completed **or** agent-reported failure), non-zero crash | A crash is indistinguishable from an abort to the hub, and both mean: no commit (FR-017) |

The hub kills the process when the elapsed run limit is reached (FR-009). A killed or crashed
process leaves the working tree to be reset and nothing committed.

## Message envelope

One JSON object per line, no embedded newlines, UTF-8.

```json
{ "type": "<event>", "…": "…" }
```

## Sequence

```text
hub                                   runner
 │  spawn ─────────────────────────────►│
 │                                      │ load src/instructions/ingest.md
 │◄──────── instruction_loaded ─────────│ (before any model call)
 │◄──────── tool_grant ─────────────────│
 │ persist both on the task             │
 │ ──────── dispatch ──────────────────►│ source text + run limits
 │ ──────── proceed ───────────────────►│
 │                                      │ query() — first model call happens HERE
 │◄──────── tool_call ──────────────────│ (one per call, in order, refusals included)
 │◄──────── tool_call ──────────────────│
 │             …                        │
 │◄──────── run_end ────────────────────│ outcome + commit message
 │  process exits                       │
```

The runner **blocks** after emitting `tool_grant` until `proceed` arrives. That is the whole point
of the handshake: FR-013 and FR-014 require the grant and the instruction version to be recorded
*before the first model call*, and without the block that ordering is a hope about scheduling
rather than a property a test can assert (Test Strategy TS-07 asserts it by comparing the persisted
record against the scripted-model server's first-request timestamp).

## Runner → hub events

### `instruction_loaded`

Emitted first, always, before anything else. The instruction loader is the only module that reads
instruction files and the only one that can construct the `SystemPrompt` type the model port
accepts (constitution I.2).

```json
{ "type": "instruction_loaded",
  "path": "src/instructions/ingest.md",
  "sha256": "<64 hex>",
  "byteLength": 4213 }
```

### `tool_grant`

```json
{ "type": "tool_grant",
  "tools": ["mcp__wiki__read_page", "mcp__wiki__write_page"] }
```

Exactly two, in 100% of runs (FR-010, SC-004). The hub records what it is told **and** asserts it
equals the grant it configured; a mismatch fails the run rather than being logged and ignored.

### `tool_call`

One per call the agent made, in the order made, emitted as it resolves. Refused and failed calls
are emitted the same way as successful ones — a refusal is a recorded tool call, not an absence
(FR-011, FR-021).

```json
{ "type": "tool_call",
  "seq": 1,
  "tool": "mcp__wiki__read_page",
  "target": "topics/kafka.md",
  "outcome": "ok",
  "detail": null,
  "at": "2026-09-16T10:31:02.441Z" }
```

`outcome` is `ok` | `failed` | `refused`. `detail` carries the reason for the latter two — e.g.
`"target resolves outside the wiki repository"`, `"tool not granted"`.

### `run_end`

```json
{ "type": "run_end",
  "outcome": "completed",
  "failureReason": null,
  "commitMessage": "Add kafka topic page, link from streaming index",
  "toolCallCount": 4,
  "modelEndpointStatus": null }
```

`modelEndpointStatus` is set when the run ended because the model endpoint answered with an error
rather than a message — the HTTP status as a string, or `no-response` when there was none. The hub
emits `grimoire.run.model_endpoint_unreachable` with it, so a broken egress path or upstream is
distinguishable from the agent failing even when the proxy is reachable. A prompt the model refuses
as too long is the source's size, not the endpoint: `modelEndpointStatus` stays `null` and
`failureReason` names the source's byte length (FR-029).

The tool-call ceiling is enforced per call by the runner's guard: the call past the ceiling is
refused and recorded, the conversation is stopped, and `run_end` reports `failed`. The hub checks the
recorded count again at run end, so a runner that failed to stop still cannot have its work
committed (FR-009).

`commitMessage` is the run's final assistant message text, verbatim — commit-message wording is
judgment and lives in the instruction file (research R9). Empty message → the hub commits under the
constant `ingest <taskId>`.

`outcome: "failed"` with a `failureReason` covers the agent-reported failure and the turn limit.
A crash produces no `run_end` at all, which the hub treats identically: no commit.

## Hub → runner messages

### `dispatch`

```json
{ "type": "dispatch",
  "taskId": "…",
  "sourceText": "…",
  "maxToolCalls": 40,
  "maxElapsedMs": 600000 }
```

`sourceText` is the source **whole** — no size limit, no truncation, no summarisation (FR-029).

### `proceed`

```json
{ "type": "proceed" }
```

Sent only after `instruction_loaded` and `tool_grant` are durably persisted on the task.

## What the runner may not do

- No writing outside `cwd` — enforced in the write tool by `realpath` containment and proven
  against `../`, absolute paths, and planted symlinks (Test Strategy TS-10).
- No tool outside the granted pair — built-ins are removed from the request via `disallowedTools`,
  and a `PreToolUse` hook denies and records anything else before any other permission step
  (research R3).
- No git. The runner never commits, never touches `.git`, and does not know the wiki is a git
  repository. Committing is `src/wiki/`'s alone (constitution II.2).
- No SQLite. The runner reports; the hub persists.
- No network beyond the configured model endpoint, and **no upstream credential** — only an opaque
  token valid against the proxy (ADR-0010). Nonessential SDK background traffic is disabled, so a
  deny-all egress policy raises no connection it has to refuse.

## Surviving the container split

The transport here is stdio because hub and runner share a container today
([ADR-0007](../../../docs/adr/0007-host-trust-boundary.md)). When the runner moves into its
own container with its own network namespace, **only the transport changes**: the same events, the
same order, and the same proceed handshake carried as NDJSON over HTTP. The event shapes are
deliberately free of file descriptors, paths outside the wiki repository, and anything else that assumes a
shared process tree, so that move is a transport change rather than a redesign.
