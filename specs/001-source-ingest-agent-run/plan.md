# Implementation Plan: Source Ingest via Agent Run

**Branch**: `001-source-ingest-agent-run` | **Date**: 2026-09-16 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-source-ingest-agent-run/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

A user submits pasted text or a URL on the web frontend; the hub creates exactly one task, retrieves URL content, and dispatches exactly one agent run. The run is a real tool-use loop: a Node child process driven by the Anthropic **Claude Agent SDK for TypeScript**, given a system prompt composed solely from the versioned ingest instruction and granted exactly two in-process MCP tools — wiki read and wiki write — with every built-in tool removed. The agent writes into the wiki repository's working tree; nothing enters the wiki until the hub commits the whole run as one commit. A failed, crashed, aborted, or limit-hit run resets that working tree and commits nothing, on the run-end path and again at startup, so no partial write is ever reachable through any user-facing surface. The task view shows state, source, instruction-file version, tool grant, ordered tool calls, commit and diff, and offers revert exactly when the task's commit is the wiki tip.

**The hub and the agent harness are built cloud native from the start**: all configuration from environment variables, structured logs to stdout, liveness and readiness endpoints, graceful shutdown on `SIGTERM`, all durable state on mounted volumes, non-root and read-only root filesystem, one replica. They ship as a container image with a compose deployment. No process in the deployment holds **an upstream model credential and makes no direct outbound connection**: `ANTHROPIC_BASE_URL` and the hub's URL-fetch client both point at a single **egress proxy**, which is the only route out of the hub container and the custodian of whatever upstream credential is in use. That seam is what lets a proxy holding Claude Code auth be dropped in later without touching harness code.

The hub — the orchestrator — is **C# 14 on .NET 10 (LTS)** with ASP.NET Core Minimal APIs; the operator surfaces are **Svelte/SvelteKit**; the agent harness runs the loop on the **TypeScript Claude Agent SDK**.

## Technical Context

**Language/Version**: **C# 14 on .NET 10 (LTS)** for the hub; TypeScript 5.x on Node.js 22 LTS for the agent runner and the frontend build.

**Primary Dependencies**:

- Hub: ASP.NET Core Minimal APIs, `Microsoft.AspNetCore.OpenApi`, `Microsoft.Data.Sqlite`, `Microsoft.Extensions.Diagnostics.HealthChecks`
- Egress proxy: ASP.NET Core + **YARP** (`Yarp.ReverseProxy`), same runtime as the hub
- Runner: `@anthropic-ai/claude-agent-sdk` (`query`, `createSdkMcpServer`, `tool`, `PreToolUse` hook), `zod`
- Frontend: SvelteKit (Svelte 5) with `@sveltejs/adapter-static`; `openapi-typescript` for the generated contract client
- External binaries shelled out to: `git`, `node`

**Storage**:

- Wiki content and history: **one ordinary git repository** on a mounted volume (`GRIMOIRE_WIKI_REPO`)
- Operational state: a **SQLite** file on a mounted volume (`GRIMOIRE_STATE_DB`) — local block storage, never a network filesystem

**Testing**: xUnit + `Microsoft.AspNetCore.Mvc.Testing` hosting real Kestrel on port 0 (hub), NetArchTest (architecture gates), Vitest with a real spawned runner process (runner), Playwright against the built SvelteKit app (surfaces), and container tests running the built image against a deny-all network (deployment). The LLM is doubled by a scripted Anthropic-compatible HTTP server (`tests/scripted-model/`) reached through `ANTHROPIC_BASE_URL` — the same variable the egress proxy occupies in production; every other dependency is real.

**Target Platform**: Linux containers, `linux/amd64` and `linux/arm64`. One `grimoire-hub` image (hub + agent harness + built frontend assets), deployed as a **single replica** alongside an egress proxy. Runs equally as plain processes on a developer machine.

**Constraints**: At most one run executing at a time; single replica, not horizontally scalable (one run at a time, SQLite, one wiki working tree); no authentication; no live updates; no maximum source size; wiki history append-only; the agent has no network and no filesystem reach beyond its worktree; the hub container has no network egress except the proxy.

**Scale/Scope**: One operator, a wiki of ~10³ pages, one concurrent run, tasks retained indefinitely, three user-facing surfaces (submit, task list, task view) plus two operations endpoints.

