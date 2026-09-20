# Implementation Plan: The First Ingest

**Branch**: `001-first-ingest` | **Date**: 2026-09-20 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-first-ingest/spec.md`

## Summary

A user pastes a text in the browser; Grimoire accepts it — unless a run is already in progress, in
which case it refuses — and dispatches one agent run with the instruction, the purpose description,
the text and the run's identifier. The agent works it into the wiki through a narrow set of tools the
hub serves. Grimoire writes exactly one thing into the wiki — who generated each page and when — and
reads exactly one thing back out of it, the run's log entry, which decides whether the run ended
done. This is the skeleton feature: it also stands up the stack, both gates, and the capability
files.

**Outcome advanced**: OUT-01 — submit a text in the browser and afterwards find new, linked pages
including a source page in the wiki

**Slice addition**: **a new operation** — ingest. The browser door and the agent process arrive with
it because nothing can exercise the operation without them. *(Constitution I.6 — the skeleton
feature is exempt from "never two", not from the budget.)*

## Technology decisions *(mandatory)*

`docs/decisions.md` was read first. **DEC-001** is in force and is not re-decided here: models are
reached with the owner's subscription sign-in, per-token API billing is not acceptable for this
project, and the fallback is an API-key adapter behind the same port. Every row below works inside
it. Rationale in full: [research.md](research.md).

All five ★ items are decided here because this is the first feature (Constitution II.6).

| Decision | Choice | Reason | Binds later features | Departs from |
| --- | --- | --- | --- | --- |
| ★ Stack | .NET 10 (ASP.NET Core) for the hub, the tools and all three test suites. No TypeScript, no `npm`, no bundler | Every requirement proven by `test` lands in the hub under the tool-ownership row below, so the hub's language is where the tests are. The two things that had required a second toolchain are gone: the SDK package (R-11) and the front-end build (R-10). A toolchain with no requirement behind it is a mechanism with no consumer (II.1) | yes | none |
| ★ How a test carries its level | xunit v3 `[Trait("level", "fast\|contract\|e2e\|deploy")]` | Native to the runner every suite uses, and readable from assembly metadata without running anything (R-05) | yes | none |
| ★ How a test carries its requirement ID | xunit v3 `[Trait("req", "<CAPABILITY>-NNN")]` | Same mechanism, same reader; repeatable for a test proving more than one requirement (R-05) | yes | none |
| ★ How `trace-check` reads the level and the requirement ID off a test | `System.Reflection.MetadataLoadContext` over the built test assemblies — metadata only, no execution, no runner | IV.3 demands one deterministic check that writes nothing. xunit v3's `-list json` is not exposed through the Microsoft.Testing.Platform entry point that .NET 10's `dotnet test` uses, so binding the gate to a runner flag would tie it to a configuration detail (R-05) | yes | none |
| ★ How `time-budget` is enforced and fails the run | Microsoft.Testing.Platform's own `--timeout`: `15s` on Fast, `90s` on Contract | III.7 asks for the simplest means the stack offers. `--timeout` is a first-class platform option — "A global test execution timeout", format `<value>[h\|m\|s]` — it measures the test session rather than the build, and it fails the run (R-06) | yes | none |
| How the model is reached | The `claude` CLI in headless mode, spawned per run as a child process of the hub by `HarnessProcess`, speaking newline-delimited JSON over stdin and stdout | DEC-001 requires the owner's subscription sign-in and rules out API-token billing; the CLI is the sign-in path (`apiKeySource: "none"` observed, no API key in the environment). The evidence in R-11 shows it meets all five needs the harness had to meet, and the TypeScript SDK is itself a wrapper that spawns this same binary | yes | none — works inside DEC-001 |
| Which model, and on whose credentials | `--model` with a **pinned model id**, never an alias and never the default; `ANTHROPIC_API_KEY` removed from the child process's environment | A run is reproducible and its cost attributable only if the model is fixed: an alias follows whatever it is pointed at, and the default follows the owner's own `model` setting — a machine setting of exactly the kind `--setting-sources ""` exists to keep out. Observed: with a pinned id and the key unset, `modelUsage` came back keyed by that id and `apiKeySource` was `none`, so the run was on the subscription (DEC-001) and on the model asked for. Leaving the key set would bill per token through an API key, which DEC-001 rules out (R-11) | yes | none |
| Deny-by-default tools (GUARD-001) | `--tools ""` plus `--mcp-config` + `--strict-mcp-config`, with `--allowed-tools "mcp__wiki__*"` and `--permission-mode dontAsk` | Evidence: with these flags `system/init` reported `"tools":["mcp__wiki__append_log"]` — the whole tool surface is the grant. An allow-list of names is not a grant: the CLI reference says of `--allowedTools`, "To restrict which tools are available, use `--tools` instead". This is deny-by-default by construction, not an enumeration of built-ins that rots (R-03, R-11) | yes | none |
| No settings picked up from the machine | `--setting-sources ""`, `--strict-mcp-config`, and a working directory Grimoire owns rather than the wiki. **Not** `--bare` | V.1 allows only the instruction and the purpose description into the agent's prompt; the documentation states that without such flags `claude -p` "loads the same context an interactive session would, including anything configured in the working directory", and a probe confirmed a `CLAUDE.md` leaking in without the flag and staying out with it. `--bare` would also close it but "doesn't use your subscription login", which DEC-001 forbids (R-11) | yes | none |
| Where the wiki tools live | The hub serves them over MCP (streamable HTTP, `ModelContextProtocol.AspNetCore`) at a per-run endpoint; the CLI is pointed at it | Puts provenance stamping, the grant, the grant record and both ceilings in one language where Fast tests prove them, and leaves `trace-check` one test format to read (R-02) | yes | none |
| The per-run tool endpoint is unauthenticated | `/mcp/runs/{runId}`, bound to loopback, no token | `docs/product.md` §2 puts Grimoire inside a network the user trusts and gives it no access control of its own. A per-run token would be half an access-control story with no consumer (II.1); the run identifier in the path is addressing, not authorisation. The first feature that puts Grimoire on an untrusted network (OUT-10, OUT-12) must revisit this | yes | none |
| The two ceilings, and what cost means | Elapsed time against `TimeProvider`; cost as **every token the run causes** — the four token fields of every entry in the `result` message's `modelUsage`, all models and the CLI's own background calls included — counted live off `message_delta` and reconciled at the `result`. **At either ceiling the hub sends the same `interrupt`**, which stops a call in flight. Initial values 2 000 000 tokens and 15 minutes, revised by the owner after the acceptance run | GUARD-004 counts model tokens, and a background call the CLI makes is the run's doing: a probe's `modelUsage` carried a Haiku entry the run never asked for beside its Opus one. One mechanism for both ceilings because the agent loops model call → tool call → model call inside a turn, so nothing can prevent the next call without ending the one in flight — GUARD-004 was reworded to say so. `--max-budget-usd` is currency from a client-side estimate the documentation says can differ from the bill, and `--max-turns` is the wrong quantity (R-04, R-11) | yes | none |
| Stopping a run | `control_request` / `interrupt` on stdin, with killing the process as the backstop | GUARD-004 requires the run to be stopped at once at either ceiling, a model call in flight included. Observed: an interrupt ended a response in flight in ~0.9 s with `terminal_reason: "aborted_streaming"`, leaving the process able to serve a further message; SIGTERM is documented to leave the turn unfinished, so it is the backstop, not the mechanism (R-11) | yes | none |
| The nudge (RUNS-005) | A second user message written on the CLI's stdin under `--input-format stream-json` | RUNS-005 needs the agent told once and allowed to continue *inside the same run*. Observed: a second message after the first `result` continued the same `session_id`, and the agent's answer referred to its own earlier turn (R-11) | yes | none |
| Where submissions and their states live | A plain object in the RUNS context, in memory. No storage port, no adapter, no database | RUNS-004 moved to the follow-up feature with the split, and it was the requirement that needed a store. An interface over an in-memory dictionary has neither a second implementation nor an outside system behind it (II.4), and the store itself would have no consumer here (II.1). The port arrives with RUNS-004 (R-08) | no — the follow-up feature chooses the storage technology | none |
| Elapsed time in tests | `TimeProvider`, with `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) in the Fast suite | The Fast budget is 15 s for the whole suite (III.7), and GUARD-004's ceiling is the one requirement that would otherwise make a test wait for real seconds. `TimeProvider` is the framework's own abstraction, so no interface of ours is created for it (II.4) and the provider itself is not tested (III.8) (R-12) | yes | none |
| Front end | One static HTML page and one script, served by the hub from `wwwroot/`. No Vite, no `npm`, no build step | The browser surface is one form (ACCESS-001) and one list of states (ACCESS-002); a bundler for two files is a mechanism with no consumer (II.1). Static content is not tested (III.8), and both requirements are browser-observable, so the E2E suite proves them (R-10) | yes | none |
| E2E driver | `Microsoft.Playwright.Xunit.v3` — a real browser against the running hub | Keeps every test in one format, so `trace-check` has one reader rather than two (R-05) | yes | none |
| How a feature's PR stack is built | The `gh stack` extension of the GitHub CLI: `gh stack init` once, `gh stack submit` per push | I.10 requires a stack whose mapping is named up front and not deepened afterwards. `gh stack` keeps every PR's base correct as branches are rebased, and links them into a stack GitHub renders, so a reviewer sees the order instead of reconstructing it from base branches. Hand-rolled `gh pr create` chains drift the moment a branch below is amended | yes | none |
| The Contract suite for the agent adapter | The real `claude` CLI with the owner's real sign-in, at most three tests, run locally before the PR and excluded from CI by `[Trait("requires", "signin")]` + `--filter-not-trait "requires=signin"` | III.4 puts a Contract suite against the real external thing, and the real external thing here is the CLI. CI has no subscription sign-in, so it cannot run them; a scripted endpoint would be a component we write and maintain that makes none of R-11's findings more true (II.1, R-09) | yes | none |

