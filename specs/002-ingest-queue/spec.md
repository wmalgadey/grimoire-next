# Feature Specification: The Ingest Queue

**Feature Branch**: `002-ingest-queue`

**Created**: 2026-09-23

**Status**: Draft

**Input**: User description: "Feature: the ingest queue. Outcome advanced: OUT-01 (second spec; OUT-01 stays Now until this feature closes). Capabilities touched: RUNS, ACCESS, INGEST. Blocking open questions: none. This feature takes over the four requirements that 001-first-ingest moved out in its 'Moved to the follow-up feature' table — RUNS-002, RUNS-003, RUNS-004, ACCESS-003 — with their IDs and their wording unchanged."

## Outcome advanced (OUT-NN) *(mandatory)*

**Outcome**: OUT-01 — submit a text in the browser and afterwards find new, linked pages including a source page in the wiki

**Capabilities touched**: RUNS, ACCESS, INGEST

This is the second spec against OUT-01. `001-first-ingest` built submission, the run and the wiki's
shape and moved four requirements out; this feature takes those four, and two more that clarification
showed they needed — RUNS-006 and ACCESS-004. OUT-01 stays Now until this feature closes.

## Blocking open questions (none, or stop) *(mandatory)*

**Blocking**: None. Both open questions in `docs/product.md` §8 block Later outcomes (OUT-08,
OUT-15), not OUT-01.

## Out of scope *(mandatory)*

- Any detail of a run beyond its state — no history, no live view of a run under way, no notification
  when one ends → OUT-02, OUT-16, OUT-17. What ACCESS-004 puts in front of the user is the user's own
  submitted text and the moment they submitted it: facts about the submission, not about the run.
- Cancelling or removing a waiting submission → not an outcome yet; propose it to the owner when it
  is needed.
- Two runs at once, and any per-run priority or reordering → the queue is one run at a time and
  strictly in the order submissions were made.
- Everything the queue does not touch: ingest itself, the wiki's shape, the generation record, the
  tool grant and the two ceilings — all of them as `001-first-ingest` left them.
- Resuming or retrying a run that a stop cut off → nothing is resumed and nothing is retried; what it
  wrote stays (WIKI-003).
- Evals. No requirement in this feature is proven by `eval`.

## Clarifications

### Session 2026-09-23 — decisions carried in with the feature description

- OWNER DECISION: one run at a time, strictly in the order submissions were made. No priorities, no
  reordering.
- OWNER DECISION: after a failed run nothing starts until the user acknowledges it; the acknowledged
  run stays failed. Acknowledging is the only new user interaction this feature adds (Constitution
  I.6).
- OWNER DECISION: a submission that waits carries no timeout. It waits until it runs or until the
  user acts. There is no "stale after".
- OWNER DECISION: a run that was in progress when Grimoire stopped reads failed after the restart.
  Nothing is resumed and nothing is retried. Whatever it wrote stays in the wiki (WIKI-003).
- OWNER DECISION: submissions waiting at the time of a stop stay waiting and start after the restart,
  in order, once no failure blocks the queue.
- Q: INGEST-005 refuses a submission made while a run is in progress, and RUNS-002 has it wait. Which
  gives way? → A: INGEST-005. It was written in `001-first-ingest` only because the queue had been
  held back, and RUNS-002 supersedes it. It is retired here and keeps its ID. INGEST-001 loses the
  qualifier "when no run is in progress" it was given at the same time, and is recorded as changed.
- Q: What does a waiting submission read in the browser? → A: submitted. ACCESS-002 keeps its four
  states and its "no further detail about the run", so no position in the queue is shown and nothing
  is said about how long a submission has waited. ACCESS-002 is neither changed nor retired; where
  the state **submitted** begins and ends was already written down in `001-first-ingest` under Key
  Entities and already covers a submission waiting its turn.
- Q: `001-first-ingest` assumed a stop loses the submissions, their states, the tools a run was
  granted and the tokens it spent. What does RUNS-004 now make survive? → A: The submissions with
  their states, and the grant recorded with a run (GUARD-003). The token counts of a run that a stop
  cut off need not survive: that run reads failed and nothing judges it again.
