# Data model: Ask the Wiki

Phase 1 of [plan.md](plan.md). What this feature adds and changes, entity by entity, with the
requirement each field serves. Everything not named here stands as `001-first-ingest`,
`002-ingest-queue` and `003-live-run-record` left it.

**Two things to hold on to while reading.** A question is not a submission: it puts nothing into the
wiki and appears in no list. And a question's run is a run like any other — queued, bounded by both
ceilings, with its grant recorded and its figures kept — differing in exactly two things: its grant
is read-only, and it has no record.

---

## `Queued` — what the queue rule reads (`Grimoire.Runs`)

The base of `Submission` and `Question`, holding only what `RunBoard.TakeNext` consults. It exists
because RUNS-002 orders waiting work across both kinds by when it was made, and one ordered list
carries that order intrinsically (research.md R-03).

| Member | Meaning | Requirement |
| --- | --- | --- |
| `Id` | Identifies it to the board and, for a question, to the chat | RUNS-002 |
| `RunId` | The run it was given, or null while it waits its turn | RUNS-002 |
| `IsWaiting` | Accepted, never handed out | RUNS-002 |
| `IsUnderWay` | Handed out and its run has not ended — the one condition that stops another run starting | RUNS-002 |
| `IsUnacknowledgedFailure` | Its run failed and the user has not said they have seen it — the one thing that holds the queue | RUNS-003 |
| `HandedTo(runId, model)` | The board has given it a run. Assumes the board's lock | RUNS-002 |
| `Ended(terminal)` | Its run ended done or failed. Terminal, and there is no way out | RUNS-001 |

It holds **no state field of its own beyond the terminal one**: the four values a submission shows
and the four the chat shows are the same four, read from whether there is a run and whether it has
ended.

**Two real implementations exist**, which is when Constitution II.4 allows an abstraction. What they
do not share is the whole of the difference: a submission is persisted, carries an excerpt and
appears in the browser's list; a question is none of those.

---

## `Submission` — unchanged, except where it sits

Derives from `Queued`. Its text, `SubmittedAt`, `Excerpt`, the four states, the acknowledgement and
the figures are exactly what `003-live-run-record` left. It is still the only kind the browser's
list shows (ACCESS-004, ACCESS-005).

---

## `Question` — a text the user asked the wiki (`Grimoire.Runs`)

Derives from `Queued`. Created only when a question is **accepted**; a refused one becomes nothing,
is stored nowhere and carries no state (QUERY-003).

| Field | Meaning | Requirement |
| --- | --- | --- |
| `Id` | Names it in the chat and in the acknowledgement the chat offers | QUERY-001, ACCESS-003 |
| `Text` | What the user asked, whole, as they typed it | QUERY-001, QUERY-002 |
| `AskedAt` | When it was asked. Shown in the chat; **not** what the queue is ordered by — that clock is not monotonic, and the board's list position is the order (as `Submission.SubmittedAt` already is) | ACCESS-007 |
| `RunId`, figures | Inherited: the run it was given, and what that run has spent | QUERY-002, RUNS-010, ACCESS-008 |

**No state field.** What the chat shows is read from the run:

| What the chat shows (ACCESS-007) | Read from |
| --- | --- |
| waiting its turn | no run yet |
| being answered | a run that has not ended |
| answered | its run ended done |
| got no answer, and why | its run ended failed, with `RunEndedBecause` saying why |

**Not persisted.** Nothing of a question reaches disk — not its text, not its answer, not its steps
(QUERY-005). Its *run* is persisted; see `StoredRun` below.

---

## `Chat` — the current conversation (`Grimoire.Hub`)

One object, in memory, for as long as the hub runs. There is exactly one, every browser reads that
same one, and starting a new chat replaces it whole (QUERY-005).