A reason names the constraint or evidence. "Owner decision" is not a reason — the owner's constraint
is named instead.

**Binding decisions are merged into `docs/decisions.md` when the feature closes** (Constitution II.6,
review-checklist item 4), each with its reason, taking the next free `DEC-NNN`. Every row marked
`yes` above goes in. The one row marked `no` stays here, where the follow-up plan will find it.

**Test time budget** *(Constitution III.7, gate `time-budget`)*: Fast under 15 s, Contract under
90 s, execution only, measured in CI. The default test run executes Fast only and fails when Fast
exceeds its budget; the Contract run fails when it exceeds its own.
Enforced by: Microsoft.Testing.Platform `--timeout 15s` / `--timeout 90s`. No purpose-built tooling.
The three sign-in Contract tests run under the same `--timeout 90s` locally; see Complexity Tracking
for what that costs.

## Technical Context

**Storage**: none of our own. Submissions and their states live in memory in the RUNS context. The
wiki is plain files in a git repository Grimoire reads and writes but never commits.

**Target Platform**: self-hosted single instance, started by the owner on a trusted network. Not
packaged and not deployed — that is OUT-10.

**Project Type**: service with a browser front end, driving the `claude` CLI as a child process.

**Constraints**: two fixed ceilings per run, elapsed time and model tokens (GUARD-004). One run at a
time, enforced by refusing (INGEST-005). Grimoire writes one thing into the wiki and reads one thing
out of it. OKF 0.2 as pinned, and only the six parts research.md R-07 names. The `claude` CLI must be
installed and signed in — it is the external system this feature integrates with.

