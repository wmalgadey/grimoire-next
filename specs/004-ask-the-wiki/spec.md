# Feature Specification: Ask the Wiki

**Feature Branch**: `004-ask-the-wiki`

**Created**: 2026-09-27

**Status**: Draft

**Input**: User description: "Build the spec from `docs/briefs/ask-the-wiki.md`. Sections 1–5 are the input. Section 6 is plan input — do not use it, do not mention its content in the spec. Section 3 items are the owner's wishes, not requirements; `docs/ux.md` says the implementing agent decides layout."

## Outcome advanced (OUT-NN) *(mandatory)*

**Outcome**: OUT-03 — ask a question and get an answer with references to wiki pages

**Capabilities touched**: QUERY, ACCESS, GUARD, RUNS, WIKI

QUERY has no registered requirement yet; this is its first feature. The capability itself is
already named in `docs/product.md` §6, so no new capability is created here (Constitution IV.1).

This feature closes the core loop's fourth step (`docs/product.md` §3). It adds exactly one new
operation — answering a question from the wiki — and the chat is the door that operation is reached
through, not a second addition (Constitution I.6). It adds no new external system: a question's run
is the same spawned agent every run already is, it reaches the same wiki through the same tools, and
where an answer's reference leads outside the browser, Grimoire writes a link and the operating
system decides what opens it.

## Blocking open questions (none, or stop) *(mandatory)*

**Blocking**: None. All four open questions in `docs/product.md` §9 block Later outcomes — OUT-08,
OUT-15, OUT-18, OUT-19/OUT-20. None of them touches OUT-03: a question changes nothing in the wiki,
so what a lint change costs, who commits, what a passage is and who records derivation are all
beside it.

## Out of scope *(mandatory)*

- **A record of its own for a question** → never. A record exists because `docs/product.md` §1 aims
  at runs the user lets go unattended and reviews *afterwards*. A chat is a direct interaction: the
  user is there while it happens and reads it as it forms, so there is no afterwards to review.
  Writing a file nobody opens is a mechanism with no consumer (Constitution II.1).
- **A chat surviving Grimoire stopping and starting again** → later, no outcome ID yet. After a
  restart the chat is empty, and with no record behind it the questions and their answers are gone.
- **A history of what questions have cost** → later, no outcome ID yet. It follows from the two
  above: the figure stands while the chat lives, and nowhere after.
- **Earlier conversations, a list of chats, searching them** → later, no outcome ID yet. Only the
  current chat is reachable; a new chat replaces it.
- **A page written from an answer, whether Grimoire proposes it or writes it unasked** → OUT-06,
  which has its own promotion trigger (`docs/product.md` §8: the second time an answer is carried
  over by hand). This feature is what makes carrying one over by hand possible at all, so it is what
  starts that trigger counting. Deciding it here would decide two things this feature deliberately
  leaves alone: that a question's grant is read-only, and whether a run proposes or acts
  (`docs/product.md` §4, OUT-08).
- **Letting the agent look anything up on the internet to answer** → OUT-09.
- **A question changing the wiki in any way, the log included** → never in this feature. A question
  reads; that the wiki got better is what ingest is for (Invariant 1).
- **A question overtaking waiting submissions, or running beside a run** → never here. One run at a
  time stays true (RUNS-002), and an unacknowledged failure blocks a question as it blocks an ingest
  (RUNS-003).
- **The agent's reasoning or thinking** → never, because there is nothing to show. The CLI reports an
  empty `thinking` beside a several-hundred-character signature, measured twice
  (`docs/capabilities/runs.md`, RUNS-009). What the chat shows instead is the agent's own prose at
  the turn boundaries and its tool calls, which is all that exists. Should a later CLI deliver
  reasoning, that is an owner's decision then, not a promise now.
- **Cost in currency** → never (`docs/product.md` §4, DEC-015).

## Clarifications

### Session 2026-09-27 — decisions carried in with the brief

Four things this feature contradicts, all the owner's decision (brief §5):

- OWNER DECISION: **RUNS-005 is contradicted.** It ends a run `done` only when `log.md` holds an
  entry for that run. A question writes nothing in the wiki, so it can never satisfy that. The
  done-condition, and the one nudge that precedes it, become properties of a run that is to change
  the wiki rather than of every run. RUNS-005 keeps its ID and is reworded here.
- OWNER DECISION: **RUNS-007 is contradicted**, and **RUNS-008 and RUNS-009** with it. RUNS-007 says
  every run must have exactly one record of its own; RUNS-008 and RUNS-009 say what that record
  holds. A question has no record. All three become properties of a run that is handed over —
  today, a run a submission causes — rather than of every run. All three keep their IDs and are
  reworded here. ACCESS-006, which is about reading a *submission's* record, is untouched.
