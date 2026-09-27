# Quickstart: Ask the Wiki

**Feature**: `004-ask-the-wiki` · **Date**: 2026-09-27

Two things live here. The **acceptance run** is what closes the feature (Constitution I.9) — the
owner exercising OUT-03 once, with a real model, a real wiki and a real editor, and no stand-ins.
The **validation scenarios** are how the automated suites and the two gates are run.

For what each interface looks like, see [`contracts/`](contracts/); for the entities,
[`data-model.md`](data-model.md); for why each choice was made, [`research.md`](research.md).
None of them is repeated here.

`001-first-ingest/quickstart.md` still describes the prerequisites in full,
`002-ingest-queue/quickstart.md` the state directory and `003-live-run-record/quickstart.md` the
records. Only what this feature adds is below.

---

## Prerequisites, added by this feature

| | |
| --- | --- |
| Nothing new to install | Server-Sent Events are in the framework and `EventSource` is in the browser; the chat is two more static files. No bundler, no npm, no vendored client (DEC-019, research.md R-01) |
| An existing state directory keeps working | The `runs` table gains nothing but a meaning: `submission_id` is null where a question caused the run. An older `submissions.db` comes back with its submissions intact. **Nothing has to be deleted**, and `--fresh` is not needed for this feature |
| Grimoire's own question instruction | `instructions/question.md`, beside `instructions/ingest.md`. It has a default path, like the ingest instruction, and `--question-instruction` overrides it. A question is refused while it is missing (QUERY-003) |
| Obsidian, for the link | `--vault <name>` and `--vault-root <directory>`. Both optional: without them the answer still arrives and page names read as plain text, and the chat says that opening a page is not set up (ACCESS-009) |

### The two new start-up inputs

```bash
dotnet run --project src/Grimoire.Hub -- \
  --wiki      ~/Vault/wiki \
  --purpose   ~/Vault/wiki/purpose.md \
  --model     <a pinned model id> \
  --vault      Vault \          # the vault's name, as Obsidian shows it
  --vault-root ~/Vault          # what the in-vault paths are relative to
```

`--vault-root` is the directory the owner actually has open in Obsidian, which is normally the
directory *above* the wiki rather than the wiki itself — that is why the path is theirs to define
rather than derived. The link the browser builds is
`obsidian://open?vault=Vault&file=wiki/<the page the answer named>`.

### Watching the wiki stay untouched

```bash
cd ~/Vault/wiki && git status --porcelain     # must be empty after every question
```

This is what GUARD-005 promises by construction: the write tools do not exist at a question's own
tool endpoint, so there is no flag that would let one through (contracts/question-run.md).

---

## Part 1 — The acceptance run (what closes the feature)

**Outcome exercised**: OUT-03 — ask a question and get an answer with references to wiki pages.

**Real external systems in place**: a signed-in `claude` on `PATH` with no `ANTHROPIC_API_KEY`
(DEC-001, DEC-009); a real wiki in a git repository the owner keeps, **with pages already in it**
from earlier ingests — there is nothing to answer from otherwise; the owner's own purpose
description; a pinned model id (DEC-010); Obsidian installed with that vault open. No stand-ins, and
no in-memory adapter anywhere.

```bash
./scripts/run-hub.sh                  # .env supplies the wiki, the purpose, the model and the vault
cd ~/Vault/wiki && git status         # note that it is clean before you start
```

Then, in the browser at `http://127.0.0.1:5057`:

1. **Reach the chat from the submit page.** A line of links gets between the three jobs
   (ACCESS-010).
2. **Ask a question about something the wiki already covers.** It appears at once and the browser is
   free — you wait for no answer to send it (QUERY-001).
3. **Watch the answer form.** Text must start arriving while the agent is still writing and must
   grow in place. *Watch the page, not just the answer*: nothing you are already reading may move as
   more arrives — not the question above it, not the steps below it (ACCESS-007).
4. **Read what the question is costing.** Beside it, what this run has spent against the ceiling it
   is held to, rising while you watch. Below, the chat's total — **with no ceiling beside it**, and
   no currency anywhere (ACCESS-008, DEC-015).
5. **Unfold one step under the answer.** It shows that call and what came back, in the shape a run's
   record is read in — there is no second format to learn (ACCESS-007). It was shut until you opened
   it.
6. **Click a page the answer names.** Obsidian opens, in your own vault, on that page. Nothing in
   the wiki changes (ACCESS-009, GUARD-005).
