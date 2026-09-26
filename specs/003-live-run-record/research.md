# Research: The Live Run Record

**Feature**: `003-live-run-record` | **Spec**: [spec.md](spec.md) | **Date**: 2026-09-26

What had to be found out before the design could be settled, and what was measured rather than
assumed. `docs/decisions.md` was read first; the decisions this feature adds are in
[plan.md](plan.md) §Technology decisions and are merged into that document when the feature closes
(Constitution II.6).

The probe below is this feature's one spike. It ran `claude` **2.1.283** on
`claude-haiku-4-5-20251001` on 2026-09-26, twice: once with a prompt on argv and one built-in tool
allowed, to see a tool call and its result; once with the prompt on stdin under
`--input-format stream-json`, to see whether the hub's own messages come back. Every line quoted
below came out of that run.

---

## R-01 — Where the record lives, and which context writes it

**Decision**: a port of the **RUNS** context, `IRunRecord`, with one adapter
`MarkdownRunRecord` under `src/Grimoire.Runs/Adapters/`. One file per run,
`<state>/runs/<runId>.md`, where `<state>` is the directory `--state` names and
`SqliteSubmissionStore` already owns.

**Rationale**: the record is what a run did, so it belongs to the context that owns what a run *is*
(RUNS). The filesystem is an external system, so it is reached only inside an adapter of the context
that declares the port (Constitution V.2) — the same shape DEC-023 gave `ISubmissionStore`. Beside
the SQLite file rather than in the wiki, because Grimoire's bookkeeping in the user's repository
would turn up in the version history that is their only undo (Invariants 1 and 3, DEC-023's
reasoning); `Program.cs` already refuses a `--state` inside the wiki, and that guard now protects the
records too, for free.

**Alternatives considered**:

- *Written by the Agent context, where the stream is read.* Rejected: the adapter reports and
  decides nothing (`contracts/agent-cli-protocol.md`), and which moments are worth recording, and
  whether a run is done, are the hub's. Writing the record there would put a second judgment in the
  adapter.
- *Written by the Wiki context's store.* Rejected outright: the record is not in the wiki, and
  `IWikiStore` refuses a path that leaves the wiki.
- *A directory of its own beside `--state`, with its own argument.* Rejected: a second path to
  configure, for files that belong with the state that is already there. `runs/` inside `--state`
  needs no argument and inherits the guard.

## R-02 — The record's shape: appended, never rewritten

**Decision**: three parts, in file order — a **frame head** written when the run begins, the
**narrative** appended as the run proceeds, and a **frame tail** appended when the run ends. Nothing
is ever rewritten, and no byte already on disk is moved.

**Rationale**: RUNS-007 has the record appended and never rewritten, and the brief's reason is
crash-safety: an append is the cheapest write to make survive a stop, which is the same concern
DEC-023 settled for the queue. A frame that was patched in place would need the file rewritten at the
end and would make a run cut off by a stop unreadable — the one case the record matters most.
Splitting the frame is what makes "a live run and a run from last month look the same" true: the live
one has no tail yet, and that *is* the difference.

What the head can hold is everything known at `Begin`: the run's and the submission's identifiers,
the model, the granted tools, both ceilings, and when the run started. What only the ending knows
goes in the tail: when it ended, why, where the run stood against both ceilings, and the tokens per
model. The one field neither can hold is the figure that grows — and it is not in the file at all,
see R-06.

**Alternatives considered**:

- *Frontmatter with the whole frame, rewritten at the end.* Rejected for the reason above. It would
  also make the record look like an OKF page, which it is not (Invariant 1).
- *Two files, a frame and a narrative.* Rejected: "one Markdown file per run" is the owner's
  decision, and a reader in their own editor should open one thing.

## R-03 — How a tool call, its result and the agent's own text are read

**Decision**: from the CLI's **complete** messages on stdout, in `AgentTranscript`:

