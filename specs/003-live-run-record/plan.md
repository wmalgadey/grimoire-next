# Implementation Plan: The Live Run Record

**Branch**: `003-live-run-record` | **Date**: 2026-09-26 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/003-live-run-record/spec.md`

## Summary

Every run gets a Markdown record of its own, appended as it proceeds, in a `runs/` directory beside
the state Grimoire already keeps: a frame at the head (model, grant, both ceilings, start), then what
the run did — each tool call with its arguments, what it returned whole, the agent's own text, the
nudge — and a tail when it ends (why, where it stood, tokens per model). Two figures and the model
reach the browser's list and follow a run while it is under way, and a second page is a window onto
the record, live and afterwards alike. ACCESS-002's "no further detail about the run" is retired here;
it is the requirement this feature exists to undo.

**Outcome advanced**: OUT-02 — see for every run what it did, why it ended and what it cost

OUT-16 — watch what the agent is doing while a run is in progress — closes with it, as the spec
records. One outcome is named (Constitution I.3); both are set Done in the single edit IV.4 allows.

**Slice addition**: **a new user interaction** — opening a run and reading its record. The run
operation already exists and is already dispatched, watched and judged; the record is what it now
leaves behind. No new external system: the filesystem is already reached by `FileSystemWikiStore` and
by the state directory of DEC-023. *(Constitution I.6 — never two.)*

## Technology decisions *(mandatory)*

`docs/decisions.md` was read first. DEC-009, DEC-010, DEC-015, DEC-019 and DEC-023 bind this feature
and none is departed from. What is new:

| Decision | Choice | Reason | Binds later features | Departs from |
| --- | --- | --- | --- | --- |
| Where a run's record lives, and who writes it | `IRunRecord`, a port of the RUNS context, with one adapter `MarkdownRunRecord` writing `<state>/runs/<runId>.md` | The record is what a run did, so it belongs to the context that owns what a run is; the filesystem is an external system and so appears only inside that context's adapter (V.2, the shape DEC-023 gave `ISubmissionStore`). Beside the SQLite file because Grimoire's bookkeeping in the user's repository would turn up in the version history that is their only undo — and `Program.cs` already refuses a `--state` inside the wiki, so the guard is inherited (research.md R-01) | yes | none |
| The record's shape | Frame head at `Begin`, narrative appended, frame tail at the end. Never rewritten, no byte moved | RUNS-007 has it appended and never rewritten, and an append is the cheapest write to survive a stop — the concern DEC-023 settled for the queue. A frame patched in place would need the file rewritten at the end, which is what makes a run cut off by a stop unreadable (research.md R-02) | yes | none |
| How a tool call, its result and the agent's text are read | From the CLI's **complete** `assistant` and `user` messages: every `tool_use`, `tool_result` and `text` block of `message.content`. Read in `AgentTranscript` and nowhere else | `contracts/agent-cli-protocol.md` specified the `assistant` message's `tool_use` blocks in `001-first-ingest` and only now has a consumer. Measured on `claude` 2.1.283: the complete message arrives for every block, so nothing needs assembling from the partial stream; and stdin messages are **not** echoed on stdout, so the nudge cannot appear twice (research.md R-03) | yes | none |
| How a large tool result stays readable | Kept whole, in a fence of backticks one longer than the longest run of backticks in it (minimum three). The browser folds the block | The owner decided nothing is cut. A fixed fence breaks on a result containing a fence, which a run reading wiki pages will produce; CommonMark's own longer-fence rule makes the block unambiguous for any content, alters nothing, and gives the browser a deterministic segmentation rule (research.md R-04) | yes | none |
| Where the run's figures live | Three columns on the run row in SQLite — tokens spent, tool calls made, entries the record could not hold — written only when one of them changes | The list polls once a second; parsing prose Grimoire has just written to recover a number it already had is the seam the brief warns about. Figures are state, so they live where the state lives (DEC-023), and both they and the narrative are written from the same event, so they cannot disagree (research.md R-06) | yes | none |
| How an existing state file gains those columns | `PRAGMA table_info(runs)`, then `ALTER TABLE runs ADD COLUMN` for each column that is missing. No version table, no scripts, no ORM | DEC-023 rejected a migration *framework* as having no consumer; a consumer for bringing an existing file up to date exists today — the owner's own `submissions.db` with the ingests they have already made. The alternative throws their list away to save eight lines, and `IF NOT EXISTS` is already this file's idempotent-schema idiom (research.md R-07) | yes | DEC-023's "two tables that do not change shape" — stated, not silently broken |
| The detail view | A second static page, `run.html` + `run.js`, polling `GET /api/submissions/{id}/record` for the record as text and appending one element per moment. No Markdown renderer | Reading a run is a second job, which is when `docs/ux.md` allows a second page; it gives the back button and a shareable URL for nothing. Appending rather than re-rendering is what makes "arriving lines must not move what the user is reading" true without measuring anything. DEC-019 rules out a renderer, and `docs/ux.md` asks for monospace where the content is a file (research.md R-08) | yes | none |
| How the list's rows stay still | Rows updated in place, keyed by the submission's identifier; each figure in its own element with tabular figures and a reserved width | `app.js` rebuilds every row once a second, which was harmless while a row said one word. With two growing numbers a proportional digit changes the column's width and reflows the row — and the Acknowledge button is replaced under the user's finger (research.md R-09) | no | none |
| What a record that cannot be written does | The port does not throw. The adapter counts what it could not write, the count travels with the figures, and the record says how many entries were lost once a write succeeds again | The owner decided the run goes on and the gap is made visible. A throw would end the run; a silence would leave an unwritten record indistinguishable from an agent that did nothing (spec, Clarifications; research.md R-10) | yes | none |

A reason names the constraint or the evidence, never "owner decision" alone: where the owner decided,
the constraint they decided on is named.

**Test time budget** *(Constitution III.7, gate `time-budget`)*: Fast under 15 s, Contract under 90 s,
execution only, measured in CI. Enforced by the test platform's own session timeout, as DEC-008
settled — `--timeout 15s` and `--timeout 90s` on the respective runs. No purpose-built tooling. This
feature adds no test that waits for real time: the moments come from recorded lines, and elapsed time
comes from `FakeTimeProvider` (DEC-018).

## Technical Context

**Storage**: the record is one Markdown file per run under `<state>/runs/`; the figures are three
columns on the existing `runs` table in `<state>/submissions.db` (DEC-023). Nothing is stored in the
wiki.

**Target Platform**: self-hosted single instance, one user, loopback only
(`docs/product.md` §2, DEC-014).

**Project Type**: service with a browser front end — three bounded contexts plus a composition root,
static files under `wwwroot/` with no build step (DEC-019).

**Constraints**: no bundler and no npm; no Markdown renderer. `AgentTranscript` stays the only reader
of the CLI protocol, and `InstructionLoader` the only thing that puts text into the prompt. RUNS-005's
"Grimoire MUST read nothing else in the wiki" is untouched — the record is not in the wiki. Cost is
tokens and never currency (DEC-015).

**Scale/Scope**: a run makes tens of tool calls; a record is kilobytes to low hundreds of kilobytes
(research.md R-04). Records accumulate and Grimoire never removes one.

## Constitution Check *(mandatory)*

Completed before design and re-checked after it. Every verdict held; the design pass added two things
to the rows below rather than changing one — that the schema change departs from DEC-023's "two tables
that do not change shape" and says so (II.6), and that ACCESS-002's retirement has to travel in the same
PR as the five tests carrying it or `trace-check` fails (IV.3). The baseline was green when this plan was
written: `trace-check: 22 requirements, 222 tests, no violations`.

| Principle | Touched? | How this plan satisfies it / why it is not touched |
| --- | --- | --- |
| I. Purpose and Focus | touched | One outcome named (OUT-02), no blocking open question, one new user interaction and no second, one acceptance scenario that all three stories advance, phases and their PRs named below before implementation starts |
| II. Simplicity | touched | Every part has a consumer in this feature: the port has the conductor, the figures have the list, the second page has the record. Range requests, a Markdown renderer and a migration framework were each weighed and left out with the trigger named (research.md R-07, R-08). `IRunRecord` is a port to the filesystem, which is the only thing II.4 allows an interface for |
| III. Testing | touched | Six requirements, all proven by `test`, none by `review` or `eval` — so the spec has no "Why review" section. Each at the lowest level that can prove it (research.md R-11); the filesystem halves are Contract, the browser halves E2E. No new `requires=signin` test, so DEC-021's budget of three is untouched |
| IV. Visibility | touched | RUNS-007…010 registered in `docs/capabilities/runs.md` in phase 1, ACCESS-005/006 and ACCESS-002's retirement in phase 3 — together with the tests that carry those IDs, because a test on a retired ID fails `trace-check`. `docs/trace.md` regenerated at close |
| V. Design Invariants | touched | V.1: the record holds facts about the run and no judgement about wiki content, and nothing of it goes into the wiki. V.2: the filesystem appears only in `MarkdownRunRecord`, the CLI protocol only in `AgentTranscript`, SQLite only in `SqliteSubmissionStore`. V.3: the grant is unchanged and is now also written into the record |
| Governance | touched | `/speckit-converge` runs once in the closing phase (Governance 2); review findings classified per Governance 3; the second reviewer is the repository's Copilot review (DEC-025). The instruction under `instructions/` is **not** touched by this feature |

## Acceptance scenario *(mandatory)*

**Scenario**: with a real wiki and a signed-in `claude`, the owner pastes a text and submits it, and
while the run is under way watches its row show the model and the two figures rising without the list
moving; they open the run and watch tool calls and the agent's text arrive; they leave, come back to a
row reading `done`, read the finished record — why it ended, where it stood against both ceilings,
what it spent per model — and then open the same file in their own editor with the hub stopped.

**How each story advances it**: US1 — the record read back at the end, and why it ended, is the
second half of the scenario; US2 — the figures rising and the lines arriving are the first half;
US3 — the file opened in the editor is its last step, and it is what proves the browser was a window
rather than the place the information lived.

**Split proposed?** No → proceed to `/speckit-tasks`. The three stories are one scenario read at
three moments — during the run, after it, and without Grimoire — not three scenarios. Nothing in US2
or US3 the owner would exercise separately: watching a run is the same run they then read back, and
the file they open is the one they just read.

## Phase PRs *(mandatory)*

| Phase | What it does | Tasks | Branch | PR |
| --- | --- | --- | --- | --- |
| 1 — Setup | **Does not exist.** No new package, no new project, no analyzer change; a setup task with nothing to set up has no consumer (II.1) | — | — | — |
| 2 — Foundational: the record | `IRunRecord` and `MarkdownRunRecord`; `RecordText` and its fence rule; `AgentTranscript`'s three new message kinds; `RunReport`'s one new delegate; the conductor writing head, moments and tail; the model onto the run; the figures in the store | T001–T027 | `003-live-run-record-phase-2-record` | not opened yet |
| 3 — US1: read back a run | The row's model and figures, the record endpoint, and the two pages that show a finished run. Retires ACCESS-002 **with the five tests that carry it** | T028–T038 | `003-live-run-record-phase-3-read-back` | not opened yet |
| 4 — US2: watch a run live | Rows updated in place without moving, segments appended without disturbing what is being read | T039–T043 | `003-live-run-record-phase-4-live` | not opened yet |
| 5 — US3: without Grimoire | The two assertions nothing else makes: a record asks nothing of the wiki, and the bytes the page shows are the bytes on disk | T044–T045 | `003-live-run-record-phase-5-without-grimoire` | not opened yet |
| 6 — Closing | `/speckit-converge` once (Governance 2), both gates, the capability files, `docs/trace.md`, `docs/decisions.md` (DEC-026…DEC-033), the mutation survivors, then the owner's acceptance run | T046–T055 | `003-live-run-record-phase-6-closing` | not opened yet |

Each PR targets the feature branch and is merged by the agent before the next phase starts, once it is
green and its review is closed (I.10, I.11).

**Why the record is Foundational and not part of US1.** All three stories consume it — US1 reads it
back, US2 watches it fill, US3 opens it in an editor — so it is the foundation the stories stand on,
which is what the tasks template's Foundational phase is for. Putting it inside US1's phase would have
made one PR of 38 tasks; splitting it out by layer *instead of* by story would have left every phase
without a user-observable result. The phases below it are the stories in their own priority order, and
the first of them, phase 3, is the point at which OUT-02 is exercisable by hand — the first place the
owner could stop and still have what they asked for.

**Why ACCESS-002 is retired in phase 3 and not in phase 2**: five tests carry that ID
(`docs/trace.md`), and `trace-check` fails on a test carrying a retired one. The retirement and those
tests move in the same PR. Two of them — `List_ShowsNothingBeyondTheState` and
`Report_CarriesNothingBeyondTheState` — assert the negative this feature undoes and are deleted with
their requirement; two are retargeted to ACCESS-005; and
`Report_CarriesNoRunIdentifier_WhileAFailureIsUnacknowledged` keeps no requirement ID, because no run
identifier reaching the browser is now a design property rather than a requirement.

## Project Structure

### Documentation (this feature)

```text
specs/003-live-run-record/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── README.md
│   ├── hub-http-api.md  # supersedes 002's
│   └── run-record.md    # new — the port and the file's shape
├── checklists/
│   └── requirements.md
├── spec.md
└── tasks.md             # /speckit-tasks, not this command
```

### Source Code (repository root)

Only what this feature adds or changes is marked; everything else stands.

```text
src/Grimoire.Runs/
├── ISubmissionStore.cs           # changed: StoredRun gains the model and the three figures
├── IRunRecord.cs                 # NEW: the port, the frame and the moments
├── RecordText.cs                 # NEW: head, moment and tail rendered to Markdown, and the
                                  #      fence rule. Pure — no filesystem, so the shape the
                                  #      browser depends on is provable in the Fast suite