- Q: `001-first-ingest` accepted, as an assumption with no requirement ID, that a run may start after
  a failed one and work on what the failed run left behind. → A: RUNS-003 ends that. The assumption
  is retired with this feature; it named no requirement and none is retired with it.

### Session 2026-09-23 — clarify

- Q: When RUNS-004 says submissions and their states must survive Grimoire stopping and starting
  again, does that cover any stop at all — a crash, a forced kill, a power cut — or only a clean
  shutdown Grimoire gets to run through? → A: Any stop at all. RUNS-004 exists because a stop must
  not lose the queue, and the stop most likely to lose it is the one that is not clean: a run went
  wrong and the user killed the process. Nothing may depend on a shutdown step having run, and
  "a run that was in progress reads failed" is settled at start-up from what is found rather than by
  anything written on the way out. RUNS-004's wording is unchanged; this says which stops it means.
- Q: RUNS-004 makes a run that was in progress *read* failed after a restart, but says nothing about
  the agent that was working on it. What happens to that agent when Grimoire stops? → A: Where
  Grimoire is given the chance to act, it stops the run with it, so no agent keeps working on a run
  after Grimoire has gone; where the stop leaves it no chance — a kill, a power cut — the agent may
  outlive it, and what it writes stays. OWNER DECISION: this is worth one requirement beyond the four
  the feature was given, and it is registered as **RUNS-006**. The alternative left the wiki changing
  under a run the browser had already reported failed, with nothing the user could stop. The
  mechanism is the one GUARD-004 already uses at a ceiling — an interrupt with a kill behind it
  (DEC-015, DEC-016) — so this is wiring, not a new mechanism (Constitution II.1). Demanding that no
  agent ever outlive Grimoire was rejected: a power cut cannot honour it, and the one primitive that
  would come close has no equivalent on the machine Grimoire is developed on.
- Q: When the user acknowledges a failed run, does the acknowledgement name which failure it is
  clearing, or is it one action that clears whatever blocks the queue? → A: It names one particular
  failure, rather than clearing whatever blocks. A stale page is then harmless: an acknowledgement
  naming something that is not an unacknowledged failure does nothing, which is what the edge case
  for that already says. **Which identifier it names was settled after the plan — see the session
  below. It is the submission's.**
- Q: A run identifier tells the user nothing about what failed. What does the browser show so they
  know which text a failed run was working on? → A: The opening of the submitted text, cut to the
  same length for every submission, together with when the submission was made. Registered as
  **ACCESS-004**. The whole text was rejected: it turns a list whose job is one word per row into a
  wall of prose. A link into the wiki was rejected too: a failed run may never have created a source
  page, and Grimoire does not serve the wiki. This does not touch ACCESS-002 — that requirement
  governs what is shown *about the run*, and the user's own text and the moment they submitted it are
  facts about the submission. It also gives an ID to something `001-first-ingest` already had the
  browser list without one: when a submission was made (Constitution IV.6).

### Session 2026-09-23 — after the plan

- Q: The acknowledgement names one failure — by the run's identifier or by the submission's? → A:
  **By the submission's.** OWNER DECISION, withdrawing the earlier answer in the session above. A
  submission has exactly one run in this feature, so the submission's identifier already identifies
  the failed run, and the browser already carries it as the row's key. Nothing about the run reaches
  the browser: ACCESS-002 keeps its "no further detail about the run" undiminished, and the sentence
  in `001-first-ingest`'s HTTP contract that says no further detail about the run is exposed by that
  API **stands as written** — this feature narrows nothing. The endpoint therefore addresses a
  submission, and the browser needs one thing it did not carry before: whether this submission's
  failure is still waiting to be acknowledged. That is not detail about the run either — it says
  whether an action is available.
- Q: The opening of the submitted text is new in this feature and the browser did not show it
  before. Does it carry a requirement ID? → A: Yes — ACCESS-004, proof `test`, registered below with
  the others. Checked rather than assumed (Constitution IV.6). The 120 characters themselves are the
  plan's, not the spec's.