**Scale/Scope**: one user, one wiki, one run at a time, a few submissions a week
(`docs/product.md` §2).

## Constitution Check *(mandatory)*

Completed before design; re-checked after Phase 1.

| Principle | Touched? | How this plan satisfies it / why it is not touched |
| --- | --- | --- |
| I. Purpose and Focus | touched | One outcome (OUT-01), no blocking open question, one vertical slice adding one operation. I.7 is met: three stories and ~36 tasks after the owner's split (see Budget and split decision). I.8: only the six OKF parts R-07 names. I.9 and I.10: the stack is named under PR stack before implementation continues, six branches for six phases, and only `001-first-ingest` reaches main, at close. |
| II. Simplicity | touched | Both gates are built here because II.2 establishes them and this is the first feature that could violate their rules; each is shown failing once. Nothing is built for later: no storage port (II.4 — no second implementation and no outside system), no TypeScript harness, no bundler, no second test runner, no token on the per-run endpoint. `trace-check` is mandated by IV.3, not purpose-built measurement (II.3). Interfaces sit only at ports to something outside the process: the wiki filesystem and the `claude` CLI (II.4). Deployment, hardening and network containment are untouched — OUT-10 and OUT-04 own them (II.5). |
| III. Testing | touched | Fifteen `test` requirements and one `review`. Levels, budgets and the four-rule gate are decided above; each test sits at the lowest level that can prove its requirement, which `/speckit-tasks` records per task (III.6). The instruction's wording is not tested (III.8) — WIKI-001 is proven by review-checklist item 3; neither is the static front-end content, whose requirements are proven at E2E. GUARD-001 and GUARD-002 are proven twice: Fast in the hub, and Contract against the real CLI, because the deny configuration is a decision we made rather than wiring. Doubles are in-memory adapters at owned ports only (III.9): `IWikiStore` and `IAgentHarness`. No evals: the spec commits to none. **Caveat**: three Contract tests need the owner's sign-in and cannot run in CI, so their execution time is not measured there — Complexity Tracking. |
| IV. Visibility | touched | Requirement IDs are capability-scoped. They are registered in `docs/capabilities/` **before the first test is written** and reconciled at close as added, changed or removed (IV.2). `trace-check` and the `docs/trace.md` writer are both built here; the check writes nothing and is code, never an agent's answer (IV.3). The three status places and no fourth (IV.4). Every behaviour the spec commits to carries an ID again now that INGEST-005 exists (IV.6). |
| V. Design Invariants | touched | V.1: only the instruction file and the user's purpose description put text into the agent's prompt — `--setting-sources ""` and a working directory Grimoire owns keep the machine's `CLAUDE.md`, settings and hooks out — and the only thing Grimoire writes into the wiki is `generated: { by, at }`. V.2: each context owns its adapters and the tree below makes that visible — the `claude` process only in `HarnessProcess`, the filesystem only in `FileSystemWikiStore`. V.3: the grant is the tool surface itself (`--tools ""` plus the hub's MCP server), deny-by-default by construction, recorded with every run (GUARD-003), and checked against what the agent reports before its first model call — a run that reports any tool outside the grant ends failed there (GUARD-001). |
| Governance | not touched | No amendment is needed — no rule blocked this plan. `/speckit-converge` runs later in the feature. |

