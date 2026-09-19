# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repository is

Grimoire is a **hub** with a web frontend that dispatches **LLM agents** which maintain a **markdown wiki in git**, exposing every operation as an inspectable **task artifact**.

Feature `001-source-ingest-agent-run` is built through Phase 6 of `specs/001-source-ingest-agent-run/tasks.md`: ingest (US1), revert (US2), queue/recovery/`SIGTERM` (US3), and the deployment — the egress proxy in `src/egress/`, the two images and the compose file in `deploy/`, and the CI gates. Branches stack: `001-source-ingest-agent-run` → `001-revert` → `001-egress`.

Vocabulary is used precisely (see `docs/adr/index.md`):
- **Hub** — the orchestrator. Owns the HTTP surface, dispatch, task artifacts, the wiki repository, the operational store, observability. It supervises agent processes; it does not run agents.
- **Agent harness** — the runtime that runs an agent: drives the loop, composes the system prompt from instruction files, enforces the tool grant.
- **Grant** — the set of actions an agent may take. Deny-by-default, recorded on the artifact before the agent acts.

## Commands

`Makefile` wraps everything below — `make build`, `make test`, `make test-cs`, `make test-hub
ARGS='--filter-method "*Readyz*"'`, `make run`, `make dev-setup`, `make help`. It encodes the build
order the tests depend on and runs the C# test applications one at a time, so a single suite can
take filters. The raw commands are kept here because they are what CI runs and what a target that
misbehaves has to be checked against.

### Build

```bash
dotnet build
npm --prefix src/agentrun ci && npm --prefix src/agentrun run build   # emits dist/main.js
npm --prefix tests/scripted-model ci && npm --prefix tests/scripted-model run build
npm --prefix frontend ci && npm --prefix frontend run build           # needs Node 20.19+
npm --prefix tests/surfaces ci                                         # Playwright for the surfaces suite
```

The runner build must be current before any C# suite that drives a run: the hub spawns `src/agentrun/dist/main.js`, not the TypeScript sources. Same for the scripted model — the C# fixtures spawn `tests/scripted-model/dist/server.js`.

### Test

`dotnet test` runs every C# suite and is what CI runs. It works only because two Microsoft.Testing.Platform opt-ins are both wired: `test.runner` in `global.json` and `UseMicrosoftTestingPlatformRunner` in `tests/Directory.Build.props`. Remove either and it reports `Zero tests ran`; if CI's C# test count ever drops to zero, look there first. For one suite or a filter, run the built test application directly:

```bash
dotnet build && dotnet test --no-build                 # every C# suite, as CI
./tests/wiki/bin/Debug/net10.0/Grimoire.Tests.Wiki            # one suite
./tests/tasks/bin/Debug/net10.0/Grimoire.Tests.Tasks --filter-class "*SqliteStoreTests*"
./tests/hub/bin/Debug/net10.0/Grimoire.Tests.Hub --filter-method "*Readyz*"

npm --prefix src/agentrun test                                 # runner + gate 1, real SDK loop
npx --prefix src/agentrun vitest run tests/agentrun/loop.test.ts --root .   # one file
npm --prefix frontend run test:e2e                             # Playwright, needs the built frontend
```

Suites that boot a hub take minutes — they spawn real processes and move real bytes. Run them in the background rather than waiting.

Every automated test runs without an Anthropic key: the LLM is reached through `ANTHROPIC_BASE_URL` pointed at `tests/scripted-model/`.

### Run

```bash
GRIMOIRE_WIKI_REPO=/path/to/wiki GRIMOIRE_STATE_DB=/path/to/grimoire.db \
GRIMOIRE_MODEL_BASE_URL=http://127.0.0.1:8787 GRIMOIRE_MODEL_TOKEN=token \
  dotnet run --project src/hub
```

Configuration is environment variables only, read once at the composition root, and a missing required one fails startup loudly. The full table is `specs/001-source-ingest-agent-run/contracts/deployment.md`.

### Spec-driven development

Spec Kit skills, invoked as `/speckit-<name>`: `specify`, `clarify`, `plan`, `tasks`, `analyze`, `checklist`, `implement`, `converge`, `constitution`, `taskstoissues`, plus `git-*` and `agent-context-update` extensions. Hooks in `.specify/extensions.yml` fire around these phases; `auto_commit` defaults to `false`, so they prompt rather than commit silently.

## Environment facts that bite

