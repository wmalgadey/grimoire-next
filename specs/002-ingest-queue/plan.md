# Implementation Plan: The Ingest Queue

**Branch**: `002-ingest-queue` | **Date**: 2026-09-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/002-ingest-queue/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the
execution workflow.

## Summary

A submission made while a run is in progress is accepted and waits its turn; runs start one at a
time in the order the submissions were made; a failed run holds the queue until the user
acknowledges it in the browser; and all of it — submissions, their states, the run each one was
given and whether its failure has been acknowledged — outlives Grimoire stopping, however it
stopped. The approach: the queue rule stays a judgment of the RUNS context (`SubmissionBoard` hands
out the next submission and refuses to hand out any while one is under way or an unacknowledged
failure stands), the hub gains one small piece that asks for the next run and dispatches it, and
RUNS gains its first port — a submission store, with SQLite behind it, written through on every
change and read back at start-up. An agent that outlived a stop Grimoire could not act on is found
by the process identity recorded with its run and terminated as Grimoire comes back up, before that
run reads failed and before anything else starts.

**Outcome advanced**: OUT-01 — submit a text in the browser and afterwards find new, linked pages
including a source page in the wiki

**Slice addition**: **a new user interaction** — acknowledging a failed run, in the browser.
*(Constitution I.6 — never two; the skeleton feature is exempt from "never two", not from the
budget.)* No new operation: a run is dispatched the same way `001-first-ingest` dispatched it, only
at a different moment. No new external system in the product sense either — the store is a file on
the machine Grimoire already runs on, reached through a port of the RUNS context, and nothing about
the agent, the model or the wiki changes.

## Technology decisions *(mandatory)*

`docs/decisions.md` was read first. DEC-001 through DEC-022 are in force and none is re-decided
here. Three of them are load-bearing for this feature and are used as they stand: **DEC-016** (a run
is stopped by an interrupt, with killing the process as the backstop) is the mechanism RUNS-006
reuses; **DEC-018** (`TimeProvider`, `FakeTimeProvider` in the Fast suite) is how the queue's
ordering is driven without waiting for real seconds; **DEC-014** (the run identifier in a path is
addressing, not authorisation) is the reasoning the acknowledgement endpoint follows.

`001-first-ingest` left exactly one decision open for this plan. Its row read: *"Where submissions
and their states live — a plain object in the RUNS context, in memory … The port arrives with
RUNS-004"*, and it was the single row marked **does not bind later features**, precisely so that
this plan could decide it. That is the first row below.

The five ★ items were decided by `001-first-ingest` (DEC-002 … DEC-008) and are not re-decided.

