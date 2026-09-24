# Quickstart: The Ingest Queue

**Feature**: `002-ingest-queue` · **Date**: 2026-09-23

Two things live here. The **acceptance run** is what closes the feature (Constitution I.9) — the
owner exercising OUT-01 once more, now with a queue in it, with a real model and a real wiki and no
stand-ins. The **validation scenarios** are how the automated suites and the two gates are run.

For what each interface looks like, see [`contracts/`](contracts/); for the entities,
[`data-model.md`](data-model.md). Neither is repeated here.

`001-first-ingest/quickstart.md` still describes the prerequisites in full. Only what this feature
adds is listed below.

---

## Prerequisites, added by this feature

| | |
| --- | --- |
| A state directory | Where the queue is kept, given at start-up: `--state <path>`, or `GRIMOIRE_STATE` in `.env` for `scripts/run-hub.sh`. Defaults to `state/` beside the hub. **Not inside the wiki** — the queue writes nothing into the wiki and Grimoire's bookkeeping does not belong in the user's version history |
| Nothing else | No new external tool. SQLite arrives as a package (`Microsoft.Data.Sqlite`) with no native install step and no server to run |

### How to tell a run's agent from every other `claude`

Parts 2 and 3 need to look at one particular process: the agent of one run. **Do not look for it with
`pgrep -f claude`, and never end one with `pkill -f claude`.** On a machine where Claude Code is
running, both match Claude Code itself and every shell it has started — its own path contains
`.claude/`. `pgrep` would then never print nothing however well RUNS-006 worked, and `pkill` would
end the owner's own session.

Ask the store instead, which is where RUNS-006 records the identity anyway:

```bash
STATE=local/state                     # whatever --state / GRIMOIRE_STATE points at
agent() { sqlite3 "$STATE/submissions.db" \
  "select agent_process_id from runs order by started_at desc limit 1;"; }

ps -p "$(agent)"                      # the newest run's agent: alive, or nothing
```

`ps -p` prints a header and nothing else when that process is gone, which is the "prints nothing"
every step below means.

Everything else is as `001-first-ingest` listed it: the .NET 10 SDK, a signed-in `claude` on `PATH`,
the Playwright browsers for the E2E suite, a real wiki, a purpose description, and a pinned model
id. Still no `ANTHROPIC_API_KEY` (DEC-001).

---

## The owner's acceptance run

**Outcome exercised**: OUT-01 — submit a text in the browser and afterwards find new, linked pages
including a source page in the wiki.

**Real external systems in place**: the `claude` CLI signed in and reaching a real model; a real
wiki repository on disk; the real state file the hub was started with. No stand-ins and no temporary
directory. The E2E suite's drivable harness proves what the browser does; this run is what proves
the queue against a real agent.

### Part 1 — the queue

1. Start the hub: `./scripts/run-hub.sh` (add `--fresh` to begin from an empty wiki).
2. Open the page and submit a first text.
3. **While that run is under way**, submit a second text and then a third.
   - Each is accepted. Nothing is refused, and no message about a run being in progress appears —
     that refusal was INGEST-005 and is retired.
   - All three rows carry the opening of their own text and the time they were submitted; the second
     and third read `submitted` (RUNS-002, ACCESS-004).
4. Let the first run finish.
   - The second run starts by itself, without the owner doing anything (RUNS-002).
   - At no moment do two rows read `running`.
5. Let the second and third run through.

**What must be seen**: three texts accepted without waiting; the runs starting one at a time, in the
order the texts were made; each row recognisable by its own opening words.

### Part 2 — a failure holds the queue

6. Submit two more texts. Make the first of them fail — the simplest way is a text that sends the
   run past a ceiling, or ending its agent by hand while it runs, which fails the run through its
   non-zero exit: `kill -9 "$(agent)"`, with `agent` as defined above. **Not `pkill -f claude`** —
   see the note in the prerequisites.
7. Wait. **Nothing starts.** The text behind it stays reading `submitted` (RUNS-003).
8. Acknowledge the failed run in the browser.
   - The waiting text's run starts.
   - The acknowledged row still reads `failed` (RUNS-003).

**What must be seen**: a queue that stops at a failure and moves only when the owner says so, and a
failed run that stays failed after being acknowledged.

### Part 3 — stopping and starting again

Two passes, in this order, because they prove different halves of RUNS-004: the clean stop also
proves RUNS-006, and the kill proves that nothing depended on RUNS-006 having run.

**Each pass runs the same six-step chain**, and the chain is the point — a pass that only shows the
submissions surviving has proven half of what the feature promises:

> a run under way, **a text waiting behind it**, the hub stopped, the hub started again, the
> interrupted run reading `failed` with the waiting text still waiting and nothing started, the
> failure acknowledged, and the waiting text's run starting.

#### Pass A — the clean stop (Ctrl-C)

9. Wait until the queue is empty: every row reads `done` or `failed`, and nothing reads `running`.
   Then submit two texts. The first starts; the second reads `submitted` and is waiting behind it.
10. While the first run is under way, note `agent` and then **stop the hub with Ctrl-C**.
    - No agent is left behind: `ps -p <that pid>` prints nothing (RUNS-006). This is the one
      observation Pass B cannot make. It is quick — measured at **under two seconds** on
      2026-09-24, because the interrupt of DEC-016 ends the turn and the ten-second kill backstop
      is never reached. If the process is still there after half a minute, that is the finding.