- OWNER DECISION: **GUARD-002 does not cover a question.** It is the grant for an ingest run and
  says what that one allows. A question runs under a grant of its own, read-only: no page written,
  no index written, no log appended. GUARD-002 is untouched; GUARD-005 is registered beside it.
- OWNER DECISION: **DEC-032 is contradicted.** It makes reading a run a page that polls. Polling is
  replaced by the browser being sent what happens — on the chat and on the two views that poll
  today. The wording of ACCESS-005 and ACCESS-006 does not change: both already ask for figures
  that follow a run and for lines that arrive as they are appended. What changes is how, which is
  the plan's decision and the plan's new entry in `docs/decisions.md`; both requirements are proven
  again under it.

### Session 2026-09-27 — specify

- Q: A question is a run, but the submissions list is headed "What became of each submission" and a
  question is not a submission. Where does the user see a question's state and what it cost? → A:
  **In the chat, and nowhere else.** The walkthrough (brief §2) puts the state of a question — it
  waits its turn, it is being answered, it got no answer and why — and its cost beside the question
  itself, which is where the user is looking while it happens. Putting questions into the
  submissions list was rejected: the list is a list of submissions and a question is not one, and a
  question has no record to open from a row, so the row's one action (ACCESS-006) would not exist.
  ACCESS-007 carries what the chat shows; ACCESS-004, ACCESS-005 and ACCESS-006 are untouched.
- Q: A question's run can end failed, and RUNS-003 then blocks every further run until the user
  acknowledges it. How does the user ask again? → A: **The chat says the question got no answer and
  why, and the acknowledgement is the one that already exists** (ACCESS-003). Nothing new is added:
  a failed question blocks the queue exactly as a failed ingest does, the user acknowledges it in
  the browser, and then asks again. A second acknowledgement path, or letting a question fail
  without blocking, were both rejected — they would make a question a run that plays by different
  rules, and RUNS-002 and RUNS-003 are untouched by this feature.
- OWNER DECISION: **what the chat shows about a question is one requirement, not one per state.**
  Waiting, being answered, answered, and got no answer and why are values inside ACCESS-007
  (Constitution IV.7), as the four submission states are values inside ACCESS-005.
- Q: What happens to a question's run when the person asking it walks away mid-answer — the tab is
  closed, the connection drops — and what does the chat show when they come back? → A: **The run
  runs to its end like any other run, and the chat is held so the answer is there when they return.**
  The chat lives while Grimoire runs, whether or not a browser is connected, so a browser that
  reconnects reads the chat as it now stands, including what arrived while it was away. Nothing is
  written to disk for this: held is held in memory, and QUERY-005 is unchanged — a restart still
  empties the chat. Stopping the run when its reader is gone was rejected: Grimoire has no way to
  stop a run today, only a ceiling ends one, and building one here would be a mechanism this feature
  does not otherwise need (Constitution II.1). Admitting the answer lost was rejected too — it is
  produced whether anyone watches or not, and throwing it away would be work done and discarded.
  QUERY-005 and ACCESS-007 gain the reconnect; no new ID.
- Q: What does the chat do when Grimoire has not been told what it needs in order to build the link
  that opens a wiki page in the user's editor? → A: **The answer arrives as normal, the page's name
  is shown as plain text rather than a link, and the chat says once that opening pages is not set
  up.** An answer is still an answer without the click, and the page's name is still in the prose
  where the agent wrote it. Refusing the question the way QUERY-003 refuses a missing purpose
  description was rejected: the purpose description is what the agent's judgment rests on and this is
  a convenience. Saying nothing was rejected too — a reader could not then tell a setting that is
  absent from a page that is simply not linkable. ACCESS-009 gains the case; QUERY-003's three
  refusals are unchanged.
- Q: With the chat page open in two browser tabs at once, do both show the same chat or does each
  tab have one of its own? → A: **One chat, shown the same in every tab.** A question asked in one
  tab appears in the other, and starting a new chat empties both. A chat per tab was rejected: it
  would make Grimoire tell one reader from another, which nothing else in this feature needs
  (Constitution II.1), and with one run at a time two conversations would queue behind each other
  anyway. Grimoire serves one person on a network they trust (`docs/product.md` §2). QUERY-005's
  "exactly one" is what this makes explicit; no new ID.