| Moment | Line | Read from |
| --- | --- | --- |
| Tool call | `assistant` | each `tool_use` block of `message.content`: its `name` and its `input` |
| Tool result | `user` | each `tool_result` block of `message.content`: its `content` |
| The agent's own text | `assistant` | each `text` block of `message.content` |
| Grimoire's nudge | *none* | the hub, which knows it nudged (RUNS-005, DEC-017) |

**Measured**, one tool call and its result, trimmed to the fields the hub reads:

```json
{"type":"assistant","message":{"role":"assistant","content":[{"type":"tool_use","id":"toolu_01GjqrP3CWV5nAbrmUKKKYHK","name":"Read","input":{"file_path":"…/notes.md"}}]}}
{"type":"user","message":{"role":"user","content":[{"tool_use_id":"toolu_01GjqrP3CWV5nAbrmUKKKYHK","type":"tool_result","content":"1\t# Probe Notes\n2\t\n3\tThe answer is forty-two.\n4\t"}]}}
{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"The number in notes.md is **42** …"}]}}
```

**Rationale**: `contracts/agent-cli-protocol.md` already says the `assistant` message is
"read for `tool_use` blocks, which is how the hub sees what the agent reached for" — the shape was
specified in `001-first-ingest` and only now has a consumer. Each message the probe returned carried
**one** content block, but the field is an array and the API allows several, so every block is read
and none is assumed to be alone.

Three further findings, all of them measured:

- **The hub's own messages are not echoed.** With the prompt written on stdin under
  `--input-format stream-json`, stdout carried `assistant` and `result` and **no** `user` line. So a
  `user` line on stdout is a tool result and never something Grimoire said, and the nudge is
  appended by the hub rather than read back — no moment can appear twice.
- **The partial stream is not needed.** The complete `assistant` message arrives for every block,
  so nothing has to be assembled from `content_block_delta`. `--include-partial-messages` stays for
  the cost ceiling alone, which is what it was added for.
- **The CLI emits `system` subtypes beyond `init`** — `status` and `thinking_tokens` were seen, and
  a `rate_limit_event` type. All of them already fall to `TranscriptSays.Nothing`, and none is
  recorded: what the record holds is what the *run* did.

**Alternatives considered**: assembling moments from `content_block_start` / `_delta` / `_stop`.
Rejected — it is three times the reading for the same result, and the result is already delivered
whole.

## R-04 — A tool result is kept whole, and the record still reads as a log

**Decision**: the result goes into the record **whole**, inside a fenced block whose fence is a run
of backticks **one longer than the longest run of backticks in the result**, and at least three. One
call is one line; its result is the block under it. The browser folds the block (R-08).

**Rationale**: the owner decided (spec, Clarifications) that nothing is cut — a record that silently
loses part of a result is not a record of what the run did. That leaves the *reader* to protect, and
two readers have to be: a person in an editor, and `app.js`. A fixed fence fails on a result that
contains a fence, which a run that reads wiki pages full of code will produce; CommonMark's own rule
— a longer fence closes only on a fence at least as long — makes the block unambiguous for any
content, with no escaping and nothing altered. It also gives `app.js` a deterministic segmentation
rule: an opening fence line, then everything up to the matching closing fence of the same length.

Measured cost, from the probe: one `Read` of a four-line file returned 62 characters. Twenty wiki
pages at a few kilobytes each puts a record in the low hundreds of kilobytes. That is accepted — see
R-08 for what it means for the poll — and records accumulate, which the spec's Assumptions record.

**Alternatives considered**:

- *Cut each result to a length.* Rejected by the owner: the cut part is gone for good.
- *A ceiling on the file's size.* Rejected by the owner: it loses the end of exactly the long run one
  most wants to read.
- *Escaping backticks inside the result.* Rejected: it alters what the tool returned, which is the
  one thing the record must not do.
- *Indented code blocks (four spaces).* Safe against any content too, and segmentable by
  indentation — but it changes every line of the result and makes a diff against what the tool
  actually returned impossible to read.

## R-05 — What the record leaves out

**Decision**: no `thinking` blocks, no partial stream, and not the prompt — neither the instruction,
the purpose description nor the submitted text.

