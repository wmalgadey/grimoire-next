# QUERY

Ask the wiki a question and read the answer: the chat, what a question's run is given, and what
becomes of a conversation.

<!-- The as-is description of the system (Constitution IV.2). An id becomes permanent the moment it
     is registered here: never renumbered, never reused. A removed requirement moves under
     "Retired" and keeps its id. -->

A question is **not** a submission. It puts nothing into the wiki, it appears in no list of
submissions, and it has no record — it is read in the chat as it happens rather than handed over and
reviewed afterwards. What it *is* is a run like any other: one at a time, in the order the questions
and submissions were made, blocked by an unacknowledged failure, bounded by both ceilings, with its
grant recorded and its figures kept (RUNS-002, RUNS-003, RUNS-006, RUNS-010, GUARD-003, GUARD-004).

## Requirements

| ID | Requirement | Proof |
| --- | --- | --- |
| QUERY-001 | Users MUST be able to ask the wiki a question, and the question MUST be accepted without the user waiting for its answer. | test |
| QUERY-002 | An accepted question MUST cause a run that is given the question, the purpose description, the question instruction, the run's identifier, and what has been asked and answered in the same chat before it, so that a follow-up is answered in the light of what came before; the run MUST run on the model Grimoire was started with. | test |
| QUERY-003 | A question MUST be refused when its text is empty or only whitespace, when the question instruction is missing, or when the purpose description is missing; no run MUST start, the refused question MUST NOT be stored and MUST carry no state, and the user MUST be told which of the three it was. | test |
| QUERY-004 | The question instruction MUST state that the answer is written for the user to read, rests on what the wiki's pages say, names every page it rests on inside its prose as a link to that page in the link form the wiki uses (WIKI-001), and that nothing in the wiki is to be written, changed or appended. | review |
| QUERY-005 | A chat MUST hold the questions asked in it, their answers and what the agent did, for as long as Grimoire runs and whether or not a browser is connected to it, and MUST NOT survive Grimoire stopping and starting again. There MUST be exactly one chat, the same one for every browser reading it. Users MUST be able to start a new, empty chat, after which nothing of the previous chat is reachable and nothing of it is kept. | test |
| QUERY-006 | When a question's run ends failed, the chat MUST say against that question that it got no answer and why, MUST NOT present anything the run had produced as its answer, and the user MUST be able to ask the question again once the failure has been acknowledged. | test |

QUERY-001 is INGEST-001's counterpart, and "without the user waiting" means the same thing it means
there: the question is answered with the turn as the chat carries one, and the run outlives the request
that started it. **A question asked while something else runs is accepted**, not refused — it waits its
turn (RUNS-002). There is no refusal for a run being in progress, which is the same position
INGEST-005 was retired for.

QUERY-003's three are checked in one order — the question instruction, then the purpose description,
then the text — so each refusal names **exactly one** thing and a start with none of them in place says
one thing rather than three. A submission is refused on the *ingest* instruction and a question on the
*question* instruction; the purpose description refuses both (INGEST-003). A refused question becomes
nothing: it is stored nowhere, carries no state, and there is no run to have failed.

**QUERY-004 is proven by `review`**, and it is the only requirement of this feature that is. It is a
requirement about **what a text says**, and a test could only match its wording — static content the
constitution does not test (III.8). An eval cannot prove it either: an eval judges whether the agent
*followed* the instruction, which is a different claim and belongs to checking the wiki, not to
whether the instruction states the shape at all. What proves it is the review-checklist item that asks
of **each** instruction a run receives whether it states the shape its requirement lists, in full
(`docs/review-checklist.md` item 3).

Where the wiki holds nothing about the question, the instruction has the agent say so plainly, name
what it looked at, and stop. That is an acceptance criterion of "rests on what the wiki's pages say" —
where the pages say nothing, resting on them *is* saying so — and a clarification refines rather than
extends, so it adds no id of its own (Constitution IV.8). Answering from what the model itself knows,
marked as not from the wiki, was declined: the answer would then not rest on the wiki, and a marked
sentence is a source with no page behind it.

The instruction is **Grimoire's own**, versioned in this repository at `instructions/question.md`.
Changing it is an owner decision named in the PR (Constitution I.11, V.1).

QUERY-002 is INGEST-002's counterpart and differs from it in one thing: what the chat has already
said is handed to the run at dispatch, the way the purpose description is. A chat is therefore a
**sequence of runs**, not one agent kept alive between questions — each question gets its own run
with its own grant and its own ceilings, so nothing in RUNS-002 or RUNS-006 is bent for it. A resumed
CLI session would carry the previous run's grant and ceilings forward, which is the opposite of what
GUARD-004 and GUARD-005 ask.

**Nothing is trimmed and there is no cap.** The whole chat goes into the dispatch. A chat too large
for one ends that run failed, and the chat says that question got no answer and why (QUERY-006) —
the path every failed run takes, with the remedy the feature already gives: a new chat. A cap, a
window or a summary would be a mechanism with no consumer until a real chat reaches the limit
(Constitution II.1); compressing a conversation is proposed as a Later outcome with exactly that
trigger.

**The steps are not in the dispatch.** What the agent did to reach an earlier answer is for the user
to check, not context the next run needs, and a run's tool results are the largest thing in a chat by
far.

**Exactly one chat, and every browser reads that same one.** A chat per browser would make Grimoire
tell one reader from another, which nothing else in this feature needs (Constitution II.1) — so
"a question asked in one tab appears in the other" and "a new chat empties both" fall out rather than
being built.

**A question still being answered is not stopped by a new chat.** Its run is not a chat and goes on
being a run: it holds the queue until it ends and its figures stay with it (RUNS-010). What it
produces belongs to the chat that is gone, so nothing of it appears in the new one. Grimoire has no
way to stop a run except a ceiling, and building one here would be a mechanism this feature does not
otherwise need (research.md R-13).

**A question still waiting its turn goes with its chat.** It has no run yet, so there is nothing of it
to keep: it leaves the queue with the chat it was asked in, and nothing of it ever starts. A failed
question the new chat takes away no longer holds the queue — it is on no screen any more, which is
RUNS-003's last clause reached by a new chat rather than by a stop.

**QUERY-006 is the other end of RUNS-003.** A question whose run failed blocks the queue exactly as a
failed ingest does, and the user must be able to clear it — but there is no row in the submissions
list to clear it from, because a question is not a submission. The chat offers the one control against
the question that failed. ACCESS-003's wording is unchanged: it asks for a failed run to be
acknowledgeable in the browser, and it is.

The acknowledged question still reads *got no answer*, as an acknowledged submission still reads
`failed`, and the user can then ask it again. **Nothing the run had produced is presented as its
answer**: half a sentence from a run that stopped at a ceiling is not an answer, and showing it as one
would be the worst kind of wrong — an answer that looks like an answer and is not.

**What a stop takes with it.** A question that got no answer and was never acknowledged holds the
queue while Grimoire runs, exactly as a failed submission does (RUNS-003) — and that block goes with
the chat. There is nothing left to acknowledge: the question is on no screen and on no disk, so a
block restored without it could never be cleared. RUNS-003's last clause says so, and
`docs/capabilities/runs.md` carries the reasoning.

QUERY-005 makes "gone after a restart" a **requirement** rather than a limitation, which is why the
chat is a plain object in memory and not a port: a persistent second implementation would contradict
this sentence rather than serve it (Constitution II.4). Held while nobody is connected, because the
chat is the hub's and not a subscriber's — a run whose reader walked away still writes into it, and a
browser that comes back reads it. No run is stopped for a missing reader; Grimoire has no way to stop
a run except a ceiling.
