# Data Model: Source Ingest via Agent Run

**Feature**: `001-source-ingest-agent-run` | **Date**: 2026-09-16 | **Plan**: [plan.md](./plan.md)

Entities are taken from the spec's Key Entities; fields and rules are traced to the functional
requirement that forces them. Two stores hold state and neither duplicates the other:

- **SQLite** (`src/tasks/adapters/`) holds the task artifact — everything an operator inspects.
- **The git repository** (`src/wiki/adapters/`) holds wiki content and history. The artifact
  stores *commit identities*, never wiki content.

---

## Task

The user-facing, inspectable record of one ingest. One per accepted submission (FR-002).

| Field | Type | Rules |
|-------|------|-------|
| `id` | string, opaque | Stable for the task's life; the task view's address (FR-002) |
| `state` | enum | `queued` \| `running` \| `completed` \| `failed` \| `reverted` — exactly these five (FR-020) |
| `submittedAt` | timestamp, UTC | Orders the queue (FR-019) and the task list, newest first (FR-030) |
| `startedAt` | timestamp, UTC, nullable | Set when the run is dispatched |
| `endedAt` | timestamp, UTC, nullable | Set when the run ends, whatever the outcome |
| `source` | Source | Exactly one, retained as long as the task is (FR-004) |
| `run` | AgentRun, nullable | At most one, never more — including across a restart (FR-005) |
| `failureReason` | string, nullable | Human-readable; required whenever `state = failed` (FR-018) |
| `revert` | RevertRecord, nullable | Present exactly when `state = reverted` (FR-025) |

**Openability**: every state renders (FR-020, SC-006). A `queued` task has no `run`; a `running`
task has a `run` with a partial tool-call record and no commit; a `failed` task has a
`failureReason` and no commit.

### State transitions

```text
                 accepted submission
                          │
                          ▼
                     ┌─────────┐   run dispatched    ┌─────────┐
                     │ queued  │────────────────────►│ running │
                     └─────────┘                     └─────────┘
                          │                            │     │
       URL retrieval fails│              run ends ok   │     │ run fails / aborts /
       (FR-003)           │              (FR-015/016)  │     │ crashes / hits limit
                          │                            ▼     │ (FR-009, FR-017)
                          │                      ┌───────────┐    │
                          │                      │ completed │    │
                          │                      └───────────┘    │
                          │                            │          │
                          ▼                            │ revert   ▼
                     ┌────────┐                        │     ┌────────┐
                     │ failed │◄───────────────────────┼─────│ failed │
                     └────────┘   hub restart while    │     └────────┘
                                  running (FR-028)     ▼
                                                 ┌──────────┐
                                                 │ reverted │  (terminal)
                                                 └──────────┘
```

Rules on transitions:

- `queued → running` happens for at most one task at a time, in `submittedAt` order (FR-019).
- `running → failed` is also produced by **startup recovery**: every task found in `running` at
  boot becomes `failed` with a reason naming the interruption, and is never dispatched again
  (FR-028). No task may remain `running` while no run is executing (SC-006).
- `completed → reverted` is the only transition out of a terminal state, and only when revert is
  eligible (FR-024). `failed` and `reverted` are terminal.
- **Immutability (FR-023)**: once a run has ended, its instruction version, tool grant, tool calls,
  and commit identity never change. The only permitted later writes to a task are `state = reverted`
  and the attached `RevertRecord`.

---

## Source

What the user submitted. Belongs to exactly one task.

| Field | Type | Rules |
|-------|------|-------|
| `kind` | enum | `text` \| `url` |
| `submittedValue` | string | The pasted text, or the URL as typed. Non-empty after trimming, else the submission is rejected with no task created (FR-001) |
| `retrievedText` | string, nullable | For `kind = url`, the text fetched before dispatch; `null` until retrieved and permanently `null` if retrieval failed (FR-003) |
| `retrievedAt` | timestamp, UTC, nullable | Set with `retrievedText` |
| `byteLength` | integer | Of the text handed to the run; recorded so an oversized-source failure can name the size (FR-029) |

**No size limit and no truncation** (FR-029). The text handed to the run is
`kind = text ? submittedValue : retrievedText`, whole. A run that cannot proceed because of size
fails with a reason identifying the byte length and produces no commit.

---

## AgentRun

One execution of the agent for one task. Never more than one per task (FR-005).

| Field | Type | Rules |
|-------|------|-------|
| `instructionVersion` | InstructionVersion | Recorded at dispatch, before the first model call (FR-014) |
| `toolGrant` | ToolGrant | Recorded at dispatch, before the first model call (FR-013) |
| `toolCalls` | ordered list of ToolCall | Append-only during the run, frozen after (FR-021, FR-023) |
| `outcome` | enum, nullable | `completed` \| `failed`; `null` while running |
| `failureReason` | string, nullable | Required when `outcome = failed` (FR-018) |
| `commit` | WikiCommit, nullable | At most one; `null` when the run changed nothing or failed (FR-015, FR-016, FR-017) |
| `toolCallCount` | integer | Against the run limit (FR-009) |
| `durationMs` | integer, nullable | Against the elapsed half of the run limit |

**Run limit (FR-009)**: configuration, not specification — an elapsed-time ceiling and a tool-call
ceiling. Reaching either ends the run `failed` with no commit.

### InstructionVersion

