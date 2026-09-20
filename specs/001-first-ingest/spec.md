# Feature Specification: The First Ingest

**Feature Branch**: `001-first-ingest`

**Created**: 2026-09-19

**Status**: Draft

**Input**: User description: "Feature: the first ingest. Outcome advanced: OUT-01. This is the first feature; it establishes the skeleton. Capabilities touched: INGEST, WIKI, GUARD, ACCESS, RUNS. Requirement IDs start at 001 in each. Blocking open questions: none."

## Outcome advanced (OUT-NN) *(mandatory)*

**Outcome**: OUT-01 — submit a text in the browser and afterwards find new, linked pages including a source page in the wiki

**Capabilities touched**: INGEST, WIKI, GUARD, ACCESS, RUNS

## Blocking open questions (none, or stop) *(mandatory)*

**Blocking**: None. Both open questions in `docs/product.md` §8 block Later outcomes (OUT-08, OUT-15), not OUT-01.

## Out of scope *(mandatory)*

- The queue, the acknowledgement gate and restart behaviour — RUNS-002, RUNS-003, RUNS-004, ACCESS-003 → the follow-up feature, still advancing OUT-01 (see "Moved to the follow-up feature").
- Submitting anything other than a pasted text — a URL → OUT-05; a file → not an outcome yet, propose when needed.
- Asking questions of the wiki → OUT-03.
- Checking or repairing the wiki → OUT-07, OUT-08.
- Any detail of a run beyond its state — no steps, no reasoning, no duration, no cost shown, no history view → OUT-02.
- Committing, reverting, undo, or reading the wiki's version history → `docs/product.md` §4 (non-goal) and OUT-15.
- Access control, several users, chat programs → `docs/product.md` §2 (trusted network), OUT-11, OUT-12.
- Continuous operation, packaging, deployment → OUT-10.
- Limiting what an agent can reach on the network → OUT-04.
- Configurable ceilings, settings pages, editing the purpose description in the browser → `docs/product.md` §4 (non-goal) and OUT-14.
- Judging the quality of what the agent writes — which pages and sections exist and what they say is the agent's decision; the owner judges it by reading the wiki. No requirement here constrains content quality.
- Checking that the wiki came out the way the instruction demanded — no requirement here verifies the agent's output against the instruction. Finding and repairing such problems → OUT-07, OUT-08 (lint). The user's gate in the meantime is reading the wiki and reverting.
- Evals. No requirement in this feature is proven by `eval`.

## Clarifications

### Session 2026-09-20

