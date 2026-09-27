---

description: "Task list for 004-ask-the-wiki"
---

# Tasks: Ask the Wiki

**Input**: Design documents from `/specs/004-ask-the-wiki/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/)

**Outcome advanced**: OUT-03 — ask a question and get an answer with references to wiki pages

**Acceptance scenario**: the owner opens the chat, types a question and sends it; the answer forms
under it while they read, with what it has spent standing beside it against its ceiling and the
chat's total below; they unfold one step to see which page the agent opened and what came back, click
a page the answer names and land on that page in their own Obsidian vault, ask a follow-up that only
makes sense in the light of the first answer and get one that takes it into account — and then start
a new chat, which leaves nothing of the old one, with the wiki byte for byte what it was before they
asked anything. Every task serves it, through a user story or the foundation the stories stand on; no
count of tasks triggers a split (Constitution I.7).

## Format

Implementation task:

`- [ ] T00N [P?] [US?] Description — **Req:** <CAPABILITY>-NNN | Principle <n>`

Test task:

`- [ ] T00N [P?] [US?] Description — **Req:** <CAPABILITY>-NNN | **Level:** Fast\|Contract\|E2E — **Why not lower:** [one line]`

- **[P]**: can run in parallel (different files, no dependencies)
- **[US?]**: the user story this task belongs to. Setup, Foundational and Closing carry none
- **Req** *(mandatory, every task)*: the requirement ID it serves, or the principle it follows (IV.5)
- **Level** and **Why not lower** *(mandatory, every test task)*: III.3 and III.6

**Not tested** (Constitution III.8) — there is no task for: that `TypedResults.ServerSentEvents`
frames an event or that `EventSource` reconnects (framework and browser behaviour, research.md R-01),
that `ALTER TABLE` commits, that the hub's three new arguments are read, that `LiveUpdates` and the
`Chat` are wired at start-up, or the wording of `instructions/question.md`. What is tested is what we
put on a stream, what a dispatch carries, and what the browser does with it.

**One `review` requirement**: QUERY-004, with the spec's "Why review" row. It gets no test task and
no eval task — a review-checklist item proves it (T072) and the owner reads what it is about before
the feature closes (T092). Every other requirement of this feature is proven by `test`; none is
`eval`, so there is no eval task (Constitution III.1, III.10).

---

## Phase 1: Setup (shared)

**There is none.** No new package, no new project, no analyzer change: Server-Sent Events are in the
installed framework (research.md R-01, verified against `Microsoft.AspNetCore.App.Ref/10.0.11`) and
the third page is two more static files under `wwwroot/` (DEC-019). A setup task with nothing to set
up would be a task with no consumer (Constitution II.1).

---

## Phase 2: Foundational — the browser is sent what happens

**Purpose**: the one change that touches code no story of this feature owns — `app.js` and `run.js`.
It lands first and alone so that the feature branch is green with polling gone and nothing new on the
screen, and every phase after it inherits a browser that is sent what happens. ACCESS-005 and
ACCESS-006 are proven again across the stream, their wording unchanged. **DEC-032 is superseded.**

**Branch**: `004-ask-the-wiki-phase-2-sent-not-polled`

**No requirement is registered here**: ACCESS-005 and ACCESS-006 are already in
`docs/capabilities/access.md` and neither sentence changes (research.md R-11).

### Tests for the foundation

> Written first and seen failing before the implementation below.

- [X] T001 [P] `LiveUpdatesTests` in `tests/Grimoire.Fast.Tests/`: a subscriber is given a snapshot
      as its first event and increments after it; a second subscriber gets a snapshot of its own; what
      is published while nobody is subscribed is not replayed to the next subscriber, because the
      snapshot is what answers a reconnect (research.md R-01) — **Req:** ACCESS-005, ACCESS-006 | **Level:** Fast — **Why not lower:** there is no lower level; the stream is read as an `IAsyncEnumerable` in-process, never over a socket
- [X] T002 [P] `LiveUpdatesTests`: nothing is kept per subscriber but how far through a record it has
      been sent, and two subscribers at different offsets are each sent only what is past their own —
      **Req:** ACCESS-006 | **Level:** Fast — **Why not lower:** the offset is our own bookkeeping and touches no disk
- [X] T003 [P] `SubmissionStreamTests` in `tests/Grimoire.Fast.Tests/`: the stream's opening event
      carries exactly the body `GET /api/submissions` answers, including the cost ceiling; every later
      event carries **the whole list again, never a delta**, because the list is read as one instant
      under the board's one lock — **Req:** ACCESS-005 | **Level:** Fast — **Why not lower:** the endpoint is reachable in-process through `HubApplication.Build`
- [X] T004 [P] `SubmissionStreamTests`: an event is sent when a state, a figure or an acknowledgement
      changed, and the figures in it are the same one reading of `SubmissionStatus` the list already
      answers with — **Req:** ACCESS-005, RUNS-010 | **Level:** Fast — **Why not lower:** the board's changes are driven in-process with the in-memory store
- [X] T005 [P] `RecordStreamTests` in `tests/Grimoire.Fast.Tests/`: the opening `record` event carries
      the record so far byte for byte; each later one carries only the bytes appended since **this**
      subscriber's last event; a `missing` event carries `entriesLost` on connect and again when the
      count rises — **Req:** ACCESS-006, RUNS-007 | **Level:** Fast — **Why not lower:** the in-memory record adapter is what can be made to grow and to lose an entry on demand
- [X] T006 [P] `RecordStreamTests`: `404` where there is no such submission, where it has no run yet,
      and where its record was never written — the same three cases and the same one answer as the
      endpoint beside it — **Req:** ACCESS-006 | **Level:** Fast — **Why not lower:** in-process through `HubApplication.Build`, as the record endpoint's own tests already are

### Implementation for the foundation

- [X] T007 `src/Grimoire.Hub/LiveUpdates.cs`: a plain class — **no port and no interface**, because
      nothing is outside the process and no second implementation exists (Constitution II.4). One
      `System.Threading.Channels.Channel` per subscriber, drained by the endpoint serving its stream,
      and a per-subscriber offset for the record (research.md R-05) — **Req:** ACCESS-005, ACCESS-006
      — **Revised while implementing**: the channel is **bounded at one signal with `DropWrite`**, not
      unbounded as this task and R-05 first wrote it. Raised in review of the phase's PR, and the
      reason is that the signal carries no payload: `next` reads the current state when the subscriber
      wakes, so one pending signal already says everything a hundred of them would. Unbounded, a
      subscriber that was behind cost a byte per change without bound; dropping loses nothing and the
      write still cannot block the board under its lock
- [X] T008 `src/Grimoire.Runs/SubmissionBoard.cs`: one `Changed` delegate the composition root
      supplies, raised where the board already changes something under its lock. A delegate rather
      than an event or an observer, following `RunConductor.NextRunMayStart` — one precedent, not a
      second mechanism beside it (Constitution II.1). **The class is renamed to `RunBoard` in phase
      3**, not here — **Req:** ACCESS-005
- [X] T009 `src/Grimoire.Hub/Api/SubmissionsEndpoints.cs`: `GET /api/submissions/events`, served with
      `TypedResults.ServerSentEvents`, `event: submissions`, `data` one line of JSON — the same body
      the list endpoint answers. The stream is **beside** `GET /api/submissions`, not instead of it
      (contracts/hub-http-api.md) — **Req:** ACCESS-005
- [X] T010 [P] `src/Grimoire.Hub/Api/RunRecordEndpoint.cs`:
      `GET /api/submissions/{id}/record/events`, `event: record` with `{"append": …}` read through
      `IRunRecord.Read`, and `event: missing` with `{"entriesLost": n}`. The bytes are read through the
      port so that `MarkdownRunRecord` stays the only thing that renders a record — two renderers of
      one record can disagree, which is the seam DEC-030 named (research.md R-05) — **Req:** ACCESS-006
- [X] T011 `src/Grimoire.Hub/RunConductor.cs`: publishes a record's growth after each `IRunRecord`
      call, at the place that already knows — **Req:** ACCESS-006
- [X] T012 `src/Grimoire.Hub/HubApplication.cs`: `LiveUpdates` built and the board's `Changed`
      supplied, so every suite gets the streams the browser gets (Constitution III.9). Wiring is not
      tested (III.8); what it wires is — **Req:** Principle V.2
- [X] T013 `src/Grimoire.Hub/wwwroot/app.js`: the list drawn from `GET /api/submissions/events` with
      `EventSource`; **the one-second poll is deleted**. Rows are still updated in place, keyed by the
      submission's id, so a rising figure and the Acknowledge control are untouched (ACCESS-005) — **Req:** ACCESS-005
- [X] T014 [P] `src/Grimoire.Hub/wwwroot/run.js`: the record drawn from
      `GET /api/submissions/{id}/record/events` with `EventSource`; **the poll is deleted**. It appends
      exactly what it appended before — the segmentation rule is the record's own
      (`specs/003-live-run-record/contracts/run-record.md`) and has not changed — **Req:** ACCESS-006
- [X] T015 `tests/Grimoire.E2E.Tests/SubmissionStatesTests.cs` and `RunRecordViewTests.cs`: the two
      suites keep their requirement IDs and their assertions, and stop waiting for a poll interval.
      **ACCESS-005 and ACCESS-006 are re-proven, not reworded** — a figure rising must still move no
      row, and a moment arriving must still appear below what is there with the scroll and an opened
      result where the user left them — **Req:** ACCESS-005, ACCESS-006 | **Level:** E2E — **Why not lower:** they are the existing browser-half tests of both requirements; what changed is how the page is fed, and only a real browser shows that it still holds

**Checkpoint**: nothing polls anywhere. Both existing views are sent what happens, their requirements
are proven again unchanged, nothing new is on the screen, and `trace-check` passes.

---

## Phase 3: Foundational — a question is a run

**Purpose**: what all three stories consume — US1 asks a question, US2 reads what it did, US3 asks a
second — and none of them is reachable until a question can queue, run under its own read-only grant
and report into a chat. Splitting this by layer *into* US1 would leave a phase with no
user-observable result and put a rename of the class that decides what may run inside the PR that
introduces the chat.

**Branch**: `004-ask-the-wiki-phase-3-a-question-is-a-run`

- [X] T016 Create `docs/capabilities/query.md` — QUERY's first capability file — and register
      **QUERY-002** and **QUERY-005** with proof `test` and the notes the spec's Requirements section
      gives them; register **GUARD-005** in `docs/capabilities/guard.md` beside GUARD-002, with the
      spec's note that GUARD-002 is untouched. **This is the first task of the feature: it comes
      before any test is written.** The plan lists `query.md` under phase 4; QUERY-002 and QUERY-005
      move here because this phase's tests carry them and a requirement is registered before the first
      test that names it (Constitution IV.2) — **Req:** Principle IV.2
- [X] T017 Reword **RUNS-005, RUNS-007, RUNS-008 and RUNS-009** in `docs/capabilities/runs.md` to the
      sentences the spec's "Changed in this feature" table gives, each keeping its ID. **One commit
      with T024 and T025**, the two tests for the cases that made them change: the IDs are kept so no
      existing test breaks on the rewording, and those two tests are what makes it real (Constitution
      IV.1, IV.2, IV.3) — **Req:** Principle IV.2

### Tests for the foundation

> Written first and seen failing before the implementation below.

- [X] T018 [P] `RunBoardTests` in `tests/Grimoire.Fast.Tests/` (`QueueTests` renamed where it names
      the board): waiting work starts in the order it was made **across both kinds** — a question
      asked after a submission waits behind it, a submission made after a question waits behind that —
      and the order is the list's position, never a clock (research.md R-03) — **Req:** RUNS-002 | **Level:** Fast — **Why not lower:** the queue rule is the board's own judgment, driven in-process under its lock
- [X] T019 [P] `RunBoardTests`: at most one run in progress across both kinds, and an unacknowledged
      failure — of a question or of a submission — holds a question exactly as it holds a submission —
      **Req:** RUNS-002, RUNS-003 | **Level:** Fast — **Why not lower:** same; there is no lower level
- [X] T020 [P] `RunBoardTests`: `All` answers with **the submissions alone**, so the list of
      submissions is exactly what it was and a question appears in none of the submission endpoints
      (research.md R-12) — **Req:** ACCESS-004 | **Level:** Fast — **Why not lower:** the board's answer is in-process; what the browser draws from it is ACCESS-005's E2E half
- [X] T021 [P] `QuestionGrantTests` in `tests/Grimoire.Fast.Tests/`: `ToolGrant.ForQuestion` is
      **exactly** `list_pages` and `read_page` — nothing written, nothing deleted, nothing moved — and
      it carries the `questions` endpoint segment, so the grant and the door that serves it are one
      value and cannot disagree — **Req:** GUARD-005 | **Level:** Fast — **Why not lower:** it is the hub's own half of the grant, as GUARD-002's Fast test already is
- [X] T022 [P] `QuestionGrantTests`: the tool type served at `/mcp/questions/{runId}` exposes those
      two names **and no others** — `write_page`, `write_index` and `append_log` are not registered
      there at all, so there is no flag that would turn one on (DEC-011, research.md R-06) — **Req:** GUARD-005 | **Level:** Fast — **Why not lower:** what a tool type exposes is readable in-process; that a real run then reports exactly it is GUARD-001's existing proof
- [X] T023 [P] `QuestionPromptTests` in `tests/Grimoire.Fast.Tests/`: the dispatch for a question
      carries, in order, the question instruction, the purpose description, the run's identifier, what
      has been asked and answered in this chat before it, and the question whole as the user typed it;
      on the model Grimoire was started with, pinned. **The steps are not included**, and nothing is
      trimmed — no cap, no window (contracts/question-run.md, research.md R-07) — **Req:** QUERY-002 | **Level:** Fast — **Why not lower:** the dispatch payload is assembled in-process, as `DispatchPayloadTests` already proves for a submission
- [X] T024 [P] `RunOutcomeTests` in `tests/Grimoire.Fast.Tests/`: a question's run that stops on its
      own inside both ceilings having written nothing in the wiki **ends done**, **no nudge is sent**,
      and the wiki store is asked for nothing at all — not `log.md`. One commit with T017 — **Req:** RUNS-005 | **Level:** Fast — **Why not lower:** the in-memory wiki store is what can be asked what it was asked; nothing below it can
- [X] T025 [P] `RunRecordTests` in `tests/Grimoire.Fast.Tests/`: a question's run leaves **no record**
      — no head, no moment, no tail, and `EntriesLost` zero — while a submission's run still leaves
      exactly one. One commit with T017 — **Req:** RUNS-007, RUNS-008, RUNS-009 | **Level:** Fast — **Why not lower:** the in-memory record adapter is what can be asked whether it was written to
- [X] T026 [P] `ChatTests` in `tests/Grimoire.Fast.Tests/`: the chat holds each question, the answer
      appended in the order it arrived as one piece of prose, and the steps under it — **while no
      browser is subscribed**, so a reader who walked away comes back to what the run produced
      (research.md R-13) — **Req:** QUERY-005 | **Level:** Fast — **Why not lower:** the chat is memory and nothing about holding it needs a browser; the two-tabs half is QUERY-005's E2E
- [X] T027 [P] `ChatTests`: nothing of a chat reaches any store — not the submission store, not the
      record port, not the wiki store. Held means held while Grimoire runs and written down nowhere,
      which is what makes "nothing is kept" reachable directly (research.md R-02) — **Req:** QUERY-005 | **Level:** Fast — **Why not lower:** the three in-memory adapters are what can be asked what they were asked
- [X] T028 `SqliteSubmissionStoreTests` in `tests/Grimoire.Contract.Tests/`: a run with **no
      submission behind it** round-trips through a real file — its identifier, start, granted tools,
      model, agent process and figures — and is read back by `LoadRunsWithoutASubmission()`; a file
      written by the **older** schema comes back with its submissions intact — **Req:** RUNS-006, RUNS-010 | **Level:** Contract — **Why not lower:** what is being read is a real file an older Grimoire wrote, and only the real SQLite decides whether a null `submission_id` round-trips (III.4, DEC-031's precedent)

### Implementation for the foundation

- [X] T029 `src/Grimoire.Runs/Queued.cs`: the base of `Submission` and `Question`, holding only what
      `TakeNext` consults — `Id`, `RunId`, `IsWaiting`, `IsUnderWay`, `IsUnacknowledgedFailure`,
      `HandedTo(runId, model)`, `Ended(terminal)`. **No state field of its own beyond the terminal
      one**: the four values a submission shows and the four the chat shows are read from whether
      there is a run and whether it has ended, so the two can never disagree. An abstraction is
      allowed here because two real implementations exist (Constitution II.4, data-model.md) — **Req:** RUNS-002
- [X] T030 `src/Grimoire.Runs/Submission.cs`: derives from `Queued`. Its text, `SubmittedAt`,
      `Excerpt`, the four states, the acknowledgement and the figures are exactly what
      `003-live-run-record` left — **Req:** RUNS-002
- [X] T031 [P] `src/Grimoire.Runs/Question.cs`: derives from `Queued`, with `Text` and `AskedAt`.
      Created **only when a question is accepted**; a refused one becomes nothing. `AskedAt` is shown
      in the chat and is **not** what the queue is ordered by — that clock is not monotonic — **Req:** QUERY-001, QUERY-002
- [X] T032 `src/Grimoire.Runs/SubmissionBoard.cs` → `src/Grimoire.Runs/RunBoard.cs`: one ordered list
      of `Queued` under the one lock it already has. The queue rule is unchanged and now reads that one
      list; `All` still answers with the submissions alone. One list rather than two, because RUNS-002
      orders across both kinds and a list carries that order intrinsically — two lists would need a
      sequence number of our own beside the ordering the list already is (research.md R-03) — **Req:** RUNS-002, RUNS-003
- [X] T033 `src/Grimoire.Runs/ISubmissionStore.cs`: `StoredRun.SubmissionId` → `QueuedId`, null where
      a question caused the run; `AddRun(StoredRun)` for a run with no submission behind it, written
      the moment the board hands the question out — for the same reason `AssignRun` is written first, a
      write that fails must leave the queue as it was; `LoadRunsWithoutASubmission()`; and `Ended(…)`
      keyed by the run, for a run with no submission state to set. `Load()` is unchanged — **Req:** RUNS-006, RUNS-010
- [X] T034 `src/Grimoire.Runs/Adapters/SqliteSubmissionStore.cs`: the `runs` table's `submission_id`
      becomes **nullable in meaning** — a null means a question caused the run — and the new reads.
      `PRAGMA table_info(runs)` then `ALTER TABLE` where something is missing: **no column is renamed
      and no table is rebuilt** (DEC-031, research.md R-04). **The only file in the tree that names
      SQLite** (DEC-023, Constitution V.2) — **Req:** RUNS-006, RUNS-010
- [X] T035 [P] `src/Grimoire.Agent/ToolGrant.cs`: `ForQuestion` with the two read tools, and
      `Endpoint` — `runs` or `questions` — beside the names, so the grant and the door that serves it
      are one value. GUARD-001's existing equality check then guards it for free (research.md R-06) — **Req:** GUARD-005
- [X] T036 `src/Grimoire.Hub/Mcp/WikiToolsServer.cs` and `src/Grimoire.Hub/Mcp/WikiReadToolsServer.cs`:
      the two read tools' bodies live **once**, called by both attributed surfaces; the new type
      carries `list_pages` and `read_page` and nothing else exists there. The cost — two attributed
      methods, about a dozen lines — is carried openly in plan.md §Complexity Tracking rather than
      argued away (DEC-011) — **Req:** GUARD-005
- [X] T037 [P] `instructions/question.md`: what a question's run is told, stating QUERY-004's list and
      nothing beyond it, plus the clause the owner decided — where the wiki holds nothing about the
      question, say so plainly, name what was looked at, and stop. **Grimoire's own instruction:
      creating it is an owner decision and is named in this phase's PR description** (Constitution
      I.11, V.1) — **Req:** QUERY-004
- [X] T038 `src/Grimoire.Hub/InstructionLoader.cs`: the question prompt assembled from the five parts
      of contracts/question-run.md §2, and `StartUpInputs` gains `QuestionInstructionPresent` — read
      per acceptance as the other two already are, the instruction looked at before the purpose
      description so that a start with neither says one thing. **Still the only thing that puts text
      into a prompt** (Constitution V.1) — **Req:** QUERY-002, QUERY-003
- [X] T039 `src/Grimoire.Hub/Chat.cs`: the one chat, in memory — `Turns`, `Total`, `Ask(question)`,
      `AgentSaid(runId, text)`, `StepHappened(runId, step)`, `Start()`. A plain class, not a port:
      nothing outside the process, and a persistent second implementation would contradict QUERY-005
      rather than serve it (Constitution II.4, research.md R-02) — **Req:** QUERY-005
- [X] T040 `src/Grimoire.Hub/RunConductor.cs`: a question's run gets **no record**, and its moments go
      to the chat instead — `AgentSaid` appended to the answer, `ToolCalled` and `ToolReturned` into
      the steps; `GrimoireSaid` never arises, because RUNS-005's nudge is asked only of a run that is
      to change the wiki. Nothing new is parsed: `AgentTranscript` already reports the three moments
      and stays the only reader of the protocol (DEC-028, Constitution V.2) — **Req:** RUNS-007, QUERY-005
- [X] T041 `src/Grimoire.Hub/RunQueue.cs`: dispatches a question at `/mcp/questions/{runId}` under
      `ToolGrant.ForQuestion`, and a submission where it already does — the grant's own `Endpoint`
      decides, so the two cannot be crossed — **Req:** GUARD-005, QUERY-002
- [X] T042 `src/Grimoire.Hub/HubApplication.cs` and `Program.cs`: the `Chat`, and the second MCP route
      serving `WikiReadToolsServer`; `--question-instruction <path>` with a default beside
      `--instruction`'s. At start-up, a question run read as having been in progress has its agent
      terminated where the recorded identity is live and is then marked ended failed — **no tail is
      written**, because it has no record, and nothing is restored into a chat (DEC-024, QUERY-005) — **Req:** RUNS-006, Principle V.2

**Checkpoint**: a question can be accepted in code, queued behind and beside the submissions, run
under a grant that cannot write, and report into a chat — with the wiki untouched, no record on disk
and its run's row kept so a start-up can kill its agent. Nothing of it is visible in the browser yet;
the branch is green and `trace-check` passes.

---

## Phase 4: User Story 1 — Ask the wiki and read the answer as it forms (Priority: P1) 🎯 MVP

**Goal**: the user switches to the chat, types a question and sends it without waiting; the answer
arrives as the agent writes it and grows in place; what the question has spent stands beside it
against its ceiling, and the chat carries the total of what has been spent in it.

**Independent Test**: ask one question against a wiki that has pages, and confirm an answer arrives
in the chat while the agent is still writing it, that it names pages of that wiki, and that what it
cost stands beside it against its ceiling. Needs no follow-up and no failure.

**Branch**: `004-ask-the-wiki-phase-4-ask-and-read`

**This is the first point at which OUT-03 is exercisable by hand**, and the first place the owner
could stop and still have what they asked for.

- [ ] T043 [US1] Register **QUERY-001** and **QUERY-003** in `docs/capabilities/query.md`, and
      **ACCESS-007**, **ACCESS-008** and **ACCESS-010** in `docs/capabilities/access.md`, with the
      notes the spec gives them — including why ACCESS-008 stands apart from ACCESS-005 rather than
      extending it. Before any test of this phase (Constitution IV.2) — **Req:** Principle IV.2

### Tests for User Story 1

- [ ] T044 [P] [US1] `QuestionAcceptanceTests` in `tests/Grimoire.Fast.Tests/`: a question is accepted
      and answered with the turn as the stream carries one, **before its run has produced anything** —
      the user waits for no part of the answer; and a question asked while something else runs is
      accepted, not refused — **Req:** QUERY-001 | **Level:** Fast — **Why not lower:** there is no lower level; acceptance is an in-process decision through `HubApplication.Build`
- [ ] T045 [P] [US1] `QuestionRefusalTests` in `tests/Grimoire.Fast.Tests/`: the three refusals in the
      order contracts/hub-http-api.md gives — `question-instruction-missing`,
      `purpose-description-missing`, `question-empty` — each naming **exactly one** thing; no run
      starts, nothing is stored and the refused question carries no state — **Req:** QUERY-003 | **Level:** Fast — **Why not lower:** the refusal is the intake's own judgment, with the in-memory adapters at every port
- [ ] T046 [P] [US1] `ChatStreamTests` in `tests/Grimoire.Fast.Tests/`: the opening `chat` event
      carries every turn, the total and the cost ceiling; then `asked`, `answer`, `step` and `question`
      events carry **the one thing that changed** and nothing else — one more field would be a
      mechanism with no consumer — **Req:** ACCESS-007 | **Level:** Fast — **Why not lower:** what is put on the stream is an in-process assertion; what the browser does with it is this story's E2E half
- [ ] T047 [P] [US1] `ChatStreamTests`: a turn's `state` is **exactly one** of `waiting`,
      `answering`, `answered`, `no-answer`, read from whether there is a run and whether it has ended —
      four values inside one requirement, never one requirement per value (Constitution IV.7) — **Req:** ACCESS-007 | **Level:** Fast — **Why not lower:** the mapping is read from domain objects the Fast suite already drives
- [ ] T048 [P] [US1] `ChatStreamTests`: a question that has a run carries `costSpent`, the same
      quantity the cost ceiling counts; a question **waiting its turn carries no `costSpent` at all** —
      not a zero — because it has no run and there is nothing true to say about one; and `total` is
      carried with **no ceiling beside it** — **Req:** ACCESS-008, RUNS-010 | **Level:** Fast — **Why not lower:** the figures are the run's own, read through the in-memory store
- [ ] T049 [P] [US1] `ChatStreamTests`: a browser that subscribes again is given a fresh snapshot
      carrying what arrived while it was away, and nothing is replayed from a buffer and no
      `Last-Event-ID` is read — **Req:** ACCESS-007 | **Level:** Fast — **Why not lower:** subscribing twice is in-process; that `EventSource` reconnects by itself is browser behaviour and is not tested (III.8)
- [ ] T050 [US1] `AskingTheWikiTests` in `tests/Grimoire.E2E.Tests/`: the owner reaches the chat,
      sends a question and the answer **grows in place** as the agent writes — text appears while the
      run is under way, and the question above it and everything already drawn stay exactly where they
      were — **Req:** ACCESS-007 | **Level:** E2E — **Why not lower:** "content arriving must not move what the user is already reading" is geometry, and only a real browser has a layout (DEC-020)
- [ ] T051 [US1] `AskingTheWikiTests`: what the question has spent stands beside it against its
      ceiling and rises while the run is under way; the chat's total stands with **no ceiling beside
      it** and no currency anywhere; and a figure rising moves nothing — **Req:** ACCESS-008 | **Level:** E2E — **Why not lower:** same; a figure changing without moving the page is only observable in a browser
- [ ] T052 [P] [US1] `NavigationTests` in `tests/Grimoire.E2E.Tests/`: from each of the three pages
      the other two are reachable — submitting a source, reading a submission's run, and asking the
      wiki — **Req:** ACCESS-010 | **Level:** E2E — **Why not lower:** a link the user follows between three served pages exists only in a browser
- [ ] T053 [US1] `AskingTheWikiTests`: after a question has been answered through the real hub, the
      wiki directory is **byte for byte** what it was — no page, no index, no log entry — and the
      hub's question endpoint answers with the two read tools and no other — **Req:** GUARD-005 | **Level:** E2E — **Why not lower:** "the wiki is unchanged after a real question" is a claim about a real directory a real hub served, which no unit can make (research.md R-11)

### Implementation for User Story 1

- [ ] T054 [US1] `src/Grimoire.Hub/ChatIntake.cs`: a question accepted or refused against the three
      conditions in order, and dispatched **without the user waiting** — the shape `SubmissionIntake`
      already has, because a question queues by the same rule — **Req:** QUERY-001, QUERY-003
- [ ] T055 [US1] `src/Grimoire.Hub/Api/ChatEndpoints.cs`: `POST /api/chat/questions` answering `202`
      with the turn, or `422` with the one `reason`; and `GET /api/chat/events` with the snapshot and
      the four increment events of contracts/hub-http-api.md. **No run identifier reaches the
      browser** — the chat addresses the question — **Req:** QUERY-001, QUERY-003, ACCESS-007
- [ ] T056 [US1] `src/Grimoire.Hub/Chat.cs` and `RunConductor.cs`: each turn carries `CostSpent` and
      `CostCeiling` read from the run's own figure (DEC-030, RUNS-010) and the chat's `Total` is the
      sum over the turns — **nothing is counted a second time**, which is why a failed question's spend
      is in the total — **Req:** ACCESS-008
- [ ] T057 [P] [US1] `src/Grimoire.Hub/wwwroot/chat.html`: the conversation — text-first, the answer as
      prose, a step in monospace where it is a log or a file (`docs/ux.md`). Each figure in its own
      element with tabular figures and a reserved width, so a rising figure moves nothing — **Req:** ACCESS-007, ACCESS-008
- [ ] T058 [US1] `src/Grimoire.Hub/wwwroot/chat.js`: `EventSource` on `GET /api/chat/events`; the
      answer grows by **appending to the text node that is already there** and an element once drawn is
      never replaced, which is what keeps the scroll and everything read where the user put it. No
      poll anywhere — **Req:** ACCESS-007, ACCESS-008
- [ ] T059 [P] [US1] `src/Grimoire.Hub/wwwroot/index.html`, `run.html` and `chat.html`: a line of links
      to the other two jobs on each of the three pages. `docs/ux.md` withholds navigation chrome
      "until a second job exists" and a third exists now; it stays a line of links — no bar, no menu
      (research.md R-14) — **Req:** ACCESS-010
- [ ] T060 [US1] `src/Grimoire.Hub/HubApplication.cs`: the chat endpoints and `chat.html` served, so
      every suite reaches the chat the browser reaches (Constitution III.9) — **Req:** Principle V.2

**Checkpoint**: OUT-03 is exercisable by hand. A question can be asked in the browser and its answer
read as it forms, with what it cost beside it. The steps are not yet readable and no page opens in an
editor.

---

## Phase 5: User Story 2 — See what the answer rests on, and open a page it cites (Priority: P2)

**Goal**: under the answer, folded shut, is what the agent did to get there; the user unfolds one step
to see the call and what came back, in the shape a run's record is read in — and clicking a page the
answer names opens that page in the editor the wiki is open in.

**Independent Test**: ask one question, unfold a step under the answer and confirm it shows the call
and what came back in the shape a run's record uses; then click a page the answer names and confirm
the request handed out addresses that page in the user's wiki.

**Branch**: `004-ask-the-wiki-phase-5-references`

- [ ] T061 [US2] Register **QUERY-004** in `docs/capabilities/query.md` with proof **`review`** and
      the spec's "Why review" reason, and **ACCESS-009** in `docs/capabilities/access.md` with the
      spec's note that it is where OUT-03's "with references to wiki pages" becomes something the user
      can act on. Before any test of this phase (Constitution IV.2) — **Req:** Principle IV.2

### Tests for User Story 2

> QUERY-004 gets no test: it is a requirement about what a text says, which III.8 does not test. T072
> and T092 are its proof.

- [ ] T062 [P] [US2] `ChatStreamTests` in `tests/Grimoire.Fast.Tests/`: the snapshot carries `vault`
      with the name and the wiki's path inside it **only where Grimoire was told both**, and the field
      is **absent** where either is missing. The two values reach the browser on the snapshot beside
      the cost ceiling, for the reason the list already sends its ceiling: they are the hub's and not
      the page's — **Req:** ACCESS-009 | **Level:** Fast — **Why not lower:** what the stream carries is an in-process assertion; what the browser builds from it is this story's E2E half
- [ ] T063 [P] [US2] `ChatStreamTests`: a `step` event carries the tool's name and what went in or
      came back **whole** — never cut and never summarised — one entry per call and one per result —
      **Req:** ACCESS-007 | **Level:** Fast — **Why not lower:** the moments come from recorded lines, so no process and no sign-in is needed
- [ ] T064 [US2] `AnswerReferencesTests` in `tests/Grimoire.E2E.Tests/`: the steps under an answer are
      **shut** until the user opens one, open **a step at a time**, and an opened step shows that call
      and what it returned in the shape a run's record is read in — so there is one format to learn
      rather than two — **Req:** ACCESS-007, ACCESS-006 | **Level:** E2E — **Why not lower:** the folding is the browser's, and only a real browser has a thing that is shut
- [ ] T065 [US2] `AnswerReferencesTests`: with the run under way, a further step appears **below**
      what is already there, the scroll is where the user left it, and a step they had opened is still
      open — **Req:** ACCESS-007 | **Level:** E2E — **Why not lower:** appending without disturbing is only observable in a browser that has scrolled
- [ ] T066 [US2] `AnswerReferencesTests`: clicking a page the answer names hands out
      `obsidian://open?vault=<name>&file=<the wiki's path in the vault>/<the page>`, addressing that
      page in the user's own wiki, and nothing in the wiki changes — **Req:** ACCESS-009 | **Level:** E2E — **Why not lower:** the rewrite is `chat.js`'s and the click is the browser's; nothing below E2E has either
