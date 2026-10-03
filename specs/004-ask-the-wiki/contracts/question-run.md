# Contract: a question's run (hub ↔ the agent)

New in `004-ask-the-wiki`. It says what is different about the run that answers a question, and
everything it does not name is the same as an ingest run's:
`specs/001-first-ingest/contracts/agent-cli-protocol.md` and
`specs/001-first-ingest/contracts/mcp-wiki-tools.md` are unchanged and still in force.

**A question's run is a run like any other.** One at a time, in the order the questions and
submissions were made, blocked by an unacknowledged failure, bounded by both ceilings, with its
grant recorded and its figures kept, and its agent's process identity written down so a start-up can
terminate it (RUNS-002, RUNS-003, RUNS-006, RUNS-010, GUARD-001, GUARD-003, GUARD-004). It differs
in exactly three things: its grant, the endpoint that serves it, and that it leaves no record.

---

## 1. The grant is read-only, and the endpoint is what makes it so

| | An ingest run | A question's run |
| --- | --- | --- |
| Tools | `list_pages`, `read_page`, `write_page`, `write_index`, `append_log` | `list_pages`, `read_page` |
| Endpoint | `/mcp/runs/{runId}` | `/mcp/questions/{runId}` |
| Requirement | GUARD-002 | **GUARD-005** |

**The tools a question is not granted do not exist at its endpoint.** The hub serves two MCP tool
types: the one it already serves, and a second carrying only the two reads. `write_page`,
`write_index` and `append_log` are not registered at `/mcp/questions/{runId}` at all — there is no
flag that would turn them on and no name that would reach them.

That is DEC-011's standing decision applied rather than repeated: tools are deny-by-default **by
construction, not by an allow-list of names**. Narrowing `--allowed-tools` to two of five would have
left the write tools served at that run's own endpoint, one flag away, which is precisely the
allow-list over a larger surface DEC-011 rejected (`../research.md` R-06).

**GUARD-001 guards it for free.** `system/init` reports the run's whole tool surface, and
`AgentTranscript.SurfaceIsTheGrant` compares by **equality**, not containment. A question's run
whose endpoint served anything beyond the two ends failed before its first model call, with
`RunEndedBecause.ToolsWereNotTheGrant` — and the chat says that question got no answer and why
(QUERY-006).

**The two read tools are the same two tools.** Same names, same arguments, same answers, serving the
same `IWikiStore`; their bodies live once. Nothing about reading the wiki is different for a
question.

**What a question's run cannot do, therefore**: write a page, write an index, append to the log,
delete anything, move anything. After it has ended, the wiki is byte for byte what it was before —
by construction, not because the instruction asked nicely (GUARD-005, SC-029).

---

## 2. What the run is given

`InstructionLoader` assembles it and nothing else puts text into a prompt (Constitution V.1). In
order:

| Part | Whose | Requirement |
| --- | --- | --- |
| The question instruction | Grimoire's own, versioned in this repository at `instructions/question.md`. Changing it is an owner decision named in the PR. Where the wiki holds nothing about the question, it has the agent **say so plainly, name what it looked at, and stop** — an acceptance criterion of QUERY-004's "rests on what the wiki's pages say", not a further id (Constitution IV.8) | QUERY-004 |
| The purpose description | The user's. Grimoire neither creates nor changes it | QUERY-002 |
| The run's identifier | Grimoire's | QUERY-002 |
| What has been asked and answered in this chat before it | The user's questions and the agent's answers, in order; a question that got no answer is there as asked, with "got no answer because <reason>" where its answer would stand | QUERY-002 |
| The question | The user's, whole, as they typed it | QUERY-001, QUERY-002 |

**The steps are not included** — what the agent did to reach an earlier answer is for the user to
check, not context the next run needs, and a run's tool results are the largest thing in a chat.

**A question that got no answer is included** — as it was asked, with the reason its run ended in
the words the chat shows, and nothing its run produced (QUERY-006). An agent not told the question
was already asked and failed walks the same way again (DEC-042, amended after closing 004).

**Nothing is trimmed and there is no cap.** The whole chat goes in. A chat too large for a dispatch
ends that run failed, and the chat says that question got no answer and why — the path every failed
run takes, with the remedy the feature already gives: a new chat. Building a cap, a window or a
summary now would be a mechanism with no consumer until a real chat hits the limit (Constitution
II.1, `../research.md` R-07). Compressing a conversation is proposed to the owner as a Later outcome
with that trigger ([../plan.md](../plan.md) §Proposed to the owner); nothing here anticipates it.

**A chat is a sequence of runs, not one agent kept alive between questions.** Each question gets its
own run with its own ceilings and its own grant, so RUNS-002 and RUNS-006 are untouched. Resuming a
CLI session would carry the previous run's grant and ceilings forward, which is the opposite of what
GUARD-004 and GUARD-005 ask.

**The model** is the one Grimoire was started with, pinned, as for every run (DEC-010, QUERY-002).

---

## 3. What is read back, and where it goes

Nothing new is parsed. `AgentTranscript` stays the only reader of the CLI protocol (Constitution
V.2) and already reports the three moments a chat shows (DEC-028). What changes is only where the
hub sends them:

| The moment | A submission's run | A question's run |
| --- | --- | --- |
| `AgentSaid` | into the record | into the **answer**, appended in the order it arrived |
| `ToolCalled`, `ToolReturned` | into the record | into the **steps**, one entry each, folded in the chat |
| `GrimoireSaid` | the nudge, into the record | **never arises** — RUNS-005's nudge is asked only of a run that is to change the wiki |
| the cost | onto the run's row | onto the run's row, and it is the figure the chat shows (DEC-030, RUNS-010) |

**Every piece of the agent's text is the answer**, and the tool calls are the steps. The line falls
there because ACCESS-007 forbids content arriving from moving what the user is already reading: it
is the only split that can be decided at the moment a block arrives and never revised
(`../research.md` R-08).

**No reasoning and no thinking is shown, because the CLI gives none.** `thinking` carries an empty
string beside a signature blob — measured twice (RUNS-009) — and `AgentTranscript` does not read
those blocks. This feature promises nothing about them.

---

## 4. What a question's run does not have

**No record.** RUNS-007 as this feature rewords it gives a record to a run **a submission causes**. A
record exists because a run is handed over and reviewed afterwards; a chat is read as it happens, so
a file for it would be one nobody opens (Constitution II.1). Nothing of
`specs/003-live-run-record/contracts/run-record.md` applies to a question's run, and `EntriesLost`
is always zero for one.

**No log entry, and so no nudge.** RUNS-005's done-condition and its single nudge are asked of a run
that is to change the wiki. A question's run stops on its own inside both ceilings having written
nothing, and **ends done** — the log is not read for it, and the wiki is not read by Grimoire at all
on its behalf.

**No row in the submissions list.** A question is not a submission (spec, Clarifications). The chat
is where it is read, and there is no afterwards to review.

**Nothing on disk of the question itself** — not the text, not the answer, not the steps (QUERY-005).
Its **run's row** is on disk, because RUNS-006 has a start-up terminate the agent of every run that
was in progress and a run with nothing recorded would leave an orphaned `claude` holding the granted
tools with no ceiling on it. What that start-up does with such a row: terminate the agent where the
recorded identity is still live (DEC-024), then mark the run ended failed. No tail is written — there
is no record — and nothing is restored into a chat, because QUERY-005 empties it.