- Q: After a stop that gave Grimoire no chance to act, the agent outlives it, goes on writing into
  the wiki with no ceiling enforced, and the restarted Grimoire reads its run as failed and starts
  the next one — two agents in one wiki, one of them unbounded and invisible. Is that acceptable? →
  A: **No.** OWNER DECISION: RUNS-006 is extended rather than a new requirement added, because it is
  the same subject — no agent goes on working on a run Grimoire has ended. The process identifier of
  a run's agent is recorded with the run; at start-up, for every run read as having been in
  progress, Grimoire terminates that process where it is still alive, before that run reads failed
  and before any further run starts. The earlier Assumption that such an agent simply outlives
  Grimoire is withdrawn: it outlives it only until the next start-up. Proof is a Contract test
  against a real process, because terminating one is an act on the operating system and no in-memory
  adapter can make it true.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Hand over a second text without waiting for the first (Priority: P1)

The user has submitted a text and a run is under way. A second text is in front of them and they
submit it too. Grimoire accepts it, and it waits its turn: when the run in progress ends, the waiting
text's run starts. Texts submitted after it start after it, one at a time, in the order the user made
them.

**Why this priority**: It is what the feature is for, and it is the only part the user cannot work
around. Without it the user has to watch for a run to end before handing over the next text — which
is exactly what `001-first-ingest` made them do by refusing. The other two stories only matter once
there is a queue to block and to keep.

**Independent Test**: Submit several texts while a run is in progress and observe that each is
accepted, that no two runs are ever in progress together, and that the runs start in the order the
submissions were made. Needs neither a failure nor a restart.

**Acceptance Scenarios**:

1. **Given** a run is in progress, **When** the user submits a further text, **Then** the submission
   is accepted, the user does not wait for the run to end, and no second run starts.
2. **Given** two texts were submitted while a run was in progress, **When** that run ends, **Then**
   the first of the two starts, and the second starts only after the first has ended.
3. **Given** a submission is waiting, **When** the user looks at the browser, **Then** it reads
   submitted, with the opening of its text and when it was made beside it, and with no position in
   the queue and no detail about the run.

---

### User Story 2 - Clear a failure before the queue moves on (Priority: P2)

A run ends failed and the queue stops there: whatever the user submitted behind it stays waiting, and
nothing new starts. In the browser the user can see which text the failed run was working on — the
opening of it, and when they submitted it — and acknowledges that run. The next waiting submission
starts, and the run they acknowledged still reads failed.

**Why this priority**: Without it, the run behind a failure works on a wiki the failed run left
half-written, and the user learns about the failure only after the damage has spread. It ranks below
Story 1 because there is nothing to hold back until submissions can wait.

**Independent Test**: Drive a run to failed with submissions waiting behind it, confirm nothing
starts and that each row shows enough of its text to be told apart, acknowledge that submission's
failed run by naming the submission, and confirm the next submission starts and the acknowledged run
still reads failed. Needs no restart.

**Acceptance Scenarios**:

1. **Given** a run has ended failed and submissions are waiting, **When** the user looks at the
   browser, **Then** no further run has started and the waiting submissions still read submitted.
2. **Given** a failed run the user has not acknowledged, **When** the user acknowledges it in the
   browser, **Then** the next waiting submission starts and the acknowledged run still reads failed.
3. **Given** several submissions, one of whose runs has failed, **When** the user looks at the
   browser, **Then** every row carries the opening of its text and when it was made, so the user can
   tell which text the failed run was working on.

---

### User Story 3 - Stop Grimoire and find the queue as it was (Priority: P3)

The user stops Grimoire — to restart the machine, to update it, or because a run went wrong — and
starts it again. Every submission they made is still listed with its state. A run that was in progress
at the moment of the stop now reads failed, and it blocks the queue until they acknowledge it. The
agent that was working on it is gone — stopped when Grimoire was, or terminated as Grimoire came
back up — so nothing goes on changing the wiki behind their back. What that run had already written
is still in the wiki.

**Why this priority**: It turns the queue from something that lives only as long as the process into
something the user can rely on, but the queue has to exist and be governed first. It is also the
story with no new user interaction: the user sees the same page they saw before the stop.

**Independent Test**: Put submissions into each state, stop Grimoire, start it again, and confirm
every submission is still listed, that the one that was running reads failed, that its agent is
gone — stopped with Grimoire where it could act, terminated at start-up where it could not — and
that the waiting ones start in the order they were made once the failure is acknowledged.