- [ ] T067 [US2] `AnswerReferencesTests`: with **neither** vault input given, the answer arrives as
      normal, the page's name is readable in the prose as **plain text**, and the chat says **once**
      that opening a page is not set up. The question is **not** refused — that is for a missing
      question instruction or purpose description — **Req:** ACCESS-009, QUERY-003 | **Level:** E2E — **Why not lower:** it is what the browser shows instead of a link, which no in-process test reaches

### Implementation for User Story 2

- [ ] T068 [US2] `instructions/question.md`: the reference shape — every page the answer rests on
      named **inside its prose** as a link in the wiki's own link form (WIKI-001, OKF §6.1), the
      target being the page's path **relative to the wiki's root**, which is the one anchor an answer
      has because §6.1's own anchor is the page a link sits on and an answer sits on no page. **An
      instruction changes: this phase's PR names it and requests the owner's review** (Constitution
      I.11, V.1) — **Req:** QUERY-004
- [ ] T069 [US2] `src/Grimoire.Hub/Program.cs`: `HubOptions` gains `VaultName` and `VaultRoot`, read
      from `--vault <name>` and `--vault-root <directory>`. **Both optional, and their absence refuses
      nothing** — the owner defines the directory the in-vault paths hang off, which is why the
      absolute-path form was rejected (research.md R-09) — **Req:** ACCESS-009