## Constitution Check

*GATE: Every box is checked or its row appears in Complexity Tracking with a
justification. Must pass before Phase 0 research. Re-check after Phase 1 design.*

**Post-Phase-1 re-evaluation**: re-run after the design artifacts were written, and again after the cloud-native direction was folded in. All boxes remain checked; no row moved to Complexity Tracking. Two decisions pulled in different directions and are recorded rather than smoothed over: where the model port's double is swapped (ADR-0004, noted under V) and whether the runner gets its own container today (ADR-0007, noted under II).

### I. Judgment in Instructions, Control in Code

- [x] Every behaviour in the spec is classified as judgment (instruction file)
      or control (hub or agent-harness code); the classification is listed below.
- [x] No hub or agent-harness code in this plan branches on wiki content or embeds prompt text.
- [x] Instruction files touched: `src/instructions/ingest.md` (created by this feature).
      Expected behaviour change in operator-observable terms: with this file present,
      a dispatched run reads existing wiki pages and writes the pages it judges
      warranted, and the **task view** shows that instruction version beside the
      resulting diff. There is no prior version to compare against; this is the
      baseline revision every later Instruction Change Workflow edit is measured from.

| Behaviour | Judgment / Control | Lives in |
|-----------|--------------------|----------|
| Accept pasted-text or URL submission; reject empty/whitespace-only | Control | `src/ingest/` |
| Retrieve URL content through the egress proxy, attach retrieved text | Control | `src/ingest/` (HTTP adapter) |
| Pass the source whole — no size limit, no truncation | Control | `src/ingest/`, `src/dispatch/` |
| Create exactly one task per accepted submission | Control | `src/tasks/` |
| Dispatch exactly one agent run per task | Control | `src/dispatch/` |
| Run the agent as an iterative tool-use loop until it stops or hits the limit | Control | `src/agentrun/` (Claude Agent SDK loop) |
| Which tools exist and which are granted | Control | `src/agentrun/wiki-tools/`, recorded by `src/dispatch/` |
| Refuse calls outside the granted set or outside the wiki | Control | `src/agentrun/wiki-tools/` (`PreToolUse` hook + path containment) |
| Record instruction-file version and granted tool set at dispatch | Control | `src/dispatch/` → `src/tasks/` |
| Record every tool call, in order, with target and outcome | Control | `src/agentrun/run/` → `src/tasks/` |
| Commit a run's changes as exactly one commit at run end | Control | `src/wiki/` |
| Leave the wiki at the pre-run commit on failure/abort/crash | Control | `src/wiki/` (working tree reset, nothing committed) |
| Task states and transitions; openability in every state | Control | `src/tasks/` |
| List every task newest first with its state | Control | `src/tasks/`, `frontend/src/routes/tasks/` |
| Show state, source, instruction version, tool calls, diff | Control | `src/hub/`, `frontend/src/routes/tasks/[taskId]/` |
| Offer revert only on the wiki tip; restore as a new commit; mark reverted | Control | `src/wiki/`, `src/tasks/` |
| Serialise runs to one at a time, in submission order | Control | `src/dispatch/` |
| Fail interrupted tasks and resume the queue after restart or `SIGTERM` | Control | `src/dispatch/` |
| Whether the source warrants one page or several | Judgment | `src/instructions/ingest.md` |
| Whether to create a new page or update an existing one | Judgment | `src/instructions/ingest.md` |
| What a page is named and where it sits | Judgment | `src/instructions/ingest.md` |
| What a good page contains, how it is worded and structured | Judgment | `src/instructions/ingest.md` |
| Which links to add, and to what | Judgment | `src/instructions/ingest.md` |
| Which existing pages to read before deciding | Judgment | `src/instructions/ingest.md` |
| Whether to leave the wiki unchanged | Judgment | `src/instructions/ingest.md` |
| How much of the source to reflect, and what to discard | Judgment | `src/instructions/ingest.md` |
| Whether and how to reorganise neighbouring content | Judgment | `src/instructions/ingest.md` |
| The commit message wording for the run's changes | Judgment | `src/instructions/ingest.md` (agent's final message text is taken verbatim as the commit message) |