**Rationale, thinking**: measured, the complete `assistant` message's thinking block carries
`"thinking": ""` and a ~400-character `signature` blob. There is nothing in it a person reads, and
putting the blob in the record would bury the run. RUNS-009 asks for "the agent's own text", and a
signature is not text.

**Rationale, the prompt**: the spec's Assumptions settle it — the opening of the submitted text is
already in the list (ACCESS-004), and the instruction and the purpose description are versioned files
of their own (Constitution V.1). Copying them into every record would put the user's whole text in
Grimoire's bookkeeping once per run.

## R-06 — Where the figures live, and when they are written

**Decision**: the tokens spent, the number of tool calls and the number of entries the record could
not hold are **three columns on the run row** in SQLite, written whenever one of them changes. They
are never parsed back out of the Markdown.

**Rationale**: the list polls every second and reads the board; parsing prose Grimoire has just
written, once a second, to recover a number Grimoire already had is the seam the brief warns about.
The figures are state, so they live where the state lives (DEC-023), and both they and the narrative
are written from the same event, so they cannot disagree.

**When**: `CostSoFar` fires on **every** `stream_event`, which the probe shows is ~60 lines per turn,
most of them `content_block_delta` carrying no usage at all. The run's own `Spent` is already a
`Math.Max`, so the store is written only where the figure actually rises — two to four writes a turn,
not sixty. The tool-call count rises once per `tool_use`.

**Rationale for persisting them at all**: RUNS-010 makes them survive a stop, which withdraws
`002-ingest-queue`'s assumption that the token counts of a cut-off run need not. `StoredRun`'s
comment says why that assumption was made — "a number nothing reads would be a placeholder for
later" — and it is exactly what changes here: OUT-02 has the user read a failed run's cost, so the
number now has a consumer (Constitution II.1).

**Alternatives considered**: deriving the figures from the record at start-up. Rejected — it makes
the Markdown a data format, which is what R-02 kept it from being, and a record that could not be
written would take the figures with it.

## R-07 — The run table gains columns, without a migration framework

**Decision**: after `CREATE TABLE IF NOT EXISTS`, the store reads `PRAGMA table_info(runs)` and
issues `ALTER TABLE runs ADD COLUMN` for each column it does not find. No version table, no ordered
scripts, no ORM.

**Rationale**: DEC-023 rejected a migration *framework* as a mechanism with no consumer, and that
still holds. But a consumer for bringing an existing file up to date exists today: the owner's own
`state/submissions.db`, holding the ingests they have already made. The alternative is asking them to
delete it, which throws their list away to save eight lines. `IF NOT EXISTS` is already the
idempotent-schema idiom this file uses; adding the columns it needs is the same idiom, one step
further, and `ADD COLUMN` on SQLite is a metadata-only change. It is not tested as such — that a
committed `ALTER TABLE` survives is SQLite's decision, not ours (Constitution III.8) — but that an
older file comes back with its submissions intact and its figures at zero is ours, and the Contract
suite proves it.

## R-08 — The detail view: a second page, polled, appended

