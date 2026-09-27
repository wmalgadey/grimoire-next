# Research: Ask the Wiki

Phase 0 of [plan.md](plan.md). One entry per decision the plan had to take, each with what was
weighed and what settled it. `docs/decisions.md` was read first; DEC-009, DEC-010, DEC-011,
DEC-013, DEC-014, DEC-015, DEC-018, DEC-019, DEC-023, DEC-026, DEC-030 and DEC-032 all bear on this
feature, and exactly one of them is departed from — DEC-032, which the brief says is superseded.

The brief's §6 constraints are binding and are treated as such; its ideas are weighed like any
other option. The brief's six open questions are answered in R-03, R-06, R-07, R-09, R-12 and R-13;
four of them the spec's Clarifications already settled with the owner, and those entries say so
rather than deciding again.

---

## R-01 — The browser is sent what happens: Server-Sent Events

**Decision.** One `text/event-stream` per view, served by ASP.NET Core 10's own
`TypedResults.ServerSentEvents`, read in the browser with `EventSource`. Three streams, one per
thing a page shows: the submissions list, one run's record, and the chat. The question itself goes
in by `POST`, as every other command does. Polling is removed from `app.js` and `run.js` in the same
feature. **DEC-032 is superseded.**

**Why not SignalR** (the brief's named fallback). Its server half is in the framework, but its
browser half is `@microsoft/signalr` — npm, or a script vendored into `wwwroot/`. DEC-019 rules both
out, and it rules them out for a reason that has not changed: the browser surface is static files
with no build step, and a vendored client is a dependency with no version, no lockfile and no way to
tell what it is. SignalR also buys duplex, reconnection with state and several transports, none of
which has a consumer here (II.1): everything the browser sends is a command that fits a `POST`, and
everything it receives flows one way.

**Why SSE carries this.** Measured against what the feature needs:

| What is needed | What SSE gives |
| --- | --- |
| Server → browser only | Exactly the shape of `EventSource` |
| No npm, no bundler, no vendored script | `EventSource` is in every browser; nothing is added to `wwwroot/` but the two lines that open it |
| Reconnection after a dropped connection (ACCESS-007) | `EventSource` reconnects by itself, and this design answers a reconnect with a full snapshot (R-02), so no `Last-Event-ID` mechanism is built |
| No new package | Verified on the installed SDK: `Microsoft.AspNetCore.App.Ref/10.0.11` exports `ServerSentEventsResult<T>` over `System.Net.ServerSentEvents.SseItem<T>`. It is framework behaviour and is therefore not tested (III.8) — what is tested is what we put on the stream |

**What replaces the poll, exactly.** Every stream opens with one **snapshot** event carrying the
whole of what that view shows, and then sends increments as they happen. A browser that reconnects
gets a fresh snapshot, so it shows the chat as it then stands, "including what arrived while it was
away" (ACCESS-007) without anything being replayed from a buffer. Nothing is kept per subscriber
beyond how far through the record it has been sent (R-05).

**Every event's `data` is a single line of JSON.** SSE frames a payload per `data:` line, so a
multi-line body needs per-line framing on the way out and re-joining on the way in. One JSON line
has neither, the browser already parses JSON, and a record's or an answer's newlines travel as `\n`
inside a JSON string — which is the same escaping the record endpoint's consumers already live with.

**What this does not become.** No push to a browser that is not on the page, no service worker, no
notification. The stream exists while a page is open and ends when it closes.

---

## R-02 — Where the chat lives: in the hub's memory, and nowhere else

**Decision.** One `Chat` object held by the composition root for as long as the hub runs. It holds,
in order, the questions asked in it and, under each, the answer as it forms and the steps the agent
took. Nothing of it is written to disk — not in the wiki (Invariant 1, GUARD-005), not in
`<state>/`, not in a record (RUNS-007 as this feature rewords it). Starting a new chat replaces the
object whole.

**Why not a port.** II.4 allows an interface only at a port to something outside the process or
where two real implementations exist. There is neither: the chat is memory, and QUERY-005 makes
"gone after a restart" a *requirement* rather than a limitation, so a persistent second
implementation would contradict the spec rather than serve it. A plain class, as `SubmissionBoard`
is a plain class for the same reason.

**Why exactly one.** QUERY-005 says so, and the spec's Clarifications record why: a chat per browser
would make Grimoire tell one reader from another, which nothing else in this feature needs (II.1).
One object, every stream reading it, and "starting a new chat empties both tabs" falls out rather
than being built.

**Held while nobody is connected.** The chat is the hub's, not a subscriber's, so a run whose reader
walked away still writes into it and a browser that comes back reads it (spec, Clarifications). No
run is stopped for a missing reader — Grimoire has no way to stop a run except a ceiling, and
building one here would be a mechanism this feature does not otherwise need (II.1).

---

## R-03 — A question queues with the submissions, in one list, under one lock

**Decision.** `SubmissionBoard` becomes **`RunBoard`** and holds **one ordered list of `Queued`**,
of which `Submission` and `Question` are the two kinds. The queue rule — at most one run in
progress, an unacknowledged failure blocks, waiting entries start in the order they were made
(RUNS-002, RUNS-003) — is unchanged and now reads that one list. `RunBoard.All` still answers with
the submissions alone, so the submissions list is exactly what it was.

**Why one list and not two.** RUNS-002 orders waiting work by when it was made, *across* both kinds:
a question asked after a submission waits behind it and a submission made after a question waits
behind that. Two lists would need a sequence number of our own to order across them — a second
ordering mechanism beside the one the list already is. One list carries the order intrinsically,
which is the same argument `TakeNext` already makes for using list position rather than
`SubmittedAt` (that clock is not monotonic).

**Why an abstraction here is allowed.** II.4 permits one where two real implementations exist, and
two do: a submission is persisted, carries a text excerpt and appears in the browser's list; a
question is none of those. `Queued` holds only what the queue rule reads — waiting, under way, an
unacknowledged failure — which is the whole of what the two have in common.

**Why the rename.** OWNER DECISION, 2026-09-27: the rename is taken rather than avoided. A class
named `SubmissionBoard` that holds questions would be a name that lies, in a tree whose comments
carry the reasons. The rename is mechanical, lands in one phase, and `CLAUDE.md` is reconciled at
close. Keeping the old name over the same one list, and keeping two lists ordered by a counter of
our own, were both put to the owner and both declined — the second for the reason above.

**A question's four values are its run's.** ACCESS-007 asks the chat to show exactly one of waiting
its turn, being answered, answered, got no answer and why — which is the same four RUNS-001 already
names, read in the chat's words. So a question stores no state of its own: it has a run or it does
not, and that run has ended or it has not. Nothing new is kept, and the two can never disagree.

---

## R-04 — A question's run is kept in the store; the question is not

**Decision.** The `runs` table gains rows for question runs. `StoredRun.SubmissionId` becomes
`StoredRun.QueuedId` — the id of whatever caused the run — and the store gains `AddRun(StoredRun)`
for a run with no submission behind it, and `LoadRunsWithoutASubmission()`, read at start-up beside
`Load()`. The question's *text*, its answer and its steps are written nowhere.

**Why the run row survives while the chat does not.** RUNS-006 has start-up terminate the agent of
every run it reads as having been in progress, and it must do that before anything else runs. A run
with nothing on disk would leave an orphaned `claude` after a crash — holding the granted tools,
with no ceiling on it, on the owner's machine. The row is also where the answer's cost comes from
(DEC-030, RUNS-010), so nothing counts twice. This is the brief's own constraint and the research
confirms it has no cheaper form: the alternative is a second place that knows a run existed, which
is what DEC-030 rejected for the figures.

**What a start-up does with such a row.** Terminates the agent where the recorded identity is still
live (DEC-024, unchanged), then marks the run ended failed. **No tail is written**, because the run
has no record — RUNS-007 as reworded gives records only to runs a submission causes. Nothing is
restored into a chat: QUERY-005 empties it.

**How the column is added to an existing file.** `PRAGMA table_info` then `ALTER TABLE`, exactly as
DEC-031 settled and for the same reason — the owner's own `submissions.db` holds the ingests they
have already made. Renaming `submission_id` is avoided: SQLite's `ALTER TABLE … RENAME COLUMN`
exists, but the cheaper and more honest change is to make the existing column nullable in meaning —
a run whose `submission_id` is null was caused by a question. No column is renamed, no table is
rebuilt, and an older file comes back with its submissions intact.

**Revised while implementing** (phase 3, raised in review): the last sentence asked for something
SQLite cannot do. `CREATE TABLE IF NOT EXISTS` does not alter a table that is already there and the
`ALTER TABLE` step only *adds* columns, so a file an older Grimoire wrote keeps
`submission_id TEXT NOT NULL` — and the first question asked against it fails on the insert. Dropping
a `NOT NULL` constraint in SQLite means **rebuilding the table**, so "nullable in meaning, no table
rebuilt" cannot both hold.

**OWNER DECISION, 2026-09-27: the file goes.** A file whose `runs` table cannot hold a run with no
submission behind it is refused at start-up, naming itself so the owner knows what to delete. The
reason is that nothing runs Grimoire in production yet, so the rows such a file holds are the owner's
own test ingests — and a rebuild would be machinery carried for ever to keep a file nobody needs. The
first Grimoire that has users with files worth keeping revisits this.

Refusing to start is what the adapter already does with a file it cannot read: a state value it does
not know throws rather than being guessed at, for the same reason — every submission the user made is
in that file, and reading one of them wrongly is worse than not starting. **DEC-031's mechanism keeps
its consumer**: a file written by an earlier commit of this feature takes a null `submission_id` but
lacks the columns added after it, and `PRAGMA table_info` + `ALTER TABLE` is what brings it up to
date. A Contract test covers each of the two files.

---

## R-05 — How each stream is fed, and what an increment is

**Decision.** One hub-owned `LiveUpdates` object. Every subscriber is a
`System.Threading.Channels.Channel`, drained by the endpoint's `IAsyncEnumerable`. Three things
publish into it, each at the place that already knows:

**Revised while implementing** (phase 2, raised in review): the channel is **bounded at one signal,
with `DropWrite`**, where this entry first said unbounded. What is on it is a bare signal and not a
payload, and `next` reads the current state when the subscriber wakes — so one pending signal already
says everything a hundred of them would, and dropping the rest loses nothing. Unbounded, a subscriber
that fell behind cost a byte per change with no bound on it, which is what the class's own comment
denied. The write still cannot block: `Changed` is called by the board under its one lock and by the
conductor under a run's.

| What changed | Who says so | What subscribers get |
| --- | --- | --- |
| Anything about a submission — state, figures, an acknowledgement | `RunBoard`, through one `Changed` delegate the hub supplies | The whole submissions list, as `GET /api/submissions` answers it today |
| A run's record grew | `RunConductor`, after each `IRunRecord` call | The bytes of that record beyond what this subscriber has been sent |
| The chat gained a question, a piece of answer, a step, or a question's state or cost changed | `RunConductor` and the chat intake | The one thing that changed, and nothing else |

**Why a delegate on the board and not an event or an observer.** `RunConductor.NextRunMayStart` is
already exactly this: a delegate the composition root supplies so that the RUNS context can say
something happened without knowing who listens. One precedent, followed, rather than a second
mechanism beside it (II.1).

**Why the record stream sends bytes past an offset.** `MarkdownRunRecord` stays the only thing that
writes a record and `IRunRecord.Read` already serves it whole. Sending what is past a per-subscriber
offset keeps the browser's rule unchanged — it appends what it has not seen and never replaces an
element it has drawn (ACCESS-006) — and keeps the record's own segmentation the single description
of the run's shape. Rendering the moment a second time in the conductor was rejected: two renderers
of one record can disagree, which is the seam DEC-030 named for the figures.

**Why the list is sent whole and the record is not.** The list is a few hundred bytes and is read as
one instant under the board's lock (ACCESS-005) — sending a delta would break exactly that. A record
reaches the low hundreds of kilobytes (003 research R-04), which is why it is the one stream that
carries an increment.

---

## R-06 — A question's grant is read-only, and the door is what enforces it

**Decision.** A second MCP endpoint, `/mcp/questions/{runId}`, serving **only** `list_pages` and
`read_page`. A question's run is dispatched at that address; an ingest run keeps
`/mcp/runs/{runId}` and its five tools. `ToolGrant` gains the endpoint segment beside the names, so
the grant and the door that serves it are one value and cannot disagree.

**Revised while implementing** (phase 3, raised in review): **mapping a second route does not give a
second catalogue.** `AddMcpServer().WithTools<A>().WithTools<B>()` builds *one*
`McpServerOptions.ToolCollection`, and `MapMcp` serves that same one at every pattern it is mapped
to — measured on `ModelContextProtocol.AspNetCore` 2.2.0, which merged the duplicate `list_pages` and
`read_page` silently rather than failing, so `/mcp/questions/{runId}` served all five tools and
GUARD-005 did not hold at all.

What replaces it is `HttpServerTransportOptions.ConfigureSessionOptions`, which the library does
offer: it runs per session with that request's `HttpContext` and that session's `McpServerOptions`,
whose `ToolCollection` is what the session serves. A session opened on the questions route is given a
catalogue built from `WikiReadToolsServer` alone. **The decision's substance is unchanged** — the
session's catalogue *is* the grant, and the write tools are not in it to be reached by any name, which
is DEC-011's deny-by-default by construction. What changed is the mechanism that carries it.

Each catalogue is built from its own attributed type rather than by taking two of the five by name: a
type that holds only the two reads has no place a third could be selected from by mistake, which is
the difference between construction and an allow-list.

Two tests were missing and now exist. `QuestionGrantTests` asserted what a tool *type* declares, and a
hub serving five tools at the question door satisfied every one of those assertions; what a **route**
serves is read over a real MCP session in `WikiToolDoorTests` (Contract — the handshake is the real
thing, and it costs seconds the Fast budget does not have). And `HarnessProcess.ArgumentsFor` built
the URL from a literal `/mcp/runs/`, so nothing consumed `ToolGrant.Endpoint` and every question's run
would have reached the ingest door; it now comes off the grant, with a Fast test on both kinds.

**Why not a narrower `--allowed-tools`.** DEC-011's decision is that tools are deny-by-default **by
construction, not by an allow-list of names**, and its evidence is that `--tools ""` makes the
served surface the whole of what exists for the run. Naming two of five in `--allowed-tools` would
be precisely the allow-list laid over a larger surface that DEC-011 rejected: `write_page` would
still be served at that run's endpoint, one flag away. It would also need a fresh signed-in probe to
establish what `system/init` then reports, and DEC-021's budget of four such tests is spent.

**What it costs.** The two read tools are exposed by two MCP tool types. Their bodies live once —
the reads are one implementation both wrappers call — so what is duplicated is two attributed
methods, about a dozen lines. That is carried openly in the plan's Complexity Tracking rather than
argued away.

**What proves it.** GUARD-001 already ends a run failed when `system/init` reports anything outside
the grant, and `AgentTranscript.SurfaceIsTheGrant` compares by equality rather than containment. So
a question whose endpoint served a write tool ends failed before its first model call — the guard is
inherited rather than written twice. GUARD-005 is proven Fast against the hub's own half of the
grant and E2E against a run that leaves the wiki byte for byte as it found it.

---

## R-07 — What a follow-up's run is given, and what happens when it grows

**Decision.** The whole of the chat so far — each earlier question and the answer text it produced,
in order — is rendered into the prompt by `InstructionLoader`, which stays the only thing that puts
text into a prompt (V.1). The steps are **not** included: what the agent did to reach an earlier
answer is for the user to check, not context the next run needs, and a run's tool results are the
largest thing in a chat by far.

**Nothing is trimmed, and there is no mechanism for later.** OWNER DECISION, 2026-09-27. This
answers the brief's open question directly: the first version sends everything. A chat that grows
past what a dispatch can carry ends that run failed, and the chat says that question got no answer
and why (QUERY-006) — which is the same path every other failed run takes, and the user's remedy is
the one the feature already gives them: start a new chat.

Dropping the oldest turns to fit a budget was rejected: a follow-up would then be answered in the
light of less than the chat shows, silently, which is worse than a failure the user can see.
Refusing an over-large question was rejected too — it would need a fourth refusal in QUERY-003, and
so a requirement change the spec does not have.

**Compressing the conversation is proposed to the owner as a Later outcome** (Constitution I.4), not
built here. Its trigger is named in [plan.md](plan.md) §Proposed to the owner: the first time a real
chat fails for this reason. Until then it is a mechanism with no consumer (II.1), and a summary of
the conversation is in any case a second piece of agent judgment — which is the kind of thing
`docs/product.md` decides before a plan does.

**Why not one long-lived agent.** The brief makes each question its own run with its own ceilings
and its own grant, and QUERY-002's note in the spec says why it matters: a chat is a sequence of
runs, so RUNS-002's one-run-at-a-time and RUNS-006's no-agent-outlives-its-run are untouched. A
resumed CLI session would also carry the previous run's grant and ceilings, which is the opposite of
what GUARD-004 and GUARD-005 ask.

---

## R-08 — Where the line falls between the answer and the steps

**Decision.** OWNER DECISION, 2026-09-27. **Every piece of the agent's own text is the answer**,
appended in the order it arrives and growing in place. **The steps are the tool calls and what they
returned** — one entry per call, shut, openable on its own, appended under the answer as they
happen.

**Why the line falls there.** ACCESS-007 binds three things at once: an answer appears as the agent
produces it, what it did to reach it is readable under it as it happens, and *content arriving must
not move what the user is already reading*. The third is what decides this. The two alternatives
were weighed:

- **The answer is the final turn's prose.** True to how the CLI's stream reads, and it cannot
  stream: nothing knows a text block is the last until the run ends, so the answer would appear all
  at once at the end. That contradicts the whole of US1.
- **The newest prose is the answer, demoted under the fold when a further call follows it.** It
  streams, and it moves text the user has already read from one place on the screen to another,
  which ACCESS-007 forbids in as many words.
- **Everything but the opening block, which goes under the fold as a preamble.** This one is
  decidable live and never revised, so it satisfies all three — it was put to the owner beside the
  decision above and is the variant they did not take. It reads closer to the brief's walkthrough
  and costs a rule that exists for exactly one block of a run; the owner chose the rule with no
  exception in it.

Only a rule decided at the moment a block arrives and never revised satisfies all three, and this is
that rule. It is also what QUERY-004 makes the agent's prose *be*: the instruction says the answer is
written for the user to read, so what the agent writes is answer-shaped rather than a narration of
its own steps.

**What is read, and by whom.** Nothing new is parsed. `AgentTranscript` already reports
`AgentSaid`, `ToolCalled` and `ToolReturned` as `TranscriptMoment`s (DEC-028), and it stays the only
reader of the CLI protocol (V.2). What changes is only where the hub sends them: for a submission's
run into the record, for a question's run into the chat.

**No reasoning is shown, because there is none.** RUNS-009's measurement stands — `thinking` is an
empty string beside a signature blob, measured twice — and `AgentTranscript` does not read those
blocks. Nothing in this feature promises otherwise.

---

## R-09 — Opening a referenced page in the editor

**Decision.** Two new start-up inputs, `--vault <name>` and `--vault-root <directory>`. The link is

```
obsidian://open?vault=<name>&file=<the page's path, relative to --vault-root>
```

The agent writes ordinary relative Markdown links (WIKI-001, OKF §6.1); **the browser** rewrites
them when it renders the answer. Nothing new goes into a wiki page, and the link form is in one
place.

**What the link target is relative to.** §6.1 writes a link relative to the page it sits on, and an
answer sits on no page. The question instruction therefore states that a reference's target is the
page's path **relative to the wiki's root**, which is the one anchor an answer has. The browser
joins that to the wiki's own path inside the vault — which is what `--vault-root` establishes, and
why the absolute-path form was rejected: the owner wants the directory the paths hang off to be
theirs to define.

**Where the browser gets the two values.** On the chat stream's opening snapshot, beside the cost
ceiling it already needs. Not written into the page: they are the hub's start-up values, the same
argument DEC-032's list already makes for the cost ceiling.

**When they are missing** (the brief's open question, and the spec's). The question is **not**
refused. The answer arrives as normal, the page's name stays readable in the prose as plain text,
and the chat says once that opening pages is not set up. Refusing the way QUERY-003 refuses a
missing purpose description was rejected: the purpose description is what the agent's judgment rests
on, and this is a convenience. Saying nothing was rejected too — a reader could not then tell a
setting that is absent from a page that is simply not linkable.

**A link to a page that does not exist** is Grimoire's business in neither direction. OKF requires
readers to tolerate a broken link and nothing in Grimoire checks one today
(`docs/capabilities/wiki.md`); the link is built and the editor does what it does with a missing
file (QUERY-004).

---

## R-10 — The question instruction

**Decision.** `instructions/question.md`, beside `instructions/ingest.md`, reached by
`--question-instruction <path>` with a default in the hub's base directory — the same shape
`--instruction` already has, because both are Grimoire's own and versioned here. `InstructionLoader`
assembles both prompts and stays the only thing that puts text into one (V.1). Changing either is an
owner decision named in the PR.

**What it states** is QUERY-004's list and nothing beyond it: that the answer is written for the
user to read, rests on what the wiki's pages say, names every page it rests on inside its prose as a
link in the wiki's own link form (R-09), and that nothing in the wiki is to be written, changed or
appended. That last sentence is not what enforces it — GUARD-005 and the read-only endpoint are
(R-06) — it is there so the agent is not left trying a tool that does not exist.

**Where the wiki holds nothing about the question.** OWNER DECISION, 2026-09-27: the instruction
tells the agent to say plainly that the wiki does not cover it, name what it looked at, and stop.
Answering from what the model itself knows, marked as not from the wiki, was rejected: the answer
would then not rest on the wiki, which is what QUERY-004 asks of it, and a marked sentence is a
source with no page behind it. Answering as far as the wiki goes *and* naming what is missing was
weighed and is the narrower case of the same rule — the instruction says the gap is named either
way.

**This adds no requirement id.** It is an acceptance criterion of QUERY-004's existing clause "rests
on what the wiki's pages say": where the pages say nothing, resting on them *is* saying so. A
clarification refines and does not extend (Constitution IV.8). It is also the signal a later feature
wants — a question the wiki cannot answer is exactly what OUT-06 and ingest feed on — but nothing is
built here for that.

**`StartUpInputs` gains a third flag.** A submission is refused on the ingest instruction and the
purpose description (INGEST-003, unchanged); a question is refused on the *question* instruction and
the purpose description (QUERY-003). One record read per acceptance, three flags, each refusal
naming exactly one thing — the order the existing code already establishes.

---

## R-11 — Which level proves what

Each test sits at the lowest level that can prove its requirement (III.6).

| Requirement | Level | Why not lower |
| --- | --- | --- |
| QUERY-001, QUERY-002, QUERY-003 | Fast | Acceptance, refusal and what a dispatch carries are in-process decisions with in-memory adapters at every port |
| QUERY-004 | review | A requirement about what a text says; III.8 does not test the wording of an instruction, and an eval would judge the agent rather than the instruction. The spec's "Why review" row states it |
| QUERY-005 | Fast, and E2E for the two browsers | The chat's lifetime and a new chat are in-process; "a question asked in one tab appears in the other" is geometry only a real browser has |
| QUERY-006 | Fast | A failed run's effect on the chat is in-process; the browser half is ACCESS-007's |
| GUARD-005 | Fast, and E2E | Fast against the hub's own half of the grant, as GUARD-002 is; E2E for the wiki being byte for byte unchanged after a real question, which is the claim a unit cannot make |
| ACCESS-007, ACCESS-008, ACCESS-009, ACCESS-010 | E2E, with the payloads Fast | What the stream carries is a Fast assertion; that an answer grows without moving what is read, that a step opens, that a link addresses the right page and that the three jobs reach each other are all real-browser claims (DEC-020) |
| ACCESS-005, ACCESS-006 | unchanged, re-proven | Their wording does not change; what changes is how the browser is fed. The existing tests move from a poll to a stream and keep their ids |
| RUNS-005, RUNS-007, RUNS-008, RUNS-009 | unchanged, plus one Fast each | The existing tests stand; each reworded requirement gains one test for the case that made it change — a run that changes nothing in the wiki ends done, and a question's run leaves no record |
| SQLite holding a run with no submission | Contract | It is the real external thing (III.4), and DEC-031's precedent has the schema change proven there |

No new `requires=signin` test: DEC-021's budget of four is spent, and nothing here needs fresh
evidence from the real CLI — R-06 chose the design that does not.

**The Fast budget.** 15 s for the whole suite (III.7, DEC-008). Nothing this feature adds waits for
real time: the chat is in memory, the moments come from recorded lines as 003 established, and
elapsed time comes from `FakeTimeProvider` (DEC-018). The streams are read as `IAsyncEnumerable` in
Fast tests rather than over a socket, so no test waits for a network.

---

## R-12 — What a question is to the submissions list: nothing

Settled with the owner in the spec's Clarifications, recorded here because the brief asked it of the
plan. A question does not appear in the list of submissions, in a list of its own, or anywhere but
the chat. The list is headed "What became of each submission" and a question is not one; a question
has no record, so the row's one action (ACCESS-006) would not exist. ACCESS-007 carries what the
chat shows; ACCESS-004, ACCESS-005 and ACCESS-006 are untouched.

The acknowledgement follows from that. A failed question blocks the queue exactly as a failed ingest
does (RUNS-003), and the user must be able to clear it — but there is no row to clear it from. The
chat offers the control against the question that failed, posting to an acknowledgement that
addresses the question. ACCESS-003's wording is unchanged: it asks for a failed run to be
acknowledgeable in the browser, and it is.

---

## R-13 — A reader who leaves mid-answer

Settled with the owner in the spec's Clarifications, recorded here because the brief asked it of the
plan. The run runs to its end like any other run, and the chat holds what it produced so a browser
that comes back reads it (R-01's snapshot, R-02's one chat object). Stopping the run when its reader
is gone was rejected: Grimoire has no way to stop a run today, only a ceiling ends one, and building
one here would be a mechanism this feature does not otherwise need (II.1). Admitting the answer lost
was rejected too — it is produced whether anyone watches or not.

Nothing is written to disk for this. Held means held while Grimoire runs, which is what keeps
"nothing is kept" a property the tests reach directly (QUERY-005).

---

## R-14 — Navigation, and the third page

**Decision.** A third static page, `chat.html` + `chat.js`, beside `index.html` and `run.html`. Each
of the three carries a line of links to the other jobs, and the chat carries one control that starts
a new chat.

`docs/ux.md` says "one page per job; no navigation chrome until a second job exists". A third job
exists now, and ACCESS-010 asks for the three to reach each other, so the chrome has its consumer.
It stays what the page style already is: text-first, a line of links, no bar and no menu.

DEC-019 is untouched — three HTML files and three scripts, served from `wwwroot/`, no bundler and no
npm.