The system prompt is the ingest instruction file and nothing else. `src/agentrun/instruction/` is the only module that reads instruction files and the only one that can construct the `SystemPrompt` branded type the model port accepts; an architecture test asserts no other module constructs it (quality gate 1). No `systemPrompt` string literal exists anywhere else in the repository, and the SDK's `claude_code` preset is deliberately not used — the run gets a custom prompt, so nothing outside the instruction file reaches the model as instruction. Container images and deployment manifests carry configuration only; no instruction text is baked into an image layer.

### II. Reversibility and Containment

- [x] Every wiki mutation this feature introduces goes through the single commit
      path; no new filesystem write to the wiki. The agent writes only inside a
      wiki repository's working tree, and only `src/wiki/` commits — once, at run end.
      A run that does not reach a successful commit has that working tree reset, on the
      run-end path and again at startup. Revert is a second commit through the same path.
      Containerisation changes where those paths are mounted, not what they are.
- [x] Tool grants: the spec's grant (FR-010) is **two tools** — `mcp__wiki__read_page` and
      `mcp__wiki__write_page` — both reversible, their only effect a file write that is
      either committed once or reset away. It is expressed and enforced by the mechanism in
      **ADR-0009**: in-process MCP tools, every built-in named in `disallowedTools` so its
      definition never reaches the model, a `PreToolUse` hook recording and deciding every
      call, and `realpath` containment in the handler. *Which* tools are granted is spec;
      *how* a grant is enforced is the ADR.
- [x] Trust-boundary impact: **this feature establishes the boundary** (there was none before),
      and **ADR-0007** makes it a container boundary. The hub container runs as a non-root user
      in a container with a read-only root filesystem, writable only on its mounted
      volumes and a `tmpfs`, and with **no network egress except the egress proxy**.
      The process boundary is kept *inside* it: a dedicated child process per run, `cwd`
      pinned to the wiki working tree, `env` fully replaced with an allowlist. **ADR-0010**
      adds the network half: no process in the deployment holds an upstream model credential, reaches the
      model only through `ANTHROPIC_BASE_URL`, fetches user-submitted URLs only through
      the same proxy, and sets `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1` so the SDK
      raises no connection the policy has to refuse. The CI containment tests are
      TS-09 and TS-10 (process and tool boundary, run everywhere) plus **TS-18**
      (container boundary and egress deny, runs against the built image).

> **Stated rather than smoothed over** (ADR-0007): hub and runner share one container, so
> the runner's restriction to the model endpoint is enforced by its configuration and by
> its having no network tool, not by a per-container network policy. Splitting the runner
> into its own container with its own network namespace is the named successor, and the
> runner protocol is already shaped to survive that move.

### III. Testing

- [x] Every control contract in this plan has a hermetic test against real
      infrastructure, named in Test Strategy below (TS-01 … TS-20).
- [x] The LLM is the only double. The scripted responses this feature needs are
      listed per row: read-then-write, write-only, read-only, no-op, N-iteration
      escalation for SC-007, escape attempts for the containment rows, and a
      never-stopping script for the run limit. The double is an Anthropic-compatible
      HTTP server the runner reaches through `ANTHROPIC_BASE_URL` — the same variable
      the proxy occupies in production — so the real SDK agent loop executes in every
      test (**ADR-0004**, unchanged by this revision).
- [x] No planned test asserts model output; judgment criteria from the spec
      (SC-008 … SC-011) are carried as observability rows in IV.

### IV. Observability

