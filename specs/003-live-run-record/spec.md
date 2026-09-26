# Feature Specification: The Live Run Record

**Feature Branch**: `003-live-run-record`

**Created**: 2026-09-26

**Status**: Draft

**Input**: User description: "erzeuge einen spec für den brief in `docs/briefs/live-run-record.md`" — the brief is the owner-written input; sections 1–5 of it are the material for this spec, section 6 is for `/speckit-plan`.

## Outcome advanced (OUT-NN) *(mandatory)*

**Outcome**: OUT-02 — see for every run what it did, why it ended and what it cost

**Capabilities touched**: RUNS, ACCESS

OWNER DECISION (brief §1): **OUT-16** — watch what the agent is doing while a run is in progress —
is achieved by the same work and closes with this feature. The recording is the same either way, and
the list already polls, so serving the record while the run proceeds is what makes it live. This spec
names OUT-02 as the one outcome it advances (Constitution I.3); at close the agent sets OUT-02 and
OUT-16 to Done and the next Now in the one edit IV.4 allows, and moves OUT-16 into this feature's row
in `docs/product.md` §7. Until then OUT-02 is the single Now and OUT-16 stays Next, so I.1 holds at
every commit.

This feature adds exactly one new user interaction — opening a run and reading its record
(Constitution I.6). It adds no new operation: the run already exists and is already dispatched,
watched and judged; what is new is that it leaves a record. It adds no new external system: the
record is a file, and the filesystem is already reached (`FileSystemWikiStore`, and the state
directory of DEC-023).

## Blocking open questions (none, or stop) *(mandatory)*

**Blocking**: None. All four open questions in `docs/product.md` §9 block Later outcomes — OUT-08,
OUT-15, OUT-18, OUT-19/OUT-20 — and none of them touches OUT-02 or OUT-16.

## Out of scope *(mandatory)*

- Being told a run ended without Grimoire open → OUT-17, which has its own promotion trigger.
- Comparing runs, trends, statistics over time, a dashboard → OUT-13.
- A stop button for a run in progress → not an outcome yet, no ID. Today only a ceiling ends a run
  early (GUARD-004); a stop the user presses is new behaviour in no outcome and is proposed to the
  owner when it is wanted.
- Cost expressed in currency → never (`docs/product.md` §4, DEC-015). The CLI's own budget figure is
  a client-side estimate its documentation says can differ from the bill. Cost is tokens.
- Anything of Grimoire's bookkeeping inside the wiki → never (Invariants 1 and 3, DEC-023). The
  record lives in a directory Grimoire owns.
- Anything that lets the user change a run from the record — re-running it, editing it, deleting it,
  removing old records → not an outcome. The record is read, and nothing else.
- Configurable ceilings, or per-run tuning of what is recorded → non-goal (`docs/product.md` §4).

## Clarifications

### Session 2026-09-26 — decisions carried in with the brief

- OWNER DECISION: one Markdown file per run, in a `runs/` directory of Grimoire's own, appended as
  the run proceeds. The detail view is a window onto that file, not a second place where the
  information lives.
- OWNER DECISION: a run still going and a run from last month look the same; the live one just has
  fewer lines yet. There is no second shape for a finished run.
- OWNER DECISION: the list row carries two figures — cost so far and the number of tool calls — plus
  the model. It stays a row: growing figures must not make the list jump or reflow while the user is
  reading it (`docs/ux.md`, "Live content grows in place").
- OWNER DECISION: cost is tokens, never currency (DEC-015).
- OWNER DECISION: **ACCESS-002 is contradicted by this feature.** Its clause "and no further detail
  about the run" is precisely what OUT-02 exists to undo. It is retired here and keeps its ID
  (Constitution IV.1, IV.2); ACCESS-005 carries the four states forward.

### Session 2026-09-26 — specify

- Q: A tool result can be kilobytes — a run that reads twenty wiki pages writes a large file. How does
  the record stay readable? → A: **The record keeps every result whole.** Nothing is cut and nothing
  is dropped, however large, because a record that silently loses part of what a call returned is not
  a record of what the run did. What keeps the run readable is the *view*: the user follows the run
  without reading the results in full and reaches any one of them when they want it — the shape the
  brief names, a call as one line with its result folded beneath. Cutting the result to a length was
  rejected because the cut part is gone for good; a ceiling on the file's size was rejected because it
  loses the end of exactly the long run one most wants to read. The cost is accepted: records are
  large and they accumulate. RUNS-009 gains "whole"; ACCESS-006 gains the concern the view answers.