**Acceptance Scenarios**:

1. **Given** submissions in several states and a run in progress, **When** Grimoire is stopped and
   started again, **Then** every submission is still listed with its state and the run that was in
   progress reads failed.
2. **Given** submissions that were waiting when Grimoire stopped, **When** the user acknowledges the
   run that the stop failed, **Then** those submissions start in the order they were made.
3. **Given** a run is in progress, **When** Grimoire is stopped and is given the chance to act,
   **Then** the agent working on that run is stopped with it and works on nothing further.
4. **Given** Grimoire was stopped in a way that gave it no chance to act and that run's agent is
   still alive, **When** Grimoire is started again, **Then** that agent is terminated before its run
   reads failed and before any further run starts.

---

### Edge Cases

| Case | Expected behaviour | Requirement ID |
| --- | --- | --- |
| A text is submitted while a run is in progress | It is accepted and waits its turn; no second run starts, and the user does not wait for the run in progress to end. | INGEST-001, RUNS-002 |
| Several texts are submitted while a run is in progress | They start one at a time, in the order they were made; none is reordered and none is dropped. | RUNS-002 |
| A submission waits for a long time | It keeps waiting. A waiting submission carries no timeout and never goes stale; it leaves the queue by running or by the user acting, and by nothing else. | RUNS-002 |
| A run ends failed while submissions are waiting | No further run starts; the waiting submissions stay waiting and still read submitted. | RUNS-003 |
| The user acknowledges the failed run | The next waiting submission starts, and the acknowledged run stays failed. | RUNS-003, ACCESS-003 |
| A text is submitted while an unacknowledged failure blocks the queue | It is accepted and waits; no run starts for it until the failure is acknowledged. | INGEST-001, RUNS-002, RUNS-003 |
| The user acknowledges a failure with nothing waiting behind it | Nothing starts and the run stays failed; the next text the user submits starts as usual. | RUNS-003 |
| The user acknowledges a submission whose run is not an unacknowledged failure — from a page loaded before the last run failed, say | Nothing starts and no state changes: the acknowledgement names one submission, and only an unacknowledged failure blocks the queue. | RUNS-003, ACCESS-003 |
| Grimoire is stopped while a run is in progress | The run is stopped with it, so no agent goes on working on it. After the restart that run reads failed; nothing is resumed and nothing is retried, and everything the run had already written stays in the wiki. | RUNS-004, RUNS-006, WIKI-003 |
| Grimoire is stopped while submissions are waiting | After the restart they are still listed, still waiting, and start in the order they were made once no failure blocks the queue. | RUNS-002, RUNS-004 |
| Grimoire is stopped while a run is in progress with submissions waiting behind it | After the restart the interrupted run reads failed and blocks the queue until the user acknowledges it; the waiting submissions stay waiting. | RUNS-003, RUNS-004 |
| Grimoire is stopped and started again after a failure was acknowledged | The failure stays acknowledged and blocks nothing; the run still reads failed. | RUNS-003, RUNS-004 |
| Grimoire is killed rather than stopped in an orderly way | The submissions, their states and any acknowledgement are still found after the restart and a run that was in progress reads failed. The agent outlives the kill, but only until the next start-up. | RUNS-004, RUNS-006, WIKI-003 |
| That agent is still alive when Grimoire is started again | It is terminated before its run reads failed and before any further run starts, so no two agents are ever in the wiki at once. What it wrote before then stays. | RUNS-006, WIKI-003 |
| The recorded process identifier now belongs to an unrelated process — the machine was restarted, or the number was reused | Nothing is terminated. A recorded identifier that is no longer that run's agent is left alone, and the run reads failed as it would have anyway. | RUNS-006 |
| The browser lists a waiting submission | It shows submitted, the opening of its text and when it was made — and nothing further: no position in the queue, no time waited, no detail of the run ahead of it. | ACCESS-002, ACCESS-004 |
| The submitted text is shorter than the length the opening is cut to | The whole text is shown; nothing is padded and nothing is cut. | ACCESS-004 |
| Two submissions begin with the same words | They are still told apart: they carry different times, and an acknowledgement names a submission rather than a text. | ACCESS-003, ACCESS-004 |

