---

description: "Task list for the first ingest"
---

# Tasks: The First Ingest

**Input**: Design documents from `/specs/001-first-ingest/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md,
data-model.md, contracts/

**Outcome advanced**: OUT-01 — submit a text in the browser and afterwards find new, linked pages
including a source page in the wiki

**Budget**: about 40 tasks (Constitution I.7). **42 here** — 36 of feature work, which is the
plan's estimate, plus the six closing tasks the constitution requires of every feature. Splitting
again would not remove those six.

## Format

Implementation task:

`- [ ] T00N [P?] [US?] Description — **Req:** <CAPABILITY>-NNN | Principle <n>`

Test task:

`- [ ] T00N [P?] [US?] Description — **Req:** <CAPABILITY>-NNN | **Level:** Fast|Contract|E2E|Deploy — **Why not lower:** [one line]`

- **[P]**: can run in parallel (different files, no dependencies)
- **[US?]**: the user story this task belongs to (US1, US2, US3)

**Not tested here** (Constitution III.8): the instruction's wording (WIKI-001 is proven by
review-checklist item 3), the static page's content, the CLI's own behaviour, and the spawn wiring
as such. **No evals**: the spec commits to none.

**Held back by the split**: RUNS-002, RUNS-003, RUNS-004 and ACCESS-003. No task below serves them.

---

## Phase 1: Setup (shared)

- [X] T001 Create the solution and the project layout of plan.md — `src/Grimoire.{Wiki,Runs,Agent,Hub}`, `tools/Grimoire.Trace`, `tests/Grimoire.{Fast,Contract,E2E}.Tests`, `instructions/` — on .NET 10, with xunit v3 in every test project — **Req:** Principle II.6
- [X] T002 [P] Turn on the analyzer the SDK already ships, in `Directory.Build.props` (`TreatWarningsAsErrors`, `.editorconfig`); write no analysis tooling of our own — **Req:** Principle II.3
- [ ] T003 [P] Add the CI workflow in `.github/workflows/ci.yml`: build; `dotnet test tests/Grimoire.Fast.Tests -- --timeout 15s`; `dotnet test tests/Grimoire.Contract.Tests -- --timeout 90s --filter-not-trait "requires=signin"`; then `trace-check` in two calls — `check` on every push, for the three conditions that always hold, and `check --complete` only on a pull request whose base is `main`, which adds the condition "a `test` requirement with no test" (IV.3). The sign-in marker is named once, here — **Req:** Principle III.7

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: only what every user story below needs. Nothing here without a consumer in this
feature (Constitution II.1).

- [x] T004 Register this feature's sixteen requirements in `docs/capabilities/{ingest,wiki,guard,access,runs}.md` with their proof kinds. **This is the first implementation task: it comes before any test is written.** The four requirements the split moved are not registered here — they belong to the follow-up feature — **Req:** Principle IV.2
- [ ] T005 Build `tools/Grimoire.Trace` with two verbs: `check` reads requirement IDs and proof kinds from `docs/capabilities/` and the `level`/`req` traits off the built test assemblies via `System.Reflection.MetadataLoadContext`, writes nothing, and fails on the three conditions of IV.3 that hold on every push — a test carrying an unknown, retired or reserved ID; a test with no level; an E2E or Deploy test with no requirement ID — while `check --complete` adds the fourth, a `test` requirement with no test, which IV.3 applies where a feature lands on main. What the check cannot read it fails on rather than skips. `write` produces `docs/trace.md` — **Req:** Principle IV.3
- [ ] T006 Show both gates failing once on a real violation and link the runs in the PR: `trace-check` against a test whose `req` trait names an id that does not exist, and `time-budget` against a Fast suite pushed past 15 s. A gate counts only after this — **Req:** Principle II.2
- [ ] T007 [P] Add the Fast suite's shared fixture in `tests/Grimoire.Fast.Tests/`: `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`, and the `[Trait("level", …)]` / `[Trait("req", …)]` conventions `trace-check` reads. No Fast test waits for real time. The gate itself is covered here too: one Fast test per condition of `TraceCheck.Run` — the three `check` always applies and the fourth `--complete` adds — and `CapabilityRegistry` is made fail-closed, so a line that looks like a requirement row and does not match in full fails the read instead of being passed over (IV.3, "what the check cannot read, it fails on rather than skips"). These are the Fast suite's first tests: **remove `--ignore-exit-code 8` from the Fast line of `.github/workflows/ci.yml` with them** — **Req:** Principle III.3, Principle IV.3

**Checkpoint**: foundation ready — user story work can begin.

---

## Phase 3: User Story 1 - Submit a text without waiting (Priority: P1) 🎯 MVP

**Goal**: a text goes in through the browser and a run is under way, without the user waiting; a
submission that cannot be accepted is refused with a reason.

**Independent Test**: paste a text, submit, and observe that the submission is accepted immediately
and a run is under way while the browser is already free. The agent is the in-memory adapter at
`IAgentHarness`; none of Story 2's wiki work has to exist yet.

### Tests for User Story 1

> Write these first and see them fail before implementing.

- [ ] T008 [P] [US1] In `tests/Grimoire.Fast.Tests/SubmissionAcceptanceTests.cs`: a text submitted with no run in progress is accepted and the call returns without waiting for the run to end; a text submitted while a run is in progress is refused with `run-in-progress`, stores nothing and starts no run — **Req:** INGEST-001, INGEST-005 | **Level:** Fast — **Why not lower:** there is no lower level; the decision is made by our own objects in process
- [ ] T009 [P] [US1] In `tests/Grimoire.Fast.Tests/SubmissionRefusalTests.cs`: a missing instruction refuses with `instruction-missing` and a missing purpose description with `purpose-description-missing`, so the refusal says which of the two it is; a text that is empty or only whitespace after trimming refuses with `text-empty`; none stores a submission nor starts a run, and both start-up inputs are checked before the text, the instruction first — **Req:** INGEST-003, INGEST-004 | **Level:** Fast — **Why not lower:** there is no lower level
- [ ] T010 [P] [US1] In `tests/Grimoire.Fast.Tests/SubmissionStateTests.cs`: a submission carries exactly one state at a time, drawn from `Submitted` · `Running` · `Done` · `Failed`, across every transition of data-model.md; it reads `Submitted` from acceptance until the agent reports in and `Running` from then on; `Done` and `Failed` are terminal. In the same file: the submissions response carries exactly the state and nothing else about the run — no step, reasoning, duration, cost or history — which is the hub-side half of ACCESS-002 — **Req:** RUNS-001, ACCESS-002 | **Level:** Fast — **Why not lower:** there is no lower level; both the state machine and the response shape are decided by our own objects in process
- [ ] T011 [US1] In `tests/Grimoire.E2E.Tests/SubmitTextTests.cs`: a real browser opens the page, pastes a text, submits, and the page reports the submission accepted — **Req:** ACCESS-001 | **Level:** E2E — **Why not lower:** Contract cannot prove it — the requirement is about a person entering and submitting a text in a browser against the running hub, which no in-process test reaches

### Implementation for User Story 1

- [ ] T012 [P] [US1] `src/Grimoire.Runs/Submission.cs`: `Id` (GUID, assigned on acceptance), `Text` (non-empty after trimming — INGEST-004), `SubmittedAt` (UTC, from `TimeProvider`), `State`; and `SubmissionState` with exactly the four values, no more — **Req:** RUNS-001
- [ ] T013 [US1] `src/Grimoire.Runs/SubmissionBoard.cs`: the in-memory holder of submissions and their states. A plain object — no interface, no port, no store; there is no second implementation and nothing outside the process behind it — **Req:** Principle II.4
- [ ] T014 [P] [US1] `src/Grimoire.Agent/IAgentHarness.cs`: the port — dispatch, nudge, stop — plus an in-memory adapter in `tests/Grimoire.Fast.Tests/` for the Fast suite. Doubles are in-memory adapters at owned ports only — **Req:** Principle III.9
- [ ] T015 [US1] `src/Grimoire.Hub/Api/SubmissionsEndpoints.cs`: `POST /api/submissions` exactly as `contracts/hub-http-api.md` specifies — `202` with `{id,state,submittedAt}`; `422 instruction-missing`; `422 purpose-description-missing`; `422 text-empty`; `409 run-in-progress`. The first two are the **start-up input absent → refuse** check, made against the paths the hub was started with and before the text is looked at, and the reason names which of the two is missing. A refused submission is stored nowhere and carries no state — **Req:** INGEST-001, INGEST-003, INGEST-004, INGEST-005
- [ ] T016 [US1] Dispatch on acceptance in `src/Grimoire.Hub`: the accepted submission moves `Submitted → Running` through `IAgentHarness` and the response returns without waiting for the run — **Req:** INGEST-001
- [ ] T017 [US1] `src/Grimoire.Hub/wwwroot/index.html` and `wwwroot/app.js`: the form, posting with `fetch` and showing the refusal message on `422`/`409`. Served as static content by the hub — no build step — **Req:** ACCESS-001

**Checkpoint**: User Story 1 is fully functional and testable on its own.

---

## Phase 4: User Story 2 - Find the result in the wiki (Priority: P2)

**Goal**: a real run reaches the wiki through a narrow grant; Grimoire hands it the instruction, the
purpose description, the text and the run identifier, stamps who generated each page and when,
reads only the log entry to decide how the run ended, and leaves a failed run's writes alone.

**Independent Test**: drive a run from a known text and inspect what Grimoire does — the dispatch
payload, the generation record, the refusal of an unreadable one, the untouched writes of a failed
run. Needs no browser.

### Tests for User Story 2

- [ ] T018 [P] [US2] In `tests/Grimoire.Fast.Tests/DispatchPayloadTests.cs`: a dispatched run is given the instruction, the purpose description, the submitted text and the run's identifier, and nothing else reaches the prompt; it is dispatched on the pinned model the hub was started with — **Req:** INGEST-002 | **Level:** Fast — **Why not lower:** there is no lower level; the payload is assembled by our own objects
- [ ] T019 [P] [US2] In `tests/Grimoire.Fast.Tests/ProvenanceStampTests.cs`, the four behaviours of data-model.md: frontmatter with no `generated` key gets one added and the write succeeds; a `generated` present in any form is replaced with Grimoire's values; an update names the updating run; frontmatter that does not parse, or a `generated` that is not a mapping, fails the write with a message the agent can act on. Nothing else about the page is judged — **Req:** WIKI-002 | **Level:** Fast — **Why not lower:** there is no lower level; the stamp is a decision our own object makes on text
- [ ] T020 [P] [US2] In `tests/Grimoire.Fast.Tests/FailedRunTests.cs`: a run that ends failed leaves every page, index and log entry it had already written in place; Grimoire removes, reverts and commits none of it — **Req:** WIKI-003 | **Level:** Fast — **Why not lower:** there is no lower level; what Grimoire does *not* do is observable at the port
- [ ] T021 [P] [US2] In `tests/Grimoire.Fast.Tests/ToolGrantTests.cs`: the per-run endpoint serves exactly the five bare names `list_pages`, `read_page`, `write_page`, `write_index`, `append_log` — reading anything in the wiki, creating and changing pages, indexes and the log, and nothing else; no delete and no move; a name outside the grant has no handler; and the grant is recorded with the run. Then, through the in-memory adapter at `IAgentHarness`: an agent that **reports a tool outside the grant** makes the run end failed **before its first model call**, and no dispatch follows — **Req:** GUARD-001, GUARD-002, GUARD-003 | **Level:** Fast — **Why not lower:** there is no lower level for the hub's half of the grant; that the real CLI reports the surface it was given is T025
- [ ] T022 [P] [US2] In `tests/Grimoire.Fast.Tests/CeilingTests.cs`, driven by `FakeTimeProvider`: **either** ceiling stops the run at once through the port's stop and ends it failed; the cost total is the sum of `inputTokens`, `outputTokens`, `cacheReadInputTokens` and `cacheCreationInputTokens` over **every** entry of a `modelUsage` with more than one model in it, so a background call counts (research.md R-04); both ceilings are fixed values — 2 000 000 tokens and 15 minutes — not settings — **Req:** GUARD-004 | **Level:** Fast — **Why not lower:** there is no lower level, and a fake clock keeps the Fast suite inside its 15 s budget
- [ ] T023 [P] [US2] In `tests/Grimoire.Fast.Tests/RunOutcomeTests.cs`, the decision table of `contracts/agent-cli-protocol.md` — an entry is present when `log.md` contains the run's identifier as plain text, and nothing else is parsed: agent stopped with the entry present and both ceilings clear → done; stopped with no entry and not yet nudged → nudged once, still running; stopped with no entry and already nudged → failed; a ceiling reached → failed whatever the log says. Nothing but the log entry is read to decide — **Req:** RUNS-005 | **Level:** Fast — **Why not lower:** there is no lower level; the decision is ours, and the log lookup goes through the wiki port
- [ ] T024 [P] [US2] In `tests/Grimoire.Contract.Tests/FileSystemWikiStoreTests.cs`: `FileSystemWikiStore` against a real wiki directory on disk — the stamped page as written, an unreadable frontmatter refused, a failed run's files still there, a path escaping the wiki root refused with `outside-wiki`, which is the spec's edge case for GUARD-001. This is the Contract suite's first test: **remove `--ignore-exit-code 8` from the Contract line of `.github/workflows/ci.yml` with it** — **Req:** GUARD-001, WIKI-002, WIKI-003 | **Level:** Contract — **Why not lower:** Fast cannot prove it — the real filesystem decides what lands on disk and how a path resolves, and that is the external thing at this port
- [ ] T025 [US2] In `tests/Grimoire.Contract.Tests/HarnessProcessTests.cs`, marked `[Trait("requires","signin")]`: one real run of the real `claude` CLI against a real wiki in a temporary directory — `system/init` reports a `tools` array **equal to the grant** — the grant's bare names each prefixed `mcp__wiki__` — and the `wiki` server connected; the `result` message's `modelUsage` is keyed by **the pinned model id the run asked for**, with no API key in play; a granted tool writes; and a run asked for something only a shell or a file tool could do makes no call outside the grant and changes nothing outside the wiki — **Req:** GUARD-001, GUARD-002 | **Level:** Contract — **Why not lower:** Fast cannot prove it — the reported tool surface and the served model are the real CLI's own answers, and whether our deny-by-default configuration and our pinned id actually take hold is exactly what this checks
- [ ] T026 [US2] In `tests/Grimoire.Contract.Tests/HarnessProcessTests.cs`, marked `[Trait("requires","signin")]`: after the agent's first stop the nudge continues the same run inside the ceilings, and the interrupt ends a run in flight so it reads failed — **Req:** RUNS-005, GUARD-004 | **Level:** Contract — **Why not lower:** Fast cannot prove it — a second message reaching a live session and an interrupt stopping a call in flight are behaviours of the real CLI process

### Implementation for User Story 2

- [ ] T027 [P] [US2] `src/Grimoire.Wiki/IWikiStore.cs` — the only way into the wiki — and `OkfFrontmatter.cs`, parsing only the six OKF 0.2 parts research.md R-07 names (`type`, `sources` with `resource` per entry, `generated`, `okf_version`, section `index.md`, `log.md`). Nothing else of the standard is built — **Req:** Principle I.8
- [ ] T028 [US2] `src/Grimoire.Wiki/ProvenanceStamp.cs`: add, replace, or fail on an unreadable place for the record. `generated: { by, at }` is the only thing Grimoire writes into the wiki — **Req:** WIKI-002
- [ ] T029 [US2] `src/Grimoire.Wiki/Adapters/FileSystemWikiStore.cs`: the only place the filesystem is touched; paths relative to the wiki root, `..` and absolute paths refused; nothing removed, reverted or committed, ever — **Req:** WIKI-003, Principle V.2
- [ ] T030 [P] [US2] `src/Grimoire.Agent/ToolGrant.cs` and `Ceilings.cs`: the five granted tool names recorded with the run — bare, as the hub serves them, mapped to `mcp__wiki__<name>` only where the reported surface is compared (data-model.md §ToolGrant) — the pinned model id, and the two fixed ceilings as constants — 2 000 000 tokens and 15 minutes (research.md R-04) — with elapsed time measured against `TimeProvider` and cost summed over every entry of `modelUsage` — **Req:** GUARD-002, GUARD-003, GUARD-004
- [ ] T031 [US2] `src/Grimoire.Agent/Adapters/HarnessProcess.cs`: the only place the `claude` process and its NDJSON live. Spawns it with exactly the argv of `contracts/agent-cli-protocol.md` — `--tools ""`, `--mcp-config` + `--strict-mcp-config`, `--allowed-tools "mcp__wiki__*"`, `--permission-mode dontAsk`, `--setting-sources ""`, `--include-partial-messages`, `--input-format stream-json`, `--no-session-persistence` — in a working directory Grimoire owns, not the wiki. Asserts `system/init`'s tool list equals the grant under the `mcp__wiki__` prefix — this file owns that mapping and is the only place that knows it (V.2) — totals `usage` off `message_delta`, reconciles against the `result` message, passes the model as a **pinned id** and removes `ANTHROPIC_API_KEY` from the child's environment, **refuses to start a run whose `system/init` reports any tool outside the grant or lacks `interrupt_receipt_v1`** — failed before the first model call — sends the nudge as a further user message, sends the `control_request` interrupt at **either** ceiling, and kills the process only as a backstop — **Req:** GUARD-001, GUARD-004, Principle V.2
- [ ] T032 [US2] `src/Grimoire.Hub/Mcp/WikiToolsServer.cs`: the five tools of `contracts/mcp-wiki-tools.md` served over streamable HTTP at `/mcp/runs/{runId}`, bound to loopback and unauthenticated. `write_page` stamps; `write_index` and `append_log` do not — indexes and the log are not pages — **Req:** GUARD-001, GUARD-002, WIKI-002
- [ ] T033 [P] [US2] `instructions/ingest.md` stating the shape the wiki is to have — source page, a `type` on every page, named sources referenced where a statement relies on them, links, one section per page and sections one level deep, a current index per section, a root index declaring the pinned OKF version and listing the sections, and a log entry per run that identifies the run and says what changed and why. Changing this file is an owner decision named in the PR. Plus `src/Grimoire.Hub/InstructionLoader.cs`, which **loads that instruction and the user's purpose description from the paths the hub was started with and assembles the dispatch payload** of `contracts/agent-cli-protocol.md` — instruction, purpose description, submitted text, run identifier, and nothing else. It is the only thing that puts text into the agent's prompt, and the absence check T015 calls lives here, reporting which of the two is missing — **Req:** WIKI-001, INGEST-002, Principle V.1
- [ ] T034 [US2] `src/Grimoire.Runs/RunStateMachine.cs`: `Run` with its grant, ceilings, `TokensUsed` and `LogEntryNudged`, and the RUNS-005 decision — done, the single nudge, or failed. The log entry is the one thing read in the wiki — **Req:** RUNS-005

**Checkpoint**: User Stories 1 and 2 both work independently — a text now reaches the wiki through a
real run.

---

## Phase 5: User Story 3 - See what became of each submission (Priority: P3)

**Goal**: the browser shows, for every submission, exactly one of the four states and nothing more.

**Independent Test**: drive submissions to each of the four states and confirm the browser reports
exactly that state and nothing else.

- [ ] T035 [US3] In `tests/Grimoire.E2E.Tests/SubmissionStatesTests.cs`: with submissions in different states, a real browser **renders** each one as exactly one of submitted, running, done, failed, and renders nothing further about the run — **Req:** ACCESS-002 | **Level:** E2E — **Why not lower:** Contract cannot prove what a browser puts on the screen. The response shape behind it is proven a level down, in T010
- [ ] T036 [US3] `GET /api/submissions` in `src/Grimoire.Hub/Api/SubmissionsEndpoints.cs` and the polling list in `wwwroot/app.js`, exactly the shape of `contracts/hub-http-api.md` — `id`, `state`, `submittedAt`, and no further field about the run — **Req:** ACCESS-002

**Checkpoint**: all user stories are independently functional.

---

## Phase 6: Closing the feature

- [ ] T037 Run the Contract suite in both halves — `--filter-not-trait "requires=signin"` as CI does, and `--filter-trait "requires=signin"` locally with the owner's sign-in — and the E2E suite. All pass, Contract inside `--timeout 90s`. The default run is Fast only, so this is the one place they are exercised before the PR — **Req:** Principle III.7
- [ ] T038 Run `trace-check check --complete`; it passes — every `test` requirement has a test carrying its ID, no test carries an unknown, retired or reserved ID, every test has a level, and every E2E test has a requirement ID. This is the call CI makes where the feature lands on main — **Req:** Principle IV.3
- [ ] T039 Regenerate and commit `docs/trace.md` with `Grimoire.Trace write`, and set OUT-01's status and spec reference in `docs/product.md` — the only two edits an agent makes to that file — **Req:** Principle IV.4
- [ ] T040 Reconcile `docs/capabilities/{ingest,wiki,guard,access,runs}.md` with this feature's requirements as added, changed or removed, and merge the plan's binding decisions into `docs/decisions.md`, each with its reason and the next free `DEC-NNN` — **Req:** Principle IV.2, Principle II.6
- [ ] T041 Walk `docs/review-checklist.md`, all twelve items — including item 3, which is what proves WIKI-001, and item 5's question whether the owner has read what WIKI-001, this feature's one review-proven requirement, is about — **Req:** Principle Gov.2, Principle I.9
- [ ] T042 The owner exercises OUT-01 once with the real external systems in place, per the acceptance run in plan.md and `quickstart.md`: the real `claude` CLI signed in against a real model, and a real wiki. This is the last task of the feature; without it the feature is not done — **Req:** Principle I.9

---

## Dependencies & Execution Order

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; blocks all user stories. T004 comes before any test
  is written (IV.2).
- **User stories (Phase 3+)**: depend on Foundational; then in priority order P1 → P2 → P3.
- **Closing (Phase 6)**: depends on every story.

### Within each user story

- Tests are written and fail before the implementation.
- Domain before adapters; adapters before the entry points that call them: T027/T028 before T029;
  T030 before T031; T034 before the dispatch path that reads it.
- US2's Contract tests (T025, T026) can only run once T031 and T032 exist — they drive the real CLI
  against the real endpoint.
- A story is finished before the next priority starts.

### Parallel opportunities

- T002 and T003 after T001.
- All of US1's Fast tests (T008–T010) together; T012 and T014 together.
- All of US2's Fast tests (T018–T023) together, and T024 alongside them.
- T027, T030 and T033 touch different projects and can proceed together.

---

## Implementation Strategy

1. Setup → Foundational, with the capability files registered first and both gates shown failing.
2. User Story 1 → validate independently, with the in-memory agent adapter. **This is the MVP**: a
   text goes in, a run is under way, a bad moment or a bad input is refused with a reason.
3. User Story 2 → the real run, the real grant, the real wiki. This is where the outcome becomes
   visible.
4. User Story 3 → the state display.
5. Closing: suites, `trace-check`, `docs/trace.md`, `docs/product.md`, capability files, decisions,
   the checklist, then the owner's acceptance run as the last task.

## Notes

- Every task names a requirement ID or a principle; every test task also names its level and why the
  level below cannot prove it.
- GUARD-001 and GUARD-002 are deliberately proven twice — Fast in the hub (T021) and Contract
  against the real CLI (T025). The deny configuration is a decision we made, not framework
  behaviour, so III.8 does not exclude it.
- ACCESS-002 is proven at two levels for the same reason its requirement has two halves: the
  response carries the state and nothing else (Fast, T010, which sits in US1's phase because that is
  where the endpoint is built), and the browser renders it (E2E, T035).
- WIKI-001 has no test task by design: it is the static content of a versioned file (III.8), proven
  by review-checklist item 3 in T041. T033 writes it.
- Commit after each task or logical group.
- A finding from review becomes a test only if it names a violated requirement ID (Governance 3);
  otherwise it becomes the smallest code change that resolves it, or is dropped.