- Q: Where should the wiki's structural rules live — as checks Grimoire runs against the wiki, or as provisions in the instruction the agent receives? → A: Instruction. They become one requirement on the instruction every run receives, WIKI-001. Grimoire checks none of them. Grimoire still writes the generation record (WIKI-002) and still leaves a failed run's writes in place (WIKI-003). Whether the wiki actually came out that way is judged by the owner reading it, and hunted systematically by lint (OUT-07).
- A requirement that a run ends done only if the wiki satisfies those rules is dropped as a consequence.
- Q: When does a run end done rather than failed? → A: When the agent stopped on its own, neither ceiling was reached, and the wiki's log holds an entry for that run. Recorded as RUNS-005. The log entry is the agent's own account of the run, so its absence is the one signal that the agent did not really finish; it is also the only thing Grimoire reads in the wiki to decide a run's state. The instruction was widened so it demands that the entry identify its run, which is what makes it findable; that clause now sits in WIKI-001.
- Q: Eight "The instruction MUST require ..." requirements proven by `test` — what can such a test actually check? → A: Nothing worth checking. A test could only match words in the instruction's text; loosely written it proves nothing, strictly written it breaks on every rewording, and the instruction gets reworded constantly because it is the main lever on the wiki's quality. The instruction is also the static content of a versioned file, which III.8 excludes from testing, and `trace-check` would go green on eight meaningless tests. The eight are clauses of one text, so they are now one requirement, WIKI-001, proven by `review`. Whether the agent follows the instruction is judgment and belongs to lint (OUT-07); the one testable part, that every run receives the instruction, is INGEST-002.
- Q: Which review item proves WIKI-001? → A: Review-checklist item 3. **Superseded in the session below**: item 3 as it stood asks only whether the standard's named parts are built, which covers WIKI-001's root-index clause and none of the rest, so it is widened by amendment instead of being read more broadly than it is written.
- Q: What happens when the agent writes a page with no provenance block, or one that cannot be read? → A: A missing block Grimoire adds; an unreadable one makes the write fail with a message to the agent. Recorded as part of WIKI-002. This is a tool precondition, not a judgment about the page's content, and Grimoire judges nothing else about the page.
- Q: A run whose agent forgot the log entry would fail and, through RUNS-003, block the queue — should that be softened? → A: Yes. Grimoire MUST tell the agent once that the entry is missing and let it continue within the ceilings; if it then stops with the entry there, the run ends done. A second stop without the entry, or a ceiling reached meanwhile, ends the run failed. The log is what RUNS-005 reads anyway, so this adds no structural checking.
- Q: May an ingest run delete or move pages? → A: No. The grant covers reading, creating and changing only (GUARD-002). Deleting and moving are not needed for a first ingest.
- The Story 2 narrative no longer says the source page holds the submitted text: WIKI-001 leaves it to the agent what the page carries, and a verbatim copy costs output tokens against the cost ceiling.
- The source page is the agent's: it decides the page's title, type, section and what text it carries. Grimoire does not place the submitted text into it. The earlier proposal that it should is therefore withdrawn, and Constitution V.1 and `docs/product.md` §4 need no amendment.
- Requirement IDs become permanent when the requirement is merged into `docs/capabilities/` at close; within a draft spec they may still change (Constitution IV.1).
- OWNER DECISION: nothing is registered in `docs/capabilities/` yet, so the WIKI requirements are renumbered 001 to 003 — WIKI-001 the instruction, WIKI-002 the generation record, WIKI-003 a failed run leaving its writes in place — and the "Dropped before close" table is removed. No ID is retired, because none was ever permanent.
- The instruction's provisions no longer end with "Grimoire MUST NOT verify that the agent complied". That Grimoire reads nothing in the wiki but the log entry is carried by RUNS-005, and staying out of the agent's output is stated under "Out of scope".
- What a run is given now includes the run's own identifier (INGEST-002), which is what lets the generation record name the run that wrote or updated a page (WIKI-002).

### Session 2026-09-20 — the split is applied

