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
| QUERY-002 | An accepted question MUST cause a run that is given the question, the purpose description, the question instruction, the run's identifier, and what has been asked and answered in the same chat before it, so that a follow-up is answered in the light of what came before; the run MUST run on the model Grimoire was started with. | test |
| QUERY-005 | A chat MUST hold the questions asked in it, their answers and what the agent did, for as long as Grimoire runs and whether or not a browser is connected to it, and MUST NOT survive Grimoire stopping and starting again. | test |

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

QUERY-005 makes "gone after a restart" a **requirement** rather than a limitation, which is why the
chat is a plain object in memory and not a port: a persistent second implementation would contradict
this sentence rather than serve it (Constitution II.4). Held while nobody is connected, because the
chat is the hub's and not a subscriber's — a run whose reader walked away still writes into it, and a
browser that comes back reads it. No run is stopped for a missing reader; Grimoire has no way to stop
a run except a ceiling.