- Q: Which of the agent's prose is the answer the user reads, and which belongs under the fold with
  the steps? → A: **Left to the plan, which decides it from what the CLI actually emits.** The spec
  holds the shape and not the cut: an answer is what the user came for and is read as one piece of
  prose, and what the agent did to reach it — its text between the steps and the pages it opened —
  is under the fold (ACCESS-007). Where the line between the two falls in the CLI's own stream is a
  property of that stream, which `AgentTranscript` is the only reader of, and the plan decides it
  with the CLI in front of it. No requirement changes and no ID is added.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ask the wiki and read the answer as it forms (Priority: P1)

The user switches to the chat and types a question — not a source this time. They send it, and the
answer starts arriving as the agent writes it, growing under the question while they read the
beginning of it. Beside it stands what this question has spent against the ceiling it is held to,
and the chat carries the total of what has been spent in it so far. The answer names the wiki pages
it rests on, inside its own prose rather than as a list at the bottom.

**Why this priority**: It is OUT-03 itself and the whole of the feature's value — the fourth step of
the core loop. Everything else is this answer trusted, followed up, or put away.

**Independent Test**: Ask one question against a wiki that has pages, and confirm an answer arrives
in the chat while the agent is still writing it, that it names pages of that wiki, and that what it
cost stands beside it against its ceiling. Needs no follow-up and no failure.

**Acceptance Scenarios**:

1. **Given** a wiki with pages and no run in progress, **When** the user sends a question, **Then**
   the question is accepted without them waiting for an answer, and a run starts that is given the
   question, the purpose description and the question instruction.
2. **Given** a question's run is under way, **When** the agent writes its answer, **Then** the answer
   appears in the chat as it is produced and grows in place, and nothing the user is already reading
   moves.
3. **Given** an answer has arrived, **When** the user reads it, **Then** what that question spent
   against its cost ceiling stands beside it, and the chat shows what it has spent altogether,
   without a ceiling beside the total.
4. **Given** an answer that rests on wiki pages, **When** the user reads it, **Then** the pages it
   rests on are named inside the prose, each one as a link.

---

### User Story 2 - See what the answer rests on, and open a page it cites (Priority: P2)

Under the answer, folded shut, is what the agent did to get there — the tools it called and what
they returned, which wiki pages it opened among them. Its own text is the answer above the fold, the
cut plan.md made from what the CLI emits (Clarifications). The user unfolds one step when they want to see what came back. It
reads the way a run's record reads, so there is no second format to learn. Seeing which pages were
read is what makes the answer believable; and clicking a page's name in the answer opens that page
in the editor the user has the wiki open in.

**Why this priority**: It is the difference between an answer and a claim. It ranks below Story 1
because an answer has to exist before it can be checked, and the user can read the answer without
ever unfolding a step.

**Independent Test**: Ask one question, unfold a step under the answer and confirm it shows the call
and what came back, in the shape a run's record uses; then click a page the answer names and confirm
the request handed out addresses that page in the user's wiki.

**Acceptance Scenarios**:

1. **Given** an answer in the chat, **When** the user looks under it, **Then** what the agent did is
   there, shut, and unfolding one step shows that step's call and what it returned, in the shape a
   run's record is read in.
2. **Given** a question's run is under way, **When** the agent makes a further call and writes
   further text, **Then** those steps appear below what is already there, and what the user is
   reading does not move.
3. **Given** an answer that names a wiki page, **When** the user clicks that page's name, **Then**
   the page is opened in the editor the wiki is open in, and nothing in the wiki changes.
4. **Given** any question, **When** its run has ended, **Then** nothing in the wiki was written,
   changed or appended by it — the log included.

---

### User Story 3 - Ask back, then put the conversation away (Priority: P3)

The answer is not quite what the user meant, so they ask back in the same chat. The agent still knows
what they were talking about and answers again, live, with its own references. When they are done
with the subject they start a new chat, which puts the old one away for good, and nothing of it is
kept. If a question arrives while an ingest is already running, it waits its turn and the chat says
so; and when a question's run fails, the chat says that this question got no answer and why, so the
user can acknowledge the failure and ask again.

**Why this priority**: It is what makes the chat a conversation rather than a one-shot answer, and
it is where the queue and a failure become visible. It ranks last because a single question already
delivers OUT-03, and because each of these is a second turn of the same machinery.

**Independent Test**: Ask a question, then a follow-up that only makes sense in the light of the
first, and confirm the second answer takes the first into account; then start a new chat and confirm
nothing of the old one is reachable.

**Acceptance Scenarios**:

1. **Given** a chat with a question and its answer, **When** the user asks a follow-up, **Then** the
   run that answers it is given what has been asked and answered in that chat so far, and the answer
   arrives in the same chat, live, with its own references.