- Q: What happens when the record cannot be written — the directory is not writable, the disk is full?
  → A: **The run goes on, and the gap is made visible.** That something is missing is recorded with
  the run and appended to the record as soon as it can be written again, and the view says that lines
  are missing, so the user never mistakes an unwritten record for an agent that did nothing. Ending
  the run failed was rejected: the wiki result of a run is worth more than its protocol, and a write
  that fails is not the agent's doing. Letting it pass silently was rejected because a silent gap is
  indistinguishable from a run that did nothing.
- OWNER DECISION: **a reason a thing fails is never a requirement of its own.** Constitution IV.7
  already says so — the values a requirement covers, "states, reasons, fields, messages", are a list
  inside it. So neither answer above registers a new ID: the first refines RUNS-009 and ACCESS-006,
  the second RUNS-007 and ACCESS-006. No amendment to the constitution is needed; the rule is there.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Read back what a run did, why it ended and what it cost (Priority: P1)

A run has ended. From the list the user opens it and reads the whole run: the model it ran on, the
tools it was granted, both ceilings and where the run stood against them, when it started and when it
ended, and why it ended that way — and then, in the order things happened, every tool call with its
arguments, what came back from each, and the agent's own text in between. Where the run reads failed,
the same place says what stopped it: a ceiling, a missing log entry, a process that died, a grant that
did not match, or Grimoire being stopped underneath it.

**Why this priority**: It is OUT-02 itself and the whole of the feature's value. Everything else is
this record seen earlier or seen elsewhere. Without it the user has one word per run and no way to
find out what happened.

**Independent Test**: Drive a run to done and a second to failed, open each from the list, and confirm
the record holds the frame and the narrative, and that the failed one says what stopped it. Needs
nothing live and no editor.

**Acceptance Scenarios**:

1. **Given** a run that ended done, **When** the user opens it from the list, **Then** they see the
   model, the granted tools, both ceilings with where the run stood against them, when it started and
   ended, and why it ended — and the tool calls with their arguments, what each returned, and the
   agent's own text, in the order they happened.
2. **Given** a run that ended failed, **When** the user opens it, **Then** the same place says what
   stopped it, and the narrative holds everything the run had got as far as.
3. **Given** a run that ended, **When** the user looks at its row in the list, **Then** the row
   carries the model it ran on, the tokens it spent and the number of tool calls it made, beside its
   state.

---

### User Story 2 - Watch a run while it is under way (Priority: P2)

The user submits a text; the row appears and turns running. From then on the row tells them which
model is driving the run, what it has cost so far and how many tool calls it has made, and those
figures grow while they watch — without the list moving under them. When they want to know what the
agent is actually doing, they open the row and see the run as far as it has got; they leave it open
and the next call arrives. Then they walk away, and later come back to the finished record in the
same place, in the same shape.

**Why this priority**: It is OUT-16, and it is what makes the record live rather than a report. It
ranks below Story 1 because a record that can be read at all has to exist before it can be read
early, and because the user can always come back later.

**Independent Test**: Start a run, and while it is in progress observe the row's figures rising and
the opened record taking up lines as the agent works, with rows staying where they are. Ends when the
run ends; needs no failure.

**Acceptance Scenarios**:

1. **Given** a run is in progress, **When** the agent spends tokens and makes tool calls, **Then** the
   row's figures follow, and the rows of the list do not move as they change.
2. **Given** a run is in progress and the user has it open, **When** the agent makes a further tool
   call and writes further text, **Then** those lines appear in what the user is reading, below what
   was already there.
3. **Given** a run the user watched and left, **When** they come back after it ended, **Then** the
   same record is in the same place, in the same shape, now complete and carrying why the run ended.

---

### User Story 3 - Read a run in my own editor, without Grimoire (Priority: P3)