## Budget and split decision *(mandatory)*

**User stories**: 3 of 3 · **Estimated tasks**: ~36 of ~40

**Within budget?** **Yes → proceed to `/speckit-tasks`.**

**Split** *(applied)*: This feature keeps submission, the run, the instruction, the generation
record, the log entry with its nudge, the four states, the tool grant, both ceilings and both gates.
**RUNS-002, RUNS-003, RUNS-004 and ACCESS-003 — the queue, its acknowledgement gate and restart
behaviour — move to a follow-up feature still advancing OUT-01.** The cut runs along "one run at a
time versus many waiting and surviving a stop", so each half still has a user-observable result:
this half is text in, wiki out.

The behaviour the split exposed now carries an ID: a submission made during a run is refused
(INGEST-005), and the spec's lifecycle answers were updated with it. The open item the earlier draft
carried is closed.

**Where the estimate went**: ~13 tasks are the skeleton itself — solution and CI layout,
`trace-check`, the `docs/trace.md` writer, `time-budget`, a shown-failing run of each gate, and the
capability files registered up front. The earlier estimate of ~45–50 came down by roughly a dozen
tasks that no longer exist: RUNS-004's store and its Contract suite, the acknowledgement endpoint and
its states, the TypeScript harness and its toolchain, the front-end build, and the scripted Anthropic
endpoint.