- OWNER DECISION: the split proposed in the Budget note is applied. This feature keeps submission, the run, the instruction, the generation record, the log entry with its nudge, the four states, the tool grant and both ceilings. RUNS-002, RUNS-003, RUNS-004 and ACCESS-003 move to a follow-up feature that still advances OUT-01, with their wording unchanged and their IDs kept; they are listed under "Moved to the follow-up feature". Nothing is renumbered and nothing is retired — a moved requirement is still wanted, only later.
- Q: With the queue gone, what happens to a submission made while a run is in progress? → A: It is refused. Recorded as INGEST-005: "A submission made while a run is in progress MUST be refused, MUST NOT cause a run, and the user MUST be told that a run is in progress." INGEST-001 is qualified with "when no run is in progress" so the two do not contradict each other, Story 1's third scenario becomes the refusal, and the edge case that had the submission waiting its turn now names INGEST-005. This closes the open item the plan raised: every behaviour this feature commits to carries a requirement ID again (Constitution IV.6).
- Q: Where do submissions and their states live now that RUNS-004 has moved? → A: In memory, for this feature only. The lifecycle answer for "Stopping and starting again" is therefore "not applicable in this feature, because submissions and their states are held in memory only; what a stop does to them is RUNS-004 in the follow-up feature. The owner accepts that a stop loses them until then." The lifecycle answer for "Concurrent use" is INGEST-005 — one run at a time is now enforced by refusing, not by queueing.
- Without the acknowledgement gate of RUNS-003, a run may start after a failed one and work on what the failed run left behind in the wiki. This is stated once under Assumptions and accepted until the follow-up feature; it adds no behaviour and carries no requirement ID of its own.
- RUNS-005 already carries the nudge — "when the agent stops inside both ceilings and the log holds no entry for the run, Grimoire MUST tell the agent once that the entry is missing and let it continue within the ceilings; if the agent then stops and the entry is there, the run MUST end done" — and no edge case or earlier clarification says the run ends failed regardless. Nothing to correct; the wording is left as it stands.
- GUARD-002 already reads "The grant for an ingest run MUST allow reading anything inside the wiki and creating and changing pages, indexes and the log, and nothing else. Deleting and moving MUST NOT be granted." Left unchanged.
- "Who writes what" already names the instruction's author as "owner — ships with Grimoire, versioned in its repository", and the lifecycle answer for "A run or operation ending partway" is already WIKI-003, RUNS-005. Both left unchanged.
- Story 3 keeps only the scenario that shows the four states; the three scenarios about the acknowledgement gate and about surviving a restart left with RUNS-003, ACCESS-003 and RUNS-004. SC-006 (RUNS-002) and SC-008 (RUNS-003) are dropped for restating moved requirements; SC-005 and SC-007 stand as written. The dropped numbers are not reused.
- The Submission entity no longer says it is "ordered against other submissions by when it was made": ordering existed for the queue. It still carries when it was made, which is what the browser lists.

### Session 2026-09-20 — remediation of the cross-artifact analysis

- Q: The plan drives the agent through the Claude Code CLI, where a turn is a loop of model call → tool call → model call that the agent runs by itself. Nothing lets Grimoire veto the *next* call without ending the one in flight, so GUARD-004's old split — "start no further model call" at either ceiling, "stop a call in flight" only at the elapsed-time one — was not implementable. Which half gives way? → A: The distinction goes. GUARD-004 now reads: "A run MUST have a fixed ceiling on elapsed time and a fixed ceiling on cost counted in model tokens. When either ceiling is reached, Grimoire MUST stop the run at once, a model call in flight included, and the run MUST end failed." Its edge case says the same. The cost of the choice is that a run stopped on cost loses the partial response it was producing; what it had already written through the tools stays, as WIKI-003 says.
- Q: The agent announces its tool surface when it starts, before any model call. If that surface is not the grant, is the run still allowed to begin? → A: No. GUARD-001 gains: "A run whose agent reports any tool outside the grant MUST end failed before its first model call." This is the earliest point at which GUARD-001 is observable, and it costs nothing to check. Added as an edge case.
- Q: With the queue gone, `submitted` became a state nothing held for any length of time, which left ACCESS-002 with nothing to show and no test able to drive a submission to it. Where is the boundary? → A: Written down once, under Key Entities: submitted = accepted, the agent has not yet reported in; running = the agent has reported in; done and failed as RUNS-005 and GUARD-004 say. No new behaviour — RUNS-001 already demanded exactly one state at a time, and this says where each one begins.
- Story 2's Independent Test said "with a scripted agent", which read as the scripted model endpoint the plan rejected. It now says "with the in-memory agent adapter" — the double at the agent port, which is what III.9 allows.
- The assumption about in-memory state now names everything a stop loses, not only the submissions and their states: the tools a run was granted (GUARD-003) and the tokens it spent (GUARD-004) go with them. The follow-up feature inherits the full list with RUNS-004.

### Session 2026-09-20 — clarify after the skeleton landed

