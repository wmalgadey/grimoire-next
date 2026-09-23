---

description: "Task list template for feature implementation"
---

# Tasks: The Ingest Queue

**Input**: Design documents from `/specs/002-ingest-queue/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md,
data-model.md, contracts/

**Outcome advanced**: OUT-01 — submit a text in the browser and afterwards find new, linked pages
including a source page in the wiki

**Budget**: about 40 tasks (Constitution I.7). **42 tasks.** The list was 39; T040–T041 came from
`/speckit-converge` and T042 from `origin/main`'s new CI job, neither of which the budget could
foresee. The plan's budget note says where the original growth went and why nothing was found to
cut.

## Format

Implementation task:

`- [ ] T00N [P?] [US?] Description — **Req:** <CAPABILITY>-NNN | Principle <n>`

Test task:

`- [ ] T00N [P?] [US?] Description — **Req:** <CAPABILITY>-NNN | **Level:** Fast\|Contract\|E2E\|Deploy — **Why not lower:** [one line]`

- **[P]**: can run in parallel (different files, no dependencies)
- **[US?]**: the user story this task belongs to (US1, US2, US3)
- **Req** *(mandatory, every task)*: the requirement ID this task serves, or the constitution
  principle it follows (Constitution IV.5). No task without one.
- **Level** *(mandatory, every test task)*: the test's level (Constitution III.3).
- **Why not lower** *(mandatory, every test task)*: one line on why the level below cannot prove
  this requirement (Constitution III.6). "Convenience" is not a reason.
- Include exact file paths in descriptions.

**Levels** — Fast: in-process, state-based, real domain objects, in-memory adapters at owned ports.
Contract: one suite per adapter against the real external thing. E2E: real processes, at most two
scenarios per user story. Deploy: smoke checks on built artifacts, CI only, only
for a deployment outcome, never in the default run.

**Not tested** (Constitution III.8) — do not write tasks for: framework or library behaviour,
argument parsing as such, dependency wiring, static configuration or deployment content, generated
code. Such a test is deleted, not fixed. In this feature that covers: the `ApplicationStopping`
hook, `--state` argument reading, the `Microsoft.Data.Sqlite` package reference, and SQLite's own
durability.

**Evals** (Constitution III.10) — no requirement in this feature has proof kind `eval`, and none has
`review`. All six are `test`.

**Phases are the plan's** (`plan.md`, Phase PRs). Each is a branch off `002-ingest-queue`, merged
back when its PR is reviewed and green, before the next starts (Constitution I.10). There is no
separate Setup phase: this feature adds nothing to the solution layout, and the two foundational
tasks belong to phase 1 because IV.2 puts them before the first test.

---

## Phase 1: User Story 1 - Hand over a second text without waiting for the first (Priority: P1) 🎯 MVP

**Branch**: `002-ingest-queue-phase-1-queue-rule`

**Goal**: a text submitted while a run is in progress is accepted and waits its turn; runs start one
at a time, in the order the submissions were made; every row carries the opening of its own text.

**Independent Test**: submit several texts while a run is under way and observe that each is
accepted, that no two runs are ever in progress together, that the runs start in the order the
submissions were made, and that each row is recognisable by its own opening words. Needs neither a
failure nor a restart.

### Foundational (before the first test of this feature)

- [X] T001 Register this feature's six requirements in `docs/capabilities/runs.md` (RUNS-002,
      RUNS-003, RUNS-004, RUNS-006) and `docs/capabilities/access.md` (ACCESS-003, ACCESS-004), with
      the wording and proof kinds of `spec.md`, and delete the paragraph in `runs.md` that says
      RUNS-002/003/004 are not registered yet. This is the first implementation task: it comes
      before any test is written — **Req:** Principle IV.2
- [X] T002 Retire INGEST-005 in `docs/capabilities/ingest.md` — move it under a "Retired" heading
      keeping its ID — and drop the qualifier "When no run is in progress, " from INGEST-001, and drop INGEST-005 from the
      sentence below the table that names which requirements say a refused submission is not stored,
      leaving INGEST-003 and INGEST-004 — **Req:** Principle IV.2
- [X] T003 Remove everything INGEST-005 carried, in one step so the branch is never red under
      `trace-check`: `Refusal.RunInProgress` and its clause in `src/Grimoire.Runs/SubmissionBoard.cs`,
      the `409 Conflict` / `run-in-progress` arm in `src/Grimoire.Hub/Api/SubmissionsEndpoints.cs`,
      the comment naming 409 in `src/Grimoire.Hub/wwwroot/app.js`, the four tests carrying
      `[Trait("req", "INGEST-005")]` in `tests/Grimoire.Fast.Tests/SubmissionAcceptanceTests.cs`
      together with that file's class comment citing it, and
      the INGEST-005 citations in the comments of `src/Grimoire.Runs/Submission.cs`,
      `src/Grimoire.Hub/SubmissionIntake.cs` and `src/Grimoire.Hub/RunConductor.cs`, which now cite
      RUNS-002 — **Req:** INGEST-001

### Tests for User Story 1

> Write these first and see them fail before implementing.

- [X] T004 [P] [US1] A text submitted while a run is in progress is accepted and waits, and waiting
      submissions start in the order they were made, in
      `tests/Grimoire.Fast.Tests/QueueTests.cs` — **Req:** RUNS-002 | **Level:** Fast — **Why not lower:** there is no level below Fast; the rule is a
      decision of a real domain object.
- [X] T005 [US1] At most one run is in progress: nothing is handed out while a submission is
      under way, and a submission already handed out is never handed out twice, in
      `tests/Grimoire.Fast.Tests/QueueTests.cs` — **Req:** RUNS-002 | **Level:** Fast — **Why not lower:** there is no level below Fast.
- [X] T006 [P] [US1] The opening of a submitted text: runs of whitespace collapsed to single spaces,
      trimmed, cut to **120 characters** with `…` appended where it was cut, and a text at or under
      120 characters returned whole with nothing appended and nothing padded, in
      `tests/Grimoire.Fast.Tests/SubmissionExcerptTests.cs` — **Req:** ACCESS-004 | **Level:**
      Fast — **Why not lower:** there is no level below Fast.

### Implementation for User Story 1

- [X] T007 [US1] `Submission` gains `RunId` (`Guid?`, null while it waits its turn) and the derived
      `Excerpt` per T006's rule; `Text` stays whole and untidied, in
      `src/Grimoire.Runs/Submission.cs` — **Req:** RUNS-002, ACCESS-004
- [X] T008 [US1] `SubmissionBoard`: `TakeNext(Guid runId)` hands out the waiting submission with the
      earliest `SubmittedAt` and marks it with that run id, or returns null; the state transitions
      move from `Submission` onto the board so every change happens under the one existing lock, in
      `src/Grimoire.Runs/SubmissionBoard.cs` — **Req:** RUNS-002
- [X] T009 [US1] `RunQueue`: ask the board for the next submission and dispatch it, with nothing
      started twice, in `src/Grimoire.Hub/RunQueue.cs` — **Req:** RUNS-002
- [X] T010 [US1] `SubmissionIntake` accepts and then asks the queue instead of dispatching itself;
      `RunConductor.Begin` takes the run id the board was given, and tells the queue when a run has
      ended, in `src/Grimoire.Hub/SubmissionIntake.cs`, `src/Grimoire.Hub/RunConductor.cs` and
      `src/Grimoire.Hub/HubApplication.cs` — **Req:** RUNS-002
- [X] T011 [US1] `excerpt` on `SubmissionView`, always present, in
      `src/Grimoire.Hub/Api/SubmissionsEndpoints.cs`; each row renders it beside the time and the
      state, in `src/Grimoire.Hub/wwwroot/app.js` and `src/Grimoire.Hub/wwwroot/index.html` —
      **Req:** ACCESS-004
- [X] T012 [US1] Extend the row assertion to the excerpt and drop the remark explaining why
      submissions are driven one after another — with a queue they no longer have to be — in
      `tests/Grimoire.E2E.Tests/SubmissionStatesTests.cs` — **Req:** ACCESS-004 | **Level:** E2E —
      **Why not lower:** the excerpt's shape is proven Fast in T006; what only a real browser can
      show is that the row renders it, which is the half of ACCESS-004 that says "the browser MUST
      show".

**Checkpoint**: User Story 1 is fully functional and testable on its own. A stop still loses
everything, and a failure does not yet hold the queue.

---

## Phase 2: User Story 2 - Clear a failure before the queue moves on (Priority: P2)

**Branch**: `002-ingest-queue-phase-2-acknowledgement`

**Goal**: a failed run holds the queue until the user acknowledges it in the browser; the
acknowledged run stays failed and the next waiting submission starts.

**Independent Test**: drive a run to failed with submissions waiting behind it, confirm nothing
starts and that each row shows enough of its text to be told apart, acknowledge that submission's
failed run, and confirm the next submission starts and the acknowledged run still reads failed.
Needs no restart.

### Tests for User Story 2

- [X] T013 [P] [US2] After a run ends failed no further run starts, and the waiting submissions stay
      waiting and still read submitted, in
      `tests/Grimoire.Fast.Tests/AcknowledgementTests.cs` — **Req:** RUNS-003 | **Level:** Fast —
      **Why not lower:** there is no level below Fast.
- [X] T014 [US2] Acknowledging starts the next waiting submission and leaves the acknowledged run
      reading failed, in `tests/Grimoire.Fast.Tests/AcknowledgementTests.cs` — **Req:** RUNS-003 |
      **Level:** Fast — **Why not lower:** there is no level below Fast.
- [X] T015 [US2] Acknowledging a submission that is not an unacknowledged failure — already
      acknowledged, not failed, or unknown — starts nothing and changes no state, in
      `tests/Grimoire.Fast.Tests/AcknowledgementTests.cs` — **Req:** RUNS-003 | **Level:** Fast —
      **Why not lower:** there is no level below Fast.
- [X] T016 [P] [US2] `awaitingAcknowledgement` is carried only where a submission reads failed and
      has not been acknowledged, and no run identifier is carried at all, in
      `tests/Grimoire.Fast.Tests/SubmissionStateTests.cs` — **Req:** ACCESS-003, ACCESS-002 |
      **Level:** Fast — **Why not lower:** the response shape is a decision of ours that a real
      browser would only obscure; what the browser does with it is T017.
- [X] T017 [US2] The user acknowledges a failed run in the browser and the next waiting submission
      starts, in `tests/Grimoire.E2E.Tests/AcknowledgementTests.cs` — **Req:** ACCESS-003, RUNS-003 |
      **Level:** E2E — **Why not lower:** ACCESS-003 says "in the browser", so nothing below a real
      browser can prove that the user can reach it. One scenario of the two this story is allowed.

### Implementation for User Story 2

- [X] T018 [US2] `Submission.AcknowledgedAt` (`DateTimeOffset?`, not a state — RUNS-001's four stay
      four) and `SubmissionBoard.Acknowledge(Guid submissionId)`; `TakeNext` hands out nothing while
      any submission reads failed with `AcknowledgedAt` null, in `src/Grimoire.Runs/Submission.cs`
      and `src/Grimoire.Runs/SubmissionBoard.cs` — **Req:** RUNS-003
- [X] T019 [US2] `POST /api/submissions/{id}/acknowledgement`, no request body and no response body,
      answering `204 No Content` in both cases of `contracts/hub-http-api.md` — the failure cleared,
      and nothing to clear — and then asking the queue for the next run, in
      `src/Grimoire.Hub/Api/SubmissionsEndpoints.cs` — **Req:** ACCESS-003
- [X] T020 [US2] `awaitingAcknowledgement` on `SubmissionView`, present only where the submission
      reads failed and is unacknowledged and then always `true`; no run identifier is added, in
      `src/Grimoire.Hub/Api/SubmissionsEndpoints.cs` — **Req:** ACCESS-003
- [X] T021 [US2] One control on a row carrying `awaitingAcknowledgement`, which posts to that row's
      own submission and refreshes; a row without it offers nothing, and no identifier is rendered,
      in `src/Grimoire.Hub/wwwroot/app.js` and `src/Grimoire.Hub/wwwroot/index.html` — **Req:**
      ACCESS-003

**Checkpoint**: User Stories 1 and 2 both work. A stop still loses everything.

---

## Phase 3: User Story 3 - Stop Grimoire and find the queue as it was (Priority: P3)

**Branch**: `002-ingest-queue-phase-3-surviving-a-stop`

**Goal**: submissions, their states, the run each was given and any acknowledgement outlive Grimoire
stopping, however it stopped; a run that was in progress reads failed; and no agent goes on working
on a run Grimoire has ended.

**Independent Test**: put submissions into each state, stop Grimoire, start it again, and confirm
every submission is still listed, that the one that was running reads failed, that its agent is
gone, and that the waiting ones start in the order they were made once the failure is acknowledged.

### Tests for User Story 3

- [X] T022 [P] [US3] A board restored from stored facts: every submission comes back with the state
      it carried, and one with a run identifier and a non-terminal state reads failed, in
      `tests/Grimoire.Fast.Tests/RestartTests.cs` — **Req:** RUNS-004 | **Level:** Fast — **Why not lower:** there is no level below Fast; the rule that turns stored facts into states is the
      board's, and the file behind it is T025.
- [X] T023 [US3] After a restore the waiting submissions start in the order they were made once
      no failure blocks, and an acknowledgement made before the stop still blocks nothing, in
      `tests/Grimoire.Fast.Tests/RestartTests.cs` — **Req:** RUNS-004, RUNS-002, RUNS-003 |
      **Level:** Fast — **Why not lower:** there is no level below Fast.
- [X] T024 [P] [US3] A run under way is stopped when the hub is told to stop, and at start-up the
      agent of a run that was in progress is terminated **before** that run reads failed and
      **before** any further run starts, in `tests/Grimoire.Fast.Tests/AgentLifetimeTests.cs` against
      a harness that records the order it was asked in — **Req:** RUNS-006 | **Level:** Fast —
      **Why not lower:** the ordering is a decision of ours and needs no real process to observe;
      that a real process actually dies is T026.
- [X] T025 [P] [US3] The SQLite adapter against a real file in a temp directory: what was written
      through one connection is read back through a new one — submission, state, run record with its
      granted tools, acknowledgement — and `LoadAsync` returns submissions oldest first, in
      `tests/Grimoire.Contract.Tests/SqliteSubmissionStoreTests.cs` — **Req:** RUNS-004 | **Level:**
      Contract — **Why not lower:** Fast uses an in-memory adapter, which cannot show that a change
      reached a file a second process can read; the real external thing decides the outcome.
- [X] T026 [P] [US3] Terminating a real child process the test starts: the recorded identity is
      terminated and the process is gone; an identity whose process is already gone terminates
      nothing and does not throw; and a **live process whose identifier matches but whose start time
      does not is left alone**, in `tests/Grimoire.Contract.Tests/AgentProcessTests.cs`. No sign-in,
      so no `[Trait("requires", "signin")]` and it runs in CI — **Req:** RUNS-006 | **Level:**
      Contract — **Why not lower:** terminating a process is an act on the operating system, and no
      in-memory adapter can make it true or make the pid-reuse guard real.
- [X] T027 [US3] The hub started, stopped and started again over one store shows every submission
      with the state it carried and the interrupted run reading failed, in
      `tests/Grimoire.E2E.Tests/RestartTests.cs` — **Req:** RUNS-004 | **Level:** E2E — **Why not lower:** Fast proves the rule and Contract the file; only a real hub started twice shows that
      the two are wired to each other. One scenario of the two this story is allowed.

### Implementation for User Story 3

- [X] T028 [US3] `ISubmissionStore` — `Load`, `Add`, `AssignRun`, `RecordAgentProcess`, `SetState`,
      `Acknowledge`, each returning only once the change is on disk; synchronous, because the board
      writes each change under the lock it decides the queue rule with and a lock cannot be held
      across an await; no flush, no close-time write, no delete — in
      `src/Grimoire.Runs/ISubmissionStore.cs`, with an in-memory adapter at the same port in
      `tests/Grimoire.Fast.Tests/InMemorySubmissionStore.cs` — **Req:** RUNS-004 | Principle III.9
- [X] T029 [US3] `SqliteSubmissionStore`: the `submissions` and `runs` tables exactly as
      `contracts/submission-store.md` specifies, raw SQL, times as ISO 8601 UTC text, created on
      first use; `Microsoft.Data.Sqlite` as a `PackageVersion` in `Directory.Packages.props` and a
      bare `PackageReference` in `src/Grimoire.Runs/Grimoire.Runs.csproj`. The only file in the tree
      that names SQLite, in `src/Grimoire.Runs/Adapters/SqliteSubmissionStore.cs` — **Req:**
      RUNS-004 | Principle V.2
- [X] T030 [US3] The board writes through to the store on every change — accepted, handed out, the
      agent reported in, ended, acknowledged — before the change is visible, and `Restore` turns
      what `LoadAsync` returned into states per `contracts/submission-store.md`, in
      `src/Grimoire.Runs/SubmissionBoard.cs` — **Req:** RUNS-004
- [X] T031 [US3] `AgentProcessIdentity(int ProcessId, DateTimeOffset StartedAt)` at the agent port:
      `RunReport` gains one report, made as soon as the child exists, and `IAgentHarness` one
      operation that terminates a recorded identity **only** where a live process carries that
      identifier *and* that start time. `HarnessProcess` supplies both from
      `System.Diagnostics.Process` and reuses its `Kill(entireProcessTree: true)`, in
      `src/Grimoire.Agent/IAgentHarness.cs` and
      `src/Grimoire.Agent/Adapters/HarnessProcess.cs` — **Req:** RUNS-006 | Principle V.2
- [X] T032 [US3] Both ends of the lifecycle: `RunConductor.StopEverythingAsync` stops the run in
      progress the way a ceiling does, called from `IHostApplicationLifetime.ApplicationStopping`;
      and the start-up order — read the store, terminate the agents of runs that were in progress,
      mark those runs failed, then pump the queue — in `src/Grimoire.Hub/RunConductor.cs` and
      `src/Grimoire.Hub/HubApplication.cs` — **Req:** RUNS-006, RUNS-004
- [X] T033 [US3] `--state <path>` with its default of `state/` beside the hub, in
      `src/Grimoire.Hub/Program.cs`; `GRIMOIRE_STATE` beside the other keys in
      `scripts/run-hub.sh` and `.env-example` — **Req:** RUNS-004

**Checkpoint**: all three user stories are independently functional.

---

## Phase 4: Closing the feature

**Branch**: `002-ingest-queue-phase-5-convergence` — shipped together with the Convergence phase
below, in one PR (see `plan.md`, Phase PRs).

**The feature is not closed until T039 is done.** Everything above it can be, and is; T039 is the
owner's, and I.9 makes it the last task of the feature rather than a formality after it.

- [X] T034 Run the Contract and E2E suites; both must pass, Contract within its 90 s budget. The
      default run is Fast only, so this is the one place they are exercised before the PR. The three
      sign-in tests of DEC-021 are run by hand here too — **Req:** Principle III.7
- [X] T035 Run `trace-check`, including `--complete`; both pass — **Req:** Principle IV.3
- [X] T036 Regenerate and commit `docs/trace.md`, and set the outcome status and spec reference for
      OUT-01 in `docs/product.md` — the only two edits an agent makes to that file — **Req:**
      Principle IV.4
- [X] T037 Walk `docs/review-checklist.md`, and run `/speckit-converge` once, classifying every
      finding before acting on it: code defect → task, spec defect → `/speckit-clarify`, else
      dropped — **Req:** Principle Gov.2
- [ ] T042 Classify the survivors from the mutation artifact of the PR to main, into
      `specs/002-ingest-queue/mutation.md`: per survivor, the test that should have killed it and
      does not, or the reason none should. A survivor becomes a test only where it names a
      requirement the suite does not actually verify; nothing here is a threshold and nothing is run
      locally — CI's `mutation` job is the measurement. **Numbered after the list rather than into
      it**: the task arrived with `origin/main`'s CI job and the template change behind it (#39),
      merged into this branch after the list was written, and no id here is renumbered — **Req:**
      Principle III.1
- [X] T038 Reconcile `docs/capabilities/` with what was built — RUNS-002/003/004/006 and
      ACCESS-003/004 added, INGEST-001 changed, INGEST-005 under "Retired" keeping its ID — and
      merge this feature's binding decisions into `docs/decisions.md` as `DEC-023` (SQLite behind a
      submission store) and `DEC-024` (recognising a process by the identifier *and* its start
      time), each with its reason — **Req:** Principle IV.2 | Principle II.6
- [ ] T039 The owner exercises the outcome once with the real external systems in place, per
      `quickstart.md` — the queue, the acknowledgement gate, and **both** stop passes including the
      `kill -9` one, where after the restart `pgrep -f claude` must print nothing. This is the last
      task of the feature; without it the feature is not done — **Req:** Principle I.9

> There is no task for "the owner reads what each review-proven requirement is about": this feature
> has no requirement with proof kind `review`. All six are `test`.

---

## Dependencies & Execution Order

- **Phase 1 (US1)**: T001–T003 come first and in that order — IV.2 puts the registration before the
  first test, and T003 must land with T002 or `trace-check` fails on a test carrying a retired ID.
- **Phase 2 (US2)**: depends on phase 1 — the acknowledgement gate is a clause of `TakeNext`, which
  T008 creates.
- **Phase 3 (US3)**: depends on phases 1 and 2 — the store persists the run identifier (T007), the
  acknowledgement (T018) and the queue's order, so it cannot be written before they exist.
- **Phase 4**: depends on all three.

The three stories are **not** independent of each other here, which is unusual and worth saying
plainly: each builds on the last, because they are three layers of one queue rather than three
features. Each phase still leaves the feature branch green and delivers a user-observable result on
its own (I.10).

### Within each user story

- Tests are written and fail before the implementation.
- Domain before adapters; adapters before the entry points that call them — so `Submission` and
  `SubmissionBoard` before `RunQueue`, and `ISubmissionStore` before `SqliteSubmissionStore` before
  the board writes through it.
- A story is finished before the next priority starts.

### Parallel opportunities

- **Phase 1**: T004 and T006 are two test files and run in parallel; T005 shares `QueueTests.cs`
  with T004 and is therefore not marked [P]. T007–T011 are a chain through the same objects and are
  not marked [P] either.
- **Phase 2**: T013 and T016 are two test files and run in parallel; T014 and T015 share
  `AcknowledgementTests.cs` with T013. T017 waits for the implementation it drives.
- **Phase 3**: T022 and T024 run in parallel; T023 shares `RestartTests.cs` with T022. T025 and T026
  are two Contract classes and run in parallel with each other. T028 is marked [P] against nothing,
  because everything after it depends on the port it declares.
- Across phases: none. The stories stack.

---

## Implementation Strategy

1. **Phase 1 is the MVP.** A queue that accepts while a run is under way and starts runs in order is
   the whole of what the user cannot work around today, and it ships on its own — a stop still loses
   it, which is exactly what `001-first-ingest` already accepted.
2. **Phase 2** adds the only new user interaction in the feature.
3. **Phase 3** is the largest and the last, and it is where the two Contract suites and the third
   external system arrive.
4. **Phase 4**: capability files, `trace-check`, `docs/trace.md`, `docs/product.md`, `docs/decisions.md`,
   the review checklist and converge, then the owner's acceptance run as the last task.

## Notes

- Every task names a requirement ID or a principle; every test task also names its level and why the
  level below cannot prove it.
- Commit after each task or logical group.
- A finding from review becomes a test only if it names a violated requirement ID (Governance 3);
  otherwise it becomes the smallest code change that resolves it, or is dropped.
- **E2E scenario budget** (III.4, at most two per user story): US1 uses none beyond the row
  assertion T012 updates, US2 one (T017), US3 one (T027). Three of a possible six.

---

## Phase 5: Convergence

**Branch**: `002-ingest-queue-phase-5-convergence` — the same branch and PR as the closing phase
above. A phase found by converge cannot be named in `plan.md` before implementation starts, which
is what converge is for; `plan.md`'s Phase PRs table records it.

One gap, found by `/speckit-converge` against the spec, the plan and the code. `research.md` R-03
names the four events that pump the queue — an accepted submission, a run that ended, an
acknowledgement, and the hub starting. The first three are proven; **the fourth is not**, and the
Fast suite cannot prove it as it stands, because `FastHub` restores from the store without doing
what the hub does once it is listening.

- [X] T040 [P] A submission that was waiting when Grimoire stopped starts by itself after the
      restart, with no failure blocking and nobody submitting anything, and several start in the
      order they were made, in `tests/Grimoire.Fast.Tests/RestartTests.cs` — **Req:** RUNS-004,
      RUNS-002 | **Level:** Fast — **Why not lower:** there is no level below Fast; the start-up
      pump is a decision of ours and needs neither a browser nor a file to observe.
- [X] T041 `FastHub` pumps the queue after restoring, as `HubApplication.Build` does once the
      server is listening, so that the suite's restart is the hub's restart and not two of its three
      steps, in `tests/Grimoire.Fast.Tests/FastHub.cs` — **Req:** RUNS-004
