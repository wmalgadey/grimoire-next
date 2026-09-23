# Contract: The submission store (hub ↔ what survives a stop)

The port RUNS-004 needs, and the third external system in the tree. Declared by the RUNS context as
`ISubmissionStore`; adapted by `SqliteSubmissionStore`, which is the only file in the repository
that names SQLite (Constitution V.2). The Fast suite has an in-memory adapter at the same port
(III.9); the Contract suite drives the real one against a real file (III.4).

---

## The promise

1. **A change is on disk before the call that made it returns.** There is no flush, no
   `SaveChanges`, and no write at close. RUNS-004 covers a stop that gives Grimoire no chance to act,
   so nothing may be waiting to be written.
2. **What was written is what is read back**, by a process that never saw the one that wrote it.
3. **Nothing is removed and no text is rewritten.** The store has no delete and no way to change a
   submission's text — the same shape `IWikiStore` has, for the same reason: nothing in this feature
   needs one, and cancelling a submission is out of scope.
4. **The store judges nothing.** Whether a run may start, whether a failure blocks, which submission
   is next — all of that is the board's (RUNS-002, RUNS-003). The store keeps facts and returns them.

---

## Operations

| Operation | Called when | Effect |
| --- | --- | --- |
| `LoadAsync` | the hub starts, before anything is served | Every submission with its state, its run's record where it has one, and its acknowledgement where it has one. Ordered by `SubmittedAt`, oldest first — the queue's order |
| `AddAsync(submission)` | a text is accepted, **before the user is answered** | The submission: id, text, when it was made, state `submitted`, no run, no acknowledgement |
| `AssignRunAsync(submissionId, run)` | the board hands a submission out | The run's identifier onto the submission, and the run's own record: identifier, submission, when it started, and the tools it was granted |
| `RecordAgentProcessAsync(runId, identity)` | the agent's child process exists | Which process this run's agent is — its identifier and the moment it started. Written as soon as the child exists, because a kill a moment later is exactly the case it is for (RUNS-006) |
| `SetStateAsync(submissionId, state)` | the agent reports in; the run ends | The submission's state. The store does not check the transition — RUNS-001 is the board's |
| `AcknowledgeAsync(submissionId, at)` | a failure is acknowledged | When it was acknowledged |

---

## What is kept

Two tables. They do not change shape in this feature, which is why there is no migration mechanism
(plan.md, Technology decisions).

**`submissions`**

| Column | Type | Null? | Notes |
| --- | --- | --- | --- |
| `id` | TEXT | no | primary key, the GUID |
| `text` | TEXT | no | as the user gave it, whole and untidied |
| `submitted_at` | TEXT | no | ISO 8601, UTC. **The queue's order** |
| `state` | TEXT | no | `submitted` · `running` · `done` · `failed` |
| `run_id` | TEXT | yes | null while the submission waits its turn |
| `acknowledged_at` | TEXT | yes | null unless the user has acknowledged this submission's failed run |

**`runs`**

| Column | Type | Null? | Notes |
| --- | --- | --- | --- |
| `id` | TEXT | no | primary key, the GUID that names the run in the wiki's log and addresses its tool endpoint |
| `submission_id` | TEXT | no | the submission it works |
| `started_at` | TEXT | no | ISO 8601, UTC |
| `granted_tools` | TEXT | no | the bare tool names the run was given, in order (GUARD-002, GUARD-003) |
| `grant_recorded_at` | TEXT | no | when the grant was recorded |
| `agent_process_id` | INTEGER | yes | the agent's process identifier. Null until the child exists (RUNS-006) |
| `agent_process_started_at` | TEXT | yes | when that process started, ISO 8601 UTC. **Kept with the identifier and never without it**: the identifier alone is not an identity, because those numbers are reused and a reboot makes every one of them point somewhere else (research.md R-11) |

**Not kept**: the tokens a run spent. A run cut off by a stop reads failed, both ceilings bind a run
only while it is in progress, and nothing judges it again — a number nothing reads would be a
placeholder for later (Constitution II.1, research.md R-08).

Times are stored as ISO 8601 UTC text, the same way the wiki's `generated.at` and the browser's list
already show them. Nothing in Grimoire needs an offset applied before two times can be compared.

---

## Where the file lives

A directory Grimoire owns, given at start-up: `--state <path>`, defaulting to `state/` beside the
hub.

**Not inside the wiki.** The queue writes nothing into the wiki and reads nothing in it (spec, "Who
writes what"), and Grimoire's bookkeeping inside the user's wiki repository would show up in their
version history, which `docs/product.md` §4 leaves to them.

---

## What the restart reads, and what the board does with it

`LoadAsync` returns facts. The rule that turns them into states is the board's (`Restore`):

| What the store holds | What the board makes of it | Requirement |
| --- | --- | --- |
| state `submitted`, `run_id` null | still waiting; starts in its turn | RUNS-002, RUNS-004 |
| state `submitted` or `running`, `run_id` set | the run was in progress when Grimoire stopped. **Its agent is terminated first** where the recorded pair still names a live process; then the run **reads `failed`** and blocks the queue until acknowledged. Nothing starts before both have happened | RUNS-006, RUNS-004, RUNS-003 |
| an `agent_process_id` whose process is gone, or is alive but started at another moment | nothing is terminated; the run reads `failed` as it would have anyway | RUNS-006 |
| state `failed`, `acknowledged_at` null | still blocks | RUNS-003 |
| state `failed`, `acknowledged_at` set | blocks nothing; still reads `failed` | RUNS-003 |
| state `done` | nothing to do | — |

Nothing is resumed and nothing is retried; what an interrupted run wrote stays in the wiki
(WIKI-003).

---

## What the Contract suite proves, and what it does not

**Proves** (against a real SQLite file in a temp directory):

- a submission added through one connection is read back through a new one, with its text, its time
  and its state;
- a run assigned through one connection comes back with its identifier and its granted tools;
- an acknowledgement comes back;
- `LoadAsync` returns submissions oldest first.

**Does not prove**: that a committed SQLite transaction survives a process being killed. That is
SQLite's decision and its own test suite's business, and III.8 says we test the decisions we made.
The one place a real uncatchable stop is exercised is the owner's acceptance run
([quickstart.md](../quickstart.md)), where the hub is killed with `kill -9` and started again.