- Q: RUNS-005 ends a run done only when `log.md` holds an entry for that run — how does Grimoire recognise which entry belongs to which run? → A: The run's identifier appears in `log.md`. Grimoire looks for that identifier as plain text and reads nothing else; WIKI-001 already demands that the entry identify its run, so no format is imposed on the agent's prose and the wiki stays a document a person reads. A run identifier is a fresh GUID, so it cannot collide with an earlier entry. Recorded in RUNS-005.
- Q: Item 3 of `docs/review-checklist.md` is "Standard scope" and does not ask what the instruction says, so WIKI-001 had no item that actually proves it (Constitution III.1). How is it settled? → A: Item 3 is widened to ask both — that only the standard's named parts are built, and that the instruction every run receives states the shape those requirements demand. Twelve items stand; no thirteenth, so nothing is retired. **The widening is an amendment and therefore its own PR, touching only the constitution, the template overrides and the checklist (Governance 4); it is not part of this feature's stack.** WIKI-001 is proven by item 3 as widened, and this feature cannot close (T041) until that PR has landed.
- Q: `docs/product.md` §2 lets the user read and edit the wiki at any time, a run in progress included, and the "Concurrent use" lifecycle answer covers only two submissions. What does Grimoire do when the user and the agent touch the same file? → A: Nothing. Grimoire neither locks the wiki nor detects a clash; a write replaces the file and the last write wins, and the wiki's own version history is the user's undo (`docs/product.md` §4). Stated under Assumptions, with no requirement ID and no edge case, because it adds no behaviour — conflict detection or a lock would be a mechanism this feature has no requirement asking for (II.1).
- Q: Does a refused submission become a listed submission with a state? → A: No. A refusal is the answer to the attempt, not a stored submission: nothing is kept, nothing is listed, no state is assigned. RUNS-001's four states stay four, and none of them has to mean "never ran". Carried by INGEST-003, INGEST-004 and INGEST-005, each of which now says so; the user has the refusal message in front of them, and a record of attempts that never ran is OUT-02's business.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Submit a text without waiting (Priority: P1)

The user has a text in front of them that belongs in the wiki. They open Grimoire in the browser, paste the text into the submission page, and submit it. The submission is accepted and they get on with their day; they do not wait for anything to happen to the wiki.

**Why this priority**: Nothing reaches the wiki without it, and it is the only step that costs the user attention. It is the smallest slice that can be shown to the user: a text goes in and a run is under way.

**Independent Test**: Paste a text, submit, and observe that the submission is accepted immediately and a run is under way while the browser is already free. Verifiable without any of the wiki structure of Story 2 being in place.

**Acceptance Scenarios**:

1. **Given** the purpose description is present and no run is in progress, **When** the user pastes a text and submits it, **Then** the submission is accepted, a run starts, and the user is not made to wait for the run to end.
2. **Given** the purpose description is missing, **When** the user submits a text, **Then** the submission is refused and the user is told that the purpose description is missing.
3. **Given** a run is in progress, **When** the user submits a further text, **Then** the submission is refused, no run starts, and the user is told that a run is in progress.

---

### User Story 2 - Find the result in the wiki (Priority: P2)

Later, the user opens the wiki and finds what the run made of the submission: a source page for the text they submitted, new or updated pages that name that source, links that lead somewhere, every page filed in a section, the section index and the root index current, and an entry in the wiki's log saying what changed and why.

**Why this priority**: This is the payoff of the outcome, but it is worthless until Story 1 can put something in. It is also the largest slice, so it follows rather than leads.

What the wiki ends up looking like is the agent's doing, not Grimoire's. Grimoire's part is to hand every run an instruction that demands this shape, to stamp who generated each page and when, and to keep its hands off what a failed run left behind. Whether the agent delivered is judged by the owner reading the wiki, and systematically by lint later (OUT-07).

**Independent Test**: Drive a run from a known text with the in-memory agent adapter and inspect what Grimoire does: it hands over the instruction, the purpose description, the submitted text and the run's identifier; it stamps the generation record over whatever the agent supplied; it adds a missing place for that record and fails a write whose place for it cannot be read; and it leaves a failed run's writes untouched. Needs no browser.