2. **Given** a chat with questions and answers in it, **When** the user starts a new chat, **Then**
   the new chat is empty and nothing of the old one is reachable or kept anywhere.
3. **Given** a run is in progress or a submission is waiting, **When** the user sends a question,
   **Then** the chat shows that the question is waiting its turn, and it is answered in the order it
   was made.
4. **Given** a question whose run ended failed, **When** the user looks at the chat, **Then** it says
   against that question that it got no answer and why, and after acknowledging the failure the user
   can ask again.

---

### Edge Cases

| Case | Expected behaviour | Requirement ID |
| --- | --- | --- |
| The question is empty or only whitespace | It is refused, no run starts, nothing is stored, and the user is told why. | QUERY-003 |
| The question instruction or the purpose description is missing | The question is refused, no run starts, nothing is stored, and the user is told which of the two is missing. | QUERY-003 |
| A question arrives while a run is in progress | It waits its turn behind whatever is running and whatever is waiting, and the chat says it is waiting. | RUNS-002, ACCESS-007 |
| A question arrives while an unacknowledged failure blocks the queue | It waits, as an ingest would; the chat shows it waiting until the failure is acknowledged. | RUNS-003, ACCESS-007 |
| A question's run ends at a ceiling, or its process dies | The chat says against that question that it got no answer and why; nothing half-written is presented as an answer. The failure blocks the next run until it is acknowledged. | QUERY-006, RUNS-003, GUARD-004 |
| A question's run reports a tool outside its grant | It ends failed before its first model call, and the chat says that question got no answer and why. | GUARD-001, GUARD-005, QUERY-006 |
| The agent tries to write a page, an index or the log while answering | It cannot: the grant does not carry it, and the wiki is unchanged when the run ends. | GUARD-005 |
| A question's run stops on its own inside both ceilings, having written nothing in the wiki | It ends done. The log entry the done-condition asks for is asked only of a run that is to change the wiki, and no nudge is sent. | RUNS-005 |
| Grimoire is stopped and started again while a chat has questions and answers in it | The chat is empty afterwards and nothing of it is kept; the run that was in progress reads failed, and its agent's process is terminated at start-up. | QUERY-005, RUNS-004, RUNS-006 |
| An answer references a page that does not exist in the wiki | It is shown as written and clicking it does what the editor does with a missing file. Grimoire checks no link — OKF requires readers to tolerate a broken one (`docs/capabilities/wiki.md`). | QUERY-004 |
| An answer rests on no page at all — the wiki holds nothing about the question | The answer is shown as the agent wrote it, with no references in it. Grimoire judges no answer. | QUERY-004, Invariant 1 |
| The user starts a new chat while a question is still being answered | The run is not a chat and goes on being a run; what it produces belongs to the chat that is gone, so nothing of it appears in the new one. Its figures stay with the run. | QUERY-005, RUNS-010 |
| A question's run touched more than one model | What is shown beside the answer is the same quantity the cost ceiling counts, over every model the run caused. | RUNS-010, GUARD-004, ACCESS-007 |
| A question's run has no record to open | Correct: only a run a submission causes has one. The chat is where that question is read, and there is no afterwards to review. | RUNS-007 |
| The chat's total and an answer's own figure are read together | The total carries no ceiling beside it, because no ceiling holds it — each question carries its own. | ACCESS-008 |
| A question in the chat got no answer, and the user reads the total | The total includes what that question spent. A run that failed still spent, and its figures stand (RUNS-010). | ACCESS-008, RUNS-010 |
| A figure rises while the user is reading | Nothing on the screen moves. | ACCESS-007 |
| The user closes the tab or loses the connection while an answer is being written | The run runs to its end like any other run; nothing stops it and nothing is discarded. The chat holds what the run produced. | QUERY-005, RUNS-002 |
| The chat page is open in two browser tabs at once | Both show the same chat: a question asked in one appears in the other, and starting a new chat empties both. Nothing tells one reader from another. | QUERY-005, ACCESS-007 |
| Grimoire was not told what opening a page in the editor needs | The answer arrives as normal and the page's name is readable in it as plain text; the browser says that opening a page is not set up. Asking is not refused — that is for a missing question instruction or purpose description. | ACCESS-009, QUERY-003 |
| The browser comes back after a connection was lost | It shows the chat as it then stands, including the answer and the steps that arrived while it was away. | ACCESS-007, QUERY-005 |

## Requirements *(mandatory)*

Binding are the requirement sentence and its acceptance scenario. Lists, screen descriptions and
examples are illustrative unless the requirement says "exactly".

### Functional Requirements

