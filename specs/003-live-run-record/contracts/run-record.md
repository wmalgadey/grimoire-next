# Contract: The run record (hub ↔ a run's record on disk)

New in `003-live-run-record`. Two promises: the **port** the hub writes a record through, and the
**shape** of the file it writes — because the browser segments that file, and a shape nobody wrote down
is a shape that changes by accident.

The record is Grimoire's own bookkeeping and is **never inside the wiki** (Invariants 1 and 3, DEC-023's
reasoning). `Program.cs` already refuses a `--state` directory inside the wiki, and the records inherit
that guard.

---

## Where a record lives

```text
<state>/runs/<runId>.md
```

`<state>` is the directory `--state` names — `state/` beside the hub by default — and is the same
directory `submissions.db` sits in. One file per run, named by the run's identifier, which is a fresh
GUID and so cannot collide.

---

## The port

`IRunRecord`, declared by the RUNS context, with one adapter `MarkdownRunRecord`. The Fast suite has an
in-memory adapter at the same port (Constitution III.9).

| Member | When it is called | Promise |
| --- | --- | --- |
| `Begin(head)` | once, as the run begins, from `RunConductor.Begin` | The file exists and holds the head. Called before the agent's process exists, so a dispatch that fails still leaves a record |
| `Append(moment)` | once per moment, in the order it happened | The moment is on disk before the call returns, appended after everything already there |
| `End(tail)` | once, where the run ends | The tail is appended. A run ends once, so this is called once; a second call appends nothing |
| `EntriesLost(runId)` | whenever the figures are written | How many of the calls above could not be written for that run |
| `Read(runId)` | whenever the browser asks for the record | The record as it stands, byte for byte, or nothing where that run has no record. A run still under way answers with the record as far as it goes |

**Every call returns only once the change is on disk.** The same promise `submission-store.md` makes and
for the same reason: RUNS-004 covers a stop that gives Grimoire no chance to act, so nothing may be
waiting to be written. A record is only ever appended to, which is the cheapest write there is to make
survive a stop.

**No delete, no rewrite, no move.** The same shape `IWikiStore` and `ISubmissionStore` have: RUNS-007
says a record is never rewritten or removed, so no member exists that could.

**There is one read, and it is on this port.** This document first said there was none, on the
reasoning that the browser reads the file. That was wrong, and `003-live-run-record` corrected it while
building the endpoint: the filesystem is an external system and appears only inside an adapter
(Constitution V.2), so the endpoint of [hub-http-api.md](hub-http-api.md) cannot open the file itself —
it asks the port and serves what comes back, unaltered. Reading is not what RUNS-007 forbids; that
sentence was reasoned from a record never *changing*, which a read does not touch. The member has a
consumer in the same feature that added it (II.1).

**`Read` is taken under the same lock the writes take.** `File.AppendAllText` is not atomic, so a read
landing inside one would serve a segment cut in half — or a byte sequence that is not UTF-8 at all — on
the very poll where the run is most alive. Every answer is a record taken at an append boundary.

**Nothing here throws.** An IO failure — an unwritable directory, a full disk — is caught by the
adapter, counted, and the run goes on (RUNS-007; the owner's decision in the spec's Clarifications). A
throw would end the run, which is the opposite of what was decided.

