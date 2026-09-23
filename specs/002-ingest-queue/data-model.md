# Data Model: The Ingest Queue

Phase 1 of [plan.md](plan.md). Only what this feature adds or changes. `Run`, `ToolGrant`,
`Ceilings`, `Page`, `ProvenanceStamp` and everything in the WIKI context stand as
`001-first-ingest/data-model.md` describes them.

Every field below has a reader in this feature; where one is stored but not read, R-08 says why.

---

## Submission

A text the user handed to Grimoire and that was *accepted*, together with its state, the run it was
given, and whether that run's failure has been acknowledged. A refused text never becomes one.

| Field | Type | New? | Meaning |
| --- | --- | --- | --- |
| `Id` | `Guid` | — | Identifies the submission. The browser's row key |
| `Text` | `string` | — | The text as the user gave it, whole and untidied. Every run receives it (INGEST-002), which is why it has to survive a stop: a submission that waits across a restart still has its run ahead of it |
| `SubmittedAt` | `DateTimeOffset` | — | When it was made. **The queue's order** (RUNS-002), and shown in the browser (ACCESS-004) |
| `State` | `SubmissionState` | — | Exactly one of the four (RUNS-001) |
| `RunId` | `Guid?` | **new** | The run this submission was given, set at the moment the board hands it out. `null` while it waits its turn (R-04) |
| `AcknowledgedAt` | `DateTimeOffset?` | **new** | When the user acknowledged this submission's failed run. `null` otherwise. Not a state — RUNS-001's four stay four |
| `Excerpt` | `string` (derived) | **new** | The opening of `Text`: whitespace collapsed, trimmed, cut to 120 characters with `…` where it was cut (ACCESS-004, R-07). Derived, never stored |

### The four states, unchanged

`submitted` → `running` → `done` \| `failed`, with `submitted` → `failed` possible (a run that ends
before the agent reports in — a surface that is not the grant, a ceiling, a stop). `done` and
`failed` are terminal. The one thing this feature adds to the picture is that **`submitted` now
covers two situations** and `RunId` is what tells them apart:

| `State` | `RunId` | What it means | Reads in the browser |
| --- | --- | --- | --- |
| `Submitted` | `null` | waiting its turn | `submitted` |
| `Submitted` | set | dispatched, agent has not reported in | `submitted` |
| `Running` | set | the agent has reported in | `running` |
| `Done` / `Failed` | set | over | `done` / `failed` |

`Failed` with `AcknowledgedAt` null is the one combination that blocks the queue (RUNS-003).

---

## SubmissionBoard

The submissions and their order, and the whole of the queue rule. A plain object, not a port: the
port is the store behind it.

| Operation | New? | What it decides |
| --- | --- | --- |
| `Accept(text, inputs)` | changed | INGEST-001/003/004 as before, **minus** the refusal for a run in progress (INGEST-005, retired). A text is accepted whatever else is under way |
| `TakeNext(runId)` | **new** | The queue rule, whole. Returns the earliest waiting submission and marks it with `runId`, or `null`. Returns `null` when a submission is under way, or when a failed submission stands unacknowledged (RUNS-002, RUNS-003) |
| `ReportedIn(submissionId)` | changed | Was on `Submission`; moves to the board so that the store is written under the same lock |
| `Ended(submissionId, terminal)` | changed | Same |
| `Acknowledge(submissionId)` | **new** | Marks that submission's failed run acknowledged. A submission that is not an unacknowledged failure changes nothing and starts nothing (RUNS-003) |
| `Restore(stored)` | **new** | Start-up: takes what the store holds, and makes every submission with a `RunId` and a non-terminal state read `failed` (RUNS-004) — **after** the agents of those runs have been terminated (RUNS-006) |
| `All` | — | Every submission, newest first — the order the browser lists them in |
| `Find(id)` | — | unchanged |

### The queue rule, stated once

`TakeNext` hands out a submission **only if** all three hold:

1. no submission has a `RunId` with a non-terminal state — nothing is under way (RUNS-002);
2. no submission reads `failed` with `AcknowledgedAt` null — no failure blocks (RUNS-003);
3. at least one submission reads `submitted` with no `RunId` — something is waiting.