| Signal | Kind (metric / log / span) | Operator decision it supports | Surface |
|--------|----------------------------|-------------------------------|---------|
| `grimoire.task.created` | log | Did my submission become exactly one task? (SC-001) | Task list |
| `grimoire.task.state_changed` | log | Where is this task now; is anything stuck? (SC-006) | Task list, task view — state field |
| `grimoire.run.dispatched` (`instructionVersion`, `toolGrant`) | log | Which instruction revision is this behaviour attributable to; was the grant deny-by-default? (SC-004, SC-009) | Task view — instruction version + granted tool set |
| `grimoire.run.tool_call` (`seq`, `tool`, `target`, `outcome`) | log | Did the run consult the wiki before writing; what did it touch; what was refused? (SC-007, SC-010) | Task view — ordered tool-call record |
| `grimoire.run.ended` (`outcome`, `failureReason`, `toolCallCount`, `durationMs`) | log | Did it finish, fail, or hit the limit — and is the limit too tight? (SC-011) | Task view — state and failure reason |
| `grimoire.run.model_endpoint_unreachable` (`endpoint`, `status`) | log | Is the run failing on its own account, or because the egress path is broken? | Task view — failure reason; `/readyz` |
| `grimoire.wiki.committed` (`commitSha`, `filesChanged`) | log | Were the wiki changes appropriate? (SC-002, SC-008) | Task view — commit identity and diff |
| `grimoire.wiki.revert_eligibility` (`eligible`, `reason`) | log | Why is revert not offered here? (SC-005, FR-027) | Task view — revert action or "superseded by a later commit" |
| `grimoire.wiki.reverted` (`revertCommitSha`) | log | Did my undo actually land? (SC-005) | Task view — revert commit identity, state `reverted` |
| `grimoire.dispatch.recovered_on_startup` (`failedTaskIds`, `requeuedTaskIds`) | log | Did the restart leave anything stranded? (FR-028, SC-006) | Task view — failure reason naming the interruption; task list |
| `grimoire.dispatch.interrupted_on_shutdown` (`taskId`) | log | Did the rollout/restart kill a run, and which one? | Task view — failure reason naming the interruption |
| `grimoire.hub.readiness` (`wikiRepo`, `stateDb`, `egress`) | log | Should this replica be taking traffic? | `/readyz` response body |

- [x] Every row has a surface that actually shows it: a field the frontend renders, or
      the readiness endpoint's body. No signal is exported to a sink nobody reads.
- [x] Each judgment criterion from the spec (constitution III.5) maps to at least
      one row: SC-008 → `grimoire.wiki.committed` + `grimoire.run.tool_call`;
      SC-009 → `grimoire.run.dispatched`; SC-010 → `grimoire.run.tool_call`;
      SC-011 → `grimoire.run.ended` + `grimoire.run.tool_call`.
- [x] Each row has a test through the production composition root in Test Strategy
      (TS-13), which boots `src/hub/` exactly as production does, drives a real run,
      and asserts both the emitted event and the field on the response the surface renders.