- [ ] T070 [US2] `src/Grimoire.Hub/Api/ChatEndpoints.cs`: the snapshot's `vault`, present only where
      both values are there — **Req:** ACCESS-009
- [ ] T071 [US2] `src/Grimoire.Hub/wwwroot/chat.js` and `chat.html`: the steps folded shut under the
      answer, each openable on its own; and the relative Markdown links in the answer rewritten to
      `obsidian://` where `vault` is in the snapshot, shown as plain text with one line saying opening
      is not set up where it is absent. **The link form lives in this one place** and nothing of it
      goes into a wiki page. Grimoire checks no link: OKF requires readers to tolerate a broken one —
      **Req:** ACCESS-007, ACCESS-009
- [ ] T072 [US2] `docs/review-checklist.md` item 3: extend it so it asks of **each** instruction a run
      receives — the ingest instruction and the question instruction — whether it states the shape its
      requirement lists, in full. **This is QUERY-004's proof** (Constitution III.1, IV.6) — **Req:** QUERY-004

**Checkpoint**: both user stories work. An answer can be checked — which pages were read, and the page
itself opened where the user reads the wiki — and it is still one question at a time.

---

## Phase 6: User Story 3 — Ask back, then put the conversation away (Priority: P3)

**Goal**: a follow-up answered in the light of what came before, a new chat that leaves nothing of the
old one, a question that says it is waiting its turn, and a question that got no answer saying why
and offering the one acknowledgement.

