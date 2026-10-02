# Implementation Plan: Ask the Wiki

**Branch**: `004-ask-the-wiki` | **Date**: 2026-09-27 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/004-ask-the-wiki/spec.md`

## Summary

A third page is a chat: the user asks the wiki a question, and a run of its own — read-only, with no
record and no row in the submissions list — answers it in the chat while they read. The answer grows
as the agent writes it, the tool calls it made sit folded underneath, what the question cost stands
beside it against its ceiling, and every wiki page the answer names opens in the editor the wiki is
open in. Polling is gone from the whole browser surface in the same feature: the chat, the
submissions list and the run record are all *sent* what happens, over Server-Sent Events.

**Outcome advanced**: OUT-03 — ask a question and get an answer with references to wiki pages

**Slice addition**: **a new operation** — answering a question from the wiki. The chat is the door
that operation is reached through, not a second addition; a question's run is the same spawned agent
every run already is, reaching the same wiki through the same tools, and the editor link is a URL
the operating system resolves rather than a system Grimoire talks to. *(Constitution I.6 — never
two.)*

## Technology decisions *(mandatory)*

`docs/decisions.md` was read first. DEC-009, DEC-010, DEC-011, DEC-013, DEC-014, DEC-015, DEC-018,
DEC-019, DEC-023, DEC-026, DEC-030 and DEC-031 bind this feature — DEC-031 as this feature amended
it — and none of them is departed from. What was departed from is R-04's own sentence that no table
would be rebuilt (research.md R-04, revised).
**DEC-032 is superseded**, which the brief names as the owner's decision and [research.md](research.md)
R-01 gives the constraint behind. What is new:

| Decision | Choice | Reason | Binds later features | Departs from |
| --- | --- | --- | --- | --- |
| How the browser learns what happened | Server-Sent Events: one `text/event-stream` per view, served by ASP.NET Core 10's own `TypedResults.ServerSentEvents`, read with `EventSource`. Each stream opens with a full snapshot, then sends increments. Every event's `data` is one line of JSON. Polling removed from `app.js` and `run.js` | The direction is one-way, which is the shape `EventSource` has. SignalR's browser client means npm or a vendored script, which DEC-019 rules out, and its duplex, transports and stateful reconnect have no consumer here (II.1). The snapshot-on-connect is what answers ACCESS-007's reconnect clause without a replay buffer. Verified on the installed SDK: `ServerSentEventsResult<T>` over `System.Net.ServerSentEvents.SseItem<T>` is in `Microsoft.AspNetCore.App.Ref/10.0.11` — framework, so no package and nothing of it is tested (III.8) (R-01) | yes | **DEC-032** — polling is replaced, on the chat *and* on the two views that poll today |
| Where a chat lives | One `Chat` object in the composition root, in memory, for as long as the hub runs. Replaced whole by a new chat. Nothing of it on disk, in the wiki or outside it | QUERY-005 makes "gone after a restart" a requirement rather than a limitation, so a persistent implementation would contradict the spec. No port and no interface: nothing outside the process, and no second implementation (II.4) — a plain class, as `SubmissionBoard` is one. Exactly one, so "starting a new chat empties both tabs" falls out rather than being built (R-02) | yes | none |
| How a question reaches the queue | `SubmissionBoard` becomes **`RunBoard`** and holds one ordered list of `Queued`, of which `Submission` and `Question` are the two kinds. The queue rule is unchanged and reads that one list; `RunBoard.All` still answers with the submissions alone | RUNS-002 orders waiting work by when it was made *across* both kinds, and one list carries that order intrinsically — the same argument `TakeNext` already makes for list position over a non-monotonic `SubmittedAt`. Two lists would need a sequence number of our own, a second ordering mechanism beside the one the list already is. Two real implementations exist, which is when II.4 allows the abstraction. The rename itself is the owner's decision, against keeping the name: a board named for one of the two things it holds would be a name that lies, in a tree whose comments carry the reasons (R-03) | yes | none |
| What survives a stop of a question's run | Its **row** — identifier, start, grant, the agent's process identity, the figures — with `StoredRun.SubmissionId` becoming `QueuedId` and a null meaning a question caused it. The question's text, its answer and its steps are written nowhere | RUNS-006 has start-up terminate the agent of every run that was in progress, before anything else runs; a run with nothing on disk would leave an orphaned `claude` holding the granted tools with no ceiling on it. The row is also where the answer's cost comes from (DEC-030), so nothing counts twice and RUNS-010 is untouched. No column is renamed. A `runs` table that declares `submission_id NOT NULL` is rebuilt once and the file then reads `user_version` 1, and DEC-031's `PRAGMA table_info` + `ALTER TABLE` adds what is missing — DEC-031 as amended (R-04) | yes | none |
| How the streams are fed | One hub-owned `LiveUpdates`, a channel per subscriber. `RunBoard` raises one `Changed` delegate the composition root supplies; `RunConductor` publishes a record's growth and the chat's | `RunConductor.NextRunMayStart` is already a delegate the root supplies so a context can say something happened without knowing who listens — one precedent followed rather than a second mechanism beside it (II.1). The record stream sends the bytes past a per-subscriber offset, read through `IRunRecord.Read`, so `MarkdownRunRecord` stays the only thing that renders a record: two renderers of one record can disagree, which is the seam DEC-030 named (R-05) | yes | none |
| The grant for a question's run | Read-only, and served by a **second MCP endpoint** `/mcp/questions/{runId}` carrying only `list_pages` and `read_page`. `ToolGrant` carries the endpoint segment beside the names | DEC-011 makes tools deny-by-default **by construction, not by an allow-list of names**; naming two of five in `--allowed-tools` would leave `write_page` served at that run's endpoint, one flag away — the allow-list over a larger surface DEC-011 rejected. It also needs no fresh signed-in probe, and DEC-021's budget of four is spent. GUARD-001's equality check then guards it for free: an endpoint serving a write tool ends the run failed before its first model call (R-06) | yes | none |
| Where the answer ends and the steps begin | Every piece of the agent's own text is the answer, appended as it arrives; the steps are the tool calls and what they returned, folded under it | ACCESS-007 binds three things at once, and the third decides it: content arriving must not move what the user is already reading. "The final turn's prose" cannot stream; "the newest prose, demoted when a call follows" moves text the user has read. Of the two rules that survive that, the owner took the one with no exception in it, over the variant that folds away the opening block. Nothing new is parsed — `AgentTranscript` already reports the three moments (DEC-028) and stays the only reader of the protocol (R-08) | yes | none |
| What a follow-up's run is given | The whole chat so far — each earlier question and the answer text it produced — rendered into the prompt by `InstructionLoader`. The steps are not included. Nothing is trimmed and there is no cap | Owner's decision, and the constraint behind it: a chat too large for a dispatch ends that run failed and the chat says so (QUERY-006) — the path every failed run takes, with a remedy that exists, while dropping the oldest turns would answer a follow-up in the light of less than the chat shows, silently. V.1 keeps one thing putting text into a prompt. The steps are for the user to check, not context the next run needs, and a run's tool results are the largest thing in a chat. Compressing the conversation is **proposed as a Later outcome** below rather than built (I.4, II.1) (R-07) | no | none |
| How a referenced page opens in the editor | Two start-up inputs, `--vault <name>` and `--vault-root <directory>`; the link is `obsidian://open?vault=<name>&file=<path relative to --vault-root>`. The agent writes ordinary relative Markdown links and the **browser** rewrites them; the target is the page's path relative to the wiki's root | Nothing new goes into a wiki page, and the link form lives in one place. §6.1 writes a link relative to the page it sits on and an answer sits on no page, so the wiki's root is the one anchor it has. The absolute-path form was rejected: the owner wants the directory the paths hang off to be theirs to define. Missing inputs do not refuse the question — the answer is still an answer without the click (R-09) | yes | none |
| The question instruction | `instructions/question.md`, reached by `--question-instruction <path>` with a default, assembled by `InstructionLoader`. `StartUpInputs` gains a third flag. Where the wiki holds nothing about the question, it tells the agent to **say so plainly, name what it looked at, and stop** | The same shape `--instruction` already has, because both are Grimoire's own and versioned here; V.1 keeps `InstructionLoader` the only thing that puts text into a prompt, and changing either instruction is an owner decision named in the PR. A submission is refused on the ingest instruction and the purpose description, a question on the question instruction and the purpose description, each refusal naming exactly one thing. The no-coverage clause is the owner's: an answer from the model's own knowledge would not rest on the wiki, which is what QUERY-004 asks of it. It adds **no requirement id** — it is an acceptance criterion of that existing clause, and a clarification refines rather than extends (IV.8) (R-10) | yes | none |
| The third page and its navigation | `chat.html` + `chat.js` in `wwwroot/`, and a line of links on each of the three pages | `docs/ux.md` withholds navigation chrome "until a second job exists", and a third job exists now — ACCESS-010 is its consumer. It stays what the pages already are: text-first, a line of links, no bar and no menu. DEC-019 is untouched: three HTML files and three scripts, no bundler and no npm (R-14) | no | none |