Months later, Grimoire is not running. The user opens the run's file in their own editor and reads
it: it is Markdown, it stands on its own, and it holds the same run the browser showed. It is not in
the wiki — Grimoire's bookkeeping never goes there — but in a directory of Grimoire's own, beside the
state it already keeps.

**Why this priority**: It is what keeps the browser a window rather than the only door
(`docs/ux.md`, Invariant 4), and it is the reason the record is a file at all. It ranks last because
it is a property of Stories 1 and 2 rather than work of its own: it is proven by opening the file
instead of the page.

**Independent Test**: Run a run to its end, stop Grimoire, and read the run's file as text. Confirm
it holds the frame and the narrative and that nothing of it was written into the wiki.

**Acceptance Scenarios**:

1. **Given** a run that has ended and a Grimoire that is not running, **When** the user opens that
   run's file as text, **Then** it holds the same frame and the same narrative the browser showed.
2. **Given** any run, **When** the user looks in the wiki, **Then** nothing of Grimoire's record of
   that run is there.

---

### Edge Cases

| Case | Expected behaviour | Requirement ID |
| --- | --- | --- |
| A tool call returns kilobytes — a run that reads twenty wiki pages | The record keeps every result whole; nothing is cut and nothing is dropped, however large. What keeps the run readable is the view: the user follows the run without reading the results in full, and reaches any one of them when they want it. | RUNS-009, ACCESS-006 |
| The record cannot be written — the directory is not writable, the disk is full | The run goes on. That something is missing is recorded with the run and appended to the record as soon as it can be written again, and the view says that lines are missing — so the user never mistakes an unwritten record for an agent that did nothing. | RUNS-007, ACCESS-006 |
| A run makes no tool call at all | The record holds its frame and an empty narrative, and the row reads zero calls. Nothing is missing and nothing is inferred. | RUNS-007, RUNS-008, RUNS-009, ACCESS-005 |
| A run ends at a ceiling | The frame names which ceiling ended it and where the run stood against both; the narrative holds everything appended up to the interrupt. | RUNS-008, RUNS-009, GUARD-004 |
| A run ends failed before its first model call, because the reported tools were not the grant | The record exists, its frame says that is why it ended, and its narrative is empty. | RUNS-007, RUNS-008, GUARD-001 |
| A run is nudged because the log entry was missing | The nudge is in the narrative, in its place among the calls and the agent's text, so the user can see the run was told and what it did next. | RUNS-009, RUNS-005 |
| Grimoire is stopped while a run is in progress | Everything appended up to that moment stays in the record; after the restart the run reads failed, its frame says the run was cut off by Grimoire stopping, and the figures it had reached are still the row's figures. | RUNS-004, RUNS-007, RUNS-008, RUNS-010 |
| Grimoire is killed rather than stopped in an orderly way | The same: the record is on disk as far as it was appended, and nothing depends on a shutdown step having run. | RUNS-004, RUNS-007, RUNS-010 |
| The user opens a submission that is still waiting its turn | It has no run, so no model, no figures and no record; the row reads submitted and there is nothing to open. | ACCESS-005, ACCESS-006 |
| The user opens a run that is in progress | They see the record as far as it goes and further lines arrive below what they are reading; it is the same view a finished run gets. | ACCESS-006 |
| A run touched more than one model — a background call the run never asked for | The tokens shown are over every model the run caused, the same quantity the cost ceiling counts; the frame names the tokens per model, so the user can see which model spent them. | RUNS-008, RUNS-010, GUARD-004 |
| The figures change while the user is reading the list | The rows stay where they are. A figure growing moves nothing. | ACCESS-005 |
| Two runs for one submission | Cannot occur: a submission causes exactly one run (INGEST-002), so exactly one record. | RUNS-007, INGEST-002 |

## Requirements *(mandatory)*

Binding are the requirement sentence and its acceptance scenario. Lists, screen descriptions and
examples are illustrative unless the requirement says "exactly".

### Functional Requirements

System behaviour only. Everything asked of the agent is one requirement on the instruction, proof
`review`. Whether the agent does it is proof `eval`. **This feature asks the agent for nothing new**:
the record is made from what the agent already reports, so no requirement on the instruction is added
and none is proven by `review` or `eval`.