11. Start the hub again with the same `--state`.
    - Every submission the owner ever made is still listed, with the state it carried.
    - The run that was under way reads `failed`.
    - The second text still reads `submitted`. **It did not start** — that failure blocks the queue
      (RUNS-003, RUNS-004).
    - Whatever the interrupted run had already written is still in the wiki, unchanged (WIKI-003).
12. Acknowledge the failed run in the browser.
    - The waiting text's run starts, by itself (RUNS-003).
    - The acknowledged run still reads `failed`.

#### Pass B — the kill (`kill -9`)

The pass RUNS-004 was clarified for, and the one that proves RUNS-006's second half. Nothing runs on
the way out here, so everything that survives, survives because it was already written — and the
agent that survives with it has to be dealt with on the way back in.

13. While the run from step 12 is under way, submit one more text, so that something is waiting
    behind it again. It reads `submitted`.
14. **Kill the hub outright**: `pgrep -f Grimoire.Hub` for the process id, then `kill -9 <pid>`.
    Nothing is sent to the agent and nothing is written on the way out; no shutdown step runs at
    all. That is the whole point of this pass.
    - **Check that the agent survived**: `ps -p "$(agent)"` prints a process. It should — a
      `SIGKILL` gives Grimoire no chance to act, so nothing stopped it. **Leave it running.** Note
      the number; the store keeps it, so it is still readable after the restart.
15. Start the hub again with the same `--state`.
    - **First, before looking at the browser**: `ps -p <the number from step 14>` prints nothing.
      The agent noted at step 14 is gone — Grimoire found it by the process identity recorded with
      its run and terminated it as it came up, before that run read `failed` and before anything
      else started (RUNS-006). This is the observation Pass B exists for.
    - Every submission is still listed, with the state it carried — including the one submitted at
      step 13, seconds before the kill.
    - The run that was under way when the kill landed reads `failed`.
    - The text from step 13 still reads `submitted`, and **nothing has started** behind the failure.
    - What the killed run had written *before the termination* is still in the wiki (WIKI-003).
16. Acknowledge the failed run in the browser.
    - The waiting text's run starts, by itself.
    - The acknowledged run still reads `failed`.
17. Let that last run finish, then read the wiki.

**What must be seen**: the six-step chain above, whole, in **both** passes — a stopped run reading
`failed` after the restart, a text still waiting behind it, nothing started until the owner
acknowledges, and then the waiting text starting. And, in both passes, **the run's own agent gone
once Grimoire is running again**: in Pass A because the stop took it, in Pass B because the next
start-up did (RUNS-006). At the end, a wiki holding every text that was submitted, each with its
source page, as OUT-01 asks.

Two ways this run fails even if everything looks right on the page:

- **Pass B loses the text submitted at step 13.** RUNS-004 is not met however well Pass A went: that
  text was accepted with a `202` and a kill followed, which is exactly the stop the clarify session
  said RUNS-004 is for.
- **Pass B step 15 still shows that agent alive.** Then an agent with the granted tools is writing
  into the wiki with no ceiling on it, beside whatever Grimoire starts next — which is the hole
  RUNS-006 was extended to close.

### After the run

- The ceilings are still the owner's to revise (DEC-015).
- Record the outcome's status and this feature's spec reference in `docs/product.md` — one of the
  two edits an agent makes to that file (IV.4).

---

## Validation scenarios

Nothing here proves the outcome; these are how the suites and the gates run. Commands are as
`CLAUDE.md` documents them.

### The suites

```bash
dotnet build Grimoire.slnx

dotnet test tests/Grimoire.Fast.Tests -- --timeout 15s
dotnet test tests/Grimoire.Contract.Tests -- --timeout 90s --filter-not-trait "requires=signin"
dotnet test tests/Grimoire.E2E.Tests
```

The Contract run now carries `SqliteSubmissionStoreTests` beside `FileSystemWikiStoreTests`. Both
work in a temp directory and neither needs a sign-in, so both run in CI inside the 90 s budget.

The agent adapter's sign-in tests are unchanged and still run by hand before the PR (DEC-021):

```bash
dotnet test tests/Grimoire.Contract.Tests -- --timeout 90s --filter-trait "requires=signin"
```

### The gates

```bash
dotnet run --project tools/Grimoire.Trace -- check
dotnet run --project tools/Grimoire.Trace -- check --complete   # on a PR based on main
dotnet run --project tools/Grimoire.Trace -- write              # regenerates docs/trace.md
```

`check --complete` is the run that fails on a `test` requirement with no test. Phase 1 registers all
six requirements in `docs/capabilities/` before the tests that prove them exist (IV.2), so that run
is red on the feature branch until the last phase lands — which is why it applies where a feature
lands on `main` and not to a phase PR (IV.3).

`time-budget` is Microsoft.Testing.Platform's `--timeout` on the two runs above (DEC-008).

### Driving the queue without a model

The Fast suite's `FastHub` and the E2E suite's `DrivableHarness` are in-memory adapters at the agent
port (III.9). Between them they drive a submission to each of the four states, hold a failure, and
restart the hub over one store — which is how every scenario of Parts 1 to 3 above is exercised
without a sign-in, a model or a real second of waiting (DEC-018).

What they cannot do is prove that a committed write survives a process being killed, because that is
SQLite's decision rather than one of ours (III.8, research.md R-02). Pass B is where that is
exercised, by a person.

Terminating an agent that outlived a stop **is** ours, and it is proven without a person: a Contract
test starts a real child process, hands its recorded identity back as a start-up would, and checks
that it is terminated — and that a live process whose identifier matches but whose start time does
not is left alone (research.md R-11). That test needs no sign-in and runs in CI.