**Independent Test**: ask a question, then a follow-up that only makes sense in the light of the
first, and confirm the second answer takes the first into account; then start a new chat and confirm
nothing of the old one is reachable.

**Branch**: `004-ask-the-wiki-phase-6-conversation`

- [ ] T073 [US3] Register **QUERY-006** in `docs/capabilities/query.md` with proof `test`, and
      complete QUERY-005's sentence there with its "exactly one chat, the same one for every browser"
      and "start a new chat" clauses. Before any test of this phase (Constitution IV.2) — **Req:** Principle IV.2

### Tests for User Story 3

- [ ] T074 [P] [US3] `QuestionPromptTests` in `tests/Grimoire.Fast.Tests/`: a follow-up's dispatch
      carries **every** earlier question and the answer text it produced, in order, and **no steps** —
      what the agent did to reach an earlier answer is for the user to check, not context the next run
      needs. A chat is a sequence of runs, not one agent kept alive: each question gets its own run
      with its own grant and its own ceilings (RUNS-002, RUNS-006 untouched) — **Req:** QUERY-002 | **Level:** Fast — **Why not lower:** what a dispatch carries is assembled in-process by `InstructionLoader`
- [ ] T075 [P] [US3] `ChatTests` in `tests/Grimoire.Fast.Tests/`: a new chat is empty, nothing of the
      previous one is reachable and nothing of it is kept anywhere; and a question **still being
      answered is not stopped** — its run goes on being a run, what it produces belongs to the chat
      that is gone and appears in no new one, and its figures stay with the run — **Req:** QUERY-005, RUNS-010 | **Level:** Fast — **Why not lower:** the chat is memory and the run is driven in-process; nothing here needs a browser