**Acceptance Scenarios**:

1. **Given** a submission, **When** the run is dispatched, **Then** it receives the instruction, the purpose description, the submitted text and the run's identifier.
2. **Given** a run that wrote a page, **When** the user reads that page, **Then** who generated it and when are Grimoire's values, whatever the agent supplied.
3. **Given** a run that fails partway, **When** the user opens the wiki, **Then** everything the run had already written is still there, unchanged and uncommitted.
4. **Given** a wiki the agent shaped differently from the instruction but a log entry it did leave, **When** the run ends, **Then** Grimoire neither corrects the wiki nor fails the run for it; the user sees the difference by reading the wiki.

---

### User Story 3 - See what became of each submission (Priority: P3)

The user returns to the browser and sees, for every text they submitted, whether it is submitted, running, done or failed — and nothing further.

**Why this priority**: Without it the user cannot tell a slow run from a broken one, but the wiki itself already shows the result, so it ranks last of the three.

**Independent Test**: Drive submissions to each of the four states and confirm the browser reports exactly that state and nothing else.

**Acceptance Scenarios**:

1. **Given** submissions in different states, **When** the user looks at the browser, **Then** each one shows exactly one of submitted, running, done, failed, and no further detail about the run.

---

### Edge Cases

| Case | Expected behaviour | Requirement ID |
| --- | --- | --- |
| The purpose description is missing when a text is submitted | The submission is refused, the user is told the purpose description is missing, no run starts, and nothing is stored or listed. | INGEST-003 |
| The submitted text is empty or only whitespace | The submission is refused, causes no run, and nothing is stored or listed. | INGEST-004 |
| A submission arrives while a run is in progress | The submission is refused, no run starts, the user is told that a run is in progress, and nothing is stored or listed. | INGEST-005 |
| A run reaches its elapsed-time ceiling or its cost ceiling | The run is stopped at once — a model call in flight included — and ends failed. What it had already written stays. | GUARD-004 |
| A run fails or is stopped at a ceiling | Everything it had already written stays in the wiki; Grimoire removes, reverts and commits nothing. | WIKI-003 |
| The agent stops on its own inside both ceilings but left no log entry for the run | Grimoire tells the agent once that the entry is missing and lets it continue within the ceilings; if it then stops with the entry there, the run ends done. A second stop without the entry, or a ceiling reached meanwhile, ends the run failed and what it wrote stays. | RUNS-005 |
| The agent writes a page without a type, links nothing, or leaves an index stale | The run is unaffected: nothing in the wiki but the log entry is read to decide how it ends. | RUNS-005 |
| The agent supplies its own value for who generated a page or when | Grimoire's values replace it. | WIKI-002 |
| A run updates a page an earlier run wrote | The record on that page names the run that updated it. | WIKI-002 |
| A page arrives without a place for the generation record | Grimoire adds the place and the write succeeds. | WIKI-002 |
| A page arrives with a place for the generation record that cannot be read | The write fails and the agent is told why; nothing else about the page is judged. | WIKI-002 |
| The wiki is empty when the first run starts | The instruction states the shape the wiki is to have, the first run's section index, root index and log entry included. | WIKI-001 |
| The agent attempts to read or write outside the wiki | The attempt does not succeed, because no granted tool reaches there. | GUARD-001 |
| The agent reports a tool outside the grant when it starts | The run ends failed before its first model call; nothing is dispatched to a model. | GUARD-001 |

## Requirements *(mandatory)*

### Functional Requirements