| Decision | Choice | Reason | Binds later features (yes / no) | Departs from (DEC-NNN, or none) |
| --- | --- | --- | --- | --- |
| Where submissions and their states live | **SQLite** through `Microsoft.Data.Sqlite`, raw SQL, no ORM and no migration framework. Two tables behind one port, `ISubmissionStore`, declared by the RUNS context with its adapter under `Grimoire.Runs/Adapters/` | RUNS-004 as clarified covers *any* stop, a kill or a power cut included, so nothing may depend on a shutdown step having run: every change has to be on disk before it is answered for. Getting that right by hand means fsync ordering and torn-record recovery — decisions a dependency has already made, and III.8 says we test our decisions rather than a dependency's. `Microsoft.Data.Sqlite` is a first-party package with no native install step and no server. An ORM was rejected: EF Core brings a migration mechanism for two tables that never change shape in this feature, which is a mechanism with no consumer (II.1). A hand-written append-only journal was rejected for the same reason in reverse — it is storage tooling of our own where an existing thing does the job (II.3), and its torn-write and replay handling would be our decisions and so our tests (R-01, R-02) | yes | none — completes the open row of `001-first-ingest` |
| Whether the store is a port at all | Yes: `ISubmissionStore` in `Grimoire.Runs`, `SqliteSubmissionStore` in `Grimoire.Runs/Adapters/`, an in-memory adapter in the Fast suite | A file on disk is outside the process, which is exactly what II.4 admits an interface for — and what `001-first-ingest` said was missing when it refused the port. V.2 then puts the adapter in the context that declares the port, and SQLite is named in no other file (R-01) | yes | none |
| Who starts the next run | The RUNS context decides *whether* and *which* (`SubmissionBoard.TakeNext`); the hub's `RunQueue` does the dispatching, and is called from exactly four places: an accepted submission, a run that ended, an acknowledgement, and start-up | RUNS-002 and RUNS-003 are judgments about runs, and every judgment about a run is already made in the RUNS context (`RunStateMachine.cs`, plan `001-first-ingest`). Handing out the next submission under the board's existing lock is also what makes "at most one run in progress" true against a race rather than by luck — the board already holds one lock for itself and every submission on it, for the same reason. No timer, no background service, no scheduler: a queue that is pumped by the four events that can unblock it needs none, and one would be a mechanism with no consumer (II.1) (R-03) | no | none |
| How a waiting submission is told apart from one under way | A nullable **run identifier on the submission**, set at the moment the board hands it out. Waiting = `submitted` with no run identifier; under way = a run identifier with a non-terminal state | With a queue, `submitted` no longer means one thing: it covers both a submission waiting its turn and one whose agent has not yet reported in (the spec's Key Entities keep that boundary). Without something outside the state, the board could hand the same submission out twice. RUNS-001's four states stay four — a run identifier is not a state — and the identifier is needed durably anyway, because ACCESS-003 addresses a run and must still work after a restart (R-04) | no | none |
| Stopping the run when the hub stops (RUNS-006, first half) | `IHostApplicationLifetime.ApplicationStopping` calls one method on `RunConductor`, which stops the run in progress exactly as a ceiling does — interrupt first, the process kill behind it | DEC-016 already established that mechanism and the evidence behind it; RUNS-006 needs no second one, so this is wiring rather than a new mechanism (II.1). The lifetime hook itself is framework wiring and is not tested (III.8); the behaviour — the run in progress is stopped — is proven in the Fast suite against the drivable harness (R-05) | no | none |
| Recognising an agent that outlived a stop (RUNS-006, second half) | The **pair** — the agent's process identifier *and* the moment that process started — recorded with the run as soon as the child exists. At start-up a process is terminated only where a live process carries that identifier **and** that start time. Both halves come from `System.Diagnostics.Process` (`Id`, `StartTime`); the act reuses `Kill(entireProcessTree: true)`, which `HarnessProcess` already performs | The identifier alone is not an identity: operating systems reuse those numbers, and after a reboot one almost certainly belongs to something else — terminating it would kill an unrelated program on the owner's machine, the one failure in this feature that does damage outside Grimoire. Two processes sharing an identifier *and* a start time to the tick do not occur, and a reboot changes every start time, so the pair also handles the reboot case with no special rule. Matching the process name as well would work but would make the proof need a real `claude` and therefore a sign-in (DEC-021), putting it outside CI (R-11) | yes | none |
| Where that termination lives | At the agent port: `RunReport` gains one report (the child's identity, as soon as it exists) and `IAgentHarness` one operation (terminate this recorded identity if it is still that agent). `HarnessProcess` is the only implementation | The `claude` process is an external system and appears only inside its adapter (V.2), and the port's shape is already "the harness reports facts, the hub decides" (`IAgentHarness`). Checked rather than assumed: `HarnessProcess` is **not** `IDisposable` and keeps its children in a plain dictionary, so there is no existing disposal path that would have cleaned one up (R-05, R-11) | no | none |
| What the acknowledgement addresses | The **submission**: `POST /api/submissions/{id}/acknowledgement`. **No run identifier reaches the browser.** The list gains one field, `awaitingAcknowledgement`, present only where a submission's run ended failed and is not yet acknowledged | A submission has exactly one run here (INGEST-002), so its identifier already identifies the failed run — and the browser has carried it as the row key since `001-first-ingest`. A stale page is still harmless: it names a submission that is no longer an unacknowledged failure, and nothing happens. This leaves ACCESS-002 entirely alone and leaves the sentence in `001-first-ingest`'s contract — no further detail about the run is exposed by that API — standing as written. A field is still needed because `failed` alone does not say whether the control is offered: an acknowledged failure still reads `failed` (RUNS-003). It says an action is available, not what the run did (R-06) | no | none |
| The excerpt (ACCESS-004) | The submitted text with runs of whitespace collapsed to single spaces and trimmed, cut to **120 characters**, with `…` appended where it was cut. Computed in the RUNS context, on the submission | ACCESS-004 asks for the same length for every submission, which leaves the length to this plan. The page is `max-width: 42rem` and a row carries the excerpt beside a time and a state, so 120 characters is about a line and a half there — long enough that a person recognises their own text, short enough that the list stays one row per submission. Collapsing whitespace is what makes a pasted document's first 120 characters a sentence rather than an indented fragment. It sits on the submission rather than in the API view because "the opening of this text" is a fact about the submission and is proven in the Fast suite (R-07) | no | none |
| What survives with a run | Its identifier, the submission it belongs to, when it started, and the tools it was granted | The spec's Assumptions commit to the grant surviving (GUARD-003 read with RUNS-004). Today a `Run` object — grant included — is dropped the moment the run ends, so GUARD-003's record lives only as long as the run does; once RUNS-004 makes the submission outlive the process, a grant that still vanishes would make GUARD-003 weaker than the state around it. It is one row of five names per run (R-08) | no | none |

A reason names the constraint or evidence. "Owner decision" is not a reason — name the owner's
constraint.

**Binding decisions are merged into `docs/decisions.md` when the feature closes** (Constitution
II.6, review-checklist item 4), taking the next free `DEC-NNN` — `DEC-023` for the store and
`DEC-024` for the port. The rows marked `no` stay here.

**Package management** is unchanged: a `PackageVersion` for `Microsoft.Data.Sqlite` in
`Directory.Packages.props` and a bare `PackageReference` in `Grimoire.Runs.csproj` — the first
package that project takes. It is a first-party package on the .NET 10 release train, so it needs no
version decision of its own beyond matching the rest of the tree.

**Test time budget** *(Constitution III.7, gate `time-budget`)*: Fast under 15 s, Contract under
90 s, execution only, measured in CI. The default test run executes Fast only and fails when Fast
exceeds its budget; the Contract run fails when it exceeds its own.
Enforced by: Microsoft.Testing.Platform's `--timeout` (DEC-008), unchanged. The Fast suite gains an
in-memory submission store, so no Fast test touches a file; the new Contract tests open a SQLite
file under a temp directory and start one short-lived child process, which is where the Contract
budget already absorbs `FileSystemWikiStoreTests`. Both new Contract classes run in CI: neither
needs a sign-in, so neither joins the three DEC-021 excludes.

## Technical Context

**Storage**: SQLite, one file, inside a directory Grimoire owns — `--state <path>`, defaulting to a
`state/` directory beside the hub. **Not** inside the wiki: the queue writes nothing into the wiki
and reads nothing in it (spec, "Who writes what"), and a queue file in the user's wiki repository
would be Grimoire's bookkeeping in the user's version history.

**Target Platform**: self-hosted single instance on the owner's machine, loopback only
(`docs/product.md` §2, DEC-014). Unchanged.

**Project Type**: service with a browser front end — ASP.NET Core on .NET 10 (DEC-002), static page
from `wwwroot/` (DEC-019). Unchanged.

**Constraints**: one run at a time and strictly FIFO; a failure blocks until acknowledged; every
change durable before it is answered for; no timeout on a waiting submission; the queue neither
reads nor writes the wiki.

**Scale/Scope**: one user, one process. Submissions accumulate at a handful a day and are never
deleted; a run makes at most five state-changing writes. Nothing here is a scale problem, and no
part of the design is chosen for scale.

## Constitution Check *(mandatory)*

*Complete before design, re-check after design. One line per principle, "touched" or
"not touched". A cross-cutting concern binds this feature only where it is touched
(Constitution II.5).*

| Principle | Touched? | How this plan satisfies it / why it is not touched |
| --- | --- | --- |
| I. Purpose and Focus | touched | One outcome (OUT-01), three user stories, one new user interaction (acknowledging), no blocking open question; phases and their PRs named below before implementation starts (I.3, I.6, I.7, I.10). |
| II. Simplicity | touched | The one new port has an external system behind it and an in-memory adapter beside it (II.4); no ORM, no migration framework, no scheduler, no background service, no timeout mechanism, no cancel; SQLite is an existing thing rather than storage tooling of our own (II.3); no new gate (II.2). |
| III. Testing | touched | Six requirements, all proven by `test`; each at the lowest level that can prove it (R-09); two Contract suites against the real things — a SQLite file, and a real process for RUNS-006 (III.4); doubles are in-memory adapters at our own port (III.9); SQLite's own durability is not tested and neither is the lifetime hook (III.8). |
| IV. Visibility | touched | All six IDs registered in `docs/capabilities/` in phase 1, before the first test (IV.2); INGEST-005 moves under "Retired" keeping its ID; `docs/trace.md` regenerated and the capability files reconciled at close (IV.4). |
| V. Design Invariants | touched | V.2 — the new adapter sits in the context that declares its port, SQLite is named nowhere else, and RUNS-006's termination sits at the agent port because only that adapter knows what a process is. V.1 untouched: no prompt changes, `instructions/ingest.md` is not edited by this feature. V.3 untouched: the grant is unchanged, and it is now also recorded durably. |
| Governance | touched | `/speckit-converge` runs once, in the closing phase (Governance 2). No amendment is proposed, and the instruction file is not changed, so nothing under Governance 4 applies. |

**Re-checked after Phase 1 design, and again after the owner's review**: unchanged. The design adds
no interface beyond `ISubmissionStore` — RUNS-006's termination is two members on the agent port
that already exists — no option, and no configuration surface except the one start-up path the store
needs. `data-model.md` holds no field without a reader in this feature; the agent's process identity
is read at start-up, which is the only place it is for.

## Budget and split decision *(mandatory)*

**User stories**: 3 of 3 · **Estimated tasks**: 39 of ~40 (filled in by `/speckit-tasks`; the estimate was ~36)

**Within budget?** **Yes → proceed to `/speckit-tasks`.**

The spec's budget note warned that anywhere near 40 tasks would mean something is being built that
the spec does not ask for. 32 is inside the budget but is not small, so here is where it went, and
what was examined for cutting:

| Group | Tasks |
| --- | --- |
| Capabilities registered, INGEST-005 retired, INGEST-001 changed, and the refusal it carried removed from the API, the page and the suites | 4 |
| RUNS: the queue rule — the run identifier on the submission, `TakeNext`, the acknowledgement gate, the excerpt | 6 |
| RUNS: the store port, the SQLite adapter, loading at start-up, interrupted runs read failed | 6 |
| The store's Contract suite | 2 |
| Agent port: the process identity reported and terminated, and its Contract suite against a real process (RUNS-006) | 3 |
| Hub: `RunQueue`, rewiring intake and conductor, the stop at shutdown, and the start-up order — terminate, then read failed, then pump (RUNS-006) | 6 |
| ACCESS: the acknowledgement endpoint, `excerpt` and `awaitingAcknowledgement` in the list, the page's excerpt and its acknowledge control | 4 |
| E2E: acknowledging in the browser, surviving a restart, the updated row assertion | 3 |
| Close: reconcile the capability files, `docs/trace.md`, `docs/decisions.md`, the outcome's status, converge, the acceptance run | 2 |

Eight of those thirty-six are the store and its Contract suite — the thing `001-first-ingest`
deliberately did not build — and nine more are RUNS-006 and ACCESS-004, the two requirements that
came out of clarification and the owner's review. Nothing was found to cut: there is no task here
that no requirement asks for. The candidates that would have grown it further were all refused in
the rows above — an ORM, a journal of our own, a background pump, a per-run acknowledgement history,
and a run identifier in the browser.

At 36 of ~40 this is no longer a small feature, and the spec said so would mean something unasked-for
was being built. It is not: the growth is two requirements the owner added after reading the spec and
the plan, each closing a hole the earlier draft left — an unbounded orphan agent, and a list of four
identical-looking rows. Both are named in the spec with IDs and proofs.

**Split** *(not applied)*: the feature is inside the budget and every requirement belongs to the
same user-observable result. A split along "the queue" versus "surviving a stop" was considered and
rejected: the first half would ship a queue that a restart silently empties, which is worse than no
queue.

## Phase PRs *(mandatory)*

Named here before implementation starts (Constitution I.10). One feature branch off `main`; each
phase of `tasks.md` is a branch off it, and each phase PR is merged back into the feature branch
once it is reviewed and green, before the next phase starts. No PR is based on another open PR.
Only `002-ingest-queue` merges to `main`, and only when the feature is done (I.9).

| Phase | Tasks | Branch | PR |
| --- | --- | --- | --- |
| 1 The queue rule (US1) | T001–T012 | `002-ingest-queue-phase-1-queue-rule` | #36, merged |
| 2 The acknowledgement gate (US2) | T013–T021 | `002-ingest-queue-phase-2-acknowledgement` | #37, merged |
| 3 Surviving a stop (US3) | T022–T033 | `002-ingest-queue-phase-3-surviving-a-stop` | #38, merged |
| 4 Closing the feature, with convergence | T034–T041 | `002-ingest-queue-phase-5-convergence` | #40 |

Task ranges are filled in by `/speckit-tasks`; the phases, their order and their branches are fixed
here.

**What the last row records rather than prescribes.** `/speckit-converge` runs in the closing phase
(Governance 2) and found one gap — the hub coming up is the fourth of the four events that pump the
queue (research.md R-03), and nothing proved it. Its remedy is T040–T041, which `tasks.md` carries
as a Convergence phase of its own, because converge appends rather than rewrites. A phase found by
converge cannot be named here before implementation starts, which is what converge is for; what
I.10 asks — that it be a branch off the feature branch, reviewed and green, merged before anything
follows it — it keeps. It shipped together with the closing tasks in one PR rather than as a fifth
branch, so the four phase PRs above are the whole record. The branch named `…-phase-4-close` was
opened and then folded into that one; it carried no commits of its own.

The order is the user stories' own priority, with two deliberate placements:

- **The capability files are registered in phase 1**, before the first test of the feature (IV.2).
  INGEST-005's retirement lands there too, because a test carrying a retired ID fails `trace-check`
  — so the retirement and the removal of the tests that carried it are one step, not two.
- **RUNS-006 lands in phase 3**, with the rest of "stopping and starting again" — both halves, the
  stop as the hub goes down and the termination as it comes back up. It is invisible to the user, so
  it leads nothing; putting it beside the restart is what lets phase 3 prove the whole lifecycle
  question in one place, and its start-up half cannot be written before the store that holds the
  process identity exists.

Each phase PR leaves the feature branch green, `trace-check` included. Phase 1 registers RUNS-004
and ACCESS-003 in `docs/capabilities/` before the tests that prove them exist; that is what IV.2
requires, and `trace-check --complete` — the run that would fail on a `test` requirement with no
test — applies where a feature lands on `main` (IV.3), not to a phase PR targeting the feature
branch.

## Project Structure

### Documentation (this feature)

```text
specs/002-ingest-queue/
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── README.md
│   ├── hub-http-api.md      # browser ↔ hub — supersedes the 001 document
│   └── submission-store.md  # hub ↔ the durable store (the new port)
├── checklists/
│   └── requirements.md  # written by /speckit-specify, re-validated by /speckit-clarify
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created by /speckit-plan)
```

### Source Code (repository root)

Only what this feature adds or changes is listed; everything else stands as `001-first-ingest` left
it.

```text
src/
  Grimoire.Runs/                          # RUNS context — gains its first port
    Submission.cs                         #   + RunId, + AcknowledgedAt, + Excerpt (ACCESS-004)
    SubmissionBoard.cs                    #   the queue rule: TakeNext, Acknowledge, Restore
    ISubmissionStore.cs                   #   NEW port — what is kept, and what a restart reads
    Adapters/SqliteSubmissionStore.cs     #   NEW adapter — the only place SQLite appears (V.2)
    RunStateMachine.cs                    #   unchanged
  Grimoire.Agent/                         # GUARD context
    IAgentHarness.cs                      #   + the agent's process identity, reported and terminated
    Adapters/HarnessProcess.cs            #   reports the child's identity; terminates a recorded one
  Grimoire.Wiki/                          # unchanged
  Grimoire.Hub/                           # composition root
    RunQueue.cs                           #   NEW — asks the board for the next run and dispatches it
    SubmissionIntake.cs                   #   accepts, then asks the queue; no longer dispatches itself
    RunConductor.cs                       #   + StopEverythingAsync (RUNS-006); tells the queue a run ended
    Api/SubmissionsEndpoints.cs           #   + excerpt, + awaitingAcknowledgement,
                                          #   + POST /api/submissions/{id}/acknowledgement; − the 409
    HubApplication.cs                     #   wires the store, the queue and the shutdown hook
    Program.cs                            #   + --state <path>
scripts/
  run-hub.sh                              #   + GRIMOIRE_STATE, beside the other .env keys
.env-example                              #   + GRIMOIRE_STATE
    wwwroot/index.html  wwwroot/app.js    #   the excerpt in each row, the acknowledge control
tests/
  Grimoire.Fast.Tests/
    InMemorySubmissionStore.cs            #   NEW double at the new port (III.9)
    QueueTests.cs  AcknowledgementTests.cs  RestartTests.cs  SubmissionExcerptTests.cs   # NEW
    SubmissionAcceptanceTests.cs          #   − the four INGEST-005 tests
  Grimoire.Contract.Tests/
    SqliteSubmissionStoreTests.cs         #   NEW — the adapter against a real SQLite file (III.4)
    AgentProcessTests.cs                  #   NEW — terminating a real process; no sign-in, runs in CI
  Grimoire.E2E.Tests/
    AcknowledgementTests.cs  RestartTests.cs   # NEW
    SubmissionStatesTests.cs              #   the row assertion gains the excerpt
docs/
  capabilities/runs.md  access.md  ingest.md   # the six IDs registered, INGEST-005 retired (IV.2)
```

**Structure Decision**: unchanged from `001-first-ingest` — three bounded contexts, each declaring
its own ports and owning its adapters, with `Grimoire.Hub` as the composition root and the only
project that knows all three. This feature adds the third port and the third external system:
the filesystem is reachable only from `FileSystemWikiStore`, the `claude` process only from
`HarnessProcess`, and now SQLite only from `SqliteSubmissionStore`.

RUNS-006's second half respects that line rather than crossing it: the hub decides *that* an agent
which outlived a stop must go, and the agent adapter is the only thing that knows what a process is
and how to end one. The store keeps the identity; it never acts on it.

`RunQueue` sits in the hub rather than in RUNS for the same reason `SubmissionIntake` does: it is
where the contexts meet. The *decision* it acts on — whether a run may start and which submission is
next — stays in `SubmissionBoard`, so no judgment about a run leaves the RUNS context.

## Quickstart — the owner's acceptance run *(mandatory)*

**Outcome exercised**: OUT-01 — submit a text in the browser and afterwards find new, linked pages
including a source page in the wiki

**Real external systems in place**: a signed-in `claude` on `PATH` with the owner's subscription
(DEC-001, DEC-009); a real wiki directory under the owner's version control; the real SQLite file
the hub was started with. No stand-ins: the E2E suite's drivable harness proves the browser, and
this run proves the queue against a real agent.

**Steps the owner runs**: start the hub with `./scripts/run-hub.sh`; submit a first text; while its
run is under way, submit a second and a third; let the first run end; watch the second start by
itself. Then the same six-step chain twice — **a run under way with a text waiting behind it, the
hub stopped, the hub started again, the interrupted run read as failed with the waiting text still
waiting, the failure acknowledged, the waiting text's run starting** — once with **Ctrl-C** and once
with **`kill -9`**. The second pass is the one RUNS-004 was clarified for: no shutdown step runs at
all — and it is where RUNS-006's second half shows, because the agent that survives the kill must be
gone once Grimoire is back.

**What the owner must see**: the second and third texts accepted while the first run was under way,
each reading `submitted` with the opening of its own text beside it; the runs starting one at a time
and in the order the texts were made; and then, in **both** passes, the whole chain — after the
restart every submission still listed with the state it carried, the interrupted run reading
`failed`, the text behind it still reading `submitted` with nothing started, and the waiting text's
run starting only once the failure is acknowledged. In the Ctrl-C pass, no `claude` process is left
running the moment the hub stops; in the kill pass one survives the kill — and is gone again once
the hub has been started (`pgrep -f claude` prints nothing), before the run reads failed and before
anything else starts. Details in [quickstart.md](quickstart.md).

## Complexity Tracking

> Fill ONLY if the Constitution Check has violations that must be justified. A rule that blocks
> needed work is amended first, not set aside (Governance 1) — an entry here is a stopgap, not an
> exception.

No violations. Two things were examined against II.1 and are recorded in `research.md` rather than
here, because each has a consumer in this feature: the grant's durability (R-08 — GUARD-003's
record read together with RUNS-004) and the run identifier in the list (R-06 — the acknowledgement
that addresses it).

One cost carried over from `001-first-ingest` is unchanged and is not re-listed: the agent
adapter's Contract tests need a sign-in and are excluded from CI (DEC-021), so their execution time
is not measured there.