The one it hands out is the waiting submission with the earliest `SubmittedAt` (RUNS-002). All of
this is decided under the board's single lock, and the store is written before the decision is
visible, so a stop between deciding and recording cannot exist.

---

## Run (record kept with a submission)

Unchanged as an object. What is new is that a run's record **outlives its process**.

| Field | Type | Stored? | Meaning |
| --- | --- | --- | --- |
| `Id` | `Guid` | yes | The run. Named in the wiki's log (RUNS-005), addresses the tool endpoint (DEC-013), and is what an acknowledgement names (ACCESS-003) |
| `SubmissionId` | `Guid` | yes | The submission it works |
| `StartedAt` | `DateTimeOffset` | yes | When it began |
| `Grant` | `ToolGrant` | yes — the tool names and `RecordedAt` | The tools this run was given (GUARD-002, GUARD-003). Stored because GUARD-003's record would otherwise be weaker than the state around it once RUNS-004 lands (R-08) |
| `AgentProcess` | `AgentProcessIdentity?` | yes — both halves | **New.** Which process this run's agent is: its identifier **and** the moment it started. The pair, never the identifier alone — operating systems reuse those numbers, and a reboot would otherwise have Grimoire terminate an unrelated program. `null` until the child exists. Read once, at start-up, to terminate an agent that outlived a stop (RUNS-006, R-11) |
| `TokensUsed` | `long` | **no** | Not stored. A run cut off by a stop reads failed and nothing judges it again; a number nothing reads would be a placeholder (II.1, R-08) |
| `Ceilings` | `Ceilings` | no | Fixed values, the same for every run |

---

## Acknowledgement

Not an entity of its own. It is `Submission.AcknowledgedAt`, and it exists so that RUNS-003 has
something to read. It changes no state — the acknowledged run still reads `failed` — and it survives
a stop, so a restart does not re-block a queue the user already cleared.

It **names the submission**, not the run: a submission has exactly one run here, so its identifier
already identifies the failed one, and nothing about the run has to reach the browser for the user
to clear it (R-06).

---

## ISubmissionStore — the new port

What has to survive, and nothing more. Declared by the RUNS context, adapted by
`SqliteSubmissionStore` (V.2). Full behaviour in [contracts/submission-store.md](contracts/submission-store.md).

| Operation | Called when |
| --- | --- |
| `LoadAsync()` | the hub starts, before anything is served |
| `AddAsync(submission)` | a text is accepted — **before** the user is answered |
| `AssignRunAsync(submissionId, run)` | the board hands a submission out, with the run's record |
| `SetStateAsync(submissionId, state)` | the agent reports in, and where the run ends |
| `AcknowledgeAsync(submissionId, at)` | a failure is acknowledged |

Every call returns only when the change is on disk. There is no `Flush`, no `SaveChanges` and no
close-time write: RUNS-004 covers a stop that gives Grimoire no chance to act, so nothing may be
waiting to be written (R-02).

**No delete and no update of a text.** The store mirrors `IWikiStore`'s shape for the same reason:
nothing in this feature removes a submission, and cancelling one is out of scope.

---

## What the browser is told

`SubmissionView`, the one shape `GET /api/submissions` returns per submission.

| Field | New? | Always present? | Why |
| --- | --- | --- | --- |
| `id` | — | yes | The row key |
| `state` | — | yes | Exactly one of the four (ACCESS-002) |
| `submittedAt` | — | yes | When it was made (ACCESS-004) |
| `excerpt` | **new** | yes | The opening of the text (ACCESS-004) |
| `awaitingAcknowledgement` | **new** | **no** — only where the submission reads `failed` and is unacknowledged, and then always `true` | That the control is offered on this row (ACCESS-003). It says an action is available, not what the run did, so ACCESS-002 is untouched (R-06) |

Nothing else, and in particular **no run identifier**. No step, no reasoning, no duration, no cost,
no history, no queue position, no time waited — ACCESS-002 and OUT-02 (spec, Out of scope). The
acknowledgement addresses `id`, which this shape has carried since `001-first-ingest`.