| ID | Requirement | Proof |
| --- | --- | --- |
| INGEST-001 | When no run is in progress, users MUST be able to submit a text, and the submission MUST be accepted without the user waiting for the run to end. | test |
| INGEST-002 | An accepted submission MUST cause a run that is given the submitted text, the purpose description, the instruction and the run's identifier. | test |
| INGEST-003 | When the purpose description is missing, a submission MUST be refused, no run MUST start, and the user MUST be told that the purpose description is missing. A refused submission MUST NOT be stored and MUST carry no state. | test |
| INGEST-004 | A submission whose text is empty or only whitespace MUST be refused and MUST NOT cause a run. A refused submission MUST NOT be stored and MUST carry no state. | test |
| INGEST-005 | A submission made while a run is in progress MUST be refused, MUST NOT cause a run, and the user MUST be told that a run is in progress. A refused submission MUST NOT be stored and MUST carry no state. | test |
| WIKI-001 | The instruction MUST state the shape the wiki is to have: a source page for the submitted text; a type on every page; named sources, referenced where a statement relies on them; links between pages; every page in exactly one section, sections one level deep; a current index per section; a current root index declaring the version of the format standard pinned in `docs/product.md` and listing the sections; a log entry per run that identifies the run and says what changed and why. | review |
| WIKI-002 | Every page a run writes MUST record who generated it and when. Grimoire MUST write both, and MUST replace any value the agent supplies for either. When a run updates a page, the record MUST name that run. When a page arrives without a place for the record, Grimoire MUST add it; when that place cannot be read, the write MUST fail and the agent MUST be told why. Grimoire MUST judge nothing else about the page. | test |
| WIKI-003 | A run that ends failed MUST leave everything it had already written in the wiki in place; Grimoire MUST NOT remove, revert or commit any of it. | test |
| GUARD-001 | A run MUST receive only the tools granted for that run, and MUST NOT be able to use any other. A run whose agent reports any tool outside the grant MUST end failed before its first model call. | test |
| GUARD-002 | The grant for an ingest run MUST allow reading anything inside the wiki and creating and changing pages, indexes and the log, and nothing else. Deleting and moving MUST NOT be granted. | test |
| GUARD-003 | The tools granted MUST be recorded for every run. | test |
| GUARD-004 | A run MUST have a fixed ceiling on elapsed time and a fixed ceiling on cost counted in model tokens. When either ceiling is reached, Grimoire MUST stop the run at once, a model call in flight included, and the run MUST end failed. | test |
| ACCESS-001 | Users MUST be able to enter a text and submit it from a page in the browser. | test |
| ACCESS-002 | The browser MUST show, for every submission, exactly one of submitted, running, done or failed, and no further detail about the run. | test |
| RUNS-001 | Every submission MUST carry exactly one state at a time, drawn from submitted, running, done, failed. | test |
| RUNS-005 | A run MUST end done when the agent stopped on its own, neither ceiling was reached, and the wiki's log holds an entry for that run; an entry belongs to a run when `log.md` contains that run's identifier. When the agent stops inside both ceilings and the log holds no entry for the run, Grimoire MUST tell the agent once that the entry is missing and let it continue within the ceilings; if the agent then stops and the entry is there, the run MUST end done. In every other case the run MUST end failed. Grimoire MUST read nothing else in the wiki to decide this. | test |

### Why review *(one line per `review` requirement)*

| ID | Why neither a test nor an eval can prove it |
| --- | --- |
| WIKI-001 | The requirement is about what a text says. A test could only match its wording, which is static content the constitution does not test (III.8). Whether the agent follows it is agent judgment, out of scope here and the business of OUT-07. Proven by item 3 of `docs/review-checklist.md`, as widened by the amendment that clarification calls for. |

### Moved to the follow-up feature

Still wanted, still advancing OUT-01, not built here. Wording unchanged, IDs kept, nothing renumbered and nothing retired. They become permanent when that feature registers them in `docs/capabilities/`.

| ID | Requirement | Proof |
| --- | --- | --- |
| RUNS-002 | At most one run MUST be in progress at any time, and waiting submissions MUST start in the order they were made. | test |
| RUNS-003 | After a run ends failed, no further run MUST start until the user has acknowledged that failure; waiting submissions MUST stay waiting, and the acknowledged run MUST stay failed. | test |
| RUNS-004 | Submissions and their states MUST survive Grimoire stopping and starting again; a run that was in progress when Grimoire stopped MUST read failed afterwards. | test |
| ACCESS-003 | Users MUST be able to acknowledge a failed run in the browser. | test |