System behaviour only. Everything asked of the agent is one requirement on the instruction, proof
`review`. Whether the agent does it is proof `eval`. This feature adds one requirement on an
instruction, QUERY-004, because a question's run is dispatched with an instruction of its own that
does not exist yet.

| ID | Requirement | Proof |
| --- | --- | --- |
| QUERY-001 | Users MUST be able to ask the wiki a question, and the question MUST be accepted without the user waiting for its answer. | test |
| QUERY-002 | An accepted question MUST cause a run that is given the question, the purpose description, the question instruction, the run's identifier, and what has been asked and answered in the same chat before it, so that a follow-up is answered in the light of what came before; the run MUST run on the model Grimoire was started with. | test |
| QUERY-003 | A question MUST be refused when its text is empty or only whitespace, when the question instruction is missing, or when the purpose description is missing; no run MUST start, the refused question MUST NOT be stored and MUST carry no state, and the user MUST be told which of the three it was. | test |
| QUERY-004 | The question instruction MUST state that the answer is written for the user to read, rests on what the wiki's pages say, names every page it rests on inside its prose as a link to that page in the link form the wiki uses (WIKI-001), and that nothing in the wiki is to be written, changed or appended. | review |
| QUERY-005 | A chat MUST hold the questions asked in it, their answers and what the agent did, for as long as Grimoire runs and whether or not a browser is connected to it, and MUST NOT survive Grimoire stopping and starting again. There MUST be exactly one chat, the same one for every browser reading it. Users MUST be able to start a new, empty chat, after which nothing of the previous chat is reachable and nothing of it is kept. | test |
| QUERY-006 | When a question's run ends failed, the chat MUST say against that question that it got no answer and why, MUST NOT present anything the run had produced as its answer, and the user MUST be able to ask the question again once the failure has been acknowledged. | test |
| GUARD-005 | The grant for a question's run MUST allow reading anything inside the wiki and nothing else: no page, index or log written, nothing deleted and nothing moved. | test |
| ACCESS-007 | The browser MUST show, for every question in the chat, exactly one of waiting its turn, being answered, answered, or got no answer and why; an answer MUST appear as the agent produces it, and what the agent did to reach it MUST be readable under it in the shape a run's record is read in (ACCESS-006) — shut by default and openable a step at a time — arriving as it happens; and content arriving MUST NOT move what the user is already reading. Where the browser's connection to Grimoire is lost and made again, the browser MUST show the chat as it then stands, including what arrived while it was away. | test |
| ACCESS-008 | The browser MUST show, for every question that has a run, what that run has spent against the cost ceiling it is held to, and for the chat what its questions have spent altogether, the total without a ceiling beside it; while a question's run is in progress its figure MUST follow it, and a figure changing MUST NOT move what the user is reading. | test |
| ACCESS-009 | Users MUST be able to open a wiki page an answer references, from the answer, in the editor the wiki is open in, without that opening changing anything in the wiki. Where Grimoire has not been told what that opening needs, the answer MUST still arrive, the page's name MUST still be readable in it, and the browser MUST say that opening a page is not set up. | test |
| ACCESS-010 | Users MUST be able to reach each of submitting a source, reading a submission's run, and asking the wiki from the others, and to start a new chat from the chat. | test |

QUERY-002 is INGEST-002's counterpart and differs from it in one thing: what the chat has already
said is handed to the run at dispatch, the way the purpose description is. A chat is therefore a
sequence of runs, not one agent kept alive between questions; nothing in RUNS-002 or RUNS-006 is
bent for it.

ACCESS-008 stands apart from ACCESS-005 rather than extending it: ACCESS-005 is a row in a list of
submissions and the figures there are a run's, while here two different quantities are shown — a
question's run against its ceiling, and a chat's total, which has no ceiling because every question
carries its own. A total dressed as `x / y` would invent a ceiling that does not exist.

ACCESS-009 is the one place OUT-03's promise "with references to wiki pages" becomes something the
user can act on. What the answer contains is the agent's (QUERY-004); that the name in it can be
followed is Grimoire's.

### Why review *(one line per `review` requirement)*

| ID | Why neither a test nor an eval can prove it |
| --- | --- |
| QUERY-004 | It is a requirement about what a text says, and a test could only match its wording, which is static content the constitution does not test (III.8). An eval cannot prove it either: an eval judges whether the agent followed the instruction, which is a different claim and belongs to OUT-07's checking of the wiki, not to whether the instruction states the shape at all. It is proven by the review-checklist item that asks whether the instruction every run receives states the shape its requirement lists, in full. |

### Changed in this feature

