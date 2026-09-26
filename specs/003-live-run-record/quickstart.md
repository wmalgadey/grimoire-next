# Quickstart: The Live Run Record

**Feature**: `003-live-run-record` · **Date**: 2026-09-26

Two things live here. The **acceptance run** is what closes the feature (Constitution I.9) — the owner
exercising OUT-02 and OUT-16 once, with a real model and a real wiki and no stand-ins. The **validation
scenarios** are how the automated suites and the two gates are run.

For what each interface looks like, see [`contracts/`](contracts/); for the entities,
[`data-model.md`](data-model.md). Neither is repeated here.

`001-first-ingest/quickstart.md` still describes the prerequisites in full, and
`002-ingest-queue/quickstart.md` the state directory. Only what this feature adds is below.

---

## Prerequisites, added by this feature

| | |
| --- | --- |
| Nothing new to install | The record is a file in the state directory Grimoire already owns, and the browser gains two static files. No bundler, no npm, no renderer (DEC-019) |
| An existing state directory keeps working | The `runs` table gains four columns, added where they are missing (research.md R-07). An older `submissions.db` comes back with its submissions intact and its figures at zero. **Nothing has to be deleted**, and `--fresh` is not needed for this feature |
| Where the records go | `<state>/runs/`, beside `submissions.db`. Created on the first run. Never inside the wiki — `Program.cs` already refuses a `--state` inside it |

### Looking at a record from the shell

```bash
STATE=local/state                     # whatever --state / GRIMOIRE_STATE points at

ls -lh "$STATE/runs"                  # one file per run, newest last
tail -f "$STATE/runs/"*.md            # the record of a run under way, growing
```

`tail -f` on the newest file is the same thing the browser's run page shows, which is the point: the
browser is a window onto this file and not a second place the run lives.

---

## Part 1 — The acceptance run (what closes the feature)

**Outcome exercised**: OUT-02 — see for every run what it did, why it ended and what it cost; and
OUT-16 — watch what the agent is doing while a run is in progress.

**Real external systems in place**: a signed-in `claude` on `PATH` with no `ANTHROPIC_API_KEY`
(DEC-001, DEC-009); a real wiki in a git repository the owner keeps; the owner's own purpose
description; a pinned model id (DEC-010). No stand-ins, and no in-memory adapter anywhere.

```bash
./scripts/run-hub.sh                  # .env supplies the wiki, the purpose and the model
```

Then, in the browser at `http://127.0.0.1:5057`:

1. **Paste a text and submit it.** The row appears at once and turns `running`.
2. **Stay on the list while the run proceeds.** The row must carry the model it runs on, a token figure
   and a count of tool calls, and the two figures must rise while you watch. *Watch the list, not just
   the row*: as a figure grows, nothing on the page may move — no row may shift, and the state beside
   the figure may not jump sideways (ACCESS-005).
3. **Open the run from its row.** The record's head is there — model, granted tools, both ceilings, when
   it started. Then tool calls, each one line with its result folded under it, and the agent's own text
   as prose between them. **Leave the page open**: further calls must arrive below what you are reading,
   and neither your scroll position nor a result you have opened may be disturbed (ACCESS-006).
4. **Open one result.** It must be the whole thing the tool returned — a `read_page` of a long wiki page
   is the case to pick — with nothing cut and nothing escaped (RUNS-009).
5. **Walk away and come back.** The row reads `done` with its final figures. The record now has its
   tail: when it ended, that it ended `done`, why, elapsed against the 15-minute ceiling, tokens against
   the 2 000 000, and the tokens of every model the run touched — the CLI's own background call among
   them, which is why the figure is over the main model's alone (RUNS-008, DEC-015).
6. **Stop the hub, and open the same run in your own editor.** `<state>/runs/<runId>.md` is Markdown and
   holds the same run the browser showed. Read it as text; nothing about it needs Grimoire (US3).
7. **Look in the wiki.** `git status` in the wiki's repository shows the pages and `log.md` the run
   wrote, and **nothing of Grimoire's record of it** (Invariants 1 and 3, SC-023).

**What the owner must see for the outcome to count as exercised**: a run they could follow while it
happened, read back afterwards in full with a reason it ended and a cost per model, and open in their
own editor with the hub stopped — and no trace of any of it inside the wiki.

### The two cases worth exercising as well

Neither is required for the outcome to count, and both are what the owner would otherwise find later:

- **A failed run.** Easiest to provoke by pointing `--model` at a pinned id and then editing
  `Ceilings.Fixed` down, or by killing the agent's process (see `002-ingest-queue/quickstart.md` for how
  to find it without `pkill -f claude`). The row must read `failed` with the figures the run reached, and
  the record's tail must say what stopped it — the ceiling, the missing log entry, or the process
  (RUNS-008).
- **A stop while a run is under way.** Stop the hub with a run going. What had been appended to the
  record is still there, the run comes back reading `failed` with the figures it had reached, and the
  tail says Grimoire stopped (RUNS-004, RUNS-007, RUNS-010).

---

## Part 2 — Validation scenarios (the suites and the gates)

Everything here runs without a signed-in `claude`: the moments come from recorded CLI lines, which is
what `RecordedTranscript` is for, and this feature adds no test carrying `requires=signin` (research.md
R-11, DEC-021).

```bash
dotnet build Grimoire.slnx

# The default run and the time-budget gate (Constitution III.7, DEC-008)
dotnet test tests/Grimoire.Fast.Tests -- --timeout 15s

# The real filesystem and the real SQLite file
dotnet test tests/Grimoire.Contract.Tests -- --timeout 90s --filter-not-trait "requires=signin"

# The browser, against the running hub with an in-memory agent (DEC-020)
dotnet test tests/Grimoire.E2E.Tests

# The trace gate, and the trace document at close
dotnet run --project tools/Grimoire.Trace -- check
dotnet run --project tools/Grimoire.Trace -- write
```

The E2E suite needs its browser once, from the built output:

```bash
pwsh tests/Grimoire.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium
```

### What each requirement is validated by

| Requirement | Validated by |
| --- | --- |
| RUNS-007 | Fast: one record per run, head then moments then tail, nothing rewritten, a failed write counted rather than thrown. Contract: the file is at `<state>/runs/<runId>.md`, is text, and survives a stop mid-narrative |
| RUNS-008 | Fast: every field of the head and the tail, and each of the seven reasons a run ends |
| RUNS-009 | Fast: from the recorded lines — call, arguments, result whole, the agent's text, the nudge, in order; and a result containing a fence still closing correctly |
| RUNS-010 | Fast: the figures rise, stand at the end, and come back from the store. Contract: they come back from a real file written by the older schema |
| ACCESS-005 | Fast: the response carries the state, the model and the figures together, and no run fields for a submission with no run. E2E: the browser renders them, and a rising figure moves no row |
| ACCESS-006 | Fast: the endpoint answers with the record, and `404` where there is no run. E2E: the user opens a run from the list, sees lines arrive while it is under way, and folds a result open |

### Manual checks, at close

Two things no test covers and the owner reads instead (Constitution I.9):

1. **`docs/capabilities/runs.md` and `access.md`** hold the six new requirements, and ACCESS-002 is
   under "Retired" with its ID kept and the reason written down (Constitution IV.1, IV.2).
2. **`docs/decisions.md`** holds this plan's decisions, and the one that departs from DEC-023's "two
   tables that do not change shape" says so (Constitution II.6).