| Group | Tasks |
| --- | --- |
| Skeleton: solution, CI, `trace-check` + shown failing, `docs/trace.md` writer, `time-budget` + shown failing, capability files registered | 7 |
| WIKI: port, OKF frontmatter, provenance stamp, filesystem adapter, its Contract suite, WIKI-003 | 6 |
| RUNS: submission and run model, in-memory state object, state machine, RUNS-005 with its nudge | 4 |
| GUARD: port, grant and its record, ceilings on `TimeProvider`, `HarnessProcess` (spawn, NDJSON, usage, nudge, interrupt) | 6 |
| GUARD Contract against the real CLI | 3 |
| Hub: MCP server and the five tools, instruction loader, `POST`/`GET` submissions, dispatch | 6 |
| ACCESS: the static page, two E2E scenarios | 3 |
| Close: reconcile capability files, `docs/trace.md`, `docs/product.md` status, acceptance run | 1 |

## PR stack *(mandatory)*

Named here before implementation continues, and not deepened afterwards (Constitution I.10). Only
`001-first-ingest` merges to `main`, and only when the feature is done (I.9) — every task complete,
both gates green, `docs/trace.md` regenerated and the owner's acceptance run behind it.

| Branch | Phases of `tasks.md` | Based on | Merges into |
| --- | --- | --- | --- |
| `001-first-ingest` | none — the spec, plan and tasks themselves | `main` | `main`, at close only |
| `001-first-ingest-phase-1-setup` | 1 Setup (T001–T003) | `001-first-ingest` | `001-first-ingest` |
| `001-first-ingest-phase-2-foundation` | 2 Foundational (T004–T007) | phase 1 | phase 1 |
| `001-first-ingest-phase-3-submit` | 3 User Story 1 (T008–T017) | phase 2 | phase 2 |
| `001-first-ingest-phase-4-wiki` | 4 User Story 2 (T018–T034) | phase 3 | phase 3 |
| `001-first-ingest-phase-5-states` | 5 User Story 3 (T035–T036) | phase 4 | phase 4 |
| `001-first-ingest-phase-6-close` | 6 Closing the feature (T037–T042) | phase 5 | phase 5 |

**Depth**: 6 of at most 6 · **Phases sharing a branch**: none — this feature uses all six, which is
what I.7's budget allows and no more.

Every PR in the stack is a draft until its phase is complete, and leaves the build and the test
suites green on its own branch. A step that cannot pass yet belongs in the PR that makes it pass:
T003's `trace-check` step is in phase 2 with the tool that runs it, not in phase 1 where the task
is listed.

**How the stack is built**: the `gh stack` extension — `gh stack init --base main <bottom> … <top>`
once, then `gh stack submit` per push. It is the one tool that keeps the bases right and links the
PRs into a stack GitHub itself understands, so a reviewer sees the order rather than having to
reconstruct it. Binds later features; goes to `docs/decisions.md` at close as its own `DEC-NNN`.

## Project Structure

### Documentation (this feature)