- [ ] T076 [P] [US3] `FailedQuestionTests` in `tests/Grimoire.Fast.Tests/`: when a question's run ends
      failed — at either ceiling, with a dead process, or reporting a tool outside its grant — the chat
      says against that question that it got no answer **and why**, and **nothing the run had produced
      is presented as its answer**; the failure blocks the next run until it is acknowledged, and
      afterwards the question can be asked again — **Req:** QUERY-006, RUNS-003, GUARD-004 | **Level:** Fast — **Why not lower:** the reasons come from `RunEndedBecause` and `FakeTimeProvider` drives the ceilings (DEC-018); the Fast suite has 15 s in total, so nothing waits for a real one
- [ ] T077 [P] [US3] `ChatStreamTests` in `tests/Grimoire.Fast.Tests/`: a turn reads `no-answer` with
      its `because`, carries `awaitingAcknowledgement` **only** while the failure is unacknowledged,
      and the acknowledgement answers `204` whether it cleared a failure or nothing — the one status
      the submission's acknowledgement already gives, because acknowledging a failure already cleared
      did exactly what it should — **Req:** QUERY-006, ACCESS-003 | **Level:** Fast — **Why not lower:** in-process through `HubApplication.Build`, as the submission's acknowledgement already is
- [ ] T078 [US3] `ChatLifetimeTests` in `tests/Grimoire.E2E.Tests/`: with the chat open in **two
      browser tabs**, a question asked in one appears in the other, and starting a new chat empties
      both — there is one chat and every browser reads it, and nothing tells one reader from another —
      **Req:** QUERY-005 | **Level:** E2E — **Why not lower:** "a question asked in one tab appears in the other" is geometry only two real browsers have (research.md R-11)
