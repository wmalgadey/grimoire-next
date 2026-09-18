---
description: "Task list for Source Ingest via Agent Run"
---

# Tasks: Source Ingest via Agent Run

**Input**: Design documents from `/specs/001-source-ingest-agent-run/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md), [.specify/memory/constitution.md](../../.specify/memory/constitution.md)

**Tests**: **Included and mandatory.** Constitution III.1 requires a failing test before the production code that satisfies it, and the tasks phase is the named enforcement point for "a test task precedes the implementation task it constrains". Every test task below carries its Test Strategy row (TS-01 … TS-21) from plan.md.

**Organization**: Tasks are grouped by user story so each story can be implemented, tested, and delivered independently.

**Starting point**: the repository contains only `specs/`, `docs/adr/`, and `.specify/`. There is no `src/`, no `tests/`, no `frontend/`, no solution file. Phase 1 creates all of it.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

Seven first-level domain slices under `src/` (`ingest`, `tasks`, `dispatch`, `wiki`, `agentrun`, `instructions`, `hub`) plus `src/egress/`, with `frontend/`, `contracts/`, `deploy/`, and `tests/` mirroring the slices — exactly as fixed in plan.md "Project Structure". `deploy/` is not a domain slice and contains no application code.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Bring the repository from documents-only to a buildable, testable skeleton.

- [X] T001 Create the directory skeleton from plan.md "Source Code (repository root)": `src/ingest/`, `src/tasks/`, `src/dispatch/`, `src/wiki/`, `src/agentrun/src/{instruction,model,wiki-tools,run}/`, `src/instructions/`, `src/egress/`, `src/hub/`, `contracts/`, `frontend/src/routes/`, `deploy/`, and `tests/{ingest,tasks,dispatch,wiki,agentrun,hub,surfaces,egress,deployment,architecture,scripted-model}/`
- [X] T002 Add `global.json` pinning the .NET SDK to `10.x` and create `Grimoire.sln` at the repository root
- [X] T003 [P] Create one C# project per slice — `src/ingest/Grimoire.Ingest.csproj`, `src/tasks/Grimoire.Tasks.csproj`, `src/dispatch/Grimoire.Dispatch.csproj`, `src/wiki/Grimoire.Wiki.csproj`, `src/egress/Grimoire.Egress.csproj`, `src/hub/Grimoire.Hub.csproj` — each targeting `net10.0` with `<LangVersion>14</LangVersion>`, and add them to `Grimoire.sln`
- [X] T004 [P] Declare package references so adapter confinement is a compile-time property as well as an architecture test (plan V.3): `Microsoft.Data.Sqlite` only in `src/tasks/Grimoire.Tasks.csproj`, `Yarp.ReverseProxy` only in `src/egress/Grimoire.Egress.csproj`, `Microsoft.AspNetCore.OpenApi` and `Microsoft.Extensions.Diagnostics.HealthChecks` only in `src/hub/Grimoire.Hub.csproj`
- [X] T005 [P] Create xUnit test projects `tests/ingest/`, `tests/tasks/`, `tests/dispatch/`, `tests/wiki/`, `tests/hub/`, `tests/architecture/`, `tests/egress/`, `tests/deployment/` (one `.csproj` each) with `Microsoft.AspNetCore.Mvc.Testing` and `NetArchTest.Rules`, added to `Grimoire.sln`
- [X] T006 [P] Initialise the runner package in `src/agentrun/package.json` and `src/agentrun/tsconfig.json`: TypeScript 5.x on Node 22, dependencies `@anthropic-ai/claude-agent-sdk` and `zod`, dev dependency `vitest`, build script emitting `src/agentrun/dist/main.js` (the exact path `src/dispatch/adapters/RunnerProcess.cs` will spawn)
- [X] T007 [P] Initialise the SvelteKit frontend in `frontend/package.json`, `frontend/svelte.config.js`, `frontend/vite.config.ts`: Svelte 5, `@sveltejs/adapter-static`, `openapi-typescript`, `@playwright/test`; the static build output is served by the hub on one origin
- [X] T008 [P] Initialise the scripted-model double package in `tests/scripted-model/package.json` and `tests/scripted-model/tsconfig.json` (Node 22, no Anthropic dependency — it is an HTTP server, not a client)
- [X] T009 [P] Add `.editorconfig` at the repository root plus ESLint and Prettier configs for `src/agentrun/`, `frontend/`, and `tests/scripted-model/`
- [X] T010 [P] Add `dependency-cruiser` configs `src/agentrun/.dependency-cruiser.cjs` and `frontend/.dependency-cruiser.cjs` (rules filled in by T035 and T051)
- [X] T011 [P] Add `.gitignore` covering `bin/`, `obj/`, `node_modules/`, `dist/`, `.svelte-kit/`, `test-results/`, `playwright-report/`
- [X] T012 Add `.github/workflows/ci.yml` running `dotnet build`, `dotnet test`, `npm --prefix src/agentrun test`, and `npm --prefix frontend run test:e2e`; the nine constitution quality gates are wired into this file as their tests land (completed by T110)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The domain vocabulary, the two stores, the LLM double, the composition root, and the process protocol — everything all three user stories stand on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Domain types (one representation per concept, constitution VII.2)

- [X] T013 [P] Create `src/tasks/TaskState.cs` — an enum with **exactly** the five values `queued`, `running`, `completed`, `failed`, `reverted` and no others (FR-020)
- [X] T014 [P] Create `src/tasks/Task.cs` — `id` (opaque string, "Stable for the task's life"), `state`, `submittedAt` (UTC), `startedAt`/`endedAt` (UTC, nullable), `source` (exactly one), `run` (nullable, "At most one, never more"), `failureReason` (nullable, "required whenever `state = failed`"), `revert` (nullable, "Present exactly when `state = reverted`")
- [X] T015 [P] Create `src/ingest/Source.cs` — `kind` (`text` | `url`), `submittedValue` ("Non-empty after trimming, else the submission is rejected with no task created"), `retrievedText` (nullable; "`null` until retrieved and permanently `null` if retrieval failed"), `retrievedAt` (UTC, nullable), `byteLength` (integer, "Of the text handed to the run")
- [X] T016 [P] Create `src/tasks/AgentRun.cs` and `src/tasks/InstructionVersion.cs` — run: `instructionVersion`, `toolGrant`, `toolCalls` (ordered, append-only during the run, frozen after), `outcome` (`completed` | `failed`, null while running), `failureReason` (required when `outcome = failed`), `commit` (nullable, at most one), `toolCallCount`, `durationMs`; version: `path`, `sha256` ("Full content hash of the file's bytes"), `byteLength`
- [X] T017 [P] Create `src/tasks/ToolGrant.cs` and `src/tasks/ToolCall.cs` — grant: `tools` + `recordedAt`; call: `seq` ("1-based, strictly increasing, the order the calls were made"), `tool` (the name as the model named it, including a name outside the granted set), `target` (nullable), `outcome` (`ok` | `failed` | `refused`), `detail` (nullable), `at`
- [X] T018 [P] Create `src/wiki/WikiCommit.cs` and `src/wiki/FileDiff.cs` — commit: `sha`, `parentSha`, `message`, `committedAt`, `fileDiffs` ("Derived from the commit on read, not stored"); diff: `path`, `changeKind` (`added` | `modified` | `removed`), `patch`
- [X] T019 [P] Create `src/tasks/RevertRecord.cs` — `revertCommitSha`, `revertedAt`

### Operational state store (ADR-0006)

- [X] T020 Write failing tests in `tests/tasks/SqliteStoreTests.cs` against a real per-test SQLite file: round-trip every entity; task list `ORDER BY submitted_at DESC` with a cursor; a second `AgentRun` for the same task is rejected by the database, not by code
- [X] T021 Create `src/tasks/adapters/SqliteSchema.cs` — the six tables `task`, `source`, `agent_run`, `tool_grant`, `tool_call`, `revert_record`, with `tool_call` append-only and ordered by `(run_id, seq)` and a **uniqueness constraint on `agent_run.task_id`** so FR-005's "never a second run" is a database property
- [X] T022 Create `src/tasks/adapters/SqliteStore.cs` — the only `Microsoft.Data.Sqlite` reference in the repository; persistence and the newest-first cursor query

### Wiki repository adapter (ADR-0005)

- [X] T023 Write failing tests in `tests/wiki/GitCliTests.cs` against a real git repository with at least one commit: `rev-parse HEAD`; `add -A` + `commit` producing exactly one commit; `reset --hard HEAD` + `clean -fd` restoring byte-identical content after arbitrary working-tree changes; per-file diff of a commit
- [X] T024 Create `src/wiki/adapters/GitCli.cs` — the only `git` invocation in the repository: `rev-parse`, `add -A`, `commit`, `reset --hard`, `clean -fd`, `revert --no-edit`, `diff`. `System.Diagnostics.Process` is confined to this file and `src/dispatch/adapters/`

### The single sanctioned test double (ADR-0004)

- [X] T025 [P] Create `tests/scripted-model/src/server.ts` — an Anthropic-compatible HTTP server the runner reaches through `ANTHROPIC_BASE_URL`; records each request's arrival timestamp, headers, and body bytes so tests can assert ordering and pass-through
- [X] T026 [P] Create `tests/scripted-model/src/scripts.ts` — the named scripts plan.md Test Strategy requires: read-then-write, write-only, read-only, no-op, N-iteration escalation (N = 1…8), never-stopping, write-then-hang, slow read-then-write, echo-length, and the escape-attempt scripts

### Process protocol (contracts/runner-protocol.md)

- [X] T027 [P] Write failing tests in `tests/agentrun/protocol.test.ts` — the NDJSON envelope is "One JSON object per line, no embedded newlines, UTF-8"; an unknown event type is rejected rather than ignored
- [X] T028 Create `src/agentrun/src/run/protocol.ts` — typed runner→hub events `instruction_loaded`, `tool_grant`, `tool_call`, `run_end` and hub→runner messages `dispatch`, `proceed`, with the exact field sets from contracts/runner-protocol.md
- [X] T029 Create `src/dispatch/adapters/RunnerEvents.cs` — the C# half of the same envelope; the port-boundary translation of existing concepts, not a parallel model (VII.2)

### Composition root and operations surface (ADR-0011)

- [X] T030 Write failing tests in `tests/hub/ConfigurationTests.cs` — every variable in contracts/deployment.md "Hub" is read from the environment only; a missing `GRIMOIRE_WIKI_REPO`, `GRIMOIRE_STATE_DB`, `GRIMOIRE_MODEL_BASE_URL`, or `GRIMOIRE_MODEL_TOKEN` fails fast and loudly at startup
- [X] T031 Create `src/hub/Program.cs` — the composition root: environment configuration with no settings abstraction over it, structured JSON logging to stdout (one event per line, no files, no rotation), DI, and endpoint mapping
- [X] T032 Write failing tests in `tests/hub/OperationsEndpointsTests.cs` — `GET /healthz` returns `{"status":"healthy"}`; `GET /readyz` returns `checks` for `wikiRepo`, `stateDb`, `egress` each `ok` | `failed`, `200` when ready and `503` when any check fails or `draining` is true
- [X] T033 Create `src/hub/Operations.cs` — `/healthz` and `/readyz` using the framework's own health-check registration (VII.1), surfacing the `grimoire.hub.readiness` signal

### Committed contract (ADR-0008)

- [X] T034 Copy the feature's contract to the repository-root mirror `contracts/hub-api.openapi.yaml` from `specs/001-source-ingest-agent-run/contracts/hub-api.openapi.yaml`, unchanged
- [X] T035 Write failing tests in `tests/hub/ContractDriftTests.cs` (TS-15) — the hub's served OpenAPI document equals `contracts/hub-api.openapi.yaml`, covering the `Tasks` and `Operations` tags; this is also the single wire-up test III.6 permits
- [X] T036 Add the `openapi-typescript` generation step to `frontend/package.json` emitting `frontend/src/lib/api/schema.d.ts` from `contracts/hub-api.openapi.yaml` at build time — the frontend's types are generated, never hand-written

### Architecture gates 2 and 3 (gate 1 lands with the instruction loader in T051)

- [X] T037 [P] Write `tests/architecture/SliceStructureTests.cs` (quality gate 3) — first-level directories under `src/` are domain slices; no `controllers`, `services`, `utils`, `helpers`, or `models` directory exists at that level; `deploy/` contains no compiled application code
- [X] T038 [P] Write `tests/architecture/AdapterConfinementTests.cs` (quality gate 2) — via NetArchTest over the built assemblies: `Microsoft.Data.Sqlite` referenced only under `src/tasks/`, `System.Diagnostics.Process` only under `src/dispatch/` and `src/wiki/`, `HttpClient` only under `src/ingest/`, `Yarp.ReverseProxy` only under `src/egress/`
- [X] T039 [P] Add the `dependency-cruiser` rule in `src/agentrun/.dependency-cruiser.cjs` (quality gate 2, TS layer) — `@anthropic-ai/claude-agent-sdk` may be imported only under `src/agentrun/src/model/`

**Checkpoint**: The solution builds, both stores work against real infrastructure, the LLM double answers on a socket, the composition root boots and reports health, and the contract is committed and drift-checked. User story work can begin.

---

## Phase 3: User Story 1 - Ingest a source and inspect what the agent did (Priority: P1) 🎯 MVP

**Goal**: A user submits pasted text or a URL, gets exactly one task, one real agent run executes as a tool-use loop under the versioned instruction file with exactly two granted tools, the run's changes land as exactly one commit, and the task view alone shows which instruction version ran, what the agent read, what it wrote, and what the wiki says now.

**Independent Test**: Submit pasted text through the frontend, wait for the run to end, open the task. The task view alone answers: which instruction version ran, what the agent looked at, what it wrote, and what the wiki looks like now versus before — without opening a terminal or the repository (quickstart Scenario 1).

### Tests for User Story 1 ⚠️ Write first; all must fail before the implementation tasks below

- [X] T040 [P] [US1] TS-01 in `tests/ingest/SubmissionTests.cs` — an empty and a whitespace-only submission are rejected with `400` and `application/problem+json`, and **no task is created** (FR-001)
- [X] T041 [P] [US1] TS-02 in `tests/ingest/TaskCreationTests.cs` — an accepted submission returns `201` with exactly one `Task`, the task is openable immediately, and it is visible within 2 seconds of submitting (FR-002, SC-001)
- [X] T042 [P] [US1] TS-03 in `tests/ingest/UrlRetrievalTests.cs` — against a second real HTTP listener returning `500`, then a non-text content type: the task ends `failed` with a human-readable reason, **no run is dispatched**, and the wiki is byte-identical (FR-003)
- [X] T043 [P] [US1] TS-20 in `tests/ingest/UrlFetchPolicyTests.cs` — a non-`http(s)` scheme and destinations resolving to loopback, link-local, or private ranges are refused, the refusal is recorded as the task's failure reason, and no run is dispatched (FR-003, research R16)
- [X] T044 [P] [US1] TS-04 in `tests/dispatch/SourcePassthroughTests.cs` — with the echo-length script, the bytes the runner receives equal the bytes submitted: no size limit, no truncation, no summarisation (FR-029)
- [X] T045 [P] [US1] TS-05 in `tests/dispatch/OneRunPerTaskTests.cs` — exactly one run per task and never a second, including after a redelivered dispatch (FR-005)
- [X] T046 [P] [US1] TS-07 in `tests/dispatch/DispatchOrderingTests.cs` — the persisted `instructionVersion` and `toolGrant` timestamps precede the scripted-model server's first-request timestamp, proving the `proceed` handshake orders them before the first model call (FR-013, FR-014)
- [X] T047 [P] [US1] TS-06 in `tests/agentrun/loop.test.ts` — parameterised N = 1…8 escalation against a real spawned runner and the real SDK loop: all N tool calls are executed and recorded **within a single run**, each result informing the next call (FR-007, FR-008, SC-007)
- [X] T048 [P] [US1] TS-08 in `tests/agentrun/toolcalls.test.ts` and `tests/tasks/ToolCallRecordTests.cs` — read, write, write-outside-wiki, and non-granted-tool attempts are all recorded in order with `seq` strictly increasing, each with its `target` and `outcome` (`ok` | `failed` | `refused`); a refusal is a recorded call, not an absence (FR-011, FR-021)
- [X] T049 [P] [US1] TS-09 in `tests/architecture/ContainmentInstructionAndInputTests.cs` (quality gate 4) — with canary files planted outside the wiki repository, adversarial **instruction-file content** and adversarial **task input** attempting `Bash`, `Read`, `WebFetch`, `Task`, and an unknown MCP tool produce no effective tool outside the granted pair; every canary is untouched and every attempt is recorded as `refused` (FR-010, constitution II.5)
- [X] T050 [P] [US1] TS-10 in `tests/architecture/ContainmentWikiContentTests.cs` (quality gate 4) — adversarial **wiki page content** read during the run, plus `../` traversal, absolute paths, and a planted symlink out of the repository, cannot escape; each is refused and recorded (FR-011)
- [X] T051 [P] [US1] TS-17 gate 1 in `tests/architecture/system-prompt-construction.test.ts` — the `SystemPrompt` branded type is constructed only inside `src/agentrun/src/instruction/`, and no `systemPrompt` string literal exists anywhere else in the repository (constitution I.2)
- [X] T052 [P] [US1] TS-11 (commit half) in `tests/wiki/CommitTests.cs` — a run that changed content produces **exactly one** commit, never zero and never two, and its identity is recorded on the task (FR-015, SC-002)
- [X] T053 [P] [US1] TS-12 (run-end half) in `tests/wiki/FailureContainmentTests.cs` — the runner killed mid-write, and the never-stopping script hitting the run limit: no commit exists for the task, the tip is unchanged, and wiki content is byte-identical to the pre-run commit (FR-009, FR-017, SC-003)
- [X] T054 [P] [US1] FR-016 in `tests/wiki/NoChangeTests.cs` — the read-only script ends the task `completed` with no commit, an empty diff, and `changedNothing: true` stated explicitly (FR-016, SC-011)
- [X] T055 [P] [US1] TS-13 (US1 rows) in `tests/hub/ObservabilityTests.cs` — booting `src/hub/` exactly as production does, assert `grimoire.task.created`, `grimoire.task.state_changed`, `grimoire.run.dispatched` (with `instructionVersion`, `toolGrant`), `grimoire.run.tool_call` (with `seq`, `tool`, `target`, `outcome`), `grimoire.run.ended` (with `outcome`, `failureReason`, `toolCallCount`, `durationMs`), `grimoire.wiki.committed` (with `commitSha`, `filesChanged`), and `grimoire.run.model_endpoint_unreachable` are each emitted **and** appear on the response field the surface renders (quality gate 6)
- [X] T056 [P] [US1] TS-16 (US1 flow) in `tests/surfaces/ingest.spec.ts` — Playwright against the built SvelteKit app: submit → task list → task view, which shows state, source, instruction version, granted tool set, ordered tool calls, diff, and commit identity (FR-020, FR-030)

### Implementation for User Story 1

- [X] T057 [US1] Write `src/instructions/ingest.md` — the versioned ingest instruction and the **sole** home of every item in the spec's Judgment Boundary: whether a source warrants one page or several, new page versus update, page naming and placement, what a good page contains, which links to add, which pages to read first, whether to leave the wiki unchanged, what to discard, whether to reorganise neighbours, and ending the run with the commit-message wording (research R9). It contains no control rule and no harness branching
- [X] T058 [P] [US1] Create `src/ingest/Submission.cs` — accept `kind` `text` | `url`, reject a submission that is empty or whitespace-only after trimming without creating a task (FR-001)
- [X] T059 [US1] Create `src/ingest/adapters/UrlFetch.cs` — the only `HttpClient` in the repository; routed through `GRIMOIRE_FETCH_PROXY`; refuses non-`http(s)` schemes and destinations resolving to loopback, link-local, or private ranges; caps redirects; a `500`, an unreachable host, or a non-text content type becomes the task's recorded human-readable failure reason with no run dispatched (FR-003, research R16)
- [X] T060 [US1] Create `src/tasks/TaskCreation.cs` — exactly one task per accepted submission, with a stable identifier and openable from the moment it is created; retains its `Source` for as long as the task is retained (FR-002, FR-004)
- [X] T061 [US1] Create `src/agentrun/src/instruction/loader.ts` and `src/agentrun/src/instruction/systemPrompt.ts` — the only module that reads instruction files and the only constructor of the `SystemPrompt` branded type; computes `sha256` (full content hash) and `byteLength`, and emits `instruction_loaded` before anything else (constitution I.2, FR-014, research R10)
- [X] T062 [US1] Create `src/agentrun/src/wiki-tools/readPage.ts` — `mcp__wiki__read_page`, input `path` (wiki-relative); a directory path lists its entries; a missing page returns an explicit "no such page" **result, not an error**; `realpath`-resolves against the repository root and refuses anything landing outside it
- [X] T063 [US1] Create `src/agentrun/src/wiki-tools/writePage.ts` — `mcp__wiki__write_page`, input `path` (wiki-relative, parent directories created as needed) and `content` (`string | null`, where `null` deletes the page); returns the resulting change kind `added` / `modified` / `removed` / `unchanged`; `realpath` containment refuses `../`, absolute paths, and symlinks that resolve outside the repository root (FR-011)
- [X] T064 [US1] Create `src/agentrun/src/wiki-tools/server.ts` — `createSdkMcpServer({ name: "wiki", tools: [readPage, writePage] })` registering **exactly** these two tools, so the model sees `mcp__wiki__read_page` and `mcp__wiki__write_page` (FR-010)
- [X] T065 [US1] Create `src/agentrun/src/wiki-tools/guard.ts` — the `PreToolUse` hook that records **and decides** every call before any other permission step; denies anything outside the granted pair with `detail: "tool not granted"` and anything outside the wiki with `detail: "target resolves outside the wiki repository"`, each emitted as a `tool_call` with `outcome: "refused"` (FR-011, FR-021, ADR-0009)
- [X] T066 [US1] Create `src/agentrun/src/model/adapter.ts` — the only `@anthropic-ai/claude-agent-sdk` import: `query()` with a **custom** `systemPrompt` from the instruction loader (the `claude_code` preset is deliberately not used), `settingSources: []`, every built-in tool name listed individually in `disallowedTools` (never the wildcard `"*"`, which would strip the granted pair), `permissionMode: "dontAsk"`, `maxTurns` from the run limit, and `env` fully replaced per contracts/deployment.md (research R3, ADR-0003)
- [X] T067 [US1] Create `src/agentrun/src/run/main.ts` and wire the build to `src/agentrun/dist/main.js` — emit `instruction_loaded` then `tool_grant`, **block** until `proceed` arrives on stdin, then run the loop; emit one `tool_call` per call as it resolves, in order; emit `run_end` with `outcome`, `failureReason`, `commitMessage` (the final assistant message text, verbatim) and `toolCallCount`; write diagnostics to stderr only, never as a source of task state (research R8)
- [X] T068 [US1] Create `src/dispatch/adapters/RunnerProcess.cs` — spawn `node src/agentrun/dist/main.js`, one process per run and never reused, with `cwd` pinned to the wiki repository working tree and `env` **replaced** by exactly `PATH`, `HOME` (per-run `tmpfs` directory), `ANTHROPIC_BASE_URL`, `ANTHROPIC_AUTH_TOKEN` (the opaque internal token, never an Anthropic credential), `ANTHROPIC_CUSTOM_HEADERS: X-Grimoire-Run: <runId>`, and `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1`; read NDJSON from stdout; treat a non-zero exit as a crash indistinguishable from an abort
- [X] T069 [US1] Create `src/dispatch/RunLimit.cs` — the elapsed-time and tool-call ceilings from `GRIMOIRE_RUN_MAX_ELAPSED_MS` and `GRIMOIRE_RUN_MAX_TOOL_CALLS`; reaching either kills the child process and ends the run `failed` with a recorded reason and no commit (FR-009)
- [X] T070 [US1] Create `src/dispatch/Dispatcher.cs` — dispatch exactly one run per task whose source is available; persist `instruction_loaded` and `tool_grant` on the task, **assert the reported grant equals the configured pair** and fail the run on a mismatch rather than logging and ignoring it, then send `proceed` (FR-005, FR-013, FR-014)
- [X] T071 [US1] Create `src/dispatch/RunOutcome.cs` with three named outcome handlers — committed, no-change, failed — written as three handlers over a single `RunOutcome` value from the start, so the run-end method never drifts over the complexity threshold (plan VII.3)
- [X] T072 [US1] Create `src/wiki/WikiCommit.cs` commit path — on a run that changed content, `git add -A` followed by one `git commit`, message taken verbatim from `run_end.commitMessage` with the fixed constant `ingest <taskId>` when it is empty; record the commit identity and its `parentSha` on the task (FR-015, research R9)
- [X] T073 [US1] Create `src/wiki/WorkingTreeReset.cs` — `git reset --hard HEAD` followed by `git clean -fd` on the run-end failure path, so a failed, crashed, aborted, or limit-hit run commits nothing and leaves content byte-identical to the pre-run commit (FR-017)
- [X] T074 [US1] Create `src/wiki/Diff.cs` — per-file `added` / `modified` / `removed` with the patch, derived from the commit on read and never stored, so the artifact cannot drift from history (FR-022)
- [X] T075 [US1] Create `src/hub/Endpoints.cs` — `POST /api/tasks` (`201` with a `Task`, `400` `Problem` on an empty submission), `GET /api/tasks` (`limit` integer minimum 1, maximum 200, default 50; opaque `cursor`; newest first), `GET /api/tasks/{taskId}` (`200` `TaskDetail`, `404` `Problem`), each deriving its shape from `contracts/hub-api.openapi.yaml` (FR-020, FR-021, FR-022, FR-030)
- [X] T076 [US1] Emit the US1 observability signals from the production composition root in `src/hub/` and `src/dispatch/` — `grimoire.task.created`, `grimoire.task.state_changed`, `grimoire.run.dispatched`, `grimoire.run.tool_call`, `grimoire.run.ended`, `grimoire.run.model_endpoint_unreachable`, `grimoire.wiki.committed` — as structured JSON on stdout, each also reaching the field the task list or task view renders (plan IV)
- [X] T077 [P] [US1] Create `frontend/src/routes/+page.svelte` — the submit surface: pasted text or URL, rejecting empty or whitespace-only input, posting to `POST /api/tasks` through the generated client (FR-001)
- [X] T078 [P] [US1] Create `frontend/src/routes/tasks/+page.svelte` — the task list: every retained task newest first with an identification of its source and its current state, each openable (FR-030, SC-006)
- [X] T079 [US1] Create `frontend/src/routes/tasks/[taskId]/+page.svelte` — the task view: state, source, instruction-file version shown as `sha256:` plus its **first 12 hex**, the granted tool set, the ordered tool-call record with each call's target and outcome, the per-file diff and commit identity, and an explicit "nothing changed" for a completed run with `changedNothing: true` (FR-021, FR-022, SC-008, SC-010, SC-011)

**Checkpoint**: User Story 1 is fully functional and independently testable — the MVP. Submit, run, commit, inspect.

---

## Phase 4: User Story 2 - Revert an ingest (Priority: P2)

**Goal**: One action on the task view restores the wiki to its pre-run content as a new commit, leaves history intact, and marks the task reverted — offered exactly on eligible tasks and explained, not hidden, on superseded ones.

**Independent Test**: Run an ingest that changes the wiki, reopen the task from the task list, revert it, and confirm the wiki content is identical to its pre-run state, the task reads `reverted`, and the original diff and tool-call record are still shown (quickstart Scenario 3).

**Depends on**: US1's commit path (there must be a commit to revert).

### Tests for User Story 2 ⚠️ Write first

- [X] T080 [P] [US2] TS-11 (revert half) in `tests/wiki/RevertTests.cs` — against a real git repository, revert restores content byte-identical to the commit preceding the task's commit as a **new** commit; the task's own commit stays in history; nothing is rewritten or discarded (FR-025, SC-005)
- [X] T081 [P] [US2] In `tests/wiki/RevertEligibilityTests.cs` — eligible **exactly when** the run produced a commit **and** that commit is the wiki's current tip **and** the task is not already reverted; otherwise `reason` is one of `no-commit`, `superseded`, `already-reverted` (FR-024, FR-027)
- [X] T082 [P] [US2] In `tests/hub/RevertEndpointTests.cs` — `POST /api/tasks/{taskId}/revert` returns `200` with the updated `TaskDetail`, `404` for an unknown task, and `409` whose `detail` names which condition failed; a second attempt is refused and two concurrent attempts revert exactly once (FR-026)
- [X] T083 [P] [US2] TS-13 (revert rows) in `tests/hub/ObservabilityRevertTests.cs` — `grimoire.wiki.revert_eligibility` (with `eligible`, `reason`) and `grimoire.wiki.reverted` (with `revertCommitSha`) are emitted through the production composition root and appear on the task view (quality gate 6)
- [X] T084 [P] [US2] TS-16 (revert flow) in `tests/surfaces/revert.spec.ts` — revert from the task view in one action with zero manual steps; a reverted task shows its revert commit identity and offers no revert; a superseded task shows that it was **superseded by a later wiki commit** rather than a disabled control

### Implementation for User Story 2

- [X] T085 [US2] Create `src/wiki/RevertEligibility.cs` — a plain `git rev-parse HEAD` comparison under the hub's single-writer lock, returning `eligible` plus a `reason` of `no-commit`, `superseded`, or `already-reverted`; re-checked at revert time so a double-click or a second tab loses the race and is refused (FR-024, FR-027)
- [X] T086 [US2] Create `src/wiki/Revert.cs` — `git revert --no-edit <sha>` in the same working tree, producing the restoring commit and returning its identity (FR-025)
- [X] T087 [US2] Extend `src/tasks/adapters/SqliteStore.cs` and `src/tasks/Task.cs` with revert persistence — write the `RevertRecord` and set `state = reverted`, and enforce FR-023: once a run has ended, the only permitted writes to the task are its transition to `reverted` and the attached revert-commit identity
- [X] T088 [US2] Extend `src/hub/Endpoints.cs` — `POST /api/tasks/{taskId}/revert` with `200` / `404` / `409` per the contract, and `revertEligibility` on every `TaskDetail` response
- [X] T089 [US2] Extend `frontend/src/routes/tasks/[taskId]/+page.svelte` — render the revert action exactly when `revertEligibility.eligible` is true; otherwise render the reason, with `superseded` explained in words; after a revert show the revert commit identity alongside the unchanged instruction version, tool calls, and original diff (FR-026)

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Follow a run while it is queued, running, or broken (Priority: P3)

**Goal**: A task is a real object in every state — before the agent starts, while it works, after it fails, and across a hub restart or a `SIGTERM`.

**Independent Test**: Open a task while its run is queued, again while it is running, and again after a crash-induced failure; each time the view loads and shows what is known so far (quickstart Scenario 8).

**Depends on**: US1's dispatch path (there must be a run to follow).

### Tests for User Story 3 ⚠️ Write first

- [X] T090 [P] [US3] TS-14 (serialisation) in `tests/dispatch/QueueTests.cs` — with a slow read-then-write script, a submission accepted while a run executes produces a `queued` task; at most one run executes at a time; queued tasks are dispatched in `submittedAt` order (FR-019)
- [X] T091 [P] [US3] TS-14 (recovery) in `tests/dispatch/StartupRecoveryTests.cs` — across two hub instances sharing one SQLite file, with the runner killed alongside the first hub: every task recorded `running` becomes `failed` with a reason **naming the interruption** and is never dispatched again; queued tasks are dispatched in submission order; no task is left `running` while no run is executing (FR-028, SC-006)
- [X] T092 [P] [US3] TS-12 (startup half) in `tests/wiki/StartupResetTests.cs` — the hub killed mid-write leaves a dirty working tree that startup recovery resets, reaching content byte-identical to the pre-run commit with no commit created (FR-017, SC-003)
- [X] T093 [P] [US3] TS-19 in `tests/deployment/GracefulShutdownTests.cs` — against a real hub process and a real signal: on `SIGTERM` the hub stops dispatching, `/readyz` reports not-ready with `draining: true`, the running runner is terminated, its task becomes `failed` with an interruption reason, the wiki working tree is reset, and the process exits non-violently; queued tasks survive to the next start (FR-017, FR-028)
- [X] T094 [P] [US3] TS-13 (lifecycle rows) in `tests/hub/ObservabilityRecoveryTests.cs` — `grimoire.dispatch.recovered_on_startup` (with `failedTaskIds`, `requeuedTaskIds`) and `grimoire.dispatch.interrupted_on_shutdown` (with `taskId`) are emitted through the production composition root and their reasons appear on the task view and task list (quality gate 6)
- [X] T095 [P] [US3] TS-16 (five states) in `tests/surfaces/states.spec.ts` — a task opens without error in each of `queued`, `running`, `completed`, `failed`, and `reverted`, showing state, instruction version, granted tool set, and the tool calls so far wherever those exist (FR-020, SC-006)

### Implementation for User Story 3

- [X] T096 [US3] Create `src/dispatch/RunQueue.cs` — one run at a time, dispatched in `submittedAt` order, with the single-writer lock that also guards revert eligibility (FR-019)
- [X] T097 [US3] Create `src/dispatch/StartupRecovery.cs` — at boot, mark every task recorded `running` as `failed` with a reason naming the interruption and never dispatch it again; reset the wiki working tree with `git reset --hard && git clean -fd`; then dispatch queued tasks in submission order (FR-028, contracts/deployment.md Lifecycle)
- [X] T098 [US3] Create `src/dispatch/GracefulShutdown.cs` — on `SIGTERM`: stop accepting dispatches and report not-ready so traffic drains, terminate the running runner, mark that task `failed` with an interruption reason, reset the wiki working tree, exit
- [X] T099 [US3] Wire the startup and shutdown sequences into `src/hub/Program.cs` in the order contracts/deployment.md fixes — configuration, stores, recovery, dispatch, then `/readyz` reporting ready — so the ungraceful and graceful paths converge on the same end state
- [X] T100 [US3] Extend `frontend/src/routes/tasks/[taskId]/+page.svelte` and `frontend/src/routes/tasks/+page.svelte` — render a `queued` task as not yet started, a `running` task with its instruction version, granted tool set, and tool calls so far, and a `failed` task with its human-readable reason; the view reflects state as of load, with a manual refresh and no polling loop (FR-018, FR-020)

**Checkpoint**: All three user stories are independently functional.

---

## Phase 6: Deployment, Egress & Cross-Cutting Concerns

**Purpose**: The container boundary and the single egress seam that make the deny-all posture real, plus the CI gates that keep every principle enforced.

### Egress proxy (ADR-0010, ADR-0012) — tests first

- [X] T101 [P] TS-21 in `tests/egress/ModelRouteTests.cs` — against a real upstream listener asserting received headers: the proxy **strips** the caller's opaque internal token and **injects** the upstream credential on the model route, and refuses a non-allowlisted upstream
- [X] T102 [P] TS-21 in `tests/egress/FetchRouteTests.cs` — the fetch route forwards **no** credential and refuses an SSRF target bound to loopback, independently of the in-process guard in `src/ingest/adapters/UrlFetch.cs`
- [X] T103 Create `src/egress/Program.cs` and `src/egress/ModelRoute.cs` — YARP hosted in ASP.NET Core; route `/v1/*` to the single allowlisted upstream, stripping the internal token and injecting the upstream credential
- [X] T104 Create `src/egress/FetchRoute.cs` — `IHttpForwarder` to a per-request destination with the same SSRF policy and no credential injected
- [X] T105 Create `src/egress/CredentialProvider.cs` — a static token today; credential refresh, usage accounting, per-run quotas, and upstream failover are explicitly **not built** here and are later swaps behind this one interface

### Container boundary (ADR-0007, ADR-0011)

- [X] T106 Create `deploy/hub.Dockerfile` — multi-stage build producing one `grimoire-hub` image carrying the .NET 10 hub, the Node 22 runner build, `src/instructions/`, and the built frontend static assets; non-root user, no added capabilities, `linux/amd64` and `linux/arm64`
- [X] T107 [P] Create `deploy/egress.Dockerfile` for the `src/egress/` proxy image
- [X] T108 Create `deploy/compose.yaml` — the hub on an `internal: true` network with the proxy as its only reachable destination, the wiki and state volumes mounted, a read-only root filesystem, a `tmpfs` for per-run `HOME`, and **exactly one replica**
- [X] T109 TS-18 in `tests/deployment/ContainerBoundaryTests.cs` (quality gate 4, network half) — against the **built image** on a deny-all network with one allowed endpoint: the container runs as non-root with a read-only root filesystem; a full ingest completes with no egress except the configured endpoint; the runner process environment contains no upstream credential beyond the injected opaque token; a blocked host produces a recorded failure reason naming the unreachable endpoint — not a hang — and `/readyz` reports `egress: failed`

### Gates, documentation, and validation

- [X] T110 Complete `.github/workflows/ci.yml` — wire all nine constitution quality gates: the three architecture tests (T037, T038, T051), the trust-boundary tests (T049, T050, T109), the revertibility test (T080), the observability tests (T055, T083, T094), the full hermetic suite, the complexity regression gate, and the ADR lint slot; TS-18 and TS-19 run in CI on every PR and skip locally with a named reason
- [X] T111 [P] Add the per-method complexity regression gate configuration (threshold in CI configuration, not in the constitution), failing only on a new or worsened violation
- [X] T112 [P] Update `docs/adr/index.md` so every record ADR-0001 … ADR-0012 carries its status, and add a repository-root `README.md` pointing at the constitution, the ADR index, and `contracts/hub-api.openapi.yaml`
- [X] T113 Run every scenario in [quickstart.md](./quickstart.md) — Scenarios 1 through 12 — as written, including the `SIGKILL` repeat of Scenario 12, and record any divergence as a defect against the task that owns it
  - **2026-09-18 run.** Scenarios 1–10 through their automated suites: every C# suite (210 tests, 0 skipped) and the runner suite (47) pass. Scenarios 11–12 through TS-18/TS-19 (pass, against the built images on Podman) and by hand against `deploy/compose.yaml`: `/healthz` healthy, `/readyz` ready with all three checks, UI served, hub uid 1654 on a read-only root, no `ANTHROPIC_API_KEY` in the hub, no route out but the proxy, fetch route refuses `169.254.169.254` with a reason, `compose stop` exits 0 through the shutdown path. The Playwright surfaces (TS-16), which drive Scenarios 1, 3, 4, 5, 8 and 10 through the browser, pass on Node 22.23.2 (12 tests; installed under nvm, shadowed by a Node 18 default). **Not run:** a live-model walkthrough — no `ANTHROPIC_API_KEY` here, and the quickstart makes a live run optional.
  - **Divergences, recorded against their owners.** T108: a container on an `internal` network cannot publish a port, so compose needed an inbound relay (`ingress`, the `caddy` image) the task did not name. T109/Scenario 11: the SDK sends `GET /api/hello` to the gateway even with non-essential traffic off. The proxy answers it `404` and forwards nothing; quickstart updated. T059/T104: https retrieval through a forward proxy is `CONNECT`, which Kestrel cannot hand to an application, so the fetch route is addressed explicitly (`GET /fetch?url=`) and `UrlFetch` changed to match. Quickstart called the wiki "bare"; it is a working tree. ESLint (not a gate) fails before this branch: 1 error in `src/agentrun`, 7 in `frontend` (the Svelte parser is not set up for `lang="ts"`).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies — starts immediately.
- **Foundational (Phase 2)**: depends on Setup — **blocks all user stories**.
- **User Story 1 (Phase 3)**: depends on Foundational. No dependency on US2 or US3.
- **User Story 2 (Phase 4)**: depends on Foundational and on US1's commit path (T072) — there must be a commit to revert.
- **User Story 3 (Phase 5)**: depends on Foundational and on US1's dispatch path (T068, T070) — there must be a run to follow.
- **Phase 6**: depends on US1 for an end-to-end ingest to run inside the image; T110 depends on every gate test existing.

### Within User Story 1

- T057 (the instruction file) before T061 — the loader needs a file to load.
- T061 … T066 (loader, tools, guardrail, model adapter) before T067 (the runner entry point that wires them).
- T067 before T068 — the hub spawns a runner that exists.
- T068, T069 before T070 — the dispatcher drives a spawn and a limit.
- T070, T071 before T072, T073 — run-end outcomes decide commit versus reset.
- T072, T073, T074 before T075 — the endpoints serve commit and diff data.
- T075 before T077, T078, T079 — the surfaces consume the endpoints.
- T076 after T075 — signals are emitted from the production composition root that serves them.

### Within User Story 2

- T085, T086 before T087 before T088 before T089.

### Within User Story 3

- T096, T097, T098 before T099 before T100.

### Parallel Opportunities

- Setup: T003 … T011 all run in parallel after T001 and T002.
- Foundational: the seven domain-type tasks T013 … T019 are seven separate files and run together; T025/T026 (the double), T027 (protocol test), and T037/T038/T039 (architecture gates) are independent of them.
- User Story 1: **all seventeen test tasks T040 … T056 run in parallel** — they are separate files and all must fail before implementation begins. On the implementation side T058, T077, and T078 are independent files.
- User Story 2: T080 … T084 in parallel.
- User Story 3: T090 … T095 in parallel.
- Phase 6: T101/T102 in parallel; T107 alongside T106; T111 and T112 alongside T110.
- Across stories: once Phase 2 is done, US1 must land first, but US2 and US3 can then proceed in parallel with each other.

---

## Parallel Example: User Story 1

```bash
# Launch every User Story 1 test together — all seventeen must fail before implementation:
Task: "TS-01 empty/whitespace submission rejected in tests/ingest/SubmissionTests.cs"
Task: "TS-02 exactly one task, openable within 2s in tests/ingest/TaskCreationTests.cs"
Task: "TS-03 URL retrieval failure in tests/ingest/UrlRetrievalTests.cs"
Task: "TS-20 SSRF refusal in tests/ingest/UrlFetchPolicyTests.cs"
Task: "TS-04 source passed whole in tests/dispatch/SourcePassthroughTests.cs"
Task: "TS-05 exactly one run per task in tests/dispatch/OneRunPerTaskTests.cs"
Task: "TS-07 dispatch ordering in tests/dispatch/DispatchOrderingTests.cs"
Task: "TS-06 N-iteration loop in tests/agentrun/loop.test.ts"
Task: "TS-08 ordered tool-call record in tests/agentrun/toolcalls.test.ts"
Task: "TS-09 containment vs instruction and input in tests/architecture/ContainmentInstructionAndInputTests.cs"
Task: "TS-10 containment vs wiki content in tests/architecture/ContainmentWikiContentTests.cs"
Task: "TS-17 gate 1 in tests/architecture/system-prompt-construction.test.ts"
Task: "TS-11 exactly one commit in tests/wiki/CommitTests.cs"
Task: "TS-12 failure containment in tests/wiki/FailureContainmentTests.cs"
Task: "FR-016 no-change run in tests/wiki/NoChangeTests.cs"
Task: "TS-13 observability rows in tests/hub/ObservabilityTests.cs"
Task: "TS-16 submit-to-view flow in tests/surfaces/ingest.spec.ts"

# Then launch the independent implementation files together:
Task: "Submission validation in src/ingest/Submission.cs"
Task: "Submit surface in frontend/src/routes/+page.svelte"
Task: "Task list surface in frontend/src/routes/tasks/+page.svelte"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational — **blocks everything**.
3. Complete Phase 3: User Story 1.
4. **STOP and VALIDATE**: run quickstart Scenarios 1, 2, 5, 6, 7, 9, 10 — the whole product loop is already there.
5. Demo: a wiki that grows from submitted sources, and a task view an operator can read to judge whether the agent's judgment was good.

### Incremental Delivery

1. Setup + Foundational → the skeleton builds and both stores work.
2. Add User Story 1 → validate → **MVP**.
3. Add User Story 2 → validate quickstart Scenarios 3 and 4 → reversibility.
4. Add User Story 3 → validate quickstart Scenarios 8 and 12 → accountability across restarts.
5. Add Phase 6 → validate quickstart Scenarios 11 and 12 → the container and egress boundary.

### Parallel Team Strategy

1. Everyone completes Setup + Foundational together.
2. US1 lands first — it is the trunk both other stories branch from.
3. Then, in parallel: Developer A on User Story 2 (revert), Developer B on User Story 3 (queue, recovery, shutdown), Developer C on Phase 6 (egress proxy and container boundary).

---

## Notes

- **Test-first is not optional here** (constitution III.1). Every test task must fail before its implementation task is started.
- **The LLM is the only double** (III.2). Every other dependency in every test is real: real filesystem, real git repositories, real child processes, real HTTP hosting, real SQLite.
- **No test asserts model output** (III.4). Nothing in CI checks what the agent wrote, which page it named, or how many tool calls it made. SC-008 … SC-011 are carried by the observability rows in T055, T083, and T094, and validated by an operator reading the task view — not by an assertion.
- **[P] means different files with no incomplete dependency.**
- Commit after each task or logical group; stop at any checkpoint to validate a story independently.

---

## Phase 7: Convergence

- [X] T114 CRITICAL: emit `grimoire.hub.readiness` (`wikiRepo`, `stateDb`, `egress`) from the `/readyz` path in `src/hub/Operations.cs` and assert the emitted event through the production composition root in `tests/hub/` per Constitution IV.2 (missing)
- [X] T115 CRITICAL: move the `TcpClient` egress reachability probe out of `src/dispatch/Dispatcher.cs` and `src/hub/Operations.cs` into a single adapter, and extend `tests/architecture/AdapterConfinementTests.cs` to confine `System.Net.Sockets` per Constitution V.3 (contradicts)
- [X] T116 Fail a URL task with a recorded reason on every retrieval exception (including a body-read error or a cancelled request in `src/ingest/adapters/UrlFetch.cs`), and make dispatch and startup recovery fail — not silently skip — any queued URL task that has no retrieved text (`src/dispatch/Dispatcher.cs`), with a test for a hub stopped mid-retrieval, per FR-003, SC-006 (partial)
  - **Resolved differently.** URL retrieval moved into dispatch (with T124), so a queued URL task with no text is retrieved when it is dispatched, not failed: the next start after a hub stopped mid-retrieval completes it. Every retrieval error, a body-read error included, fails the task with a reason. `UrlRetrievalTests.AUrlTaskWhoseRetrievalAStoppedHubInterruptedIsNotStranded`.
- [X] T117 Record a failure reason identifying the source's byte length when a run cannot proceed because the source is too large, and replace the `201`-only assertion in `tests/dispatch/SourcePassthroughTests.cs` with a scripted-model "too large" response asserting the reason and no commit, per FR-029 (missing)
- [X] T118 Emit `grimoire.run.model_endpoint_unreachable` with the real `status` when the model route answers with a proxy or upstream error (not only when the TCP probe fails), and assert the event in TS-13, per plan IV / Constitution IV.2 (partial)
- [X] T119 Distinguish a runner crash from an elapsed-limit kill in `src/dispatch/Dispatcher.cs` `Interpret` (non-zero exit without `run_end` currently records the elapsed-limit reason) so each gets its own human-readable reason, per FR-018, FR-009 (contradicts)
- [X] T120 Enforce the tool-call ceiling during the run — refuse or abort in the runner's guard once `count` reaches `maxToolCalls`, since `maxTurns` does not bound parallel `tool_use` blocks — and test it with a script issuing parallel tool uses, per FR-009 (partial)
- [X] T121 Have `Interpret` produce `RunOutcome.ChangedNothing` for a successful run whose working tree is clean, and leave the tree equal to the tip after a commit (including git-ignored files a run wrote), with a test that writes a `.gitignore` plus a matching file, per FR-016, FR-017, plan VII (partial)
- [X] T122 Guard `SqliteStore` state writes so a finished run's record and a settled task cannot be overwritten (`FailTask` without a state check, `EndRun`/`AppendToolCall` without `outcome IS NULL`, `RecordRevert` without `completed`), closing the `GracefulShutdown` vs. dispatcher-settle race, with a store test, per FR-023, FR-020 (partial)
  - Guarded writes return whether they applied. A run that the gate refused before `StartRun` falls back to `FailTask`; a commit that finds its task already settled is logged critical.
- [X] T123 Make the TS-09 escape script target the per-test canary paths (read and mutate attempts), and assert no canary content appears in any later model request, per TS-09, Constitution II.5 (partial)
- [X] T124 Return the task from `POST /api/tasks` for a URL submission as soon as it is stored and retrieve the URL in the background, with a 2-second test for URL submissions, per SC-001 (partial)
- [X] T125 Assert each declared field (`instructionVersion`, `toolGrant`, `seq`, `tool`, `target`, `outcome`, `failureReason`, `toolCallCount`, `durationMs`, `commitSha`, `filesChanged`) on the emitted event for every US1 row in `tests/hub/ObservabilityTests.cs`, per TS-13, Constitution IV.2 (partial)
- [X] T126 Emit `grimoire.run.ended` exactly once per run with its declared fields, and rename the diagnostic "retrying"/"unrecorded" and exception-path lines in `src/dispatch/Dispatcher.cs` and `src/dispatch/RunQueue.cs` so they no longer reuse the declared name, per plan IV (contradicts)
- [X] T127 Extend `tests/hub/ContractDriftTests.cs` to diff the served component schemas (fields, `required`, enums, nullability) against `contracts/hub-api.openapi.yaml`, per TS-15, Constitution V.5 (partial)
- [X] T128 Add a surfaces case with a deterministically failing script asserting state `failed` and a visible `failure-reason`, replacing the `completed|failed` match in `tests/surfaces/states.spec.ts`, per TS-16, SC-006 (partial)
- [X] T129 Assert in the TS-06 escalation test that request k+1 carries the `tool_result` for call k, including its content, per SC-007, FR-007 (partial)
- [X] T130 Resolve the runner environment against `contracts/deployment.md`: stop passing the hub path `GRIMOIRE_INSTRUCTION` and the hub's full `PATH` in `src/dispatch/adapters/RunnerProcess.cs`, or amend the contract, per plan II (contradicts)
  - Resolved by amending the contract: `GRIMOIRE_INSTRUCTION` is listed as the one hub path the runner receives, and `PATH` is now minimal (the `node` directory, `/usr/bin`, `/bin`). `tests/dispatch/RunnerEnvironmentTests.cs`.
- [X] T131 Catch `readFile` errors in `src/agentrun/src/wiki-tools/readPage.ts` and record the call with outcome `failed` and a detail, so no tool call goes unrecorded, per FR-021 (partial)
- [X] T132 Apply the dispatcher's retry-and-critical-log containment to `store.RecordRevert` after a revert commit in `src/hub/Endpoints.cs`, so a revert commit is never left unaccounted for, per FR-025 (partial)
  - The retry and critical-log path has no automated test: nothing in a hermetic test can make SQLite fail on that write without a seam the code does not otherwise need.
- [X] T133 Test the grant-mismatch path with a stub runner reporting a wider grant: task failed, no model request, no commit, per FR-013, runner-protocol "tool_grant" (partial)
- [X] T134 Add TS-12 cases for a runner that crashes on its own and one that emits a completed `run_end` then exits non-zero, asserting the reason as well as the reset, per TS-12, FR-017 (partial)
- [X] T135 Add a test that ingests A and B, reverts B, and asserts A offers no revert and shows superseded, per edge case "revert immediately after a revert", FR-027 (missing)
- [X] T136 Add a TS-20 test where a public origin redirects to loopback or `169.254.169.254` and the task fails with no run, per TS-20, FR-003 (missing)
  - **Narrowed.** Every origin a test can run is on loopback, which both SSRF policies refuse at the first hop, so a public-then-private redirect cannot be built without a test-only hole in the policy. The test asserts the per-hop re-check instead — a redirect to `file:` fails the task with no run — and the private-address hop is the proxy's connect-step refusal, covered by `tests/egress/FetchRouteTests.cs`. The fetch-route fixture now forwards `Location`, as the real proxy does.
- [X] T137 Show the submitted URL alongside the retrieved text on the task view (`frontend/src/routes/tasks/[taskId]/+page.svelte`), per FR-021 (partial)
- [X] T138 Enforce that only `src/agentrun/src/model/` imports `query` from the SDK (a dependency-cruiser or lint rule inside gate 2), matching the departure CLAUDE.md records, per plan V (partial)
- [X] T139 Supersede ADR-0012 with a record stating that the credential provider is a class, not an interface (ADR-0012 says "the same interface"; `src/egress/CredentialProvider.cs` is a sealed class per V.4), in its own PR, per Constitution VI.2 (contradicts)
  - **Resolved differently.** Nothing on this branch stack is merged (PRs #1, #4 and #12 are open), and plan.md states that records drafted in the unmerged plan are revised rather than superseded. ADR-0012's sentence was corrected in place instead of opening a superseding record in a separate PR.