7. **Ask a follow-up that only makes sense in the light of the first answer.** The second answer must
   take the first into account — the run is given what has been asked and answered before it
   (QUERY-002).
8. **Start a new chat.** It is empty, and nothing of the old one is reachable anywhere (QUERY-005).
9. **Check the wiki.** `git status` is clean: no page, no index, no log entry. The wiki is byte for
   byte what it was before you asked anything (GUARD-005, SC-029).

**What must be true for OUT-03 to count as exercised**: an answer that named real pages of this
wiki, read as it formed, checked by opening a step and a page, and followed up — with the wiki
unchanged and nothing of the chat left after it was put away.

---

## Part 2 — The cases worth walking before the acceptance run

These are not the acceptance run; they are what to try while the hub is up, because each one is a
requirement the automated suites prove and the owner should see once.

**A question waiting its turn.** Submit a text on the list page, then switch to the chat and ask a
question while that run is under way. The chat must say the question is waiting, and it must be
answered after the ingest finishes — never beside it (RUNS-002, ACCESS-007, SC-033).

**A question that gets no answer.** Easiest to provoke by stopping `claude` mid-run, or by letting a
question reach a ceiling. The chat must say against that question that it got no answer and **why**,
must show nothing half-written as an answer, and must offer the one control that acknowledges the
failure. Nothing further runs until you acknowledge it — a waiting question stays waiting — and
afterwards you can ask again (QUERY-006, RUNS-003, SC-034).

**A reader who walks away.** Ask a question, close the tab while the answer is being written, and
open the chat again. The run was not stopped, and the chat shows the answer and the steps that
arrived while nothing was connected (QUERY-005, ACCESS-007, SC-037).

**Two tabs at once.** Open the chat twice. A question asked in one appears in the other, and
starting a new chat empties both: there is one chat and every browser reads it (QUERY-005).

**Grimoire stopped and started again.** With a chat full of questions and answers, stop the hub and
start it. The chat is empty and nothing of it is anywhere; a question that was being answered has
its agent terminated at start-up and its run reads failed (QUERY-005, RUNS-004, RUNS-006, SC-032).

**Without the vault inputs.** Start the hub with neither `--vault` nor `--vault-root` and ask a
question. The answer must arrive as normal with the page names readable in it as plain text, and the
chat must say once that opening pages is not set up. The question is **not** refused — that is for a
missing question instruction or purpose description (ACCESS-009, QUERY-003).

**A question the wiki cannot answer.** Ask something the wiki genuinely does not cover. The answer
must say plainly that the wiki holds nothing about it and name what the agent looked at — not answer
from what the model itself knows. That is the question instruction's doing (QUERY-004), so it is one
of the things to read before the feature closes (Constitution I.9).

**A question's run leaves nothing behind.** After a question has been answered, look in
`<state>/runs/`: there is no new file. Only a run a submission causes has a record (RUNS-007,
SC-035).

---

## Part 3 — The validation scenarios (the suites and the gates)

```bash
dotnet build Grimoire.slnx

dotnet test tests/Grimoire.Fast.Tests -- --timeout 15s                       # the time-budget gate
dotnet test tests/Grimoire.Contract.Tests -- --timeout 90s \
  --filter-not-trait "requires=signin"
dotnet test tests/Grimoire.Contract.Tests -- --timeout 90s \
  --filter-trait "requires=signin"                                           # needs a signed-in claude
dotnet test tests/Grimoire.E2E.Tests                                         # real browser
```

The E2E browser is installed once from the built output:

```bash
pwsh tests/Grimoire.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium
```

Both gates, and the trace document:

```bash
dotnet run --project tools/Grimoire.Trace -- check              # every push
dotnet run --project tools/Grimoire.Trace -- check --complete   # on a PR based on main
dotnet run --project tools/Grimoire.Trace -- write              # regenerates docs/trace.md
```

`--complete` fails on a `test` requirement with no test, so it is what catches a requirement
registered in `docs/capabilities/query.md` before its test exists. QUERY-004 is proven by `review`
and is therefore not among them — the review-checklist item is what proves it, and the owner reads
what it is about before the feature closes (Constitution I.9, III.1).

**No new `requires=signin` test.** DEC-021's budget of four is spent, and this feature needs no fresh
evidence from the real CLI: the read-only grant is enforced by which tools exist at the run's
endpoint, not by a flag whose behaviour would have to be measured (research.md R-06).