- **The default Node here is 18; the project targets 22, which is installed under nvm.** The runner suite and scripted model happen to work on 18. `vite build` (so all of `frontend/` and `tests/surfaces/`) and `dependency-cruiser` (gate 2's TypeScript half) need 20.19+. Put Node 22 first on `PATH` (`$HOME/.nvm/versions/node/v22.*/bin`, after `unset -f node npm npx nvm`, since the shell's lazy-nvm functions otherwise shadow it and can hang a background command) and `make test-e2e` runs.
- **`Grimoire.Tasks.Task` collides with `System.Threading.Tasks.Task`.** The artifact is called Task because that is the product's word for it. Async files that touch it carry `using Task = System.Threading.Tasks.Task;` and spell the artifact `Grimoire.Tasks.Task` in full. Test projects get that alias globally from `tests/Directory.Build.props`.
- **TS-18 (`tests/deployment/ContainerBoundaryTests.cs`) builds and runs the real images.** It needs a container runtime (`docker`, or `GRIMOIRE_CONTAINER_CLI`; Podman works) and skips with the reason when there is none — except under `CI`, where it fails. The first run builds `deploy/hub.Dockerfile`, which takes minutes. The SDK sends `/api/hello` to `ANTHROPIC_BASE_URL` even with non-essential traffic off; the proxy answers it and forwards nothing.
- **Test hubs get their configuration per host, never through the process environment.** Production reads `HubConfiguration.FromEnvironment()` — the process environment and nothing else, deliberately not `builder.Configuration`, which would also honour command-line arguments (`WebApplicationFactory` passes `UseSetting` values that way) and settings files. It is a DI singleton, so `tests/support/GrimoireHub.cs` registers each host's own through `ConfigureTestServices`, parsed by the same `HubConfiguration.Read`. That is what lets the C# suites run their tests in parallel; a test that calls `Environment.SetEnvironmentVariable` for a hub brings back the failures that move between runs. Parallelism is capped at half the cores per suite by the one `tests/xunit.runner.json`: `dotnet test` runs every suite at once, and uncapped, runs slowed past the suites' timeouts.

## Architecture

Seven first-level domain slices under `src/` plus the egress proxy — **no technical-layer directories at the first level**:

| Slice | Role |
|---|---|
| `src/ingest/` | Submission validation, URL retrieval (the only `HttpClient`) |
| `src/tasks/` | The task artifact and its states; the only `Microsoft.Data.Sqlite` |
| `src/dispatch/` | Run queue (one at a time), run limits, startup recovery, `SIGTERM` drain, runner process spawn |
| `src/wiki/` | The single wiki mutation path: commit, reset, revert, diff; the only `git` invocation |
| `src/agentrun/` | TypeScript runner (Node child process): instruction loader, model port + Claude Agent SDK adapter, the two granted MCP tools, NDJSON protocol to the hub |
| `src/instructions/` | Versioned instruction files — the only judgment in the system |
| `src/egress/` | YARP proxy: the only route out; holds the upstream credential |
| `src/hub/` | Composition root, Minimal API endpoints, `/healthz`, `/readyz` |

Hub and proxy are **C# 14 / .NET 10** on Minimal APIs; the runner is **TypeScript on Node 22** with `@anthropic-ai/claude-agent-sdk`; the frontend is **SvelteKit / Svelte 5** built static and served by the hub on one origin. Wiki content and history live in a **git repository** on a volume; operational state in **SQLite**. `frontend/` ↔ hub go through the committed `contracts/hub-api.openapi.yaml`, from which the frontend's types are generated. `tests/` mirrors the slices, not test kinds.

### Five mechanisms that span files

These are the things you cannot see from any single file, and the things most likely to be broken by a well-meaning edit.

**The `proceed` handshake orders the artifact before the model.** The runner emits `instruction_loaded` and `tool_grant`, then *blocks* on stdin. `Dispatcher` persists both, then sends `proceed`. Without the block, "recorded before the first model call" (FR-013, FR-014) would be a hope about scheduling; with it, `tests/dispatch/DispatchOrderingTests.cs` compares the persisted timestamps against the scripted model's first request and the ordering is a property. Don't make the gate asynchronous.

**Containment is four overlapping mechanisms, and two of them exist only to make it observable.** (1) Every built-in is named in `disallowedTools` so its definition never reaches the model. (2) The model port checks every `tool_use` block it sees, because a tool removed from the request is rejected by the SDK as *unknown* before any permission step — so without this the most hostile attempts would be the ones leaving no trace. (3) A `PreToolUse` hook decides and records anything that does reach a permission step. (4) `realpath` containment in each handler refuses targets outside the repository. A refusal is a recorded tool call, never an absence.

The `disallowedTools` list rots — the SDK grows tools between releases, and eleven of them were reaching the model before anyone noticed. `tests/agentrun/toolcalls.test.ts` therefore asserts the *request* carries exactly the granted pair, so the list rotting fails a build.

**Run end is one value with three named handlers** (`RunOutcome` → `RunOutcomeHandler`): changed, changed-nothing, failed. Commit, reset, reset. This is the one method in the system that would otherwise accumulate conditionals until nobody could tell which combinations were reachable. Keep it three handlers.

**The SSRF policy lives in two places, and the in-process half steps aside when a proxy is configured.** With `GRIMOIRE_FETCH_PROXY` set the hub requests `GET {GRIMOIRE_FETCH_PROXY}?url=…` from the egress proxy and never resolves the submitted host, so checking in-process would refuse every destination a container can reach. The proxy enforces instead, in its connect step (`src/egress/DestinationPolicy.cs`, ADR-0010), and its refusal reason comes back in `X-Grimoire-Egress-Reason`. The route is addressed explicitly, not used as a forward proxy, because https through a forward proxy is `CONNECT` and Kestrel does not hand a tunnel to an application. Both policies refuse loopback, so `tests/ingest/UrlRetrievalTests.cs` routes through `tests/support/FetchRouteFixture.cs` — the same protocol, no policy — while `UrlFetchPolicyTests.cs` and `tests/egress/FetchRouteTests.cs` exercise the two policies.

