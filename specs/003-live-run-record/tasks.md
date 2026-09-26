---

description: "Task list for 003-live-run-record"
---

# Tasks: The Live Run Record

**Input**: Design documents from `/specs/003-live-run-record/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/)

**Outcome advanced**: OUT-02 — see for every run what it did, why it ended and what it cost
(OUT-16 closes with it, as the spec records)

**Acceptance scenario**: the owner pastes a text, watches the row's figures rise and the record fill
while the run is under way, comes back to a finished record that says why it ended and what it spent
per model, and opens the same file in their own editor with the hub stopped. Every task serves it,
through a user story or the foundation the stories stand on; no count of tasks triggers a split
(Constitution I.7).

## Format

Implementation task:

`- [ ] T00N [P?] [US?] Description — **Req:** <CAPABILITY>-NNN | Principle <n>`

Test task:

`- [ ] T00N [P?] [US?] Description — **Req:** <CAPABILITY>-NNN | **Level:** Fast\|Contract\|E2E — **Why not lower:** [one line]`

- **[P]**: can run in parallel (different files, no dependencies)
- **[US?]**: the user story this task belongs to. Setup, Foundational and Closing carry none
- **Req** *(mandatory, every task)*: the requirement ID it serves, or the principle it follows (IV.5)
- **Level** and **Why not lower** *(mandatory, every test task)*: III.3 and III.6

**Not tested** (Constitution III.8) — there is no task for: that `<pre>` renders monospace, that
`ALTER TABLE` commits, that `text/markdown` is served, that the hub's arguments are read, that the
adapters are wired, or the wording of the record's headings. What is tested is that each part of a
record is there, in order, and whole.

**No evals**: every requirement of this feature is proven by `test`. None is `eval` or `review`, so
the spec has no "Why review" section and there is no eval task.

## Phase 1: Setup (shared)

**There is none.** No new package, no new project, no analyzer change: the record is a file in a
directory Grimoire already owns and the browser gains two static files (DEC-019). A setup task with
nothing to set up would be a task with no consumer (Constitution II.1).

---

## Phase 2: Foundational — the record itself

**Purpose**: what all three user stories stand on. US1 reads the record back, US2 watches it fill, and
US3 opens it in an editor — none of them exists without it, which is what puts it here rather than in
US1's phase (Constitution II.1: every part has a consumer in this feature).

**Branch**: `003-live-run-record-phase-2-record`

- [ ] T001 Register RUNS-007, RUNS-008, RUNS-009, RUNS-010 in `docs/capabilities/runs.md` and
      ACCESS-005, ACCESS-006 in `docs/capabilities/access.md`, each with proof `test`, and the notes
      the spec's Requirements section gives them. **This is the first task of the feature: it comes
      before any test is written.** ACCESS-002 is *not* retired here — see T025 — **Req:** Principle IV.2
- [ ] T002 Declare the port in `src/Grimoire.Runs/IRunRecord.cs`: `Begin(RunFrameHead)`,
      `Append(RunMoment)`, `End(RunFrameTail)`, `EntriesLost(Guid)`, with the record types of
      data-model.md. Synchronous, no read, no delete, no rewrite, and **nothing throws** — **Req:** RUNS-007
- [ ] T003 [P] Add `RunEndedBecause` to `src/Grimoire.Runs/IRunRecord.cs` with exactly the seven
      values of data-model.md §Why a run ended: `StoppedWithItsLogEntry`,
      `StoppedWithoutItsLogEntry`, `TimeCeiling`, `CostCeiling`, `ToolsWereNotTheGrant`,
      `AgentProcessDied`, `GrimoireStopped`. Seven values inside one requirement, never one
      requirement per value (IV.7) — **Req:** RUNS-008
- [ ] T004 [P] Add `InMemoryRunRecord` to `tests/Grimoire.Fast.Tests/`, an in-memory adapter at the
      port — never a generated mock — **Req:** Principle III.9
- [ ] T005 [P] Add the three lines the spike recorded to
      `tests/Grimoire.Fast.Tests/RecordedTranscript.cs`: the `assistant` message's `tool_use` block,
      the `user` message's `tool_result` block, and the `assistant` message's `text` block, verbatim
      from research.md R-03 — **Req:** RUNS-009

### Tests for the foundation

> Written first and seen failing before the implementation below.

- [ ] T006 [P] `RunRecordTests` in `tests/Grimoire.Fast.Tests/`: one record per run; head, then the
      moments, then the tail, in that order; nothing already written is changed; a second `End`
      appends nothing — **Req:** RUNS-007 | **Level:** Fast — **Why not lower:** there is no lower level; the ordering is our own code's and needs no filesystem
- [ ] T007 [P] `RunRecordTests`: a write that fails is counted and **does not throw**, the run goes
      on, and the count is appended to the record once a write succeeds again — **Req:** RUNS-007 | **Level:** Fast — **Why not lower:** the in-memory adapter can be made to fail on demand; a real disk cannot be made full on demand
- [ ] T008 [P] `RunFrameTests` in `tests/Grimoire.Fast.Tests/`: the head holds the run's and the
      submission's identifiers, the pinned model, the granted tools, both ceilings and when the run
      started; the tail holds when it ended, `done` or `failed`, why, elapsed against the elapsed
      ceiling, tokens against the cost ceiling, and the tokens of every model the run touched — **Req:** RUNS-008 | **Level:** Fast — **Why not lower:** every field comes from domain objects the Fast suite already drives
- [ ] T009 [P] `RunFrameTests`: each of the seven reasons a run ended is the one the tail records,
      driven through the conductor with `FakeTimeProvider` for the two ceilings (DEC-018) — **Req:** RUNS-008 | **Level:** Fast — **Why not lower:** the Fast suite has 15 s in total, so no test may wait for a real ceiling
- [ ] T010 [P] `RunNarrativeTests` in `tests/Grimoire.Fast.Tests/`: from `RecordedTranscript`, the
      record holds the tool call with its arguments, what the call returned **whole**, the agent's own
      text, and Grimoire's nudge — in the order they happened — **Req:** RUNS-009 | **Level:** Fast — **Why not lower:** the lines are recorded, so no process and no sign-in is needed
- [ ] T011 [P] `RunNarrativeTests`: a `tool_result` whose `content` is an array of blocks is read as
      the text of its text blocks; one that is neither a string nor such an array is recorded as a
      result that could not be read, never dropped — **Req:** RUNS-009 | **Level:** Fast — **Why not lower:** the shapes are recorded lines
- [ ] T012 [P] `RecordTextTests` in `tests/Grimoire.Fast.Tests/`: a result containing a run of
      backticks is fenced with a run one longer and closed with one of the same length; a line
      starting with `## ` inside a result is **not** a segment boundary; a result is never cut and
      never escaped — **Req:** RUNS-009 | **Level:** Fast — **Why not lower:** the rendering is a pure function and touches no disk, which is why it is a class of its own