├── RunStateMachine.cs            # changed: Run carries its model, its tool-call count, why it ended
├── Submission.cs                 # changed: the figures read together with the state
├── SubmissionBoard.cs            # changed: the figures written under the one lock
└── Adapters/
    ├── SqliteSubmissionStore.cs  # changed: three columns, and the columns added where missing
    └── MarkdownRunRecord.cs      # NEW: the only place a record file is written

src/Grimoire.Agent/
├── IAgentHarness.cs              # changed: RunReport gains one delegate for a moment
└── Adapters/
    ├── AgentTranscript.cs        # changed: tool_use, tool_result and text blocks read
    └── HarnessProcess.cs         # changed: the new events reported through RunReport

src/Grimoire.Hub/
├── HubApplication.cs             # changed: the record adapter put at its port
├── Program.cs                    # changed: the record directory under --state
├── RunConductor.cs               # changed: head, moments and tail written; the nudge recorded
├── Api/
│   ├── SubmissionsEndpoints.cs   # changed: the model and the figures in SubmissionView
│   └── RunRecordEndpoint.cs      # NEW: GET /api/submissions/{id}/record
└── wwwroot/
    ├── index.html                # changed: the two figures and the link to a run
    ├── app.js                    # changed: rows updated in place
    ├── run.html                  # NEW: the record view
    └── run.js                    # NEW: polls the record and appends its moments

