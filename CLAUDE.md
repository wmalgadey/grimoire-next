# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Grimoire runs an LLM agent that maintains a Markdown wiki ([OKF 0.2](https://github.com/GoogleCloudPlatform/open-knowledge-format)); the user decides which sources go in and which questions get asked. Grimoire itself judges no wiki content — it dispatches runs, keeps an agent's tool surface narrow, and makes afterwards visible what a run did.

Read `README.md` for the idea and `docs/product.md` for what is actually built. Where they differ, `docs/product.md` is right.

## The documents that govern a change

This repository is spec-driven, and the rules are enforceable, not advisory. Before changing code, know which of these you are working under:

| Question | File |
| --- | --- |
| What gets built, what does not, which outcome is Now | `docs/product.md` (owner-written; an agent edits only an outcome's status and spec reference) |
| The rules (`I.`–`V.`, Governance) every change is held to | `.specify/memory/constitution.md` |
| The as-is behaviour, per capability, with requirement IDs | `docs/capabilities/*.md` |
| What is proven and by which tests | `docs/trace.md` (generated — never edit by hand) |
| Why the stack is what it is (`DEC-001`…`DEC-021`) | `docs/decisions.md` |
| What a PR is checked against (12 items) | `docs/review-checklist.md` |
| How the current feature came about | `specs/001-first-ingest/` — `spec.md`, `plan.md`, `tasks.md`, `research.md`, `contracts/`, `quickstart.md` |

Consequences worth internalising:

- Every requirement has an ID `<CAPABILITY>-NNN` (capabilities: INGEST, QUERY, LINT, WIKI, RUNS, ACCESS, GUARD, OPS). IDs are permanent once registered in `docs/capabilities/`, never renumbered or reused; retired ones move under "Retired" and keep their ID.
- Nothing is built without a consumer in the same feature (II.1). No option, abstraction, gate or placeholder "for later".
- An interface exists only at a port to something outside the process, or where two real implementations exist (II.4). That is why `RunReport` is delegates and `SubmissionBoard` is a plain class.
- Not tested (III.8): framework behaviour, argument parsing, dependency wiring, static config, the wording of instructions. Such a test is deleted, not fixed.
- The code comments carry the *reasons* (requirement IDs, `research.md` findings, DEC numbers). When changing behaviour, follow the citation before rewriting the code — most odd-looking constructions are load-bearing.
- Spec Kit skills (`/speckit-*`) drive the workflow: specify → plan → tasks → implement → converge. `tasks.md` has no converge task, but Governance 2 requires one run of `/speckit-converge` per feature, in the closing phase.

## Commands

Build (the whole tree; `Grimoire.slnx` is the solution):

```
dotnet build Grimoire.slnx
```

Tests — .NET 10 runs `dotnet test` through Microsoft.Testing.Platform (`global.json` plus `UseMicrosoftTestingPlatformRunner` in `tests/Directory.Build.props`); runner options go after `--`:

```
dotnet test tests/Grimoire.Fast.Tests -- --timeout 15s          # the default run; the time-budget gate
dotnet test tests/Grimoire.Contract.Tests -- --timeout 90s --filter-not-trait "requires=signin"
dotnet test tests/Grimoire.Contract.Tests -- --timeout 90s --filter-trait "requires=signin"   # needs a signed-in `claude`
dotnet test tests/Grimoire.E2E.Tests                            # real browser; no time budget
```

A single class or method (xunit v3 filters; wildcards allowed at either end):

```
dotnet test tests/Grimoire.Fast.Tests -- --filter-class "Grimoire.Fast.Tests.CeilingTests"
dotnet test tests/Grimoire.Fast.Tests -- --filter-method "Grimoire.Fast.Tests.CeilingTests.Run_EndsFailed_*"
```

The E2E suite needs its browser once, from the built output:

```
pwsh tests/Grimoire.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium
```

The `trace-check` gate and the trace document (both need the tree built; the tool finds the repository root by `Grimoire.slnx` and reads traits off the built assemblies):

```
dotnet run --project tools/Grimoire.Trace -- check              # every push: unknown/retired id, missing level, E2E without req
dotnet run --project tools/Grimoire.Trace -- check --complete   # adds: a `test` requirement with no test (PRs based on main)
dotnet run --project tools/Grimoire.Trace -- write              # regenerates docs/trace.md
```

Mutation measurement (Stryker, Fast suite only, via `Grimoire.Mutation.slnx`; a measurement, not a gate):

```
./scripts/mutation.sh
```

Run the hub by hand — `.env` at the root (copy `.env-example`), a real wiki, and a signed-in `claude` on `PATH`:

```
./scripts/run-hub.sh            # --fresh throws the configured wiki away first
dotnet run --project src/Grimoire.Hub -- --wiki <dir> --purpose <file> --model <pinned-id>
```

`GRIMOIRE_MODEL` / `--model` must be a pinned model id; aliases are refused (DEC-010). No `ANTHROPIC_API_KEY` — runs go through the owner's subscription sign-in (DEC-001), and the harness strips the variable from the child.

## Build and analysis settings you will hit

`Directory.Build.props` turns SDK analyzers up to `Recommended`, switches on `EnforceCodeStyleInBuild` and makes every warning an error. CA1502 (complexity) is an error at threshold 15 from `CodeMetricsConfig.txt` (DEC-007). `tests/.editorconfig` switches off CA1707 and CA1502 for the suites only. Central package versions live in `Directory.Packages.props` — add a `PackageVersion` there, a bare `PackageReference` in the project.

## Architecture

Three bounded contexts plus a composition root (`plan.md`, Structure Decision). Each context declares its own ports and owns its adapters; an external system appears only inside its adapter (V.2).

- **`src/Grimoire.Runs`** — what a submission and a run *are*. `SubmissionBoard` accepts or refuses a text (one run at a time, both start-up inputs present, non-empty); `Submission` holds one of four states behind the board's single lock; `Run` (`RunStateMachine.cs`) makes every judgment about a run — the nudge decision and the final verdict.
- **`src/Grimoire.Agent`** — the port to the agent (`IAgentHarness`, `AgentDispatch`, `RunReport`), the `ToolGrant` (five bare tool names) and the fixed `Ceilings` (15 min, 2 000 000 tokens). Adapters: `HarnessProcess` owns the `claude` child process and the two things written to its stdin; `AgentTranscript` is the only place that reads the CLI's NDJSON protocol and the MCP name prefix.
- **`src/Grimoire.Wiki`** — `IWikiStore` (list, read, write, append-log — deliberately no delete, move, revert or commit, because WIKI-003 leaves undo to the user's git history), `ProvenanceStamp` / `OkfFrontmatter` (the `generated` record is the only thing Grimoire writes into a page; the rest of the page comes back byte for byte). Adapter: `FileSystemWikiStore`, the only code that touches the filesystem, and it refuses paths that leave the wiki.
- **`src/Grimoire.Hub`** — the only project that knows all three. `Program.cs` reads the arguments and puts the two real adapters at their ports; `HubApplication.Build` is what every suite builds too, with in-memory adapters (III.9). It serves the static page from `wwwroot/` (no bundler, DEC-019), `POST`/`GET /api/submissions`, and the five wiki tools over MCP at `/mcp/runs/{runId}` — unauthenticated, loopback only (DEC-014). `InstructionLoader` is the *only* thing that puts text into the agent's prompt (V.1); `SubmissionIntake` accepts and dispatches without the user waiting; `RunConductor` holds what happens while a run is under way.
- **`tools/Grimoire.Trace`** — the `trace-check` gate and the `write` command. Reads requirement IDs from `docs/capabilities/`, and `level`/`req` traits off built assemblies via `MetadataLoadContext` (DEC-005). Fails on what it cannot read rather than skipping it.

### The run lifecycle

`SubmissionIntake.SubmitAsync` → `RunConductor.Begin` (grant and ceilings recorded, elapsed timer armed) → `HarnessProcess.DispatchAsync` spawns `claude` with the argv of `contracts/agent-cli-protocol.md` and returns while the run continues. Then, through `RunReport`: `system/init` must report *exactly* the grant, the wiki server connected and an interrupt capability, or the run ends failed before its first model call (GUARD-001); streamed usage raises the running cost, and either ceiling sends one interrupt with a process kill behind it (GUARD-004, DEC-015/016); a `result` makes the nudge decision (RUNS-005 — `log.md` is read for the run's GUID and nothing else in the wiki is read at all), never the verdict. **A run ends at its process's exit**, where result, log entry and exit code are read together. The harness decides nothing; the hub decides everything.

## Test conventions

`tests/README.md` is the full statement; the essentials:

- Every test **class** carries `[Trait("level", "fast"|"contract"|"e2e"|"deploy")]`. Missing or misspelt fails the gate.
- A test proving a requirement carries `[Trait("req", "<CAPABILITY>-NNN")]` (repeatable). E2E and Deploy must; Fast and Contract may. Tests that prove a principle rather than a requirement carry none.
- Class is `<Subject>Tests` where the subject is the spec's vocabulary; method is `<Action>_<Result>[_<Scenario>]`, scenario starting with When/While/With/Without/After. No implementation names, no status codes, no `Works`/`Succeeds`. A name needing `And` is two tests.
- Doubles are in-memory adapters at owned ports (`InMemoryAgentHarness`, `InMemoryWikiStore`, `DrivableHarness`) — never generated mocks of our own types. Time comes from `TimeProvider`, with `FakeTimeProvider` in the Fast suite (DEC-018); the Fast suite has 15 s total, so no test waits for real time.
- Contract tests that drive the real signed-in CLI carry `[Trait("requires", "signin")]` and are excluded in CI; there are at most three (DEC-021).

## Branching and PRs

One feature branch off `main`; each phase of `tasks.md` is a branch off the feature branch, merged back when its PR is reviewed and green, before the next phase starts. No PR based on another open PR, PRs stay drafts until their phase is complete, and nothing of a feature reaches `main` before the whole feature is done (I.9, I.10). The instruction under `instructions/` is Grimoire's own: changing it is an owner decision and must be named in the PR description.

<!-- SPECKIT START -->
<!-- SPECKIT END -->