**The scripted model picks its turn from the conversation, never from a request counter.** The SDK legitimately sends the same request twice — a streaming attempt and a non-streaming fallback, or a retry — and a counter hands each a different turn, silently skipping steps so the loop under test is not the loop that ran. `turnFor()` counts assistant messages in the request. The double also speaks SSE when asked to; answering a `stream: true` request with plain JSON is what caused the duplicate in the first place.

## Non-negotiable rules

`.specify/memory/constitution.md` is authoritative and self-contained; read it before planning or implementing. The rules most likely to be violated by accident:

- **Judgment lives in instruction files, control lives in code.** No prompt text, no wiki-content branching, no hard-coded content rules in hub or harness code. Exactly one module (`src/agentrun/src/instruction/`) composes the system prompt and is the sole constructor of the `SystemPrompt` branded type; `tests/architecture/system-prompt-construction.test.ts` enforces it. No `systemPrompt` string literal anywhere else, and the SDK's `claude_code` preset is deliberately unused.
- **Every wiki mutation is a revertible git commit** through `src/wiki/`. One commit per run, at run end; a failed/crashed/aborted run resets the working tree and commits nothing. No filesystem write to the wiki outside that path.
- **Tools are deny-by-default** and the granted set is recorded on the artifact. Adding a tool, widening a grant, or relaxing the boundary is an *agent autonomy change*: it needs its own ADR and must not be bundled with anything else.
- **Test-first, classicist, real infrastructure** — real filesystem, git repositories, child processes, HTTP hosting. The LLM is the *only* sanctioned double, replaced at the wire. Never write a test asserting what the model produced, and never let a build fail because model output changed.
- **Observability is the control loop, not diagnostics.** Every declared signal needs a test through the production composition root and a named user-facing surface. Never add a declared signal without a surface, or claim a surface that doesn't show it.
- **Ports only for external systems that are doubled or have multiple adapters** — today, the model port alone. Filesystem, git, clock, process spawning, and network get adapter confinement instead. Never introduce an interface with one implementation and nothing external behind it. Never wrap a framework.
- **One model per concept.** No parallel representation of the same domain concept; translation only at a port boundary.
- **ADRs only for four kinds**: technology choices, external ports, security boundaries, agent autonomy changes. One decision aspect each; fixed heading order (Title, Status, Context, Decision, Consequences, Alternatives Considered); merged in the same PR as the plan that decides. An accepted ADR is **superseded whole, never edited in place**, and **ADRs never cite one another** — state the fact depended on, not the record. Add the row to `docs/adr/index.md` in the same PR. Capability and scope belong in specs, not ADRs.
- **Quality gates must not be disabled to land a change**; fixing a wrong gate is a separate PR.
- **Amendments are never retroactive** — pre-amendment artifacts are not reworked to satisfy a new rule.

When two principles conflict, resolve in precedence order: II (reversibility/containment) > I (judgment/control) > III (testing) > V (architecture) > VII (simplicity).

### Where the built code departs from plan.md

Two decisions were taken during implementation that a reader of `plan.md` alone would get wrong:

- **Gate 2's TypeScript rule names `query()`, not the SDK package.** `plan.md` V says `@anthropic-ai/claude-agent-sdk` may be imported only under `src/agentrun/src/model/`, but `createSdkMcpServer`/`tool` are how the grant is *declared*, which is `src/wiki-tools/`'s subject (ADR-0009). Routing them through the model port would mean wrapping a framework. `.dependency-cruiser.cjs` permits the SDK under `model/` and `wiki-tools/`, and forbids `wiki-tools/ → model/`; `tests/architecture/model-call-confinement.test.ts` confines the `query` import itself to `model/`.
- **Health checks are mapped as ordinary Minimal API routes**, not `MapHealthChecks`, because health-check endpoints carry no OpenAPI description and the contract drift test covers the whole served surface. The checks themselves are still the framework's own registrations run through `HealthCheckService`.

## Change routes

- *Feature* → specify → plan → tasks → implement. No phase skipped; implementation never begins before its tasks exist.
- *Bug* → assess → fix → test.
- *Instruction-file change* → the Instruction Change Workflow: cite the observed behaviour by task-artifact id or surface+signal, state the expected change in operator-observable terms, name the surface, add no CI assertion over model output. If the desired behaviour is deterministic it is harness control and goes through the feature route instead.
- *Agent autonomy change* → ADR, alone in its PR.

Feature branches are `NNN-slug` (sequential), created by `speckit-git-feature`; the active feature directory is recorded in `.specify/feature.json`. Every PR names the principles it touches.