tests/Grimoire.Fast.Tests/        # RunRecordTests, RunFrameTests, RunNarrativeTests, RunFiguresTests,
                                  # InMemoryRunRecord, RecordedTranscript (the three probed lines)
tests/Grimoire.Contract.Tests/    # MarkdownRunRecordTests, SqliteSubmissionStoreTests (the columns)
tests/Grimoire.E2E.Tests/         # RunRecordViewTests, SubmissionStatesTests (changed)
```

**Structure Decision**: unchanged from `001-first-ingest` — three bounded contexts plus a composition
root. This feature adds one port and one adapter, both in the RUNS context, because a record is what a
run did and the filesystem is an external system (V.2). The Agent context gains no port and no
adapter: the three new moments are read by the transcript that already reads the protocol, and
reported through the `RunReport` delegates that already carry every other fact about a run. The hub
stays the only project that knows all three contexts and the only place that decides anything.

## Quickstart — the owner's acceptance run *(mandatory)*

**Outcome exercised**: OUT-02 — see for every run what it did, why it ended and what it cost
(and OUT-16 — watch what the agent is doing while a run is in progress)

**Real external systems in place**: a signed-in `claude` on `PATH` with no `ANTHROPIC_API_KEY`
(DEC-001, DEC-009); a real wiki in a git repository the owner keeps; the owner's own
purpose description; a pinned model id (DEC-010).

**Steps the owner runs**: `./scripts/run-hub.sh`, then paste a text into the page and submit it —
and stay on the page while the run proceeds.

**What the owner must see**: the row turns `running` and carries the model, a rising token figure and
a rising count of tool calls, and the list does not move as they rise. Opening the run shows the frame
and then tool calls, their results and the agent's own text arriving in order while they watch. When
the run ends the row reads `done` with its final figures, and the record holds why it ended, where the
run stood against both ceilings and what it spent per model. With the hub stopped, the same run opens
in the owner's editor as Markdown, holding the same thing — and nothing about the record is anywhere
in the wiki. The full steps, including the failed-run and restart cases, are
[quickstart.md](quickstart.md).

## Complexity Tracking

No violation of the Constitution Check to justify. Two costs are carried openly rather than as
exceptions, and both are recorded above and in `research.md`:

| Cost | Why accepted | Simpler alternative rejected because |
| --- | --- | --- |
| Records grow without bound and Grimoire never removes one | The owner decided a tool result is kept whole (spec, Clarifications), and retention is not an outcome — clearing them is the user's business, as undo in the wiki is | Cutting results or capping the file loses exactly the long run one most wants to read (research.md R-04) |
| `run.js` reads the record's own line shape, so the record's segmentation is load-bearing for the browser | It is what keeps the record the single place the information lives; a second, machine-shaped copy of the run is what the brief forbids | Serving the run as JSON beside the file would be a second place where the information lives (brief §3) |