> Log transport is structured JSON on stdout — the container convention — and the proxy's
> own access log is **undeclared diagnostic logging** (IV.4's explicit exemption), joinable
> to a task because the runner sends `ANTHROPIC_CUSTOM_HEADERS: X-Grimoire-Run: <runId>`.
> No OpenTelemetry exporter is added: a declared signal must not be added without a surface
> (IV.4), and there is no dashboard to be that surface yet. It becomes the natural successor
> the moment there is one.

### V. Architecture

- [x] Slices touched or created: `ingest`, `tasks`, `dispatch`, `wiki`, `agentrun`,
      `instructions`, `hub` — all created by this feature. No technical-layer directory
      at first level; `hub` is the composition root and HTTP surface, named after the
      product's own domain term. `deploy/` holds image and compose definitions and is not
      a source slice — it contains no domain code.
- [x] External systems touched: the **LLM** (model port, `src/agentrun/model/`,
      doubled in tests); **git** (adapter-contained in `src/wiki/`, CLI child process);
      **SQLite** (adapter-contained in `src/tasks/`); **HTTP fetch of a submitted URL**
      (adapter-contained in `src/ingest/`, routed through the egress proxy); **process
      spawning** (adapter-contained in `src/dispatch/` and `src/wiki/`). Model port only
      where doubled; everything else adapter-contained, no new port. Architecture tests
      assert `Microsoft.Data.Sqlite` appears only under `src/tasks/`,
      `System.Diagnostics.Process` only under `src/dispatch/` and `src/wiki/`,
      `HttpClient` only under `src/ingest/`, and `@anthropic-ai/claude-agent-sdk` only
      under `src/agentrun/model/` (gate 2).
- [x] No new interface with a single implementation and no external system behind it.
      The model port is the single port, and the external system behind it — the LLM —
      is genuinely replaced in tests. It is replaced at the wire rather than by a second
      adapter class, so the SDK's own agent loop stays under test; the trade-off is
      recorded in **ADR-0004**. The egress proxy is **not** a port: it is deployment
      topology behind an environment variable, with no in-repo interface at all.
- [x] API contract change: the frontend↔hub contract is **created** by this feature as
      `contracts/hub-api.openapi.yaml`, committed here and mirrored at `contracts/` in
      the repository root. The SvelteKit client's types are generated from that file at
      build time and the hub's served OpenAPI document is diffed against it in CI (TS-15),
      so a contract change cannot happen without appearing in the PR diff (**ADR-0008**).
      The two operations endpoints (`/healthz`, `/readyz`) are in the same document under
      an `Operations` tag, because the drift test covers the whole served surface. The
      runner↔hub process protocol, the wiki tool schemas, and the deployment environment
      contract are internal and are documented in `contracts/` for review,
      not exposed to users.

### VI. ADRs

- [x] Decision kinds in this plan: **technology choice** (hub runtime, frontend
      framework, runner runtime, state store, wiki repository layout, contract mechanism,
      cloud-native runtime contract, egress proxy), **external port** (the model port and its double),
      **security boundary** (host trust boundary, tool-grant enforcement, model egress and credential
      custody).
- [x] For each: ADR drafted and included in this plan's PR —
      ADR-0001 hub runtime and HTTP surface;
      ADR-0002 frontend framework;
      ADR-0003 agent runner runtime and SDK (external port);
      ADR-0004 LLM test-double mechanism behind the model port;
      ADR-0005 wiki repository layout and mutation path;
      ADR-0006 operational state store;
      ADR-0007 host trust boundary — container, with a per-run process boundary inside it;
      ADR-0008 frontend↔hub contract mechanism;
      ADR-0009 how agent tool grants are expressed and enforced (security boundary);
      ADR-0010 model egress and credential custody (security boundary);
      ADR-0011 cloud-native runtime contract (technology choice);
      ADR-0012 egress proxy implementation — YARP (technology choice).
      The index at `docs/adr/index.md` carries every record with its status.
- [x] Nothing else in this plan gets an ADR. The stdin `proceed` handshake, the
      instruction-version hash format, the NDJSON event shapes, the SQLite schema, the
      SvelteKit static adapter, the SSRF guard on URL retrieval, and the choice of
      xUnit/Vitest/Playwright are elaborations of the decisions above.

> None of these records is superseded. VI.2's no-edit-in-place rule protects **accepted** decisions;
> every record here is still being drafted in this unmerged plan, so a revision is a revision. The
> boundary record (ADR-0007) was revised in place when the direction changed from processes to
> containers, rather than being split into a decision and its immediate supersession — a chain for a
> decision that was never accepted is history that did not happen.
>
> No ADR here records *what an agent may do*. That is stated by the spec that requires it — FR-010
> and FR-011 for this feature — and instantiates ADR-0009's mechanism. A later feature adding a
> query or lint agent writes a spec, not a record in `docs/adr/`. Governance's "agent autonomy
> change" route covers altering an existing agent's reach on its own, outside a feature.
>
> ADR-0010 and ADR-0012 are split deliberately: the egress boundary should survive swapping the
> proxy, so "all egress goes through one credential-holding proxy" and "that proxy is YARP" are two
> decision aspects and get two records (VI.1).

### VII. Simplicity

- [x] No framework wrapper, no speculative extension point or toggle. Minimal API
      endpoints call slice code directly; the SDK's `query()` is called at the point of
      use; `git` and `sqlite` are invoked through their own APIs inside their adapters;
      health checks use the framework's own registration rather than a hand-rolled
      endpoint. Configuration is read from environment variables at the composition root,
      with no settings abstraction over it.
- [x] One representation per domain concept. `Task`, `AgentRun`, `ToolCall`,
      `ToolGrant`, `Source`, `WikiCommit`, `RevertRecord` each exist once in C#; the
      runner's NDJSON event shapes are the port-boundary translation of the same concepts
      across a process boundary, not a parallel model. The frontend's types are
      **generated** from the committed contract, never hand-written.
- [x] Methods expected to exceed the complexity threshold: none. The one method that
      could drift is the dispatch loop's run-end handling (commit vs no-change vs
      failure); it is written as three named outcome handlers over a single `RunOutcome`
      value from the start.

