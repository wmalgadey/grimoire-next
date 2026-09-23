# Research: The Ingest Queue

Phase 0 of [plan.md](plan.md). One entry per question this feature had to settle before it could be
designed. Each is a decision, the reason behind it, and what was rejected.

`001-first-ingest/research.md` is not repeated here. Where a question was already answered there —
how the agent is reached, what a ceiling counts, where the wiki tools live — this feature uses the
answer as it stands.

---

## R-01 — Where submissions and their states live

**Supersedes** `001-first-ingest/research.md` R-08, which decided "in memory, no port, no adapter"
and said in as many words: *"When the port arrives: with RUNS-004, in the follow-up feature. That
plan chooses the storage technology; this one deliberately does not."* This is that plan.

**Decision**: **SQLite**, through `Microsoft.Data.Sqlite`, with raw SQL and no ORM. One file in a
directory Grimoire owns. Behind one port, `ISubmissionStore`, declared by the RUNS context, with
`SqliteSubmissionStore` under `Grimoire.Runs/Adapters/` and an in-memory adapter in the Fast suite.

**Rationale**: RUNS-004 as the clarify session settled it covers *any* stop — a crash, a forced
kill, a power cut — so nothing may depend on a shutdown step having run. That is a durability
requirement, not a serialization requirement: each change has to be on disk before Grimoire answers
for it. SQLite gives that in its default configuration, in a single file, with no server and no
native install step, and `Microsoft.Data.Sqlite` is a first-party package already inside the .NET 10
stack this repository settled on (DEC-002).

**Alternatives considered**:

| Alternative | Why not |
| --- | --- |
| A JSON file rewritten atomically on each change | Correct but crude: it rewrites every submitted text on every state change, and "atomically" is ours to get right — temp file, fsync, rename, fsync of the directory. Those are decisions we would then have to test, where SQLite's are already made and III.8 says we do not test a dependency's decisions |
| An append-only NDJSON journal, replayed at start-up | The same objection, sharper. It is storage tooling of our own where an existing thing does the job (II.3), and torn last lines, replay ordering and compaction all become our decisions and our tests. It reads well beside the wiki's own append-only log, which is why it was taken seriously — but the wiki's log is a document a person reads, and this is not |
| EF Core (or any ORM) over SQLite | Brings a migration mechanism for two tables that do not change shape in this feature: a mechanism with no consumer (II.1). Raw SQL against two tables is about forty lines in one adapter |
| LiteDB, or another embedded document store | A second dependency doing what the first does, with a smaller ecosystem and no advantage for a shape that is two flat tables |
| Keeping it in memory and writing only at shutdown | Ruled out by the clarification: a kill is exactly the stop RUNS-004 exists for |

**What it costs**: one package reference, one new port, one new adapter, one Contract suite, and one
start-up input (`--state <path>`).

---

## R-02 — What is proven about durability, and what is not

**Decision**: The Contract suite proves that what the adapter wrote through one connection is read
back through a **new** connection to the same file, and that a submission whose run was under way
comes back marked so. It does **not** kill a process, and it does not test SQLite's durability.

**Rationale**: III.8 — we test decisions we made, not ones a dependency made. That a committed
SQLite transaction survives the process dying is SQLite's decision and its own test suite's
business; a test of ours that killed a process to check it would be measuring the dependency.
What *is* ours, and therefore tested: that every change is committed before Grimoire answers for it,
that nothing is buffered in the adapter waiting for a close, and that start-up reads back exactly
what was written.

**Where each half sits**: the ordering — commit before answering — is visible in the Fast suite
through the in-memory adapter, because the board calls the store before it returns. The
file-on-disk half is the Contract suite's, against a real SQLite file under a temp directory
(III.4), where `FileSystemWikiStoreTests` already runs inside the 90 s budget.

**The owner's run closes the gap**: `quickstart.md` has the owner kill the hub with `kill -9` and
start it again. That is the one place a real uncatchable stop is exercised, and it is exercised by
a person rather than by a test (Constitution I.9).

---

## R-03 — Who starts the next run

**Decision**: the RUNS context decides *whether* a run may start and *which* submission is next —
`SubmissionBoard.TakeNext(runId)`, under the lock the board already holds. The hub's new `RunQueue`
does the dispatching, and is called from exactly four places:

1. a submission was accepted (`SubmissionIntake`),
2. a run ended (`RunConductor`),
3. a failure was acknowledged (the acknowledgement endpoint),
4. the hub started (after the store has been read and interrupted runs marked failed).

**Rationale**: those four are the complete list of events that can unblock the queue. Nothing else
can: a waiting submission carries no timeout (the owner's decision in the spec), so no clock can
make it start. A queue whose only transitions are those four needs no timer, no hosted background
service and no scheduler, and one would be a mechanism with no consumer (II.1).

The decision stays in RUNS because RUNS-002 and RUNS-003 are judgments about runs, and
`001-first-ingest` put every judgment about a run in that context. Handing out the next submission
under the board's existing lock is also what makes "at most one run in progress" true against a
race: the board already keeps one lock for itself and every submission on it, because the
single-run rule is decided by reading all their states together — the comment on `Submission.gate`
says exactly that, and the rule has only got stricter.

**Alternatives considered**: a `BackgroundService` polling the board (a mechanism with no consumer,
and it would make the queue's behaviour depend on a poll interval); letting `SubmissionIntake`
dispatch as it does today (it cannot: three of the four events do not pass through it);
`Channel<T>` as the queue itself (it would hold the order in a second place beside the store, and
the store's order is the one that has to survive a restart).

---

## R-04 — How a waiting submission is told apart from one under way

**Decision**: a nullable **run identifier on the submission**, assigned at the moment the board
hands it out.

| The submission is | State | Run identifier |
| --- | --- | --- |
| waiting its turn | `submitted` | none |
| under way, agent not yet reported in | `submitted` | set |
| under way, agent reported in | `running` | set |
| over | `done` or `failed` | set |

**Rationale**: with a queue, `submitted` stops meaning one thing. The spec's Key Entities keep the
boundary `001-first-ingest` drew — submitted runs from acceptance until the agent reports in — and
a submission waiting its turn is inside that. So the state alone cannot tell the board whether a
submission has already been handed out, and a board that cannot tell would hand the same one out
twice.

RUNS-001 is not touched: it says a submission carries exactly one of four **states**, and a run
identifier is not a state. The identifier also has to exist durably regardless: it is what names the
run in the wiki's log (RUNS-005), and it is what the run's own record — its agent's process among it
— hangs off at start-up (R-11). It does **not** reach the browser (R-06).

**What it makes possible at start-up**: the restart rule is one sweep with no ambiguity — a
submission with a run identifier and a non-terminal state was in progress when Grimoire stopped, and
reads `failed` (RUNS-004); a submission with no run identifier is still waiting, and starts in its
turn.

---

## R-05 — Stopping the run when the hub stops (RUNS-006)

**Decision**: `IHostApplicationLifetime.ApplicationStopping` calls one method on `RunConductor`,
which stops the run in progress the same way either ceiling stops it — the interrupt first, the
process kill behind it (DEC-016).

This is only half of RUNS-006. The other half — the agent that outlives a stop Grimoire could not
act on — is R-11.

**Rationale**: DEC-016 settled the mechanism with evidence — an interrupt ended a response in flight
in about 0.9 s, and SIGTERM is documented to leave the turn unfinished, which is why it is the
backstop. RUNS-006 asks for the same thing at a different moment, so it needs no second mechanism
(II.1).

**What is tested and what is not**: the lifetime hook is framework wiring and dependency wiring, and
III.8 tests neither. What is tested is the behaviour: given a run under way, `StopEverythingAsync`
stops it and the run ends failed — Fast, against the in-memory harness, which records that the stop
was asked for.

**What was checked rather than assumed**: `HarnessProcess` is not `IDisposable` and holds its
children in a plain dictionary, so there is no existing disposal path that would clean one up. The
lifetime callback is the only thing that stops a run as the hub goes down, which is why the gap it
leaves had to be closed elsewhere (R-11).

**The limit that remains**: where Grimoire is killed outright the callback never runs and the agent
outlives it — until the next start-up, which is where R-11 picks it up. Closing it *at kill time*
was rejected: `PR_SET_PDEATHSIG` is Linux-only with no macOS equivalent, and a promise a power cut
cannot honour is worse than an admitted limit with a bounded window.

---

## R-06 — What the acknowledgement addresses, and what the browser is told

**Decision**: the acknowledgement addresses the **submission** — `POST
/api/submissions/{id}/acknowledgement`. **No run identifier reaches the browser.** The list gains
one field, `awaitingAcknowledgement`, present only on a submission whose run ended failed and has
not been acknowledged.

**Rationale**: a submission has exactly one run in this feature (INGEST-002), so its identifier
already identifies the failed run — and the browser has carried that identifier as its row key since
`001-first-ingest`. Naming the submission therefore gives the whole of what naming the run would
give: a page loaded before the last run failed cannot clear a failure nobody saw, because it names a
submission that is no longer an unacknowledged failure and nothing happens.

What it gives in addition is that **ACCESS-002 is left entirely alone**. An earlier draft of this
plan had `runId` in the list and argued that an identifier the page never renders is not "shown",
narrowing the sentence in `001-first-ingest`'s contract that says no further detail about the run is
*exposed* by this API. That argument was available but it was not necessary, and a requirement
survives better unbent than bent for a good reason: the 001 sentence stands as written, and this
feature narrows nothing.

**Why a field is needed at all**: the page has to know which row offers the control, and `failed`
alone does not say — an acknowledged failure still reads `failed` (RUNS-003) and must not offer it
again. `awaitingAcknowledgement` is the smallest thing that answers it. It is not detail about the
run: it says an action is available, which is ACCESS-003's own subject, and it carries nothing about
what the run did.

**Alternatives considered**: naming the run (the owner withdrew it — see the spec's session "after
the plan"); one unaddressed "clear the block" action (rejected in clarification, a stale page clears
an unseen failure); no field, with the control on every `failed` row and the server ignoring an
already-cleared one (a control that does nothing is a lie to the user); a second endpoint reporting
what blocks the queue (it would say what the list already says).

---

## R-07 — The excerpt (ACCESS-004)

**Decision**: runs of whitespace collapsed to single spaces, trimmed, cut to **120 characters**,
with `…` appended where it was cut. A text at or under 120 characters is shown whole, with nothing
appended and nothing padded. Computed on `Submission` in the RUNS context.

**Rationale**: ACCESS-004 fixes the shape — "the same length for every submission" — and leaves the
length here. The page is `max-width: 42rem` and each row carries the excerpt beside a time and a
state, which is about 90 to 110 characters to a line; 120 is a line and a half there. Long enough
that a person recognises their own text before acknowledging its failure, short enough that the
list stays one row per submission rather than becoming a wall of prose — which is the reason the
whole text was rejected in clarification.

Collapsing whitespace is what makes the first 120 characters of a pasted document a readable
sentence rather than an indented fragment or a run of blank lines. The submitted text itself is kept
whole and unchanged; `001-first-ingest` was explicit that the agent receives what the user pasted,
not a tidied version, and nothing here touches that.

It sits on the submission rather than in the API view because "the opening of this text" is a fact
about the submission, which makes it a Fast test against a real object rather than an assertion
about a serializer.

---

## R-08 — What survives with a run, and the question II.1 asks of it

**Decision**: a run's record survives its submission's stop, carrying the run identifier, the
submission it belongs to, when it started, and the tools it was granted.

**The question**: nothing in this feature reads the granted tool names back. II.1 forbids building
anything without a consumer in the same feature, and "OUT-02 will want it" is exactly the kind of
placeholder it forbids. So is this a violation?

**The answer, and why it is not**: GUARD-003 says the tools granted must be recorded **for every
run**. Today the record lives on the `Run` object, which `RunConductor` drops the moment the run
ends — so GUARD-003's record lives only as long as the run does, and only as long as the process
does. RUNS-004 now makes the submission that run belongs to outlive the process. A grant that still
vanished would leave GUARD-003 weaker than the state around it: the run would be listed, addressable
and acknowledgeable after a restart, and the one thing the requirement says must be recorded about
it would be gone. The consumer is GUARD-003 itself, read together with RUNS-004 — not a later
outcome. The spec's Assumptions say so, and the owner approved that in the clarify session.

**What is deliberately not stored**: the tokens a run spent. The spec says so — a run cut off by a
stop reads failed, both ceilings bind a run only while it is in progress, and nothing judges it
again. Storing a number nothing reads *would* be the placeholder II.1 forbids.

---

## R-11 — The agent that outlives a stop, and how it is recognised again

**The hole this closes**: after a stop Grimoire could not act on, the agent goes on running. It
holds the granted tools, so it keeps writing into the wiki; no ceiling is enforced on it, because
the Grimoire that was counting tokens and elapsed time is gone; and the restarted Grimoire reads its
run as failed and — once the user acknowledges — starts the next one. Two agents in one wiki, one of
them unbounded and invisible, with the browser saying the first is finished. WIKI-003 says what a
failed run wrote stays, and that is right; it does not license a failed run to go on writing.

**Decision**: the process identifier of a run's agent is recorded with the run. At start-up, for
every run read as having been in progress, Grimoire terminates that process where it is still alive
— **before** that run reads failed and **before** any further run starts.

**Why that ordering**: the browser must never show `failed` while the agent is still at work, and no
second run may begin beside a first that is still writing. Both are observable, which is what makes
the ordering part of the requirement rather than an implementation detail.

### Recognising the process, and the pid-reuse trap

A recorded process identifier alone is not enough. Operating systems reuse those numbers, and after
a reboot the number almost certainly belongs to something else — terminating it would kill an
unrelated program on the owner's machine. This is the one place in the feature where getting it
wrong does damage outside Grimoire.

**Decision**: the identity is the **pair** — the process identifier *and* the moment that process
started, both recorded when the run is dispatched. At start-up a process is terminated only where a
live process with that identifier exists *and* its start time is the one recorded. Two different
processes sharing an identifier *and* a start time to the tick do not occur in practice, and a
reboot changes every start time, so the pair also handles the reboot case with no special rule.

`System.Diagnostics.Process` exposes both halves — `Id` and `StartTime` — at dispatch and again at
start-up, so nothing of our own has to be invented to read them.

**Alternatives considered**:

| Alternative | Why not |
| --- | --- |
| The process identifier alone | Kills an unrelated process after a reboot or a wrapped-around number. Not acceptable |
| Matching the process's name or command line as well | Would work, but it makes the Contract test need a real `claude` and therefore a sign-in (DEC-021), which would put this proof outside CI. The pair is already exact |
| A lock file the agent holds | A mechanism of our own where the operating system already answers the question (II.3) |
| Writing the identifier into the wiki | The queue writes nothing into the wiki (spec, "Who writes what") |
| A process group killed at start-up | Grimoire's own new process would be in whatever group the shell gives it; the group of a process that is gone is not addressable afterwards |

### Where it lives

The `claude` process is an external system and appears only inside its adapter (Constitution V.2),
so all of this sits at the agent port:

- `RunReport` gains one more report — the agent's process identity, as soon as the child exists —
  which is the shape the port already has: the harness reports facts, the hub decides (`IAgentHarness`).
- `IAgentHarness` gains one operation: terminate this recorded identity if it is still that agent.
  `HarnessProcess` already kills a process tree (`Kill(entireProcessTree: true)`), so the act itself
  is not new.
- The store keeps the pair with the run (contracts/submission-store.md).

The hub's start-up sequence becomes: read the store → terminate the agents of runs that were in
progress → mark those runs failed → pump the queue. One order, in one place.

### Proof

**A Contract test against a real process** (`Grimoire.Contract.Tests`), which is what III.4 asks for:
terminating a process is an act on the operating system, and no in-memory adapter can make it true.
Three things are proven:

1. a real child process, recorded and then handed back at "start-up", is terminated and is gone;
2. a recorded identity whose process is already gone terminates nothing and does not fail;
3. a live process whose identifier matches but whose start time does not is **left alone** — the
   pid-reuse guard, and the one of the three that protects something outside Grimoire.

The child is an ordinary long-lived process the test starts, not `claude`: nothing here is about the
CLI's protocol, so **this suite needs no sign-in and runs in CI**, unlike the three tests DEC-021
excludes. CI is `ubuntu-latest` and the developer machine is macOS; both have the POSIX utilities
such a child needs.

The Fast suite still carries the other half of RUNS-006 — that a run under way is stopped when the
hub is told to stop — against the in-memory harness, and the start-up *ordering* against a harness
that records when it was asked to terminate.

---

## R-09 — Which level proves which requirement

Each test sits at the lowest level that can prove its requirement (III.6). E2E and Deploy must carry
the requirement ID; Fast and Contract may (III.5).

| ID | Fast | Contract | E2E | Why not lower |
| --- | --- | --- | --- | --- |
| RUNS-002 | the queue rule against the real board with an in-memory store | — | — | Nothing about "one at a time, in order" needs a browser or a file: it is a decision of the board, and the board is a real object a Fast test drives |
| RUNS-003 | the gate: nothing starts while an unacknowledged failure stands; the acknowledged run stays failed | — | one scenario, with the browser | The rule is Fast. That a person can *reach* it is ACCESS-003, and that is browser-observable, so the two share one E2E scenario |
| RUNS-004 | the board restored from a store: states come back, a run that was under way reads failed | the adapter: written through one connection, read through another | one scenario: the hub started twice over one store | The behaviour is Fast, the file is Contract, and the two together are what a restart is. The E2E scenario is what proves they are wired to each other |
| RUNS-006 | a run under way is stopped when the hub is told to stop; at start-up the agents of runs that were in progress are terminated before those runs read failed and before anything starts | a real child process: terminated when the recorded identity matches, **left alone** when only the identifier does (R-11) | — | The hook and the ordering are Fast. Terminating a process is an act on the operating system and no in-memory adapter can make it true, so the act itself is Contract — and it needs no sign-in, so it runs in CI |
| ACCESS-003 | the endpoint's effect on the board, and that `awaitingAcknowledgement` appears only where a failure is unacknowledged | — | shares the RUNS-003 scenario | "In the browser" is the requirement, so it cannot be proven below E2E — but only the reaching of it needs to be |
| ACCESS-004 | the excerpt's shape, including a text shorter than the cut | — | the row carries it | The rule is Fast; that a real browser renders it is E2E, and it rides on the existing row assertion rather than adding a scenario |

E2E scenario budget (III.4, at most two per user story): Story 1 uses none beyond the row assertion
this feature updates, Story 2 uses one, Story 3 uses one. Three of a possible six.

---

## R-10 — What retiring INGEST-005 leaves behind

**Decision**: the retirement is one step that touches all of it at once, in phase 1.

**Rationale**: `trace-check` fails on a test carrying a retired ID (IV.3). So the requirement cannot
move under "Retired" in `docs/capabilities/ingest.md` in one commit and the tests carrying it be
removed in another — the feature branch would be red in between, which I.10 forbids.

**What goes with it**, found by reading for it rather than by grep alone:

| Where | What |
| --- | --- |
| `docs/capabilities/ingest.md` | INGEST-005 moves under "Retired", keeping its ID; INGEST-001 loses "When no run is in progress" |
| `src/Grimoire.Runs/SubmissionBoard.cs` | `Refusal.RunInProgress` and the clause that returns it |
| `src/Grimoire.Hub/Api/SubmissionsEndpoints.cs` | the `409 Conflict` / `run-in-progress` arm |
| `src/Grimoire.Hub/wwwroot/app.js` | the comment naming 409 |
| `tests/Grimoire.Fast.Tests/SubmissionAcceptanceTests.cs` | the four tests carrying `[Trait("req", "INGEST-005")]` — the refusal, that it is not stored, that it starts no run, and that the same text is accepted once the run has ended. The first three go with the requirement; the fourth becomes a queue test, because a second text is now accepted *while* the first run is under way |
| `tests/Grimoire.E2E.Tests/SubmissionStatesTests.cs` | the remark explaining why submissions are driven one after another — with a queue they no longer have to be |
| `src/Grimoire.Runs/Submission.cs`, `src/Grimoire.Hub/SubmissionIntake.cs` | comments citing INGEST-005 for the single-run rule; the rule is now RUNS-002 and the citations follow it |
| `specs/001-first-ingest/contracts/hub-http-api.md` | left as written — it is the change record of a closed feature, and this feature's contract supersedes it |

The last row is the rule for all of `specs/001-first-ingest/`: it is a change record and is not
edited. `docs/capabilities/` is the as-is description and is (IV.2).