**Synchronous**, like `ISubmissionStore` and unlike every other port in the tree: the calls come from
the conductor on whatever thread the harness reads on, and the writes underneath are synchronous, so an
async signature would promise a yielding call that never yields (DEC-023's reasoning).

---

## The file's shape

Markdown. **The wording is the adapter's and is not part of this contract** — it is not tested
(Constitution III.8). What *is* promised is the segmentation, because `run.js` depends on it:

1. **A record is a sequence of segments.** A segment begins at a line that starts with `## ` at column
   one and runs to the line before the next such line, or to the end of the file. Everything before the
   first such line is the head.
2. **A segment's first line says what it is**, after the time: `called <tool>`, `<tool> returned`,
   `the agent`, `Grimoire`, or `ended <done|failed> — <reason>`.
3. **A fenced block holds everything that is not prose** — a call's arguments, and a call's result. Its
   opening fence is a run of backticks **one longer than the longest run of backticks in what it holds**,
   and at least three; its closing fence is a run of the same length at column one. This is CommonMark's
   own rule, and it is what makes a result containing a fence — which a run reading wiki pages full of
   code will produce — unambiguous without altering a byte of it (research.md R-04).
4. **A line that starts with `## ` inside a fenced block is not a segment boundary.** A reader finds the
   fence first and skips to its close. This is the one rule a naive split would get wrong, and it is why
   rule 3 is a promise and not a detail.
5. **Nothing of a tool result is cut or escaped.** It goes in whole, byte for byte, however large
   (RUNS-009, the owner's decision in the spec's Clarifications).

### What a record holds, in order

| Part | Written at | Holds |
| --- | --- | --- |
| Head | `Begin` | The run's identifier, the submission's, the pinned model, the granted tools, both ceilings, when the run started (RUNS-008) |
| Moments | `Append` | Each tool call with its arguments; what each returned, whole; the agent's own text; anything Grimoire said to the agent — today only the nudge of RUNS-005 (RUNS-009) |
| Tail | `Ended` | When it ended, `done` or `failed`, why, elapsed against the elapsed ceiling, tokens against the cost ceiling, and the tokens of every model the run touched (RUNS-008, DEC-015) |

**A run that never reported a breakdown has no model rows.** The tokens per model come from a turn's
own report; a run stopped inside its first turn — a cost ceiling reached, a process that died, a tool
surface that was not the grant — has none, and its tail holds the total against the ceiling and
nothing more. The head still names the model it was dispatched on. Nothing is invented: attributing the
whole total to that model would claim the CLI's background calls, which a run causes but never asks
for, were made on it.

**A record of a run still under way is the head and however many moments have happened.** It has no
tail, and that is the only difference between it and a run from last month (the owner's wish, brief §3).

**A run that ended before its first model call** — a reported tool surface that was not the grant
(GUARD-001) — has a head, no moments, and a tail saying so. The record exists for every run there is.

### What a record never holds

- The instruction, the purpose description, or the submitted text. The record is what the run *did*; the
  opening of the text is in the list (ACCESS-004) and the two texts are versioned files of their own
  (Constitution V.1).
- `thinking` blocks. Measured: the complete `assistant` message carries `"thinking": ""` and a signature
  blob, which is nothing a person reads (research.md R-05).
- Anything of the wiki's content beyond what a tool call returned. Grimoire reads nothing in the wiki to
  write a record; RUNS-005's `log.md` read is unchanged and is for the run's identifier alone.
- A currency figure. Cost is tokens (DEC-015).

### Reasons a run ended

One of seven, and each is a state the code already reaches (data-model.md §Why a run ended). They are
**values inside RUNS-008**, not requirements of their own — Constitution IV.7 has the values a
requirement covers as a list inside it.

---

## What the hub promises about writing

- **The head before the agent.** `Begin` runs inside `RunConductor.Begin`, which the board calls under
  its lock at the moment it hands the submission out — before the dispatch. A run whose dispatch fails
  still has a record, and its tail says the agent's process died.
- **One moment per thing that happened, once.** The CLI does not echo what Grimoire writes to it
  (measured, research.md R-03), so the nudge appears exactly once: appended by the hub, never read back.
- **The tail where the verdict is taken**, which is the run's process exit or the interrupt — never at a
  `result` (`agent-cli-protocol.md`). A run that is nudged gets no tail at its first `result`.
- **Nothing is written after the tail.** A run ends once. The conductor removes it, and a later report
  is a no-op, as it already is for the states — but the guarantee is the *adapter's*: the conductor
  looks a run up and appends in two steps, so a moment already in flight can arrive after the ending
  that removed it, and only the adapter knows whether the tail is on disk. Such a moment is dropped and
  is **not** counted as an entry lost, because no write failed.
- **A run ends once, whether or not its tail reached the disk.** Marking it ended only on a successful
  write was tried, so that a lost tail could be written later; nothing writes it later, because a run has
  no moments after its end, and it left the record able to take a late moment with no tail behind it — a
  record whose last line is not its ending. A tail that could not be written is one more entry lost, and
  RUNS-007's promise is kept by the count rather than by a retry: the row says lines are missing, and so
  does the record view.