| Member | Meaning | Requirement |
| --- | --- | --- |
| `Turns` | In order: each question, the answer forming under it, and the steps the agent took | QUERY-005, ACCESS-007 |
| `Total` | What every question in this chat has spent, a failed one included — **no ceiling beside it**, because each question carries its own | ACCESS-008 |
| `Ask(question)` | A question joins the chat, reading *waiting its turn* | QUERY-001, ACCESS-007 |
| `AgentSaid(runId, text)` | A piece of the answer, appended as it arrives | ACCESS-007 |
| `StepHappened(runId, step)` | A tool call, or what one returned | ACCESS-007 |
| `Start()` | A new chat: the turns and the total are gone and nothing of them is reachable | QUERY-005 |

**A turn**:

| Field | Meaning |
| --- | --- |
| `Question` | The text the user asked |
| `Answer` | The agent's own text, in the order it arrived, as one piece of prose (research.md R-08) |
| `Steps` | One per tool call, and one per result: the tool's name, and what went in or came back, whole |
| `CostSpent`, `CostCeiling` | What this question's run has spent, against the ceiling it is held to (GUARD-004, DEC-015) |
| `State`, `Because` | Read from the run, as the table above says |

**The chat is not a record and is never written down.** A record exists because a run is handed over
and reviewed *afterwards*; a chat is read as it happens, so a file for it would be one nobody opens
(Constitution II.1, RUNS-007 as this feature rewords it).

**The answer's cost is the run's own figure** (RUNS-010, DEC-030). Nothing is counted a second time,
and the chat's total is a sum over the turns — which is why a failed question's spend is in it: a
run that failed still spent.

---

## `StoredRun` — one field renamed, and a null that means something

| Change | Was | Is now | Why |
| --- | --- | --- | --- |
| `SubmissionId` → `QueuedId` | The submission this run works | The submission **or the question** that caused it | One name for one thing, now that two kinds cause runs |
| `submission_id` column | Always a submission | **Null where a question caused the run** | RUNS-006 needs the agent's process identity on disk for every run; the question itself is not on disk (QUERY-005) |

Everything else — `StartedAt`, `GrantedTools`, `GrantRecordedAt`, `Model`, `AgentProcess`,
`CostSpent`, `Tokens`, `ToolCalls`, `EntriesLost` — is unchanged and means the same for both kinds
(GUARD-003, RUNS-010). `EntriesLost` is always zero for a question's run: it has no record to lose
entries from.

**The schema change** is DEC-031's mechanism for the columns that are missing:
`PRAGMA table_info(runs)`, then `ALTER TABLE` for each. No column is renamed on disk and no table is
rebuilt.

**A file that cannot hold a null `submission_id` is refused**, not migrated — see research.md R-04's
revision. SQLite cannot drop a `NOT NULL` constraint without rebuilding the table, and the owner
decided the file goes: nothing runs Grimoire in production yet, so such a file holds their own test
ingests. It is refused at start-up and names itself, so they know what to delete.

---

## `ISubmissionStore` — two members added

| Member | Meaning | Requirement |
| --- | --- | --- |
| `AddRun(StoredRun run)` | A run with no submission behind it, written the moment the board hands the question out — for the same reason `AssignRun` is written before the submission is marked: a write that fails must leave the queue as it was | RUNS-006, RUNS-010 |
| `LoadRunsWithoutASubmission()` | The question runs the last Grimoire left in progress, read at start-up beside `Load()` | RUNS-006, RUNS-004 |
| `Ended(…)` | Gains a form keyed by the run rather than by a submission, for a question's run, which has no submission state to set | RUNS-010 |

`Load()` is unchanged and still answers with the submissions, oldest first.

**What a start-up does with a question run it finds in progress**: terminates the agent where the
recorded identity is still live (DEC-024, unchanged), then marks the run ended failed. **No tail is
written** — it has no record — and nothing is restored into a chat, because QUERY-005 empties it.

---

## `ToolGrant` — a second grant, and the door that serves it