### Key Entities

- **Submission**: a text the user handed to Grimoire and *accepted*, together with its state and when it was made. A refused text never becomes one: nothing about it is stored and it carries no state.
- **The four states**, and where each begins and ends (RUNS-001 names them; ACCESS-002 shows them): **submitted** — the submission has been accepted and the agent has not yet reported in; **running** — the agent has reported in; **done** and **failed** — as RUNS-005 and GUARD-004 say. Done and failed are final.
- **Run**: one attempt to work one submission into the wiki. Carries its own identifier, the tools it was granted, its two ceilings, and how it ended.
- **Purpose description**: the hand-written description of what the wiki is for. Belongs to Grimoire, not to the wiki; every run receives it; Grimoire neither creates nor changes it.
- **Instruction**: the versioned text every run receives alongside the purpose description. It is where the wiki's shape is demanded of the agent. It belongs to Grimoire and is not a wiki page.
- **Page**: an entry in the wiki, written by the agent. Grimoire stamps who generated it and when; everything else on it is the agent's.
- **Source page**: the page the agent creates for a submitted text and that other pages name as their source.
- **Section**: one grouping of pages, exactly one level deep, carrying an index of its pages.
- **Root index**: the wiki's entry point, declaring the format standard's version and listing the sections.
- **Log**: the wiki's record of what each run changed and why. Grimoire reads one fact from it and no other: whether it contains a given run's identifier (RUNS-005).
- **Tool grant**: the list of tools a given run may use, recorded with the run.
- Indexes and the log are not pages: they carry no type, no sources and no generation record.

## Who writes what *(mandatory whenever the feature touches anything in the wiki)*

user = the person using this wiki; owner = whoever ships Grimoire.

| Artifact | Written by (agent / Grimoire / user / owner) | What Grimoire adds, if anything |
| --- | --- | --- |
| Source page for the submitted text | agent | who generated it and when (WIKI-002) |
| Any other page a run writes or updates | agent | who generated it and when (WIKI-002) |
| Section index | agent | nothing |
| Root index | agent | nothing |
| The run's log entry | agent | nothing |
| Purpose description — Grimoire's, not a wiki artifact | user | nothing |
| Instruction — Grimoire's, not a wiki artifact | owner — ships with Grimoire, versioned in its repository | nothing |

## Lifecycle questions *(mandatory)*

| Question | Answer (requirement ID, or "not applicable, because ...") |
| --- | --- |
| Stopping and starting again | not applicable in this feature, because submissions and their states are held in memory only; what a stop does to them is RUNS-004 in the follow-up feature. The owner accepts that a stop loses them until then. |
| A run or operation ending partway | WIKI-003, RUNS-005 |
| Concurrent use | INGEST-005 |
| A missing input | INGEST-003, INGEST-004 |

## Success Criteria *(mandatory)*

### Measurable Outcomes

These restate the requirements as observable outcomes; proofs attach to the requirement IDs, not to the criteria.

- **SC-001**: After submitting a text the user can leave the page immediately; no submission requires the user to wait for the run to end. — **Restates:** INGEST-001
- **SC-002**: Every run receives the instruction, the purpose description, the submitted text and its own identifier. — **Restates:** INGEST-002
- **SC-003**: For 100% of pages a run writes, who generated them and when are Grimoire's values, never the agent's. — **Restates:** WIKI-002
- **SC-004**: Grimoire changes nothing in the wiki beyond those two facts, and removes nothing a run wrote — including after a failure. — **Restates:** WIKI-002, WIKI-003
- **SC-005**: No run exceeds either of its two fixed ceilings; every run ends done or failed, never neither, and ends done only when the agent stopped on its own inside both ceilings and left a log entry for the run. — **Restates:** GUARD-004, RUNS-001, RUNS-005
- **SC-007**: For every submission the user made, the browser reports its current state; nothing beyond that state is shown. — **Restates:** ACCESS-002