The sentences keep their IDs; `docs/capabilities/` carries the current wording and these rows are the
change record (Constitution IV.1, IV.2). All four follow from one thing: a question is a run that
changes nothing in the wiki and is watched rather than handed over.

| ID | Status | Was | Is now, and why |
| --- | --- | --- | --- |
| RUNS-005 | changed | "A run MUST end done when the agent stopped on its own, neither ceiling was reached, and the wiki's log holds an entry for that run; … When the agent stops inside both ceilings and the log holds no entry for the run, Grimoire MUST tell the agent once …" | "A run MUST end done when the agent stopped on its own and neither ceiling was reached, and, **for a run that is to change the wiki**, the wiki's log holds an entry for that run; … When **such a run** stops inside both ceilings and the log holds no entry for it, Grimoire MUST tell the agent once …" A question writes nothing in the wiki and can never satisfy the log condition, so the condition and its one nudge become properties of a run that is to change the wiki. The last clause — Grimoire MUST read nothing else in the wiki to decide this — is unchanged, and a question's run reads nothing in the wiki at all |
| RUNS-007 | changed | "Every run MUST have exactly one record of its own, a Markdown file …" | "**Every run a submission causes** MUST have exactly one record of its own, a Markdown file …" A record exists for a run the user hands over and reviews afterwards. A question is a direct interaction read as it happens, so a record of it would be a file nobody opens (Constitution II.1). Everything else in the requirement is unchanged |
| RUNS-008 | changed | "A run's record MUST hold the frame of that run: …" | "**A record MUST hold the frame of the run it belongs to**: …" The frame is unchanged; what changes is that it is a property of a record rather than of every run, and which runs have records is RUNS-007's business |
| RUNS-003 | changed | "After a run ends failed, no further run MUST start until the user has acknowledged that failure; …" | The same, plus: "A failure the user can no longer see MUST NOT hold the queue: a question's failure goes with the chat that held it when Grimoire stops." Found while implementing phase 3, raised in review of its PR, and decided by the owner. RUNS-003 and QUERY-005 disagree across a restart: the block holds until acknowledged, and the chat does not survive a stop, so afterwards there is no question on any screen to acknowledge. A block restored without its question is a queue nothing can ever clear; persisting the question to keep the block is what QUERY-005 forbids. A submission's failure is untouched and still holds the queue across a restart |
| RUNS-009 | changed | "A run's record MUST hold what the run did, in the order it happened: …" | "**A record MUST hold what the run it belongs to did**, in the order it happened: …" Same reason as RUNS-008. The measurement that there is no reasoning to record stands unchanged |

RUNS-003's change is the one this feature did not foresee: it was found while building phase 3 rather
than while specifying, which is why it sits in this table with the reason it was decided on rather than
in the Requirements table above.

RUNS-006 and RUNS-010 are untouched and both bind a question's run: no agent outlives its run and a
start-up kills what it finds alive, and a run's figures are kept current, stand as its final figures
and survive a stop. ACCESS-008 shows what ACCESS-005 shows, from the same figures, in the one place
a question is read.

### Retired in this feature

None.

### Key Entities

- **Question**: a text the user asks the wiki. It is not a submission: it puts nothing into the wiki
  and does not appear in the list of submissions. It is accepted or refused (QUERY-001, QUERY-003)
  and, when accepted, causes exactly one run (QUERY-002).
- **Answer**: what a question's run produces, read in the chat as it is written. It rests on wiki
  pages and names them inside its prose (QUERY-004); Grimoire judges none of it (Invariant 1).
- **Chat**: the current conversation — its questions, their answers, and what the agent did under
  each. It lives while Grimoire runs — with or without a browser attached to it — and is replaced
  whole by a new one (QUERY-005). There is exactly one, every browser reads that same one, and no
  earlier chat is reachable.
- **Reference**: a wiki page an answer rests on, written by the agent in the wiki's own link form
  (WIKI-001, OKF §6.1) and followable from the answer into the editor the wiki is open in
  (ACCESS-009). Nothing of it is written into a wiki page.
- **A question's run**: a run like any other — one at a time, in order, bounded by both ceilings,
  with its grant recorded and its figures kept (RUNS-002, RUNS-003, RUNS-006, RUNS-010, GUARD-001,
  GUARD-003, GUARD-004). It differs in two things: its grant is read-only (GUARD-005) and it has no
  record (RUNS-007).
- **Submission**, **the four states**, **Acknowledgement**, **Run record**: as
  `001-first-ingest`, `002-ingest-queue` and `003-live-run-record` have them, unchanged.

## Who writes what *(mandatory whenever the feature touches anything in the wiki)*