| ID | Requirement | Proof |
| --- | --- | --- |
| RUNS-007 | Every run MUST have exactly one record of its own, a Markdown file in a directory Grimoire owns and never inside the wiki, created when the run begins and appended to as the run proceeds, so that it can be read while the run is in progress and after it has ended. Grimoire MUST NOT rewrite or remove a record; what has been appended stays, a run cut off by a stop included. Where a record cannot be written, the run MUST go on; that something is missing MUST be recorded with the run and MUST be appended to the record once it can be written again. | test |
| RUNS-008 | A run's record MUST hold the frame of that run: the pinned model id it ran on, the tools it was granted, both ceilings with the values the run reached against them, the tokens spent per model the run caused, when the run started, when it ended, and why it ended — one of: the agent stopped inside both ceilings with its log entry present; it stopped without that entry after being told once; the time ceiling; the cost ceiling; the reported tools were not the grant; the agent's process died; Grimoire was stopped while the run was in progress. | test |
| RUNS-009 | A run's record MUST hold what the run did, in the order it happened: every tool call with its arguments, what that call returned — whole, with nothing cut and nothing dropped however large it is — the agent's own text between the calls, and anything Grimoire said to the agent. | test |
| RUNS-010 | For every run, the tokens it has spent — the same quantity the cost ceiling counts — and the number of tool calls it has made MUST be kept current while the run is in progress, MUST stand as the run's final figures once it has ended, and MUST survive Grimoire stopping and starting again. | test |
| ACCESS-005 | The browser MUST show, for every submission, exactly one of submitted, running, done or failed, and for a submission that has a run also that run's model, the tokens it has spent and the number of tool calls it has made; while a run is in progress these figures MUST follow it, and a figure changing MUST NOT move the rows of the list. | test |
| ACCESS-006 | Users MUST be able to open a submission's run from the list and read its record in the browser — its frame, and what the run did in the order it happened — both while the run is in progress, where lines MUST arrive as they are appended, and after it has ended, where the record MUST be shown in the same shape. The user MUST be able to follow what the run did without reading the tool results in full and MUST be able to reach any one result when they want it; and where lines of the record could not be written, the view MUST say that something is missing. | test |

RUNS-010 exists apart from ACCESS-005 because keeping the figures and showing them are two
behaviours, and because a stop must not lose them: `002-ingest-queue` assumed the token counts of a
cut-off run need not survive, and that assumption is withdrawn here — the row of a failed run still
carries what that run spent.

### Retired in this feature

| ID | Was | Why retired |
| --- | --- | --- |
| ACCESS-002 | The browser MUST show, for every submission, exactly one of submitted, running, done or failed, and no further detail about the run. | Its second clause is what OUT-02 exists to undo: the whole point of this feature is further detail about the run. The requirement was written in `001-first-ingest` to hold the browser to one word per run while no outcome had asked for more, and OUT-02 asks for more. It moves under "Retired" in `docs/capabilities/access.md` and keeps its ID; no ID is renumbered or reused (Constitution IV.1, IV.2). ACCESS-005 carries the four states forward unchanged and adds the run's model and its two figures. |

Retiring ACCESS-002 also retires the sentence in `001-first-ingest`'s HTTP contract that says no
further detail about the run is exposed by that API, and the note in `docs/capabilities/access.md`
explaining that ACCESS-003 and ACCESS-004 do not reach past it. ACCESS-001, ACCESS-003 and ACCESS-004
are untouched: the form, the acknowledgement and the opening of the submitted text stand as written.

### Key Entities

- **Run record**: one Markdown file per run, in a directory Grimoire owns, holding that run's frame
  and what it did. It is appended to while the run proceeds and never rewritten. It is the single
  place the information lives; the browser is a window onto it (`docs/ux.md`). It is not in the wiki,
  carries no OKF metadata and is not a wiki page (Invariants 1 and 3).
- **The frame**: what is true of the run as a whole — model, grant, the two ceilings and where the run
  stood, tokens per model, start, end, and why it ended (RUNS-008). It is the part that is complete
  only once the run has ended; until then it holds what is known.
- **What the run did**: the ordered narrative — tool call, what it returned, the agent's own text, and
  anything Grimoire said to the agent (RUNS-009). It only ever grows.