SC-006 and SC-008 restated RUNS-002 and RUNS-003 and left with them. The numbers are not reused.

## Assumptions

- Grimoire runs inside a network the user trusts, so no sign-in stands between the user and the submission page (`docs/product.md` §2). — **Requirement:** ACCESS-001
- The wiki is a repository the user version-controls. Grimoire reads and writes the working state and never touches the history. — **Requirement:** WIKI-003
- Exactly these parts of the format standard pinned in `docs/product.md` apply in this feature: every page carries its type; every page names its sources; every page records who generated it and when; the root index declares the standard's version and lists the sections; every section has an index listing its pages; the log records changes. Nothing else of the standard is in scope (Constitution I.8). They apply as provisions of the instruction, stated once in WIKI-001, not as checks Grimoire runs — the generation record of WIKI-002 is the only thing Grimoire writes, and the log entry of RUNS-005 the only thing it reads. — **Requirement:** WIKI-001, WIKI-002, RUNS-005
- Who generated a page and when are facts about the run, so Grimoire writes them; everything else a page carries comes from the agent, the submitted text of the source page included (`docs/product.md` §4, Constitution V.1). — **Requirement:** WIKI-002
- The instruction is a versioned text belonging to Grimoire, distinct from the user-written purpose description. Both go to every run; the instruction is where the wiki's shape is demanded (Constitution V.1). — **Requirement:** INGEST-002, WIKI-001
- The ceilings are fixed values, not settings, and cost is counted in model tokens rather than currency. — **Requirement:** GUARD-004
- Submissions and their states are held in memory in this feature, and so is everything recorded with a run — the tools it was granted (GUARD-003) and the tokens it spent (GUARD-004). A stop loses all of it; the owner accepts that until RUNS-004 arrives with the follow-up feature. — **Requirement:** none; this is what makes the "Stopping and starting again" lifecycle answer "not applicable" rather than a behaviour.
- Without the acknowledgement gate of RUNS-003, a run may start after a failed one and work on what the failed run left behind in the wiki. The owner accepts this until the follow-up feature; it adds no behaviour here. — **Requirement:** none; WIKI-003 already says the failed run's writes stay.
- The user may read and edit the wiki while a run is writing to it (`docs/product.md` §2). Grimoire neither locks the wiki nor detects a clash: a write replaces the file, the last write wins, and the wiki's own history is the user's undo (`docs/product.md` §4). — **Requirement:** none; this adds no behaviour, and the "Concurrent use" lifecycle answer stays INGEST-005, which is about two submissions.
- `trace-check` and `time-budget` are built in this feature because the constitution establishes them (II.2, IV.3). They are not product requirements: they carry no requirement ID and appear in no user story. The plan owns them.
- Whether OUT-01 is reached is not decided by any check in Grimoire. The feature closes only after the owner has run one ingest of a known text with a real model and read the resulting wiki; the plan's quickstart describes that run. — **Requirement:** none; this is how the feature closes (Constitution I.9), not behaviour of the system.

## Budget note

Three stories fit the constitution's budget (I.7). Sixteen requirements, fifteen proven by `test` and
one by `review`.

**The split is applied.** This feature keeps submission, the run, the instruction, the generation
record, the log entry with its nudge, the four states, the tool grant and both ceilings.

**The follow-up feature, still advancing OUT-01**, takes the queue, the acknowledgement gate and
restart behaviour: RUNS-002 (one run at a time, waiting submissions start in order), RUNS-003 (no
further run until a failure is acknowledged), RUNS-004 (submissions and states survive a restart)
and ACCESS-003 (acknowledging a failed run in the browser). With those held back, a submission made
during a run is refused — INGEST-005 — and submissions live only in memory.

If the plan is over budget, do not cut requirements; split again.