A reason names the constraint or the evidence, never "owner decision" alone: where the owner
decided, the constraint they decided on is named.

**Test time budget** *(Constitution III.7, gate `time-budget`)*: Fast under 15 s, Contract under
90 s, execution only, measured in CI. Enforced by the test platform's own session timeout, as
DEC-008 settled — `--timeout 15s` and `--timeout 90s`. No purpose-built tooling. This feature adds no
test that waits for real time: the chat is in memory, the moments come from recorded lines, elapsed
time comes from `FakeTimeProvider` (DEC-018), and a stream is read as an `IAsyncEnumerable` in the
Fast suite rather than over a socket. No new `requires=signin` test — DEC-021's four are spent, and
R-06 chose the design that needs no fresh evidence from the real CLI.

## Technical Context

**Storage**: a question's **run** is a row on the existing `runs` table in `<state>/submissions.db`
(DEC-023), with `submission_id` null where a question caused it. The question, its answer and its
steps are stored nowhere — the chat is memory (R-02). A question's run has **no record**: RUNS-007
as this feature rewords it gives one to a run a submission causes. Nothing is written into the wiki
at all.

**Target Platform**: self-hosted single instance, one user, loopback only (`docs/product.md` §2,
DEC-014).

**Project Type**: service with a browser front end — three bounded contexts plus a composition root,
static files under `wwwroot/` with no build step (DEC-019).