- [ ] T079 [US3] `ChatLifetimeTests`: a question asked while a submission's run is under way reads
      **waiting its turn** in the chat, is answered after that run ends and never beside it; and a
      question whose run failed offers the one control that acknowledges it, after which the queue
      moves — **Req:** ACCESS-007, RUNS-002, QUERY-006 | **Level:** E2E — **Why not lower:** the waiting state and the one control are what the browser shows, and the two kinds queueing together is only visible where both pages are open

### Implementation for User Story 3

- [ ] T080 [US3] `src/Grimoire.Hub/Api/ChatEndpoints.cs`: `POST /api/chat` answering `204` and
      sending every reader the new, empty snapshot; and
      `POST /api/chat/questions/{id}/acknowledgement` answering `204` either way. The acknowledgement
      exists because a failed question blocks the queue as a failed ingest does and there is no row to
      clear it from — ACCESS-003's wording is unchanged (research.md R-12) — **Req:** QUERY-005, QUERY-006, ACCESS-003
- [ ] T081 [US3] `src/Grimoire.Hub/Chat.cs`: `Start()` replaces the turns and the total whole, and a
      run still in progress writes into the chat that is gone and never into the new one — **Req:** QUERY-005
- [ ] T082 [US3] `src/Grimoire.Hub/wwwroot/chat.js` and `chat.html`: the control that starts a new
      chat; a waiting question said to be waiting; a question that got no answer saying why, with the
      one control that acknowledges it beside it and nothing half-written shown as an answer. No modal
      confirmation and no self-dismissing toast (`docs/ux.md`) — **Req:** QUERY-005, QUERY-006, ACCESS-007