user = the person using this wiki; owner = whoever ships Grimoire.

This feature writes nothing into the wiki at all. A question's run reads it and leaves it as it
found it, which is what GUARD-005 enforces by construction rather than by asking the agent nicely.
Nothing of a chat is on disk anywhere, in the wiki or outside it.

| Artifact | Written by (agent / Grimoire / user / owner) | What Grimoire adds, if anything |
| --- | --- | --- |
| Any wiki page, during a question's run | nobody | nothing; the grant carries no write (GUARD-005) |
| A section index or the root index, during a question's run | nobody | nothing; the grant carries no write (GUARD-005) |
| `log.md`, during a question's run | nobody | nothing; a question is not a change to the wiki and RUNS-005 asks no entry of it |
| The question instruction | owner | nothing; it is versioned in the repository and changing it is an owner decision named in the PR (Constitution V.1) |
| The question the user asks | user | nothing; it is handed to the run at dispatch and stored nowhere |
| The answer and what the agent did | agent | nothing; it is shown and never written to disk |

## Lifecycle questions *(mandatory)*

| Question | Answer (requirement ID, or "not applicable, because ...") |
| --- | --- |
| Stopping and starting again | QUERY-005 — the chat does not survive it and nothing of it is kept; RUNS-004 and RUNS-006 as before for the run that was in progress, which reads failed and whose agent is terminated at start-up |
| A run or operation ending partway | QUERY-006 — the chat says that question got no answer and why, and nothing half-produced is shown as an answer; GUARD-004 as before for a ceiling ending it, RUNS-003 as before for the acknowledgement that unblocks the next run |
| Concurrent use | RUNS-002 and RUNS-003 as before — a question queues behind whatever is running and whatever is waiting, and never runs beside a run; ACCESS-007 for the chat showing that it is waiting. QUERY-005 for two browsers at once: there is one chat and every browser reads the same one |
| A missing input | QUERY-003 — an empty question, a missing question instruction and a missing purpose description are each refused, nothing is stored, and the user is told which it was. ACCESS-009 for the one input that does not refuse anything: where Grimoire was not told what opening a page in the editor needs, the answer still arrives with its page names readable, and the browser says opening is not set up |

## Success Criteria *(mandatory)*

### Measurable Outcomes

These restate the requirements as observable outcomes; proofs attach to the requirement IDs, not to
the criteria. Numbering continues from `003-live-run-record`, which ended at SC-024.

- **SC-025**: The user can ask the wiki a question and read its answer without ever leaving the chat,
  and sending a question never makes them wait for the answer. — **Restates:** QUERY-001, QUERY-002,
  ACCESS-007
- **SC-026**: For 100% of answers, the answer appears while the agent is still writing it, and
  nothing already on the screen moves as more arrives. — **Restates:** ACCESS-007
- **SC-027**: For 100% of answers, what the agent did to reach it is readable under the answer —
  shut until the user opens a step — in the same shape a run's record is read in, so there is one
  format to learn rather than two. — **Restates:** ACCESS-007, ACCESS-006
- **SC-028**: Every page an answer rests on is named inside the answer's prose, and the user can open
  any one of them in the editor the wiki is open in with one click — or, where Grimoire was not told
  what that needs, still read which page it was. — **Restates:** QUERY-004, ACCESS-009
- **SC-029**: After 100% of questions, the wiki is byte for byte what it was before — no page, no
  index, no log entry — and the grant a question ran under makes any other outcome impossible. —
  **Restates:** GUARD-005, RUNS-005
- **SC-030**: For every question that has a run, what it spent against its cost ceiling is on the
  screen beside it, and the chat's total is shown with no ceiling beside it, so no figure claims a
  limit that does not exist. — **Restates:** ACCESS-008, RUNS-010
- **SC-031**: A follow-up is answered in the light of every question and answer before it in the same
  chat. — **Restates:** QUERY-002
- **SC-032**: Starting a new chat leaves nothing of the previous one reachable, and after Grimoire is
  stopped and started again no question or answer is anywhere. — **Restates:** QUERY-005
- **SC-033**: A question asked while something else is running is answered in the order it was made,
  never beside another run, and the chat says it is waiting. — **Restates:** RUNS-002, ACCESS-007
- **SC-034**: A question whose run failed is never shown as answered: the chat says it got no answer
  and why, and the user can ask it again after acknowledging the failure. — **Restates:** QUERY-006,
  RUNS-003
- **SC-035**: A question's run leaves no record and no file of its own anywhere, while every run a
  submission causes still has exactly one. — **Restates:** RUNS-007