**Constraints**: no bundler and no npm, so no SignalR browser client. `AgentTranscript` stays the
only reader of the CLI protocol (V.2) and `InstructionLoader` the only thing that puts text into a
prompt (V.1). A question's run must not be able to write, delete or move anything in the wiki, by
construction rather than by instruction (GUARD-005). One run at a time, in order, with a failure
blocking the next, is untouched (RUNS-002, RUNS-003). Cost is input-token equivalents and never
currency (DEC-015). No reasoning is shown, because the CLI gives none (RUNS-009).

**Scale/Scope**: a chat is tens of questions at most before the user starts a new one; a question's
run makes a handful of read calls. The chat's prompt grows with the chat and is not trimmed (R-07).
Three browser tabs at once is the realistic upper bound on subscribers per stream.

## Constitution Check *(mandatory)*

Completed before design and re-checked after it. Every verdict held; the design pass added two
things to the rows below rather than changing one — that `RunBoard`'s `Queued` abstraction is the
II.4 case of two real implementations and says so, and that the four reworded requirements must
travel in the same PR as the tests that carry them or `trace-check` fails (IV.3).

| Principle | Touched? | How this plan satisfies it / why it is not touched |
| --- | --- | --- |
| I. Purpose and Focus | touched | One outcome named (OUT-03), no blocking open question, one new operation and no second (the chat is its door, the editor link is a URL the OS resolves), one acceptance scenario that all three stories advance, phases and their PRs named below before implementation starts |
| II. Simplicity | touched | Every part has a consumer in this feature: the stream has three pages, the read-only endpoint has a question's grant, the `Queued` base has two real implementations (II.4), the vault inputs have the link. A prompt cap, a replay buffer, a chat per browser, a record for a question and a list of past chats were each weighed and left out with the trigger named (R-02, R-07, R-12). `LiveUpdates` and `Chat` are plain classes, not ports |
| III. Testing | touched | Eleven requirements: ten `test`, one `review` (QUERY-004, with the spec's "Why review" row). Each at the lowest level that can prove it (R-11); the schema half is Contract, the browser halves E2E. No new `requires=signin` test, so DEC-021's budget is untouched |
| IV. Visibility | touched | `docs/capabilities/query.md` is created and QUERY-001…006 registered in phase 3, GUARD-005 in phase 2, ACCESS-007…010 in phases 3–5 — each before the first test that carries the id. RUNS-005/007/008/009 are reworded in phase 2, in the same PR as the tests for the cases that made them change. `docs/trace.md` regenerated at close |
| V. Design Invariants | touched | V.1: `InstructionLoader` assembles both prompts and nothing else puts text into one; the question instruction is versioned here and named in the PR that changes it; Grimoire writes nothing into the wiki in this feature at all. V.2: the CLI protocol only in `AgentTranscript`, the process only in `HarnessProcess`, SQLite only in `SqliteSubmissionStore`, the wiki's files only in `FileSystemWikiStore`, the record file only in `MarkdownRunRecord`. V.3: a question's grant is recorded with its run (GUARD-003) and is the whole of what exists for it (GUARD-005) |
| Governance | touched | `/speckit-converge` runs once in the closing phase (Governance 2); review findings classified per Governance 3; the second reviewer is the repository's Copilot review (DEC-025). **The instruction files are touched** — `instructions/question.md` is new — which I.11 makes an owner-review case and V.1 makes a thing named in the PR description |

## Acceptance scenario *(mandatory)*

**Scenario**: with a real wiki that already has pages and a signed-in `claude`, the owner opens the
chat, types a question and sends it; the answer forms under it while they read, with what it has
spent standing beside it against its ceiling and the chat's total below; they unfold one step to see
which page the agent opened and what came back, click a page the answer names and land on that page
in their own Obsidian vault, ask a follow-up that only makes sense in the light of the first answer
and get one that takes it into account — and then start a new chat, which leaves nothing of the old
one, with the wiki byte for byte what it was before they asked anything.

**How each story advances it**: US1 — the question sent and the answer forming with its cost is the
first half; US2 — the step unfolded and the page opened in Obsidian is the middle, and it is what
turns the answer from a claim into something checked; US3 — the follow-up and the new chat are the
last step, and they are what make it a conversation rather than one answer.

**Split proposed?** No → proceed to `/speckit-tasks`. The three stories are one conversation read at
three moments — asking, checking, and carrying on — not three scenarios. Nothing in US2 or US3 the
owner would exercise separately: the step they unfold is under the answer they just watched form,
and the follow-up is asked in the same chat.

## Phase PRs *(mandatory)*

| Phase | What it does | Branch | PR |
| --- | --- | --- | --- |
| 1 — Setup | **Does not exist.** No new package, no new project, no analyzer change: SSE is in the framework (R-01) and the third page is two static files. A setup task with nothing to set up has no consumer (II.1) | — | — |
| 2 — Foundational: the browser is sent what happens | `LiveUpdates`, the two streams for the views that exist, `RunBoard.Changed`, polling removed from `app.js` and `run.js`. ACCESS-005 and ACCESS-006 proven again under the stream, their wording unchanged. **Supersedes DEC-032** | `004-ask-the-wiki-phase-2-sent-not-polled` | not opened yet |
| 3 — Foundational: a question is a run | `Queued`, `Question`, the `SubmissionBoard` → `RunBoard` rename, the store's run without a submission and its schema change, the read-only grant and `/mcp/questions/{runId}`, `instructions/question.md` and the loader's second prompt, the conductor sending a question's moments to the chat instead of a record. Registers GUARD-005; **rewords RUNS-005, RUNS-007, RUNS-008 and RUNS-009 with the tests for the cases that made them change** | `004-ask-the-wiki-phase-3-a-question-is-a-run` | not opened yet |
| 4 — US1: ask, and read the answer as it forms | `chat.html` + `chat.js`, the chat stream, the intake and its refusals, the cost beside each question and the chat's total. Creates `docs/capabilities/query.md` with QUERY-001…003 and QUERY-005's holding clause; registers ACCESS-007, ACCESS-008 and ACCESS-010 | `004-ask-the-wiki-phase-4-ask-and-read` | not opened yet |
| 5 — US2: what it rests on, and opening a page | The steps folded under the answer, `instructions/question.md`'s reference shape (QUERY-004), the vault inputs and the `obsidian://` rewrite, and the case where they are missing. Registers QUERY-004 and ACCESS-009 | `004-ask-the-wiki-phase-5-references` | not opened yet |
| 6 — US3: ask back, then put it away | The chat handed to a follow-up's run, a new chat, a question waiting its turn, and a failed question said so in the chat and acknowledged from it. Completes QUERY-005; registers QUERY-006 | `004-ask-the-wiki-phase-6-conversation` | not opened yet |
| 7 — Closing | `/speckit-converge` once (Governance 2), both gates, the capability files reconciled, `docs/trace.md`, `docs/decisions.md` (the new entries and DEC-032 moved under "Superseded"), `CLAUDE.md`'s architecture paragraph brought up to date after the rename, then the owner's acceptance run | `004-ask-the-wiki-phase-7-closing` | not opened yet |

Each PR targets the feature branch and is merged by the agent before the next phase starts, once it
is green and its review is closed: reviewed by someone other than its author, every finding answered
on the PR, the round decision recorded, the owner's review requested where I.11 says so (I.10,
I.11). **Phase 5 touches an instruction and phase 3 creates one**, so both request the owner's
review and name it in the PR description (I.11, V.1).