**Checkpoint**: all three user stories are independently functional. The chat is a conversation that
can be put away, the queue and a failure are visible in it, and the wiki is still untouched by any of
it.

---

## Phase 7: Closing the feature

**Branch**: `004-ask-the-wiki-phase-7-closing`

- [ ] T083 Run `/speckit-converge` once for this feature and classify every finding before acting:
      code defect → a task here; spec defect → `/speckit-clarify`; else dropped. `tasks.md` has no
      converge task of its own, and Governance 2 requires one run — **Req:** Principle Gov.2
- [ ] T084 Run the Contract and E2E suites; both pass, Contract within its 90 s budget. The default
      run is Fast only, so this is the one place they are exercised before the PR. **No new
      `requires=signin` test**: DEC-021's budget of four is spent and R-06 chose the design that needs
      no fresh evidence from the real CLI — **Req:** Principle III.7
- [ ] T085 Run `trace-check`; it passes, including `--complete` on the PR to main — every `test`
      requirement of this feature has a test, QUERY-004 is `review` and so not among them, and no test
      carries an unknown or retired ID — **Req:** Principle IV.3
- [ ] T086 Reconcile `docs/capabilities/query.md`, `access.md`, `guard.md` and `runs.md` with what
      shipped, as added or changed. QUERY-001…006 and GUARD-005 are new; ACCESS-007…010 are new;
      RUNS-005, RUNS-007, RUNS-008 and RUNS-009 keep their IDs with their new sentences. **Nothing is
      retired in this feature**, so the "Retired" sections are untouched — **Req:** Principle IV.2
- [ ] T087 Regenerate and commit `docs/trace.md` — **Req:** Principle IV.4
- [ ] T088 Merge this plan's eleven binding decisions into `docs/decisions.md`, each with its reason
      and "Made by: plan `004-ask-the-wiki`", and **move DEC-032 under "Superseded"** naming the entry
      that replaces it — polling is gone from the chat *and* from the two views that polled — **Req:** Principle II.6
- [ ] T089 Bring `CLAUDE.md`'s architecture paragraph up to date: `SubmissionBoard` is `RunBoard` over
      one ordered list of `Queued`, the hub serves a second MCP endpoint and a third page, and
      `InstructionLoader` assembles two prompts. The comments carry the reasons, so a stale name in the
      map is a comment that lies — **Req:** Principle II.6
- [ ] T090 Walk `docs/review-checklist.md`, item 3 included as T072 extended it — **Req:** Principle Gov.2
- [ ] T091 Classify the survivors from the mutation artifact of the PR to main into
      `specs/004-ask-the-wiki/mutation.md`: per survivor, the test that should have killed it and does
      not, or the reason none should. A survivor becomes a test only where it names a requirement the
      suite does not actually verify. Nothing here is a threshold and nothing is run locally — CI's
      `mutation` job is the measurement — **Req:** Principle III.1