- [ ] T013 [P] `RunFiguresTests` in `tests/Grimoire.Fast.Tests/`: the tokens and the tool-call count
      rise with the run, never go backwards, stand as the final figures once it has ended, and are
      read as **one instant** with the state and the acknowledgement — **Req:** RUNS-010 | **Level:** Fast — **Why not lower:** the figures are the board's state, read through the in-memory store
- [ ] T014 [P] `RunFiguresTests`: a run restored after a stop comes back with the figures it had
      reached, and a run cut off by the stop reads `failed` carrying them — **Req:** RUNS-010, RUNS-004 | **Level:** Fast — **Why not lower:** the in-memory store makes the restore path reachable without a file
- [ ] T015 `MarkdownRunRecordTests` in `tests/Grimoire.Contract.Tests/`: the file is at
      `<state>/runs/<runId>.md`, is text, holds everything appended to it after the process is
      stopped part-way through the narrative, and an unwritable directory is counted rather than
      thrown — **Req:** RUNS-007 | **Level:** Contract — **Why not lower:** the real filesystem decides all four, and an in-memory adapter cannot make any of them true
- [ ] T016 `SqliteSubmissionStoreTests` in `tests/Grimoire.Contract.Tests/`: the four figures
      round-trip through a real file, and a file written by the **older** schema comes back with its
      submissions intact and its figures at zero — **Req:** RUNS-010 | **Level:** Contract — **Why not lower:** what is being read is a real file an older Grimoire wrote; no double can be one

### Implementation for the foundation

- [ ] T017 `src/Grimoire.Runs/RecordText.cs`: the head, one moment and the tail rendered to Markdown,
      and the fence rule — a run of backticks one longer than the longest run in the content, and at
      least three (contracts/run-record.md). Pure, no filesystem, which is what makes the shape the
      browser depends on provable in the Fast suite — **Req:** RUNS-008, RUNS-009