**Why the stream comes first and alone.** It is the one change that touches code no story of this
feature owns — `app.js` and `run.js` — and ACCESS-005 and ACCESS-006 have to go on being proven
across it. Landing it with the chat would put a rewrite of two working views inside the PR that
introduces a third, and a failure in either would be ambiguous. Landed first, the feature branch is
green with polling gone and nothing new on the screen, and every phase after it inherits a browser
that is sent what happens.

**Why "a question is a run" is Foundational and not part of US1.** All three stories consume it —
US1 asks one, US2 reads what it did, US3 asks a second — and none of them is reachable until a
question can queue, run under its own grant and report into a chat. Splitting it by layer *instead*
of by story would leave every phase without a user-observable result; phase 4 is the first point at
which OUT-03 is exercisable by hand, and the first place the owner could stop and still have what
they asked for.

**Why the reworded requirements travel with their tests.** `trace-check` reads requirement ids off
the built assemblies against `docs/capabilities/`. RUNS-005, RUNS-007, RUNS-008 and RUNS-009 keep
their ids, so no test breaks on the rewording itself — but the two new cases (a run that changes
nothing in the wiki ends done; a question's run leaves no record) are the proof that the rewording
is real, and they land in the same PR as the sentences.

## Project Structure

### Documentation (this feature)

```text
specs/004-ask-the-wiki/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── README.md
│   ├── hub-http-api.md  # supersedes 003's — the three streams and the chat's endpoints
│   └── question-run.md  # new — the grant, its endpoint, and what a question's run is given
├── checklists/
├── spec.md
└── tasks.md             # /speckit-tasks, not this command
```

### Source Code (repository root)

Only what this feature adds or changes is marked; everything else stands.

```text
instructions/
├── ingest.md                     # unchanged
└── question.md                   # NEW: what a question's run is told (QUERY-004, V.1)

src/Grimoire.Runs/
├── Queued.cs                     # NEW: what the queue rule reads — waiting, under way, an
                                  #      unacknowledged failure. Two real implementations (II.4)
├── Submission.cs                 # changed: derives from Queued; nothing else moves
├── Question.cs                   # NEW: a question that was accepted, and the run it was given.
                                  #      No state of its own — its four values are its run's (R-03)
├── SubmissionBoard.cs → RunBoard.cs  # renamed: one ordered list of Queued; `All` still answers
                                  #      with the submissions alone
├── ISubmissionStore.cs           # changed: StoredRun.SubmissionId → QueuedId; AddRun, and the
                                  #      runs with no submission read back at start-up
└── Adapters/
    └── SqliteSubmissionStore.cs  # changed: submission_id nullable in meaning, the new reads

src/Grimoire.Agent/
└── ToolGrant.cs                  # changed: ForQuestion, and the endpoint segment beside the names
                                  #      — the grant and the door that serves it are one value

src/Grimoire.Hub/
├── Chat.cs                       # NEW: the one chat, in memory. Questions, their answers as they
                                  #      form, and the steps under each (QUERY-005)
├── ChatIntake.cs                 # NEW: a question accepted or refused, and dispatched without the
                                  #      user waiting (QUERY-001, QUERY-003)
├── LiveUpdates.cs                # NEW: a channel per subscriber, and the three things that publish
├── InstructionLoader.cs          # changed: the question prompt, and the third start-up flag
├── RunConductor.cs               # changed: a question's run gets no record, and its moments go to
                                  #      the chat; the record's growth and the chat's are published
├── HubApplication.cs             # changed: the chat, LiveUpdates and the second MCP route put in
├── Program.cs                    # changed: --question-instruction, --vault, --vault-root
├── Api/
│   ├── SubmissionsEndpoints.cs   # changed: the list's stream beside the list
│   ├── RunRecordEndpoint.cs      # changed: the record's stream beside the record
│   └── ChatEndpoints.cs          # NEW: ask, start a new chat, acknowledge a failed question, and
│                                 #      the chat's stream
├── Mcp/
│   ├── WikiToolsServer.cs        # changed: the two read tools' bodies live once
│   └── WikiReadToolsServer.cs    # NEW: list_pages and read_page, and nothing else exists there
└── wwwroot/
    ├── index.html, app.js        # changed: sent rather than polled; the line of links
    ├── run.html, run.js          # changed: sent rather than polled; the line of links
    ├── chat.html                 # NEW: the conversation
    └── chat.js                   # NEW: the chat stream, the answer growing in place, the steps
                                  #      folded, and the obsidian:// rewrite

tests/Grimoire.Fast.Tests/        # ChatTests, QuestionTests, RunBoardTests (renamed),
                                  # QuestionGrantTests, QuestionPromptTests, LiveUpdatesTests
tests/Grimoire.Contract.Tests/    # SqliteSubmissionStoreTests (a run with no submission)
tests/Grimoire.E2E.Tests/         # AskingTheWikiTests, AnswerReferencesTests, ChatLifetimeTests,
                                  # SubmissionStatesTests and RunRecordViewTests (changed: streamed)
```

**Structure Decision**: unchanged from `001-first-ingest` — three bounded contexts plus a
composition root. This feature adds no port and no adapter. The chat, the stream and the chat's
endpoints are the hub's, because the hub is the only project that decides anything and the chat is
decided nowhere else; `Queued`, `Question` and the board are the RUNS context's, because what may
run and in which order is that context's judgment (RUNS-002); the read-only tool server is the hub's
beside the one it already serves, because the wiki is reached only through the tools the hub serves
for a run (DEC-013). The Agent context gains one thing — a grant that names its own endpoint — and
no new reader of the protocol: `AgentTranscript` already reports the three moments a chat shows
(DEC-028).

## Quickstart — the owner's acceptance run *(mandatory)*

**Outcome exercised**: OUT-03 — ask a question and get an answer with references to wiki pages

**Real external systems in place**: a signed-in `claude` on `PATH` with no `ANTHROPIC_API_KEY`
(DEC-001, DEC-009); a real wiki in a git repository the owner keeps, with pages already in it from
earlier ingests; the owner's own purpose description; a pinned model id (DEC-010); Obsidian
installed, with the vault that holds the wiki open, and `--vault` / `--vault-root` set to it.

**Steps the owner runs**: `./scripts/run-hub.sh`, open the chat from the submit page, type a
question about something the wiki already covers and send it — then stay on the page.

**What the owner must see**: the question appears at once and the answer starts arriving under it
while the agent is still writing, growing in place with nothing they are reading moving; what the
question has spent stands beside it against its ceiling, and the chat's total below carries no
ceiling. The answer names wiki pages inside its own prose. Unfolding one step under the answer shows
that call and what came back, read the way a run's record reads. Clicking a page the answer names
opens that page in Obsidian, in their own vault. A follow-up asked in the same chat is answered in
the light of the first. Starting a new chat leaves it empty with nothing of the old one reachable.
And `git status` in the wiki is clean: no page, no index, no log entry — the wiki is byte for byte
what it was before they asked anything. The full steps, including the waiting-its-turn, failed-
question and missing-vault cases, are [quickstart.md](quickstart.md).

## Owner decisions taken for this plan *(2026-09-27)*

Four questions the spec left to the plan, or that the plan surfaced, were put to the owner before
Phase 1 closed. Each is written into the decision it belongs to above and argued in `research.md`;
they are gathered here so that what was *chosen against* is on the record too.

| Question | Decided | Declined, and why it was on the table |
| --- | --- | --- |
| Where the answer ends and the steps begin | Every piece of the agent's prose is the answer; the tool calls are the steps (R-08) | *Only the final turn's prose* — closer to the brief's walkthrough, but it cannot stream, which is the whole of US1. *Everything but the opening block* — also decidable live, and reads closer to the walkthrough; declined for the rule with no exception in it |
| How far into the existing code a question's queueing may reach | `SubmissionBoard` → `RunBoard` over one ordered list of `Queued` (R-03) | *Keep the name* — no rename diff, at the cost of the one class that decides what may run being named after half of what it holds. *Two lists and a counter* — no rename and no base class, at the cost of a second ordering mechanism beside the list that already is one |
| What a chat too large for a dispatch does | Nothing is trimmed; the run ends failed and the chat says so (R-07) | *Drop the oldest turns* — no run fails, but a follow-up is answered in the light of less than the chat shows, silently. *Refuse the question* — honest, but needs a fourth refusal in QUERY-003 and so a requirement the spec does not have |
| What the question instruction says when the wiki holds nothing | Say so plainly, name what was looked at, stop (R-10) | *Answer from the model's own knowledge, marked* — useful in the moment, but the answer would no longer rest on the wiki, which is what QUERY-004 asks of it |

### Proposed to the owner as a Later outcome *(Constitution I.4)*

**Compressing a conversation so a long chat can go on being asked.** It advances no outcome that is
Now, so it is not specified here — it is proposed for `docs/product.md` §Outcomes, which is
owner-written and which an agent edits only for an outcome's status and spec reference (I.1, IV.4).

- **What it would be**: a chat that has grown past what a dispatch can carry is summarised rather
  than sent whole, so that a follow-up is still answered in the light of what came before.
- **Trigger that would promote it**: the first time a real chat fails for that reason — the owner
  sees a question read *got no answer* with a reason that is the prompt's size, rather than a
  ceiling or a dead process.
- **Why not now**: until that happens it is a mechanism with no consumer (II.1). It is also a second
  piece of agent judgment about what may be dropped from a conversation, which is the kind of thing
  `docs/product.md` settles before a plan does.

## Complexity Tracking

No violation of the Constitution Check to justify. Three costs are carried openly rather than as
exceptions, and all three are recorded above and in `research.md`:

| Cost | Why accepted | Simpler alternative rejected because |
| --- | --- | --- |
| Two MCP tool types expose `list_pages` and `read_page` — about a dozen attributed lines, with the bodies living once | It is what makes a question's grant deny-by-default **by construction** rather than an allow-list over a larger surface, which is DEC-011's standing decision | A narrower `--allowed-tools` leaves `write_page` served at that run's endpoint one flag away, and would need a fresh signed-in probe that DEC-021's spent budget has no room for (R-06) |
| The `SubmissionBoard` → `RunBoard` rename touches the hub and every suite that names it | A board named for one of the two things it holds would be a name that lies, in a tree whose comments carry the reasons; the change is mechanical and lands in one phase | Keeping the name would leave the one class that decides what may run describing only half of what it holds (R-03) |
| A chat's prompt grows without bound and nothing trims it | The owner asked for what the first version does and no mechanism for later. A chat too large ends that run failed, which the chat says, and the remedy — a new chat — already exists | A cap, a window or a summary is a mechanism with no consumer until a real chat hits the limit (II.1); the trigger for building one is the first time the owner sees a question fail for that reason (R-07) |