- [ ] T092 The owner reads what **QUERY-004** is about — the only review-proven requirement of this
      feature — against `instructions/question.md`: that the answer is written for the user to read,
      rests on what the wiki's pages say, names every page it rests on inside its prose as a link in
      the wiki's own form, says nothing is to be written, and where the wiki holds nothing says so
      plainly and names what it looked at — **Req:** Principle I.9
- [ ] T093 Propose **compressing a conversation** to the owner for `docs/product.md` §Outcomes as a
      Later outcome, with the trigger plan.md names: the first time a real chat fails because it
      outgrew a dispatch. `docs/product.md` is owner-written and an agent edits only an outcome's
      status and spec reference, so this is a proposal and not an edit (Constitution I.4, IV.4) — **Req:** Principle I.4
- [ ] T094 Set **OUT-03** to Done and name the next Now, in **one** edit to `docs/product.md`,
      together with the spec reference — one edit, so exactly one outcome is Now at every commit
      (Constitution IV.4, I.1). Only after T095 — **Req:** Principle IV.4
- [ ] T095 The owner exercises OUT-03 once with the real external systems in place, per plan.md
      §Quickstart and [quickstart.md](quickstart.md) Part 1 — a real wiki with pages in it, a
      signed-in `claude`, a pinned model, Obsidian with the vault open, no stand-ins — and walks
      quickstart.md Part 2's cases. **This is the last task of the feature; without it the feature is
      not done** — **Req:** Principle I.9

---

## Dependencies & Execution Order

- **Setup (Phase 1)**: does not exist.
- **Phase 2 (Foundational, the stream)**: no dependency on anything else in this feature, and it
  comes first because it rewrites two working views. Landing it with the chat would put that rewrite
  inside the PR that introduces a third view, and a failure in either would be ambiguous.
- **Phase 3 (Foundational, a question is a run)**: depends on Phase 2 for `LiveUpdates`, which the
  conductor publishes the chat's growth into. T016 is first, before any test of the feature (IV.2).
  Blocks all three stories — none of them exists until a question can queue and run.
- **US1 (Phase 4)**: depends on Phases 2 and 3. This is the MVP and the phase that makes OUT-03
  exercisable by hand.
- **US2 (Phase 5)**: depends on Phase 4, because it folds the steps under the answer that phase draws
  and rewrites the links inside it.
- **US3 (Phase 6)**: depends on Phase 4 for the chat and on Phase 3 for the prompt the follow-up's
  run is given.
- **Closing (Phase 7)**: depends on every phase above. T095 is last, and T094 follows it.

Each phase is a branch off the feature branch, merged back by the agent once its PR is green and its
review is closed: reviewed by someone other than its author, every finding answered on the PR, the
round decision recorded (I.10). No PR is based on another open PR, and only `004-ask-the-wiki` merges
to `main`, when the feature is done (I.9).

**Phase 3 creates `instructions/question.md` and Phase 5 changes it.** Both PRs name it in their
description and request the owner's review (Constitution I.11, V.1).

### Within each phase

- Tests are written and seen failing before the implementation.
- Domain before adapters; adapters before the entry points that call them. So: `Queued` before
  `Question` and `RunBoard`; `ISubmissionStore` before `SqliteSubmissionStore`; `ToolGrant` before
  `WikiReadToolsServer`; `Chat` before `RunConductor`; all of them before `HubApplication` and
  `Program`.
- **T017, T024 and T025 are one commit.** The four reworded sentences and the two tests for the cases
  that made them change cannot be apart: the tests are what makes the rewording real, and IV.3 reads
  the IDs off the built assemblies against `docs/capabilities/`.
- **T008 is not T032.** Phase 2 puts the `Changed` delegate on `SubmissionBoard`; Phase 3 renames the
  class. The rename is mechanical and touches the hub and every suite that names the board, so it
  lands in one phase rather than being spread across two.

### Parallel opportunities

- Phase 2: T001–T006 are all [P] — six Fast test classes in different files. Then T010 beside T009,
  and T014 beside T013.
- Phase 3: T018–T028 are all [P] once T016 has registered the IDs; T031 and T035 are [P] after T029;
  T037 is [P] throughout, being a text file nothing else in the phase reads.
- Phase 4: T044–T049 and T052 are [P]; T057 and T059 are [P] beside T058.
- Phase 5: T062 and T063 are [P].
- Phase 6: T074–T077 are [P].
- The one Contract task, T028, touches a suite nothing else in Phase 3 touches and can go beside any
  of the Fast tasks.

---

## Implementation Strategy

1. **Phase 2** — the browser is sent what happens, on the two views that already exist. Nothing new
   is on the screen, both requirements are proven again unchanged, and the branch stays green.
2. **Phase 3** — a question becomes a run: queued with the submissions, read-only by construction,
   reporting into a chat, with no record and its row on disk. Still nothing on the screen.
3. **Phase 4** — US1, the MVP: the chat, the answer forming, the cost beside it. **OUT-03 is
   exercisable by hand here**, which is the first point at which the owner could stop and still have
   what they asked for.
4. **Phase 5** — US2: the steps folded under the answer and the page opened in the owner's editor.
   This is what turns an answer from a claim into something checked.
5. **Phase 6** — US3: the follow-up, the new chat, the waiting question and the one that got no
   answer.
6. **Phase 7** — converge, both gates, the capability files, `docs/trace.md`, `docs/decisions.md`
   with DEC-032 superseded, `CLAUDE.md`, the checklist, the mutation survivors, the owner's reading of
   QUERY-004 and the Later outcome proposed; then the owner's acceptance run, and only then the
   outcome status.

## Notes

- Every task names a requirement ID or a principle; every test task also names its level and why the
  level below cannot prove it.
- **No test of this feature carries `requires=signin`.** DEC-021's budget of four is spent, and R-06
  chose the read-only design that needs no fresh evidence from the real CLI.
- **Nothing waits for real time.** The chat is memory, the moments come from recorded lines, elapsed
  time comes from `FakeTimeProvider` (DEC-018), and a stream is read as an `IAsyncEnumerable` in the
  Fast suite rather than over a socket.
- Commit after each task or logical group, except T017/T024/T025, which are one commit.
- A review finding becomes a test only if it names a violated requirement ID (Governance 3);
  otherwise it becomes the smallest code change that resolves it, or is dropped. Findings are
  answered on the PR, never silently dropped.