| Member | Meaning | Requirement |
| --- | --- | --- |
| `ForIngest` | `list_pages`, `read_page`, `write_page`, `write_index`, `append_log` — unchanged | GUARD-002 |
| `ForQuestion` | `list_pages`, `read_page`. **Nothing else** — no page, index or log written, nothing deleted and nothing moved | GUARD-005 |
| `Endpoint` | Which of the hub's two tool endpoints serves this grant: `runs` or `questions` | GUARD-001, GUARD-005 |

`Endpoint` sits on the grant so that the grant and the door that serves it are one value and cannot
disagree. The tools a question's run is not granted **do not exist at its endpoint at all**, which is
DEC-011's deny-by-default by construction rather than an allow-list over a larger surface
(research.md R-06). GUARD-001's existing equality check then guards it for free: a surface that is
not the grant ends the run failed before its first model call.

---

## `RunMomentKind` — unchanged, read by a second consumer

`ToolCalled`, `ToolReturned`, `AgentSaid`, `GrimoireSaid` stand exactly as `003-live-run-record`
defined them, and `AgentTranscript` stays the only thing that reads the protocol behind them
(DEC-028, Constitution V.2). What changes is where the hub sends them:

| The run was caused by | A moment goes to |
| --- | --- |
| a submission | the run's record, as today (RUNS-007, RUNS-009) |
| a question | the chat — `AgentSaid` into the answer, the two tool kinds into the steps (ACCESS-007) |

`GrimoireSaid` never arises for a question's run: it carries only the nudge, and RUNS-005's nudge is
asked only of a run that is to change the wiki.

---

## `StartUpInputs` — a third flag

| Field | Refuses | Requirement |
| --- | --- | --- |
| `InstructionPresent` | a **submission**, when the ingest instruction is missing | INGEST-003 |
| `QuestionInstructionPresent` | a **question**, when the question instruction is missing | QUERY-003 |
| `PurposeDescriptionPresent` | both | INGEST-003, QUERY-003 |

Read per acceptance rather than once at start-up, as today: the requirements are about the state of
those paths when the text is submitted or the question asked. Each refusal names exactly one thing,
and the instruction is looked at before the purpose description so that a start with neither in
place says one thing.

---

## `HubOptions` — two start-up inputs for the editor link

| Field | Meaning | Requirement |
| --- | --- | --- |
| `QuestionInstructionPath` | Grimoire's own question instruction, versioned in this repository | QUERY-004 |
| `VaultName` | The Obsidian vault the wiki is read in, or null where the owner has not said | ACCESS-009 |
| `VaultRoot` | The directory the in-vault paths are relative to, or null | ACCESS-009 |

Both vault fields are optional **and their absence refuses nothing**: the answer still arrives, the
page's name is still readable in it, and the browser says that opening a page is not set up
(ACCESS-009). They reach the browser on the chat stream's opening snapshot, beside the cost ceiling,
for the reason the list already sends its ceiling: they are the hub's values and not the page's.

---

## `LiveUpdates` — what the browser is sent (`Grimoire.Hub`)

A plain class: nothing outside the process, and no second implementation (Constitution II.4). One
channel per subscriber, drained by the endpoint that serves its stream.

| Stream | Opens with | Then sends |
| --- | --- | --- |
| the submissions list | every submission, as `GET /api/submissions` answers it | the whole list again, whenever anything about one changed |
| one run's record | the record so far, byte for byte | the bytes appended since this subscriber's last event |
| the chat | every turn, the totals, the cost ceiling and the vault settings | the one thing that changed |

**Every stream opens with a snapshot**, which is what answers ACCESS-007's reconnect clause with no
replay buffer: a browser that comes back reads the chat as it then stands, including what arrived
while it was away. Nothing is kept per subscriber but how far through a record it has been sent.

Who publishes: `RunBoard` raises one `Changed` delegate the composition root supplies — the
precedent `RunConductor.NextRunMayStart` already sets — and `RunConductor` publishes a record's
growth and the chat's, at the place that already knows (research.md R-05).