- [ ] T018 `src/Grimoire.Runs/Adapters/MarkdownRunRecord.cs`: creates `<state>/runs/`, appends what
      `RecordText` renders, and catches and counts its own IO failures. **The only place a record file
      is written** (Constitution V.2) — **Req:** RUNS-007
- [ ] T019 `src/Grimoire.Agent/Adapters/AgentTranscript.cs`: read every `tool_use`, `tool_result` and
      `text` block of a complete `assistant` or `user` message as three new `TranscriptSays` values.
      `thinking` blocks are not read (research.md R-05), and the partial stream stays the cost
      ceiling's alone. **Still the only reader of the CLI protocol** — **Req:** RUNS-009
- [ ] T020 `src/Grimoire.Agent/IAgentHarness.cs` and `Adapters/HarnessProcess.cs`: `RunReport` gains
      **one** delegate carrying a moment, and the reader reports the three new events through it. The
      adapter still decides nothing — **Req:** RUNS-009
- [ ] T021 `src/Grimoire.Runs/RunStateMachine.cs`: `Run` carries `Model`, `ToolCalls` and
      `EndedBecause`. `Model` moves here from `RunQueue`, because the grant and both ceilings are
      already recorded at `Begin` and the model belongs in the same breath (data-model.md §Run) — **Req:** RUNS-008, RUNS-010
- [ ] T022 `src/Grimoire.Hub/RunConductor.cs`: `Begin` writes the head; each reported moment is
      appended; the nudge is appended by the hub itself, which knows it nudged, so it cannot appear
      twice (research.md R-03); `RunEnded` writes the tail with the reason and the tokens per model —
      **Req:** RUNS-007, RUNS-008, RUNS-009
- [ ] T023 `src/Grimoire.Hub/RunQueue.cs`: reads `run.Model` for the dispatch instead of holding the
      model itself. One fewer place the model lives, not one more — **Req:** RUNS-008
- [ ] T024 `src/Grimoire.Runs/ISubmissionStore.cs`: `StoredRun` gains `Model`, `TokensUsed`,
      `ToolCalls` and `EntriesLost`, and the port gains
      `RecordFigures(Guid runId, long tokensUsed, int toolCalls, int entriesLost)` — one member for
      the three, because one event writes them. The comment saying the tokens are "deliberately not
      here" goes with them, and `002-ingest-queue`'s assumption is recorded as withdrawn — **Req:** RUNS-010
- [ ] T025 `src/Grimoire.Runs/Adapters/SqliteSubmissionStore.cs`: the four columns on `runs`, added
      where they are missing — `PRAGMA table_info(runs)`, then `ALTER TABLE runs ADD COLUMN`. No
      version table and no scripts (research.md R-07) — **Req:** RUNS-010
- [ ] T026 `src/Grimoire.Runs/Submission.cs` and `SubmissionBoard.cs`: `SubmissionStatus` gains
      `RunFigures?`, null for a submission with no run; the figures are written under the board's one
      lock and **only where one has risen**, and restored from the store — **Req:** RUNS-010
- [ ] T027 `src/Grimoire.Hub/HubApplication.cs` and `Program.cs`: `MarkdownRunRecord` at its port,
      `runs/` under `--state`. Wiring is not tested (III.8); what it wires is — **Req:** Principle V.2

**Checkpoint**: every run leaves a record on disk that a person can read, and the figures survive a
stop. Nothing of it is visible in the browser yet, and no requirement is retired — the feature branch
is green and `trace-check` passes.

---

## Phase 3: User Story 1 — Read back what a run did, why it ended and what it cost (Priority: P1) 🎯 MVP

**Goal**: from the list, the owner opens a run that has ended and reads the whole of it — the frame,
then every tool call with its arguments and what it returned, and the agent's text between them — and
the row beside it carries the model, the tokens and the tool calls.

**Independent Test**: drive one run to `done` and one to `failed`, open each from the list, and
confirm the record holds the frame and the narrative and that the failed one says what stopped it.
Nothing live, no editor.

**Branch**: `003-live-run-record-phase-3-read-back`

### Tests for User Story 1

- [ ] T028 [P] [US1] `SubmissionStateTests` in `tests/Grimoire.Fast.Tests/`: the response carries the
      state, the model and both figures for a submission that has a run, and **no run fields at all**
      — not zeros — for one that has none — **Req:** ACCESS-005 | **Level:** Fast — **Why not lower:** there is no lower level; the response is built in-process
- [ ] T029 [P] [US1] `SubmissionStateTests`: `entriesLost` is absent where nothing was lost and a
      number above zero where the record could not hold something — **Req:** ACCESS-005, RUNS-007 | **Level:** Fast — **Why not lower:** the in-memory record adapter is what can be made to lose an entry