> The proxy is built here in its **minimal** form (ADR-0012): two routes, a static upstream
> credential, a single-upstream allowlist, and the SSRF policy — enough that the deny-all network
> posture is real rather than aspirational and TS-18 asserts something. Credential refresh, usage
> accounting, per-run quotas and upstream failover are deferred; they are swaps behind
> `CredentialProvider` and additions to one pipeline, not reopenings of the application. The calling
> side is unchanged either way: it takes an endpoint and an opaque token from configuration and
> connects to nothing else.

## Test Strategy

| ID | Contract | Real infrastructure used | Scripted LLM responses | Test location |
|----|----------|--------------------------|------------------------|---------------|
| TS-01 | Empty/whitespace submission is rejected, no task created (FR-001) | Kestrel, SQLite | none | `tests/ingest/` |
| TS-02 | Accepted submission creates exactly one task, openable immediately (FR-002, SC-001) | Kestrel, SQLite | none | `tests/ingest/` |
| TS-03 | URL retrieval failure fails the task with a reason and dispatches no run; wiki untouched (FR-003) | Kestrel, a second real HTTP listener returning 500 / non-text, bare git repo | none | `tests/ingest/` |
| TS-04 | Source is passed whole, unmodified and untruncated, to the run (FR-029) | Kestrel, real runner process, real git repo | echo-length script asserting received bytes | `tests/dispatch/` |
| TS-05 | Exactly one run per task, never a second (FR-005) | Kestrel, SQLite, real runner process | single read-then-write | `tests/dispatch/` |
| TS-06 | The loop feeds every tool result back: N tool calls across N iterations in one run (FR-007, FR-008, SC-007) | real runner process, real SDK loop, scripted-model HTTP server, real git repo | N-iteration escalation, N = 1…8 | `tests/agentrun/` |
| TS-07 | Instruction version and tool grant are recorded before the first model call (FR-013, FR-014) | Kestrel, SQLite, real runner process, scripted-model server | read-then-write | `tests/dispatch/` |
| TS-08 | Every tool call recorded in order with target and outcome, refusals included (FR-021) | real runner process, real git repo, SQLite | read, write, write-outside-wiki, non-granted-tool attempt | `tests/agentrun/`, `tests/tasks/` |
| TS-09 | **Containment, gate 4**: adversarial *instruction* and *task input* cannot produce an effective tool outside the granted pair (II.5, FR-010) | real runner process, real filesystem, canary files outside the wiki repository | Bash, Read, WebFetch, Task, unknown MCP tool attempts | `tests/architecture/` |
| TS-10 | **Containment, gate 4**: adversarial *wiki page content* and traversal/symlink targets cannot escape the wiki repository (FR-011) | real runner process, real filesystem, symlink + `../` + absolute-path canaries | write scripts targeting `../`, `/etc/…`, a symlink out | `tests/architecture/` |
| TS-11 | **Revertibility, gate 5**: one commit per changed run; revert restores byte-identical content as a new commit leaving history intact (FR-015, FR-025, SC-002, SC-005) | real git repo, Kestrel | write script | `tests/wiki/` |
| TS-12 | Failure/abort/crash/limit leaves the wiki byte-identical to the pre-run commit with no commit, whether cleaned at run end or at startup (FR-009, FR-016, FR-017, SC-003) | real git repo, real runner killed mid-write, hub killed mid-run, real clock | write-then-hang; never-stopping script | `tests/wiki/`, `tests/dispatch/` |
| TS-13 | **Observability, gate 6**: every signal in IV is emitted through the production composition root and appears on its named surface | `src/hub/` booted as in production, Kestrel, SQLite, real runner, real git | read-then-write, no-op, failing | `tests/hub/` |
| TS-14 | Serialisation, submission order, and startup recovery: running→failed with an interruption reason, queued dispatched in order, never re-run (FR-019, FR-028, SC-006) | Kestrel, SQLite across two hub instances, real runner killed with the hub | slow read-then-write | `tests/dispatch/` |
| TS-15 | Served OpenAPI document equals the committed `contracts/hub-api.openapi.yaml`; generated frontend types build against it (V.5) | Kestrel, `openapi-typescript`, real build | none | `tests/hub/` |
| TS-16 | Surfaces: submit → task list → task view → revert, and a task opens in each of the five states (FR-020, FR-030, SC-006) | Playwright against the built SvelteKit app, real hub, real git, real runner | read-then-write, no-op, failing, hanging | `tests/surfaces/` |
| TS-17 | **Architecture, gates 1–3**: `SystemPrompt` constructed only in `src/agentrun/instruction/`; external libraries confined to their adapters; first-level directories are domain slices | NetArchTest over built assemblies, `dependency-cruiser` over runner and frontend sources | none | `tests/architecture/` |
| TS-18 | **Container boundary, gate 4 (network half)**: the built image runs as non-root with a read-only root filesystem; a run completes with **no egress except the configured endpoint**; the runner process environment contains no upstream credential beyond the injected token; a blocked host produces a recorded failure, not a hang | the built `grimoire-hub` image, a deny-all network with one allowed endpoint, real git volume, real SQLite volume | read-then-write, plus a script whose upstream is blocked | `tests/deployment/` |
| TS-19 | Graceful shutdown: on `SIGTERM` the hub stops dispatching, terminates the running runner, marks that task `failed` with an interruption reason, and exits non-violently; the wiki is at the pre-run commit and queued tasks survive to the next start (FR-017, FR-028) | real hub process, real signal, SQLite across the restart, real git | slow read-then-write | `tests/deployment/` |
| TS-20 | URL retrieval refuses non-`http(s)` schemes and loopback/private/link-local targets, records the refusal as the task's failure reason, and dispatches no run (FR-003) | Kestrel, a listener bound to loopback, real DNS resolution | none | `tests/ingest/` |
| TS-21 | The proxy strips the caller's internal token and injects the upstream credential on the model route; forwards no credential on the fetch route; refuses a non-allowlisted upstream and an SSRF target (ADR-0010, ADR-0012) | real Kestrel proxy, a real upstream listener asserting received headers, a listener bound to loopback | none | `tests/egress/` |