## Requirements *(mandatory)*

### Functional Requirements

System behaviour only. Everything asked of the agent is one requirement on the instruction, proof
`review`. Whether the agent does it is proof `eval`. This feature asks the agent for nothing new: it
adds no requirement on the instruction and none proven by `review` or `eval`.

The first four requirements below are the ones `001-first-ingest` listed under "Moved to the
follow-up feature". Their wording and their IDs are unchanged. Two are new. RUNS-006 exists because
RUNS-004 settles what a run *reads* after a restart and says nothing about the agent that was working
on it. ACCESS-004 exists because ACCESS-003 has the user acknowledge a *particular* failed run, and a
list of states alone gives them nothing to recognise it by. All six become permanent when they are
registered in `docs/capabilities/`, which happens before this feature's first test (Constitution
IV.2).

| ID | Requirement | Proof |
| --- | --- | --- |
| RUNS-002 | At most one run MUST be in progress at any time, and waiting submissions MUST start in the order they were made. | test |
| RUNS-003 | After a run ends failed, no further run MUST start until the user has acknowledged that failure; waiting submissions MUST stay waiting, and the acknowledged run MUST stay failed. | test |
| RUNS-004 | Submissions and their states MUST survive Grimoire stopping and starting again; a run that was in progress when Grimoire stopped MUST read failed afterwards. | test |
| ACCESS-003 | Users MUST be able to acknowledge a failed run in the browser. | test |
| RUNS-006 | No agent MUST go on working on a run once Grimoire has ended that run. When Grimoire stops and is given the chance to act, a run that is in progress MUST be stopped with it. The process identifier of a run's agent MUST be recorded with the run; at start-up, for every run Grimoire reads as having been in progress, it MUST terminate that process where it is still alive — before that run reads failed and before any further run starts — and MUST NOT terminate a process that is no longer that run's agent. | test |
| ACCESS-004 | The browser MUST show, for every submission, the opening of the text that was submitted, cut to the same length for every submission, and when the submission was made, so that the user can tell one submission from another. | test |

### Changed in this feature

| ID | Was | Now | Why changed |
| --- | --- | --- | --- |
| INGEST-001 | When no run is in progress, users MUST be able to submit a text, and the submission MUST be accepted without the user waiting for the run to end. | Users MUST be able to submit a text, and the submission MUST be accepted without the user waiting for the run to end. | The qualifier was added in `001-first-ingest` only so that INGEST-001 and INGEST-005 would not contradict each other while the queue was held back. RUNS-002 makes a submission during a run wait instead of being refused, INGEST-005 is retired, and the qualifier has nothing left to protect. The ID, the proof kind and the rest of the wording stand. |

### Retired in this feature

| ID | Was | Why retired |
| --- | --- | --- |
| INGEST-005 | A submission made while a run is in progress MUST be refused, MUST NOT cause a run, and the user MUST be told that a run is in progress. A refused submission MUST NOT be stored and MUST carry no state. | Superseded by RUNS-002: a submission made while a run is in progress is now accepted and waits its turn. The requirement existed only because the queue was held back out of `001-first-ingest`. It moves under "Retired" in `docs/capabilities/ingest.md` and keeps its ID; no ID is renumbered or reused (Constitution IV.1, IV.2). |

Retiring INGEST-005 leaves INGEST-003 and INGEST-004 as the only refusals, and both still say that a
refused submission is not stored and carries no state.

ACCESS-002 is neither changed nor retired, and nothing about a run reaches the browser. The
acknowledgement of ACCESS-003 names the *submission*, whose identifier the browser already carries
as its row's key; what ACCESS-004 shows — the opening of the user's own text and when they submitted
it — are facts about the submission; and whether a submission's failure is still waiting to be
acknowledged says that an action is available, not what the run did. "Exactly one of submitted,
running, done or failed, and no further detail about the run" stands undiminished, and so does the
sentence in `001-first-ingest`'s HTTP contract that says no further detail about the run is exposed
by that API.

### Key Entities

Carried over from `001-first-ingest` and changed only where this feature changes them.

