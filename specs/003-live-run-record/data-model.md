# Data Model: The Live Run Record

**Feature**: `003-live-run-record` | **Plan**: [plan.md](plan.md) | **Date**: 2026-09-26

What this feature adds to the model of `001-first-ingest` and `002-ingest-queue`, and what it changes.
Everything not named here stands as those features left it.

---

## Run *(changed — `Grimoire.Runs/RunStateMachine.cs`)*

One attempt to work one submission into the wiki. It gains three things, all of them read by the
record or by the list:

| Field | What it is | Requirement |
| --- | --- | --- |
| `Model` | The pinned model id this run runs on. **Moved onto the run** from `RunQueue`, which held it as a constructor argument: the grant and both ceilings are already recorded at `Begin`, and the model belongs in the same breath — a record from last month must say which model served it, and the owner may change `--model` between runs (DEC-010) | RUNS-008 |
| `ToolCalls` | How many tool calls the run has made. Rises once per `tool_use`; never goes backwards | RUNS-010 |
| `EndedBecause` | Why the run ended, as one of the seven reasons below. Set where the verdict is taken, beside the outcome | RUNS-008 |

`TokensUsed` is unchanged and already exists (GUARD-004). It is now also read by the list, which is
the change: a number that was only ever compared against a ceiling is now shown.

`RunQueue` stops holding the model and reads `run.Model` for the dispatch. That is one fewer place
the model lives, not one more.

### Why a run ended

Seven values, one requirement (RUNS-008; Constitution IV.7 — the values a requirement covers are a
list inside it, never one requirement per value). Each one is a state the code already reaches; none
is new behaviour:

| Value | Reached where | Existing requirement |
| --- | --- | --- |
| `StoppedWithItsLogEntry` | `Run.Exited`, all three agreeing | RUNS-005 |
| `StoppedWithoutItsLogEntry` | `Run.AgentStopped` after the one nudge | RUNS-005 |
| `TimeCeiling` | `RunConductor.ElapsedCeilingReached` | GUARD-004 |
| `CostCeiling` | `RunConductor.CostSoFar` past the ceiling | GUARD-004 |
| `ToolsWereNotTheGrant` | `TranscriptSays.InitIsNotAcceptable` | GUARD-001 |
| `AgentProcessDied` | a non-zero exit, or a reader that lost its process | RUNS-005 |
| `GrimoireStopped` | `RunConductor.StopEverythingAsync`, and a run restored as having been under way | RUNS-004, RUNS-006 |

A run still under way has no reason yet. That is what makes the record's tail the part that is only
there once the run has ended (research.md R-02).

## RunRecord *(new — the port `Grimoire.Runs/IRunRecord.cs`)*

Where a run's record is written. The RUNS context's **second** port, after `ISubmissionStore`; its one
adapter is `MarkdownRunRecord`, and the Fast suite has an in-memory one at the same port (Constitution
II.4, III.9, V.2). A new port, **not** a new external system: the filesystem is already reached by
`FileSystemWikiStore` and by the state directory of DEC-023, which is what keeps this feature to one
slice addition (Constitution I.6).

```text
void Begin(RunFrameHead head);      // the head, once, when the run begins
void Append(RunMoment moment);      // one moment, in the order it happened
void End(RunFrameTail tail);        // the tail, once, when the run ends
int EntriesLost(Guid runId);        // how many of the above could not be written
```

Synchronous, for the reason DEC-023 gave `ISubmissionStore`: the calls are made from the conductor on
whatever thread the harness reads on, and the writes underneath are synchronous, so an async
signature would promise a yielding call that never yields.

**No read, no delete, no rewrite.** The same shape `IWikiStore` and `ISubmissionStore` have and for
the same reason: RUNS-007 says the record is never rewritten or removed, so no member exists that
could. Reading the record back for the browser is a *file* the endpoint serves, not a member of this
port — see `contracts/run-record.md`.

**Nothing here throws.** An IO failure is caught by the adapter and counted; `EntriesLost` is how the
count is read back for the figures (RUNS-007, research.md R-10).

### RunFrameHead

Everything known when the run begins: `RunId`, `SubmissionId`, `Model`, `GrantedTools`,
`GrantRecordedAt`, `Ceilings` (both), `StartedAt`.

### RunMoment

One thing that happened, in the order it happened (RUNS-009). Four kinds, one type:

| Kind | Carries | Where it comes from |
| --- | --- | --- |
| `ToolCalled` | the tool's name and its arguments | an `assistant` message's `tool_use` block |
| `ToolReturned` | what the call returned, **whole** | a `user` message's `tool_result` block |
| `AgentSaid` | the agent's own text | an `assistant` message's `text` block |
| `GrimoireSaid` | what Grimoire told the agent — today only the nudge | the hub, which knows it nudged (RUNS-005, DEC-017) |

`ToolCalled` and `ToolReturned` are not paired in the record beyond their order. The CLI's
`tool_use_id` is read only to nothing: a run makes one call at a time in the order the stream reports
it, and an identifier in the record would be a field with no reader (Constitution II.1).

A `tool_result`'s `content` is a string or an array of blocks, which the protocol allows both of. A
string is itself; an array is the text of its text blocks, joined. Anything else is recorded as a
result that could not be read — refused rather than read around, the way `AgentTranscript` already
treats a `tools` array it cannot make names of (GUARD-001's precedent).

### RunFrameTail

What only the ending knows: `EndedAt`, `Outcome` (done or failed), `EndedBecause` (the seven above),
`Elapsed` against the elapsed ceiling, `TokensUsed` against the cost ceiling, and `TokensPerModel` —
every entry of the run's `modelUsage`, so the user can see *which* model spent them (DEC-015,
`ModelTokens`).