TS-18 and TS-19 need a container runtime; they run in CI on every PR and skip locally with a named
reason, which is a developer convenience, not a disabled gate — the gate runs on the PR.

Every row can fail because of a change to our own code. The single wire-up test permitted by III.6
is TS-15's assertion that the hub's OpenAPI registration is present.

## Project Structure

### Documentation (this feature)

```text
specs/001-source-ingest-agent-run/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output: the committed API contract change (V.5)
│   ├── hub-api.openapi.yaml    # frontend ↔ hub + operations endpoints
│   ├── runner-protocol.md      # hub ↔ agent runner NDJSON process protocol (internal)
│   ├── wiki-tools.md           # the two granted MCP tool schemas (internal)
│   └── deployment.md           # container, environment, volume and egress contract (internal)
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── ingest/                       # Grimoire.Ingest — submission, validation, source retrieval
│   ├── Submission.cs             # accept/reject, whitespace rule (FR-001)
│   ├── Source.cs                 # pasted text or URL + retrieved text (FR-004, FR-029)
│   └── adapters/UrlFetch.cs      # the only HttpClient; proxy-routed, SSRF-guarded (FR-003)
├── tasks/                        # Grimoire.Tasks — the inspectable artifact
│   ├── Task.cs, TaskState.cs     # five states and their transitions (FR-020)
│   ├── AgentRun.cs               # instruction version, grant, outcome, commit (FR-023)
│   ├── ToolCall.cs, ToolGrant.cs # ordered record incl. refusals (FR-021)
│   ├── RevertRecord.cs           # revert commit identity (FR-025)
│   └── adapters/SqliteStore.cs   # the only Microsoft.Data.Sqlite reference
├── dispatch/                     # Grimoire.Dispatch — queue, lifecycle, recovery
│   ├── RunQueue.cs               # one at a time, submission order (FR-019)
│   ├── RunLimit.cs               # elapsed + tool-call ceiling (FR-009)
│   ├── StartupRecovery.cs        # running→failed, requeue, never retry (FR-028)
│   ├── GracefulShutdown.cs       # SIGTERM: drain, terminate runner, fail the task
│   └── adapters/RunnerProcess.cs # spawns src/agentrun, reads NDJSON events
├── wiki/                         # Grimoire.Wiki — the single mutation path
│   ├── WikiCommit.cs             # one commit per changed run (FR-015)
│   ├── Revert.cs                 # eligibility + restoring commit (FR-024…FR-027)
│   ├── Diff.cs                   # per-file added/changed/removed (FR-022)
│   └── adapters/GitCli.cs        # the only git invocation; commit, reset, revert, diff
├── agentrun/                     # TypeScript agent runner (Node child process)
│   └── src/
│       ├── instruction/          # THE instruction loader; sole constructor of SystemPrompt (I.2)
│       ├── model/                # the model port + Claude Agent SDK adapter (V.2)
│       ├── wiki-tools/           # the two granted MCP tools + PreToolUse guardrail
│       └── run/                  # NDJSON event protocol to the hub, proceed handshake
├── instructions/
│   └── ingest.md                 # the versioned ingest instruction (I.1) — all judgment
├── egress/                       # Grimoire.Egress — YARP proxy: the only route out (ADR-0012)
│   ├── ModelRoute.cs             # strips the internal token, injects upstream credential
│   ├── FetchRoute.cs             # IHttpForwarder to a per-request destination, SSRF-checked
│   └── CredentialProvider.cs     # static token today; a refreshing impl is a swap
└── hub/                          # Grimoire.Hub — composition root + Minimal API surface
    ├── Program.cs                # endpoint map, DI, OpenAPI, health checks, env config
    ├── Endpoints.cs              # POST /api/tasks, GET /api/tasks, GET/POST .../{id}[/revert]
    └── Operations.cs             # /healthz, /readyz

contracts/
└── hub-api.openapi.yaml          # committed contract; frontend types generated from it (V.5)

frontend/
└── src/routes/
    ├── +page.svelte              # surface: submit (FR-001)
    ├── tasks/+page.svelte        # surface: task list (FR-030)
    └── tasks/[taskId]/+page.svelte  # surface: task view (FR-021, FR-022, FR-024)

deploy/                           # not a domain slice: no application code lives here
├── hub.Dockerfile            # multi-stage: .NET 10 + Node 22 + built frontend assets
├── egress.Dockerfile             # the src/egress proxy image
└── compose.yaml                  # hub on an `internal: true` network + proxy + volumes

tests/
├── ingest/                       # TS-01 … TS-03, TS-20
├── tasks/                        # TS-08
├── dispatch/                     # TS-04, TS-05, TS-07, TS-12, TS-14
├── wiki/                         # TS-11, TS-12
├── agentrun/                     # TS-06, TS-08
├── hub/                          # TS-13, TS-15
├── surfaces/                     # TS-16 (Playwright)
├── egress/                       # TS-21 (credential injection, SSRF policy)
├── deployment/                   # TS-18, TS-19 (built image, signals, egress policy)
├── architecture/                 # TS-09, TS-10, TS-17 — constitution quality gates 1–5
└── scripted-model/               # the single LLM double, spawned by both test suites

docs/adr/                         # ADR-0001 … ADR-0011 + index.md, merged with this plan (VI.6)
```

**Structure Decision**: Seven first-level slices under `src/` — `ingest`, `tasks`, `dispatch`,
`wiki`, `agentrun`, `instructions`, `hub` — each named after a domain concept from the spec's Key
Entities and the constitution's own vocabulary. Each C# slice is its own project so that adapter
confinement (V.3) is enforced by project references as well as by the architecture test.
`src/agentrun/` is an npm package built to `dist/` and spawned by
`src/dispatch/adapters/RunnerProcess.cs`; it holds the model port, the instruction loader, and the
tool guardrail, because that is where the SDK and therefore the LLM actually live.
`src/instructions/` holds the only judgment in the system. `deploy/` holds the image and compose
definitions and deliberately contains no domain code, so the first-level-slice gate stays honest.
`tests/` mirrors the slices rather than test kinds, with `tests/architecture/` carrying constitution
quality gates 1–5, `tests/deployment/` carrying the container half of gate 4, and
`tests/scripted-model/` holding the one sanctioned double.

## Complexity Tracking

> **Fill ONLY if Constitution Check has unchecked boxes that must be justified**

No unchecked boxes. Nothing to justify.