- **The run's figures**: tokens spent and tool calls made (RUNS-010). Tokens are the same quantity the
  cost ceiling counts (GUARD-004, DEC-015) — never currency, and never a second definition of cost.
- **Submission**, **the four states**, **Acknowledgement**, **Run**: as `001-first-ingest` and
  `002-ingest-queue` have them. A run now also carries its record and its two figures, and what
  reaches the browser about it is no longer only its state (ACCESS-005, ACCESS-006).

## Who writes what *(mandatory whenever the feature touches anything in the wiki)*

user = the person using this wiki; owner = whoever ships Grimoire.

This feature writes nothing into the wiki. The record it introduces is Grimoire's own bookkeeping and
lives outside the wiki, which is what Invariants 1 and 3 and DEC-023's reasoning require: Grimoire's
files in the user's repository would show up in the version history that is their only undo. What a
run writes into the wiki is unchanged from `001-first-ingest`. RUNS-005's "Grimoire MUST read nothing
else in the wiki" is untouched — the record is not in the wiki, so writing it reads nothing there.

| Artifact | Written by (agent / Grimoire / user / owner) | What Grimoire adds, if anything |
| --- | --- | --- |
| Anything in the wiki | agent | nothing beyond what `001-first-ingest` already records: who generated a page and when (WIKI-002) |
| `log.md` in the wiki | agent | nothing; it is read for the run's identifier and nothing else (RUNS-005), and the record does not replace it |
| The run record, outside the wiki | Grimoire | the whole file: facts about the run and what the agent reported doing. No judgement about wiki content (Invariant 1) |

## Lifecycle questions *(mandatory)*

| Question | Answer (requirement ID, or "not applicable, because ...") |
| --- | --- |
| Stopping and starting again | RUNS-007 for the record — it is on disk as far as it was appended and is neither rewritten nor removed; RUNS-010 for the figures surviving; RUNS-004 as before for what the run then reads |
| A run or operation ending partway | RUNS-008 for why it ended, including a ceiling and a stop of Grimoire; RUNS-007 for the narrative keeping whatever it got as far as |
| Concurrent use | RUNS-002 as before — one run at a time, so one record is being appended at a time. The user reading a record while it is appended is ACCESS-006 |
| A missing input | INGEST-003, INGEST-004 as before for a submission with nothing to run. A submission that has no run yet shows no model and no figures and has nothing to open (ACCESS-005, ACCESS-006). A record that cannot be written does not stop the run: RUNS-007 for what is recorded instead, ACCESS-006 for the user seeing that something is missing |

## Success Criteria *(mandatory)*

### Measurable Outcomes

These restate the requirements as observable outcomes; proofs attach to the requirement IDs, not to
the criteria. Numbering continues from `002-ingest-queue`, which ended at SC-015.

- **SC-016**: For 100% of runs, the user can read afterwards which model it ran on, which tools it was
  granted, where it stood against both ceilings, when it started and ended, why it ended, and what it
  spent per model — without reading anything in the wiki. — **Restates:** RUNS-007, RUNS-008
- **SC-017**: For 100% of runs, every tool call the agent made is readable with its arguments and what
  it returned, in the order it happened, together with the agent's own text between the calls. Nothing
  a call returned is cut or dropped, and the run can still be followed without reading the results in
  full. — **Restates:** RUNS-007, RUNS-009, ACCESS-006
- **SC-018**: While a run is in progress the user can see its model, what it has spent and how many
  tool calls it has made from the list, and those figures follow the run; the rows of the list do not
  move when a figure changes. — **Restates:** ACCESS-005, RUNS-010
- **SC-019**: A run in progress and a run that ended are opened the same way and read the same way;
  the live one differs only in having fewer lines. — **Restates:** ACCESS-006
- **SC-020**: Every run's record can be read as text in the user's own editor with Grimoire not
  running, and holds the same run the browser showed. — **Restates:** RUNS-007, RUNS-008, RUNS-009
- **SC-021**: After Grimoire is stopped and started again — however it was stopped — 100% of what had
  been appended to a record is still there, and the figures a cut-off run had reached still show
  beside it. — **Restates:** RUNS-007, RUNS-010, RUNS-004