| Field | Type | Rules |
|-------|------|-------|
| `path` | string | Repo-relative path of the instruction file loaded (`src/instructions/ingest.md`) |
| `sha256` | string | Full content hash of the file's bytes; the task view shows the first 12 hex (research R10) |
| `byteLength` | integer | Of the instruction file as loaded |

### ToolGrant

| Field | Type | Rules |
|-------|------|-------|
| `tools` | list of string | For this feature, exactly `["mcp__wiki__read_page", "mcp__wiki__write_page"]` — exactly two, in 100% of runs (FR-010, SC-004) |
| `recordedAt` | timestamp, UTC | Before the first model call (FR-013) |

### ToolCall

One entry per call the agent made, **including refused calls** (FR-011, FR-021).

| Field | Type | Rules |
|-------|------|-------|
| `seq` | integer | 1-based, strictly increasing, the order the calls were made (FR-021) |
| `tool` | string | The tool name as the model named it — including a name outside the granted set, which is how a denied attempt is recorded |
| `target` | string, nullable | The wiki page path for a granted call; the raw requested target for a refused one |
| `outcome` | enum | `ok` \| `failed` \| `refused` |
| `detail` | string, nullable | Why it failed or was refused — e.g. target outside the wiki, tool not granted |
| `at` | timestamp, UTC | |

An operator reading this list can tell a run that read before writing from one that wrote blind
(SC-010), and can tell a deliberate no-change run from a broken one (SC-011).

---

## WikiCommit

The single commit produced by a run that changed content, and the diff derived from it.

| Field | Type | Rules |
|-------|------|-------|
| `sha` | string | Opaque to the user (spec assumption); the wiki commit this run produced |
| `parentSha` | string | The pre-run tip — the commit a failed run leaves the wiki at (FR-017, SC-003) and the content revert restores (FR-025) |
| `message` | string | The run's final assistant message, verbatim (research R9) |
| `committedAt` | timestamp, UTC | |
| `fileDiffs` | list of FileDiff | Derived from the commit on read, not stored (FR-022) |

**Exactly 1:1 with runs that changed content** (SC-002): never zero, never more than one.

### FileDiff

| Field | Type | Rules |
|-------|------|-------|
| `path` | string | Wiki-relative page path |
| `changeKind` | enum | `added` \| `modified` \| `removed` |
| `patch` | string | Per-file content added, changed, and removed (FR-022) |

---

## RevertRecord

The fact that a task's commit was undone.

| Field | Type | Rules |
|-------|------|-------|
| `revertCommitSha` | string | The commit that performed the restoration; the run's own commit stays in history (FR-025) |
| `revertedAt` | timestamp, UTC | |

Creating it sets the task's state to `reverted`. A reverted task keeps its instruction version,
tool calls, and original diff, shows the revert commit identity, and offers no revert action
(FR-026). A second revert attempt is refused.

### Revert eligibility (FR-024, FR-027)

Revert is offered **exactly when all three hold**:

1. the task's run produced a commit, **and**
2. that commit is the wiki's current tip, **and**
3. the task is not already `reverted`.

Otherwise no action is offered, and when the reason is (2) the task view says the task was
**superseded by a later wiki commit** rather than showing a disabled control. The check is against
the live wiki tip at page load, and the revert re-checks that tip under the same single-writer lock,
so a double-click or a second browser tab loses the race and is refused rather than reverting twice.

Because revert is itself a commit, immediately after a revert no task's commit is the tip — undo
reaches one ingest back, not further.

---

## Ingest instruction

Not stored in SQLite: it is the file `src/instructions/ingest.md`, versioned in git. Loaded at
dispatch by `src/agentrun/instruction/`, which is the only module that reads it and the only one
that can construct the `SystemPrompt` the model port accepts (constitution I.2). Its content is the
sole home of everything in the spec's Judgment Boundary; its version is recorded on every task
(FR-014), which is what makes a behaviour change attributable to a specific instruction revision
(SC-009).

---

## Relationships

```text
Task 1 ──── 1 Source
Task 1 ──── 0..1 AgentRun          (never more than one, ever)
Task 1 ──── 0..1 RevertRecord      (present iff state = reverted)

AgentRun 1 ──── 1 InstructionVersion
AgentRun 1 ──── 1 ToolGrant
AgentRun 1 ──── 0..n ToolCall      (ordered by seq, includes refusals)
AgentRun 1 ──── 0..1 WikiCommit    (absent when nothing changed or the run failed)

WikiCommit 1 ──── 0..n FileDiff    (derived from git on read, never stored)
```

## Storage notes

- Seven SQLite tables — `task`, `source`, `agent_run`, `tool_grant`, `tool_call`, `revert_record`,
  `pending_settlement` — with `tool_call` append-only and ordered by `(run_id, seq)`.
- `pending_settlement` holds a run commit or revert commit put on record **before** the wiki branch
  moves to it, and is cleared in the transaction that records that commit on its task. A row that
  survives a restart is a settlement the previous process began: startup records the commit on its
  task if it is in history, and forgets it if not (FR-015, FR-025, FR-028). Nothing is rewritten
  either way.
- The task list query is `ORDER BY submitted_at DESC` with a cursor; every retained task is
  reachable from it, so closing the browser loses access to no task (SC-006).
- `agent_run` carries a uniqueness constraint on `task_id`. FR-005's "never a second run" is then a
  database property, not a code convention — a retry after a restart cannot slip through.
- Diffs are read from git on demand rather than stored, so the artifact cannot drift from history.