- **Submission**: a text the user handed to Grimoire and *accepted*, together with its state and when
  it was made. It is **ordered against other submissions by when it was made**, and that order is the
  order their runs start (RUNS-002). The browser shows the opening of its text and when it was made,
  so the user can tell one from another (ACCESS-004). A refused text never becomes one: nothing about
  it is stored and it carries no state.
- **The four states** (RUNS-001 names them; ACCESS-002 shows them): **submitted** — the submission has
  been accepted and the agent has not yet reported in, which is also what a submission waiting its
  turn reads; **running** — the agent has reported in; **done** and **failed** — as RUNS-005 and
  GUARD-004 say. Done and failed are final. A restart does not change which state a submission is in,
  except that a run that was in progress reads failed (RUNS-004).
- **Acknowledgement**: the user's confirmation that they have seen a failed run. It **names the
  submission** whose run failed, which identifies that run because a submission has exactly one. It
  is the only thing that lifts the block a failure puts on the queue, it changes nothing about the
  run — which stays failed — and it is not a state of its own: RUNS-001's four states stay four.
- **Run**: one attempt to work one submission into the wiki, carrying its own identifier, the tools
  it was granted, its two ceilings, and how it ended — all as `001-first-ingest` had it. It now also
  carries **which process its agent is**, so that an agent which outlived a stop can be found and
  terminated at the next start-up (RUNS-006). None of this reaches the browser.

## Who writes what *(mandatory whenever the feature touches anything in the wiki)*

user = the person using this wiki; owner = whoever ships Grimoire.

This feature writes nothing into the wiki and reads nothing in it. The queue decides when a run
starts; what a run then writes is unchanged from `001-first-ingest`, whose table stands as written.
A run that a stop cut off is no exception: Grimoire removes, reverts and commits none of what it had
already written (WIKI-003).

| Artifact | Written by (agent / Grimoire / user / owner) | What Grimoire adds, if anything |
| --- | --- | --- |
| Anything in the wiki | agent | nothing beyond what `001-first-ingest` already records: who generated a page and when (WIKI-002) |

## Lifecycle questions *(mandatory)*

| Question | Answer (requirement ID, or "not applicable, because ...") |
| --- | --- |
| Stopping and starting again | RUNS-004 for what survives and what a run then reads; RUNS-006 for what happens to the agent as Grimoire goes |
| A run or operation ending partway | WIKI-003, RUNS-005 as before; a run that a stop cut off is RUNS-006 for the agent and RUNS-004 for what the run then reads, and a run that ended failed holds the queue under RUNS-003 |
| Concurrent use | RUNS-002 |
| A missing input | INGEST-003, INGEST-004 as before |

## Success Criteria *(mandatory)*

### Measurable Outcomes

These restate the requirements as observable outcomes; proofs attach to the requirement IDs, not to
the criteria. Numbering continues from `001-first-ingest`, which ended at SC-008 and said its dropped
numbers are not reused.

- **SC-009**: A text can be submitted at any time and is never refused because a run is in progress;
  the user waits for no run to end before handing over the next text. — **Restates:** INGEST-001,
  RUNS-002
- **SC-010**: At no moment is more than one run in progress, and every run starts in the order its
  submission was made. — **Restates:** RUNS-002
- **SC-011**: After a run ends failed, no further run starts until the user acknowledges that
  failure; once acknowledged, the next waiting submission starts and the acknowledged run still reads
  failed. — **Restates:** RUNS-003, ACCESS-003
- **SC-012**: After Grimoire is stopped and started again — however it was stopped, an abrupt kill
  included — 100% of the submissions the user made are still listed with the state they carried, and
  a run that was in progress reads failed. — **Restates:** RUNS-004
- **SC-013**: A waiting submission reads submitted and, beyond the opening of its text and when it
  was made, shows nothing further — no position in the queue, no time waited. — **Restates:**
  ACCESS-002, RUNS-001, ACCESS-004
- **SC-014**: No agent goes on working on a run Grimoire has ended — stopped with Grimoire where it
  could act, terminated at the next start-up where it could not. At no point are two agents at work
  in the wiki. — **Restates:** RUNS-006