- **SC-022**: No figure about cost is shown in currency, and the tokens shown are the same quantity
  the cost ceiling counts, so the number exists in exactly one definition. — **Restates:** RUNS-010,
  ACCESS-005, GUARD-004
- **SC-023**: Nothing of Grimoire's record of a run is in the wiki. — **Restates:** RUNS-007
- **SC-024**: A record Grimoire could not write leaves a gap the user can see rather than a silent
  one: the run still finishes, and a missing record is never mistaken for an agent that did nothing. —
  **Restates:** RUNS-007, ACCESS-006

## Assumptions

- Where the record directory sits is the plan's decision; the spec requires only that Grimoire owns
  it and that it is not inside the wiki. The brief's `runs/` beside the state of DEC-023 is the
  obvious place and the plan is expected to take it. — **Requirement:** RUNS-007
- How a record is named and how a run is found from a submission is the plan's decision. The spec
  requires only that a submission's run can be opened from the list. — **Requirement:** ACCESS-006
- Whether the browser renders the record as Markdown or serves it as text in monospace is the plan's
  decision, under DEC-019 (no bundler, no npm) and `docs/ux.md` ("monospace wherever the content is a
  log or a file"). The record is meant to be readable as text either way. How the user reaches a
  result they do want to read in full is the plan's decision too; the spec requires only that they can
  follow the run without it and can get at it. — **Requirement:** ACCESS-006
- The record keeps every tool result whole, so a run that reads many wiki pages leaves a large file,
  and records accumulate without expiring. That is accepted rather than worked around: the record is
  the single place the information lives, and what keeps it readable is the view, not a smaller file.
  — **Requirement:** RUNS-009, ACCESS-006
- How often the record view picks up new lines is the plan's decision; the list's one-second poll is
  the existing precedent. The spec requires only that lines arrive while the user is watching. —
  **Requirement:** ACCESS-006
- The figures a row shows and the narrative a record holds are written from the same events, so they
  cannot disagree about a run. Whether they are stored in one place or two is the plan's decision. —
  **Requirement:** RUNS-009, RUNS-010
- Grimoire never removes a record, and no record expires. Records accumulate, and clearing them out
  is the user's business, as undo in the wiki is (`docs/product.md` §2). Retention is not an outcome
  and no requirement gives Grimoire a way to delete one. — **Requirement:** RUNS-007
- The record does not repeat the submitted text, the purpose description or the instruction. It is a
  record of what the run did; the opening of the submitted text is already in the list (ACCESS-004),
  and the instruction and the purpose description are versioned files of their own (V.1). —
  **Requirement:** none; this bounds RUNS-008 rather than adding behaviour.
- The record's readability to a human is a property of it being Markdown text in a file, not something
  a test can judge; Invariant 4 is what requires it. What the tests reach is that the file exists
  outside the wiki, is text, and holds the frame and the narrative. — **Requirement:** RUNS-007
- Grimoire runs inside a network the user trusts and serves one user (`docs/product.md` §2), so
  opening a run's record needs no identity and no permission: whoever can reach the page can read it.
  — **Requirement:** ACCESS-006
- `002-ingest-queue` assumed that the token counts of a run a stop cut off need not survive. That
  assumption is withdrawn: RUNS-010 makes them survive, because a failed run's cost is exactly what
  OUT-02 has the user look at. — **Requirement:** RUNS-010
- The tool calls and the agent's own text are new kinds of message to read from what the agent already
  reports; `system/init`, streamed usage and the `result` are already read (DEC-009). No new external
  system is reached. — **Requirement:** RUNS-009
- Whether OUT-02 and OUT-16 are reached is not decided by any check in Grimoire. This feature closes
  only after the owner has watched a run through the browser, read its record back afterwards, and
  opened the same file in their own editor; the plan's quickstart describes that. — **Requirement:**
  none; this is how the feature closes (Constitution I.9), not behaviour of the system.

## Budget note

Three user stories and one acceptance scenario — submit a text, watch the run, read it back, open the
file — which every story advances (Constitution I.7). Six requirements are registered and one,
ACCESS-002, is retired. They become permanent when they are registered in `docs/capabilities/`, which
happens before this feature's first test (Constitution IV.2).