```text
specs/001-first-ingest/
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── README.md
│   ├── hub-http-api.md      # browser ↔ hub
│   ├── mcp-wiki-tools.md    # agent ↔ hub (the grant)
│   └── agent-cli-protocol.md # hub ↔ the claude CLI
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
  Grimoire.Wiki/                     # WIKI context
    IWikiStore.cs                    #   port — the only way into the wiki
    ProvenanceStamp.cs               #   WIKI-002: add, replace, or fail on unreadable
    OkfFrontmatter.cs                #   only the six OKF parts R-07 names
    Adapters/FileSystemWikiStore.cs  #   the only place the filesystem is touched
  Grimoire.Runs/                     # RUNS context
    Submission.cs                    #   the submission and its four states (RUNS-001)
    SubmissionBoard.cs               #   the in-memory states — a plain object, no port (R-08)
    RunStateMachine.cs               #   Run, plus RUNS-005 and its single nudge
  Grimoire.Agent/                    # GUARD context
    IAgentHarness.cs                 #   port — dispatch, nudge, stop
    ToolGrant.cs  Ceilings.cs        #   GUARD-002/003, GUARD-004
    Adapters/HarnessProcess.cs       #   the only place the claude process and its NDJSON live
  Grimoire.Hub/                      # composition root + the two HTTP surfaces
    Api/SubmissionsEndpoints.cs      #   ACCESS-001/002, INGEST-001/003/004/005
    Mcp/WikiToolsServer.cs           #   serves the grant at /mcp/runs/{runId}
    InstructionLoader.cs             #   the only thing that puts text in the prompt (V.1)
    wwwroot/index.html  wwwroot/app.js   #   the static page — no build step (R-10)
instructions/
  ingest.md                          # WIKI-001 — versioned, owner-changed, named in the PR
tools/
  Grimoire.Trace/                    # trace-check (writes nothing) + the docs/trace.md writer
tests/
  Grimoire.Fast.Tests/               # INGEST, WIKI-002/003, GUARD, RUNS
  Grimoire.Contract.Tests/           # FileSystemWikiStore (CI) + HarnessProcess (local, sign-in)
  Grimoire.E2E.Tests/                # ACCESS-001, ACCESS-002 — Playwright .NET
docs/
  capabilities/                      # ingest.md wiki.md guard.md access.md runs.md
                                     #   registered BEFORE the first test, reconciled at close (IV.2)
  trace.md                           # written by the single documented command (IV.4)
```

**Structure Decision**: three bounded contexts, each declaring its own port and owning its adapter,
with `Grimoire.Hub` as the composition root and the only project that knows all three. Every
external system is reachable from exactly one file (Constitution V.2): the filesystem from
`FileSystemWikiStore`, the `claude` process from `HarnessProcess`. Interfaces exist only at those two
ports (II.4) — there is none over `ProvenanceStamp`, `RunStateMachine` or `SubmissionBoard`, which
are real objects the Fast tests use directly.

The MCP server lives in the hub rather than in `Grimoire.Wiki` because it is a door into the process
like the HTTP API, not wiki logic; it calls `IWikiStore` like any other caller.

## Quickstart — the owner's acceptance run *(mandatory)*

Carried into [quickstart.md](quickstart.md) in full.

**Outcome exercised**: OUT-01 — submit a text in the browser and afterwards find new, linked pages
including a source page in the wiki

**Real external systems in place**: the `claude` CLI signed in with the owner's subscription and
reaching a real model, and a real wiki repository on disk. No stand-ins, no temporary directory.
These are the only external systems the outcome touches.

**Steps the owner runs**: start the hub against the real wiki and the real purpose description
(`dotnet run --project src/Grimoire.Hub -- --wiki … --purpose …`), open the submission page, paste
one text they actually want in the wiki, submit it, and leave. Later, read the wiki.

**What the owner must see**: the submission reads `done`, and the wiki holds a source page for that
text; pages naming it as their source, with links that lead somewhere; a `type` on every page, each
filed in one section whose `index.md` lists it; a root `index.md` declaring `okf_version: "0.2"` and
listing the sections; an entry in `log.md` identifying that run and saying what changed and why; and
`generated: { by, at }` carrying Grimoire's values on every page the run wrote. Whether the pages are
*good* is not judged here — that is the owner's reading and, later, OUT-07.

## Complexity Tracking

> Fill ONLY if the Constitution Check has violations that must be justified. A rule that blocks
> needed work is amended first, not set aside (Governance 1) — an entry here is a stopgap, not an
> exception.

| Violation | Why needed | Simpler alternative rejected because |
| --- | --- | --- |
| III.7 — the Contract time budget is "measured in CI", but the three `HarnessProcess` tests cannot run in CI | They exercise the real `claude` CLI with the owner's subscription sign-in, which CI does not have; III.4 requires a Contract suite against the real external thing, and this is it. The suite CI *does* run — the wiki adapter's — carries and fails on the 90 s budget as written | A scripted endpoint in place of the real CLI would put the suite in CI, but it is a component we would write and maintain that proves none of R-11's findings, and III.4 asks for the real external thing. Giving the three tests their own budget mechanism would be purpose-built tooling (III.7). They run locally under the same `--timeout 90s` before the PR |

The earlier entry for I.7 is removed: the estimate is inside the budget.