- **SC-015**: For every submission the browser carries the opening of its text and when it was made,
  so a user looking at a failed run can tell which text it was working on before acknowledging that
  submission. Nothing about the run itself is carried. — **Restates:** ACCESS-002, ACCESS-003,
  ACCESS-004

## Assumptions

- Where submissions, their states and the acknowledgement of a failure are kept so that they survive a
  stop is the plan's decision. The spec requires only that they survive, and that they survive *any*
  stop: a crash, a forced kill or a power cut as much as an orderly shutdown. Nothing may depend on a
  shutdown step having run. — **Requirement:** RUNS-004
- The tools recorded with a run survive a stop along with the submission they belong to. The token
  counts of a run that a stop cut off need not: that run reads failed, both ceilings bind a run only
  while it is in progress, and nothing judges it again. — **Requirement:** RUNS-004, GUARD-003
- A submission that waits carries no timeout. It leaves the queue by running or by the user acting,
  and by nothing else; no submission is dropped, expired or marked stale. — **Requirement:** RUNS-002
- Nothing is resumed and nothing is retried after a restart. A run that was in progress reads failed
  and everything it had already written stays in the wiki. — **Requirement:** RUNS-004, WIKI-003
- A stop that leaves Grimoire no chance to act — a kill it cannot catch, a power cut — leaves the
  agent running, and for as long as it runs no ceiling is enforced on it, because the Grimoire that
  was counting is gone. That window is bounded by the next start-up and by nothing else: Grimoire
  terminates the agent there, before the run reads failed and before any further run starts, so two
  agents are never in the wiki at once. Grimoire does not adopt such an agent, does not resume its
  run and does not take back what it wrote. — **Requirement:** RUNS-006, RUNS-004, WIKI-003
- Recognising a run's agent by a recorded process identifier alone would be unsafe, because the
  operating system reuses those numbers. What makes it safe is the plan's business; the spec
  requires only that a process which is no longer that run's agent is left alone. —
  **Requirement:** RUNS-006
- At most one unacknowledged failure exists at any time, because nothing starts while a failure blocks
  the queue. — **Requirement:** none; this follows from RUNS-003 and adds no behaviour.
- `001-first-ingest` accepted that a run may start after a failed one and work on what the failed run
  left behind. RUNS-003 ends that; the assumption goes with this feature and retires no requirement
  ID, because it never carried one. — **Requirement:** RUNS-003
- Grimoire runs inside a network the user trusts and serves one user (`docs/product.md` §2), so
  acknowledging a failure needs no identity and no permission: whoever can reach the page can
  acknowledge. — **Requirement:** ACCESS-003
- How long the opening of a submitted text is cut to is the plan's decision. The spec requires only
  that it is the same length for every submission, so the list reads as a list, and that a text
  shorter than that length is shown whole. — **Requirement:** ACCESS-004
- The user may read and edit the wiki while a run is writing to it, and Grimoire neither locks it nor
  detects a clash (`docs/product.md` §2, §4) — unchanged from `001-first-ingest`. The queue is about
  two submissions, not about the user and the agent touching the same file. — **Requirement:** none;
  this adds no behaviour.
- Whether OUT-01 is reached is not decided by any check in Grimoire. This feature closes only after
  the owner has stopped and started Grimoire with a queue in it and worked a failure through the
  browser; the plan's quickstart describes that. — **Requirement:** none; this is how the feature
  closes (Constitution I.9), not behaviour of the system.

## Budget note

Three user stories, within the constitution's budget (I.7). Six requirements are registered — four
taken over unchanged from `001-first-ingest` and two new, RUNS-006 and ACCESS-004 — one is changed in
wording only, one is retired, and no requirement is proven by `review` or by `eval`. This feature
adds exactly one new user interaction — acknowledging a failed run — and no new operation and no new
external system (I.6): RUNS-006 acts on a process Grimoire already spawns, with the kill GUARD-004
already established, and ACCESS-004 adds to a list the browser already draws.

RUNS-006 is the one requirement here that cannot be proven in the Fast suite alone: terminating a
process that outlived a stop is an act on the operating system, so it carries a Contract test
against a real process beside its Fast tests (Constitution III.4).

This is a small feature. If the plan comes out anywhere near 40 tasks, something is being built that
this spec does not ask for; cut that, do not split.