- **SC-036**: From any of Grimoire's three jobs the user can reach the other two. — **Restates:**
  ACCESS-010
- **SC-037**: An answer written while nobody was connected is not lost: a browser that comes back
  reads the chat as it then stands, and no run was stopped to achieve that. — **Restates:**
  QUERY-005, ACCESS-007

## Assumptions

- The chat is one page and one conversation at a time; how it is laid out, how the steps under an
  answer fold, how navigation between the three jobs looks and how a new chat is started are the
  implementing agent's decisions under `docs/ux.md`. The spec requires only that the concerns are
  visibly addressed. — **Requirement:** ACCESS-007, ACCESS-010
- A question's run is dispatched with an instruction of its own, beside the one ingest already has.
  Changing either is an owner decision and is named in the PR (Constitution V.1). Which file it is
  and where it sits is the plan's decision. — **Requirement:** QUERY-004
- How much of the earlier conversation a follow-up's run is given, and how it is given, is the
  plan's decision; the spec requires only that the answer takes what came before into account. —
  **Requirement:** QUERY-002
- Where the chat lives while Grimoire runs is the plan's decision, bounded by QUERY-005: it is held
  whether or not a browser is connected, it must not survive a restart, and a new chat must leave
  nothing of the old one. Held means held while Grimoire runs and not written down — that is what
  keeps "nothing is kept" a property the tests can reach directly, and what keeps this feature clear
  of the record it has ruled out. — **Requirement:** QUERY-005
- The figure beside an answer is the run's own, the same quantity the cost ceiling counts
  (GUARD-004, DEC-015), and is already kept by RUNS-010. No second counting is introduced and no
  figure is in currency. — **Requirement:** ACCESS-008, RUNS-010
- A question's run still needs whatever bookkeeping RUNS-006 and RUNS-010 already require of every
  run — the process to terminate at start-up, the figures to keep. That it has no *record* does not
  make it a run Grimoire has forgotten. Where that bookkeeping lives is the plan's decision. —
  **Requirement:** RUNS-006, RUNS-010, RUNS-007
- What the editor link looks like, and what the user must have told Grimoire for it to be built, is
  the plan's decision. The spec requires only that the page named in an answer can be opened where
  the user reads the wiki, and that where Grimoire was not told what that needs, the answer still
  arrives with the page's name readable in it and the browser says opening is not set up. —
  **Requirement:** ACCESS-009
- Grimoire checks no link an answer contains. OKF requires readers to tolerate a broken link and
  nothing in Grimoire checks one today (`docs/capabilities/wiki.md`); whether the links lead
  anywhere is the owner's reading, and later lint's (OUT-07). — **Requirement:** QUERY-004
- Grimoire runs inside a network the user trusts and serves one person (`docs/product.md` §2), so
  asking a question and reading a chat need no identity and no permission. — **Requirement:**
  QUERY-001, ACCESS-007
- The answer and the steps under it are made from what the agent already reports; no new kind of
  message and no new external system is involved (DEC-009). — **Requirement:** ACCESS-007
- Where the line falls between the answer and the agent's text between the steps is the plan's
  decision, taken from what the CLI emits. The spec requires only that the answer is read as one
  piece of prose and that what the agent did to reach it is under the fold. — **Requirement:**
  ACCESS-007
- The chat's total is what every question in it has spent, a question whose run failed included: a
  failed run's figures are kept and stand (RUNS-010), and a question that cost something and
  answered nothing is exactly the kind of cost the total exists to show. — **Requirement:**
  ACCESS-008, RUNS-010
- How what is shown reaches the browser changes for the chat and for the two views that poll today,
  and the requirements' wording does not (ACCESS-005, ACCESS-006, ACCESS-007, ACCESS-008). The
  mechanism, and the entry in `docs/decisions.md` that supersedes DEC-032, are the plan's. —
  **Requirement:** ACCESS-005, ACCESS-006, ACCESS-007, ACCESS-008
- Whether OUT-03 is reached is not decided by any check in Grimoire. This feature closes only after
  the owner has asked a real wiki a question through the browser, watched the answer form, unfolded
  a step, opened a referenced page in their editor and asked a follow-up (Constitution I.9); the
  plan's quickstart describes that. — **Requirement:** none; this is how the feature closes, not
  behaviour of the system.

## Budget note

Three user stories and one acceptance scenario — ask, read the answer forming, check what it rests
on, follow it up — which every story advances (Constitution I.7). Eleven requirements are registered,
four are reworded and keep their IDs, and none is retired. They become permanent when they are
registered in `docs/capabilities/`, which happens before this feature's first test (Constitution
IV.2). QUERY gets its first capability file.