**Decision**: a second static page, `run.html` with `run.js`, reached from the row by a link
carrying the submission's identifier. It polls `GET /api/submissions/{id}/record`, which returns the
record as `text/markdown`, and renders it as segments: one element per moment, appended below what is
already on the page. Existing elements are never replaced. The poll is the list's one second
(`app.js`'s existing precedent).

**Rationale**: reading a run is a second job, and `docs/ux.md` allows a second page once one exists.
A page of its own gives the browser's back button and a shareable URL for nothing, and keeps the
list's script the size it is. Appending rather than re-rendering is what makes "arriving lines must
not move what the user is reading" true without any measurement: a segment already on the page is
never touched, so the scroll position holds and a folded result the user has opened stays open.

**Range requests were considered and rejected for now.** The file only grows, so `Range: bytes=n-`
would fetch exactly the new text, and the host supports it. But the poll is over loopback and a
record in the low hundreds of kilobytes costs nothing there (R-04), so the mechanism has no consumer
yet (Constitution II.1). The trigger, if it comes: a record large enough that the owner feels the
poll.

**Markdown is not rendered.** DEC-019 rules out a bundler and npm, so there is no renderer to reach
for, and `docs/ux.md` asks for monospace wherever the content is a log or a file. The record is
served as text and shown as text; the only structure `run.js` reads is the record's own line shape,
which R-04 made deterministic and a Fast test pins.

## R-09 — Keeping the rows still while the figures grow (ACCESS-005)

**Decision**: rows are **updated in place**, keyed by the submission's identifier, instead of the
whole list being rebuilt. Each figure sits in its own element with tabular figures
(`font-variant-numeric: tabular-nums`, which the `time` column already uses) and a reserved width.

**Rationale**: `app.js` today calls `submissions.replaceChildren(...)` every second, which rebuilds
every row. That was harmless while a row said one word; with two growing numbers it is not — a
proportional digit changes the column's width, which reflows the row, and `1 000` becoming `10 000`
would move the state beside it. Updating in place also fixes something the rebuild already risks: the
Acknowledge button is replaced under the user's finger once a second. Proven by an E2E test that
measures a row's box before and after a figure rises.

## R-10 — A record that cannot be written

**Decision**: the port's methods **do not throw**. The adapter catches its own IO failures, counts
them, and the count travels with the run's figures (R-06). When a write succeeds again, the record
gets one line saying how many entries were lost before it. The record view says that lines are
missing whenever the count is above zero.

**Rationale**: the owner decided (spec, Clarifications) that the run goes on and the gap is made
visible. A throw out of the port would end the run — the opposite — and a silent failure would leave
the user unable to tell an unwritten record from an agent that did nothing. One count, written by the
same call that writes the other two figures, is what makes the gap visible in both places the user
looks. Where the directory cannot be created at all, the head is the first entry lost and the count
is simply higher; nothing special-cases it.

The reasons a write fails do **not** become requirements of their own: Constitution IV.7 has the
values a requirement covers — states, reasons, fields, messages — as a list inside it.

## R-11 — Which level proves which requirement

**Decision**:

| Requirement | Fast | Contract | E2E |
| --- | --- | --- | --- |
| RUNS-007 | the port's behaviour against an in-memory adapter: one record per run, head then narrative then tail, nothing rewritten | the real adapter against the real filesystem: the file is where it is promised, is text, survives a stop mid-narrative, and a write that fails is counted rather than thrown | — |
| RUNS-008 | the frame from a driven run: every field, and each of the seven reasons a run ends | — | — |
| RUNS-009 | the narrative from the recorded CLI lines: call, arguments, result whole, the agent's text, the nudge, in order; the fence longer than any run in the result | — | — |
| RUNS-010 | the figures rise with the run and stand at its end; restored from the store | the figures come back from a real file written by an older schema | — |
| ACCESS-005 | the response carries the state, the model and the figures, and nothing for a submission with no run | — | the browser renders them, and a rising figure does not move the rows |
| ACCESS-006 | the endpoint answers with the record, and with nothing where there is no run | — | the user opens a run from the list, reads it while it is under way, sees lines arrive, and folds a result open |

**Rationale**: each sits at the lowest level that can prove it (Constitution III.6). The filesystem
half of RUNS-007 and RUNS-010 needs the real thing, which is what a Contract suite is for (III.4);
everything the browser must *show* is browser-observable and therefore E2E, as DEC-019 settled for
ACCESS-001 and ACCESS-002. Nothing here needs a signed-in `claude`: the moments are read from
recorded lines, which is what `RecordedTranscript` exists for, and the probe above adds the three
lines it did not yet hold. So no test of this feature carries `requires=signin`, and DEC-021's budget
of three is untouched.

**What is not tested**: that `<pre>` renders monospace, that `ALTER TABLE` commits, that
`text/markdown` is served — framework and dependency behaviour (III.8). The wording of the record's
headings is not tested either; what is tested is that each moment is there, in order, and whole.