## The record file *(new — `<state>/runs/<runId>.md`)*

Markdown, appended, never rewritten. Its exact wording is the adapter's and is not tested
(Constitution III.8); what is tested is that each part is there, in order, and whole. Its **shape** is
load-bearing, because `run.js` segments it — `contracts/run-record.md` is where that shape is promised
and where the fence rule is written down. In outline, one line per element:

| Line | What it is |
| --- | --- |
| `# Run <runId>` | the head's first line, once |
| a two-column table | submission, model, granted tools, both ceilings, when it started |
| `## <time> · called <tool>` | a tool call. Its arguments follow in a fenced block |
| `## <time> · <tool> returned` | its result. The result follows in a fenced block, whole |
| `## <time> · the agent` | the agent's own text, as prose, unfenced |
| `## <time> · Grimoire` | what Grimoire told the agent — today only the nudge |
| `## <time> · ended <done\|failed> — <reason>` | the tail's first line, once |
| a two-column table | when it ended, elapsed against its ceiling, tokens against theirs, and the tokens of each model the run touched |

Every fenced block opens with a run of backticks **one longer than the longest run of backticks in
what it holds**, and at least three, and closes with a run of the same length — CommonMark's own rule,
which makes the block unambiguous for any content without altering a byte of it (research.md R-04).

A record of a run still under way is the head and however many moments have happened. It has no tail,
and that is the only difference between it and a run from last month.

**Not OKF, and not a wiki page.** No frontmatter, no `generated` record, nowhere in the wiki
(Invariants 1 and 3, `ProvenanceStamp` untouched).

## StoredRun *(changed — `Grimoire.Runs/ISubmissionStore.cs`)*

The run as what survives a stop keeps it. Four new fields, and the comment that said the tokens are
"deliberately not here" goes with them:

| Field | Column | Why it survives |
| --- | --- | --- |
| `Model` | `model` | A record from last month must say which model served it; the owner may change `--model` between runs (RUNS-008, DEC-010) |
| `TokensUsed` | `tokens_used` | RUNS-010. This withdraws `002-ingest-queue`'s assumption that a cut-off run's tokens need not survive: OUT-02 has the user read a failed run's cost, so the number now has a consumer (Constitution II.1) |
| `ToolCalls` | `tool_calls` | RUNS-010, for the same reason |
| `EntriesLost` | `entries_lost` | RUNS-007 — the gap is visible after a restart too, and a record that could not be written must not look like a run that did nothing |

**The schema is brought up to date rather than migrated**: after `CREATE TABLE IF NOT EXISTS`, the
store reads `PRAGMA table_info(runs)` and issues `ALTER TABLE runs ADD COLUMN` for each column it does
not find. No version table and no scripts (research.md R-07). An older file comes back with its
submissions intact and its figures at zero, which is what a Contract test asserts.

`ISubmissionStore` gains two members:

```text
void RecordFigures(Guid runId, long tokensUsed, int toolCalls, int entriesLost);
void Ended(Guid submissionId, SubmissionState terminal, Guid runId,
           long tokensUsed, int toolCalls, int entriesLost);
```

`RecordFigures` is one member for the three, because they are written by the same events and read as one
row. Called only where a figure has actually risen — `Run.Spent` is already a `Math.Max`, so the store
sees two to four writes a turn rather than the sixty `stream_event` lines a turn carries (research.md
R-06).

`Ended` is the ending, and it exists because the ending is **one** change and not two. Written as
`SetState` and then `RecordFigures`, a stop between them — which RUNS-004 covers, a kill or a power cut
— would leave a submission reading done or failed beside the figures it had one moment earlier. RUNS-010
has the figures stand as the run's final ones once it has ended *and* survive a stop, and two changes
cannot promise both. The board writes it inside the same pass of its own lock in which it sets the
state, for the same reason one reading up the stack: ACCESS-005 has the state and the figures read as
one instant, and a reading is only as atomic as the writing behind it.

## Submission and SubmissionStatus *(changed — `Grimoire.Runs/Submission.cs`)*

`SubmissionStatus` gains the run's figures, so that the state, the acknowledgement and the figures are
**one reading under the one lock**:

```text
SubmissionStatus(SubmissionState State, bool AwaitingAcknowledgement, RunFigures? Run)

RunFigures(string Model, long TokensUsed, int ToolCalls, int EntriesLost)
```

`Run` is null for a submission that has no run — one waiting its turn. That is what makes ACCESS-005's
"for a submission that has a run" a property of the response rather than a rule the browser applies.

Read together for the reason `SubmissionStatus` already exists: asked one after the other, a run
ending between two answers would put `running` beside a final figure, a pair that never existed.

Written through the board, under its lock, as every other change to a submission is:
`board.Spent(submissionId, tokens)` and `board.ToolCalled(submissionId)`, each writing the store only
where a figure rose.

## What is unchanged

- **ToolGrant, Ceilings, ProvenanceStamp, OkfFrontmatter, IWikiStore** — untouched. The record writes
  nothing into the wiki and reads nothing in it; RUNS-005 still reads `log.md` for the run's
  identifier and nothing else.
- **The four states** (RUNS-001) — still four. A record, a figure and a reason a run ended are none of
  them a state.
- **The acknowledgement** (RUNS-003, ACCESS-003) — unchanged, and still addresses the submission.
- **The run identifier still does not reach the browser.** ACCESS-002's retirement lifts the rule that
  required it, but nothing needs it: the record endpoint addresses the submission, which has exactly
  one run (INGEST-002), exactly as the acknowledgement does. It is now a design property rather than a
  requirement, which is why the test that proves it keeps no requirement ID.