- [ ] T030 [P] [US1] `RunRecordEndpointTests` in `tests/Grimoire.Fast.Tests/`: the endpoint answers
      with the record's bytes unaltered and `404` where there is no such submission, no run yet, or no
      record — **Req:** ACCESS-006 | **Level:** Fast — **Why not lower:** the endpoint is reachable in-process through `HubApplication.Build`
- [ ] T031 [US1] `SubmissionStatesTests` in `tests/Grimoire.E2E.Tests/`: the row of a run that has
      ended carries the model, the tokens and the tool calls beside its state — **Req:** ACCESS-005 | **Level:** E2E — **Why not lower:** what the *browser renders* is the half of ACCESS-005 no in-process test reaches (DEC-019's precedent for ACCESS-001/002)
- [ ] T032 [US1] `RunRecordViewTests` in `tests/Grimoire.E2E.Tests/`: the owner opens a finished run
      from its row, reads its frame and its moments in order, and opens one folded result to find it
      whole — **Req:** ACCESS-006 | **Level:** E2E — **Why not lower:** the folding and the segmentation are the browser's, and only a real browser exercises them
- [ ] T033 [US1] Retire ACCESS-002 **with the five tests that carry it**, in this one commit:
      `Report_CarriesNothingBeyondTheState` and `List_ShowsNothingBeyondTheState` are **deleted**,
      because they assert the negative this feature undoes; `Report_NamesEachStateAsOneOfTheFour` and
      `List_ShowsEachSubmissionInItsState` are retargeted to ACCESS-005;
      `Report_CarriesNoRunIdentifier_WhileAFailureIsUnacknowledged` keeps **no** requirement ID,
      because no run identifier reaching the browser is now a design property rather than a
      requirement. Retirement and tests travel together or `trace-check` fails on a test carrying a
      retired ID (IV.3) — **Req:** ACCESS-005 | **Level:** Fast — **Why not lower:** it is an edit to existing Fast and E2E tests, not a new level

### Implementation for User Story 1

- [ ] T034 [US1] `docs/capabilities/access.md`: ACCESS-002 moves under "Retired" keeping its ID, with
      the reason the spec gives; the note saying ACCESS-003 and ACCESS-004 do not reach past it goes
      with it (Constitution IV.1, IV.2) — **Req:** Principle IV.2
- [ ] T035 [US1] `src/Grimoire.Hub/Api/SubmissionsEndpoints.cs`: `SubmissionView` gains `model`,
      `tokensUsed`, `toolCalls` and, only where it is above zero, `entriesLost` — all four from the one
      reading of `SubmissionStatus`, all four absent where there is no run. The scope note that said
      no further detail about the run is exposed is withdrawn, with a line saying which requirement
      withdrew it — **Req:** ACCESS-005
- [ ] T036 [P] [US1] `src/Grimoire.Hub/Api/RunRecordEndpoint.cs`: `GET /api/submissions/{id}/record`,
      answering `text/markdown; charset=utf-8` with the file's bytes, or `404`. It serves the file and
      renders nothing: no second, machine-shaped view of a run exists (contracts/hub-http-api.md) —
      **Req:** ACCESS-006
- [ ] T037 [US1] `src/Grimoire.Hub/wwwroot/index.html` and `app.js`: each row gains the model, the two
      figures and a link to that run's page. No identifier is rendered — **Req:** ACCESS-005
- [ ] T038 [US1] `src/Grimoire.Hub/wwwroot/run.html` and `run.js`: the record shown in monospace
      (`docs/ux.md`), segmented by the rule of contracts/run-record.md — a tool call as one line, its
      result folded under it, the agent's text as prose — and a line saying lines are missing where
      `entriesLost` is above zero — **Req:** ACCESS-006

**Checkpoint**: OUT-02 is exercisable by hand. A run that has ended can be read back in full from the
browser. The figures do not yet have to rise while anybody watches.

---

## Phase 4: User Story 2 — Watch a run while it is under way (Priority: P2)

**Goal**: the row's figures follow a run as it spends and calls, without the list moving; the opened
record takes up lines as they are appended, without disturbing what is being read.

**Independent Test**: start a run and, while it is in progress, watch the figures rise and the lines
arrive, with the rows staying where they are and an opened result staying open. Needs no failure.

**Branch**: `003-live-run-record-phase-4-live`

### Tests for User Story 2

- [ ] T039 [US2] `SubmissionStatesTests` in `tests/Grimoire.E2E.Tests/`: while a run is in progress
      its figures rise, and the row's box and the position of every other row are unchanged as they do
      — **Req:** ACCESS-005 | **Level:** E2E — **Why not lower:** "a figure changing must not move the rows" is geometry, and only a real browser has a layout
- [ ] T040 [US2] `RunRecordViewTests` in `tests/Grimoire.E2E.Tests/`: with the run under way and the
      page left open, a further moment appears **below** what is already there, the scroll position is
      where the user left it, and a result they had opened is still open — **Req:** ACCESS-006 | **Level:** E2E — **Why not lower:** same reason; appending without disturbing is only observable in a browser that has scrolled

### Implementation for User Story 2

- [ ] T041 [US2] `src/Grimoire.Hub/wwwroot/app.js`: rows **updated in place**, keyed by the
      submission's id, instead of `replaceChildren` rebuilding the list every second. This also stops
      the Acknowledge button being replaced under the user's finger (research.md R-09) — **Req:** ACCESS-005
- [ ] T042 [P] [US2] `src/Grimoire.Hub/wwwroot/index.html`: each figure in its own element with
      tabular figures and a reserved width, so that `1 000` becoming `10 000` moves nothing
      (`docs/ux.md`: live content grows in place) — **Req:** ACCESS-005
- [ ] T043 [US2] `src/Grimoire.Hub/wwwroot/run.js`: polls once a second and **appends** only the
      segments that are not already on the page, never replacing one that is — which is what keeps the
      scroll and the open results where the user put them — **Req:** ACCESS-006

**Checkpoint**: OUT-16 is exercisable by hand. Both user stories work, and a run in progress and a run
from last month are opened and read the same way.

---

## Phase 5: User Story 3 — Read a run in my own editor, without Grimoire (Priority: P3)

**Goal**: the record stands on its own as a file, holds the same run the browser showed, and nothing
of it is in the wiki.

**Independent Test**: run a run to its end, stop Grimoire, read the file as text, and confirm the wiki
holds nothing of it.

**Branch**: `003-live-run-record-phase-5-without-grimoire`

This phase is mostly proof rather than work: the spec says US3 "is a property of Stories 1 and 2
rather than work of its own", and what it adds is the two assertions nothing else makes.

### Tests for User Story 3

- [ ] T044 [P] [US3] `RunRecordTests` in `tests/Grimoire.Fast.Tests/`: writing a whole record asks the
      wiki store for **nothing** — no read, no write, no append. RUNS-005's `log.md` read is the only
      thing that touches the wiki while a run is watched, and it is unchanged — **Req:** RUNS-007 | **Level:** Fast — **Why not lower:** the in-memory wiki store is what can be asked what it was asked
- [ ] T045 [US3] `RunRecordViewTests` in `tests/Grimoire.E2E.Tests/`: the bytes the record endpoint
      serves are the bytes of the file under `<state>/runs/`, and the wiki directory holds no file of
      the record. This is what makes the browser a window rather than a second place the run lives —
      **Req:** RUNS-007, ACCESS-006 | **Level:** E2E — **Why not lower:** it compares what a real hub serves with what is really on disk; neither half exists below E2E

**Checkpoint**: all three user stories are independently functional.

---

## Phase 6: Closing the feature

**Branch**: `003-live-run-record-phase-6-closing`

- [ ] T046 Run `/speckit-converge` once for this feature and classify every finding before acting:
      code defect → a task here; spec defect → `/speckit-clarify`; else dropped. `tasks.md` has no
      converge task of its own, and Governance 2 requires one run — **Req:** Principle Gov.2
- [ ] T047 Run the Contract and E2E suites; both pass, Contract within its 90 s budget. The default
      run is Fast only, so this is the one place they are exercised before the PR — **Req:** Principle III.7
- [ ] T048 Run `trace-check`; it passes, including `--complete` on the PR to main — every `test`
      requirement of this feature has a test, and no test carries a retired ID — **Req:** Principle IV.3
- [ ] T049 Reconcile `docs/capabilities/runs.md` and `access.md` with what shipped, as added, changed
      or removed; ACCESS-002 stays under "Retired" with its ID — **Req:** Principle IV.2
- [ ] T050 Regenerate and commit `docs/trace.md` — **Req:** Principle IV.4
- [ ] T051 Merge this plan's eight binding decisions into `docs/decisions.md` as DEC-026…DEC-033, each
      with its reason and "Made by: plan `003-live-run-record`". The one that departs from DEC-023's
      "two tables that do not change shape" says so — **Req:** Principle II.6
- [ ] T052 Walk `docs/review-checklist.md` — **Req:** Principle Gov.2
- [ ] T053 Classify the survivors from the mutation artifact of the PR to main into
      `specs/003-live-run-record/mutation.md`: per survivor, the test that should have killed it and
      does not, or the reason none should. A survivor becomes a test only where it names a requirement
      the suite does not actually verify. Nothing here is a threshold and nothing is run locally —
      **Req:** Principle III.1
- [ ] T054 Set OUT-02 **and OUT-16** to Done and name the next Now, in **one** edit to
      `docs/product.md`, together with the spec reference — and move OUT-16 into this feature's row.
      One edit, so exactly one outcome is Now at every commit (Constitution IV.4, I.1). Only after
      T055 — **Req:** Principle IV.4
- [ ] T055 The owner exercises OUT-02 and OUT-16 once with the real external systems in place, per
      `plan.md` §Quickstart and [quickstart.md](quickstart.md) Part 1: a real wiki, a signed-in
      `claude`, a pinned model, no stand-ins. **This is the last task of the feature; without it the
      feature is not done** — **Req:** Principle I.9

There is **no** task for the owner reading what a review-proven requirement is about: this feature has
none. Every requirement is proven by `test` (Constitution III.1).

---

## Dependencies & Execution Order

- **Setup (Phase 1)**: does not exist.
- **Foundational (Phase 2)**: T001 first, before any test of the feature (IV.2). Blocks all three
  stories — none of them exists without a record.
- **US1 (Phase 3)**: depends on Phase 2. This is the MVP and the phase that makes OUT-02 exercisable.
- **US2 (Phase 4)**: depends on Phase 3, because it changes the two pages Phase 3 writes. It is
  OUT-16.
- **US3 (Phase 5)**: depends on Phase 2 for what it asserts and on Phase 3 for the endpoint it
  compares against.
- **Closing (Phase 6)**: depends on every phase above. T055 is last, and T054 follows it.

Each phase is a branch off the feature branch, merged back by the agent once its PR is green and its
review is closed, before the next phase starts (I.10, I.11). No PR is based on another open PR, and
only `003-live-run-record` merges to `main`, when the feature is done (I.9).

### Within each phase

- Tests are written and seen failing before the implementation.
- Domain before adapters; adapters before the entry points that call them. So: `RecordText` before
  `MarkdownRunRecord`, `AgentTranscript` before `HarnessProcess`, both before `RunConductor`, and all
  of them before `HubApplication`.
- **T033 and T034 are one commit.** ACCESS-002's retirement and the five tests carrying it cannot be
  apart: `trace-check` fails on a test carrying a retired ID (IV.3).

### Parallel opportunities

- Phase 2: T003–T005 in parallel after T002; then the eleven Fast test tasks T006–T014 are all [P] —
  they touch different files and share only the in-memory adapters T004 and T005 provide.
- Phase 3: T028–T030 in parallel; T036 in parallel with T035 and T037.
- Phase 4: T042 in parallel with T041.
- Phase 5: T044 in parallel with T045.
- The two Contract tasks T015 and T016 touch different suites' files and can go beside each other.

---

## Implementation Strategy

1. **Phase 2** — the record, end to end, proven in the Fast and Contract suites. Nothing is visible
   to the user yet, and the branch stays green.
2. **Phase 3** — US1, the MVP: the row's figures and the record read back. OUT-02 is exercisable by
   hand here, which is the first point at which the owner could stop and still have what they asked
   for.
3. **Phase 4** — US2: the same two pages made live. OUT-16.
4. **Phase 5** — US3: the two assertions that keep the browser a window and the wiki clean.
5. **Phase 6** — converge, the gates, the capability files, `docs/trace.md`, `docs/decisions.md`, the
   mutation survivors; then the owner's acceptance run, and only then the outcome statuses.

## Notes

- Every task names a requirement ID or a principle; every test task also names its level and why the
  level below cannot prove it.
- No test of this feature carries `requires=signin`: the moments come from recorded lines, so DEC-021's
  budget of three signed-in Contract tests is untouched (research.md R-11).
- Commit after each task or logical group, except T033/T034, which are one commit.
- A review finding becomes a test only if it names a violated requirement ID (Governance 3); otherwise
  it becomes the smallest code change that resolves it, or is dropped. Findings are answered on the
  PR, never silently dropped.
