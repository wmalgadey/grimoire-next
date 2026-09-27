# Brief: ask-the-wiki

Owner-written input for `/speckit-specify` (sections 1–5) and `/speckit-plan` (section 6). One page.
Wishes are wishes; the implementing agent decides layout and implementation (see `docs/ux.md`).

## 1. Outcome

- Outcome: **OUT-03** — ask a question and get an answer with references to wiki pages.
  This is the fourth step of the core loop (`docs/product.md` §3) and the first feature of the
  QUERY capability, which has no registered requirement yet.
- Afterwards I can say: "I ask the wiki a question on a page of its own, watch the answer form as
  the agent writes it, ask back until I have what I wanted, and open any page it cites in Obsidian
  with one click."

## 2. Walkthrough

I switch to the ask page and type my question — not a source this time, a question. I send it and the
answer starts arriving as the agent writes it, growing in place while I read the beginning of it.
Underneath it, folded shut, is what the agent did to get there — its own text between the steps and
which wiki pages it opened — and I unfold one step when I want to see what came back. Beside the
answer stands what this question cost against its ceiling, and the chat carries the total of what I
have spent on it so far. The answer names the wiki pages it rests on, and each one is a link:
clicking it opens that page in Obsidian, in the vault I actually have open, which is the directory
above the wiki rather than the wiki itself. The answer is not quite what I meant, so I ask back in
the same chat — the agent still knows what we were talking about and answers again, live, with its
own references. I stay with it while it works, because this is a conversation and not something I
hand over and walk away from. If my question arrives while an ingest run is already going, it waits
its turn and the chat tells me so. When a question's run fails — a ceiling, a process that died — the
chat says that this question got no answer and why, and I can ask it again. When I am done with the
subject I start a new chat, which puts the old one away for good, and nothing of it is kept.

## 3. Wishes

- A page of its own, laid out as a conversation — my question, the answer below it, then my next
  question — plus enough navigation to get between the three jobs (submit a source, read a run, the
  chat) and to start a new chat. Text-first, like the rest of Grimoire.
- The answer appears as the agent produces it, not fetched a moment later, and it grows in place —
  nothing I am reading moves under me. The same for the folded steps underneath it.
- References read as part of the answer's prose, not as a bibliography at the bottom. What I click
  is the page's name.
- Under each answer, what the agent did — shut by default, in the shape the run record uses today,
  so there is no second format to learn. Seeing which pages it read is what makes me believe the
  answer; I open a step when I want to check one.
- Every answer says what it cost against its ceiling, and the chat says what it has cost me
  altogether. I want to know what asking costs me before I start asking a lot.

## 4. Not in this feature

- **A record of its own for a question — never.** A record exists because `docs/product.md` §1 aims
  at runs I let go unattended and review *afterwards*. A chat is a direct interaction: I am there
  while it happens, I read it as it forms, and there is no afterwards to review. Writing a file
  nobody opens is a mechanism with no consumer (II.1).
- A chat surviving a restart of Grimoire — later, no ID yet. After a restart the chat page is empty,
  and with no record behind it that means the questions and their answers are gone.
- A history of what questions have cost me — later, no ID yet. It follows from the two above: the
  figure is on the chat while the chat lives, and nowhere after.
- Access to earlier conversations, a list of chats, searching them — later, no ID yet. Only the
  current chat is reachable; a new chat replaces it.
- A page written from an answer, whether Grimoire proposes it or writes it unasked — later (OUT-06,
  has its own promotion trigger: the second time I carry an answer over by hand). This feature is
  what makes carrying one over by hand possible at all, so it is what starts that trigger counting.
  Deciding it here would mean deciding two things this feature has deliberately left alone: that a
  question's grant is read-only, and whether a run proposes or acts (`docs/product.md` §4, OUT-08).
- Letting the agent look anything up on the internet to answer — later (OUT-09).
- A question changing the wiki in any way, the log included — never in this feature. A question
  reads; that the wiki got better is what ingest is for (Invariant 1).
- A question overtaking waiting ingests, or running beside a run — never here. One run at a time
  stays true (RUNS-002).
- The agent's reasoning or thinking — never, because there is nothing to show. The CLI reports an
  empty `thinking` beside a several-hundred-character signature, measured twice
  (`docs/capabilities/runs.md`, RUNS-009). What the chat shows instead is the agent's own prose at
  the turn boundaries and its tool calls, which is all that exists. Should a later CLI deliver
  reasoning, that is an owner's decision then, not a promise now.
- Cost in currency — never (DEC-015).

## 5. Already in force

- Decisions: DEC-009 (a run is a spawned `claude` CLI whose NDJSON stream is where its events come
  from), DEC-010 (pinned model id), DEC-011 (tools are deny-by-default by construction),
  DEC-013/DEC-014 (wiki tools over MCP at a per-run loopback endpoint), DEC-015 (the two ceilings
  and what cost means), DEC-019 (the browser surface is static files, no bundler, no npm), DEC-023
  (state in SQLite under `--state`, never in the wiki), DEC-030 (a run's figures are columns on its
  row, never parsed back out of anything).
- Capabilities touched: QUERY (new — the question, the answer, the references, the chat), ACCESS
  (the page, the navigation, and how what is shown arrives), GUARD (the grant a question runs
  under), RUNS (a question is a run, and what a run owes), WIKI (what a reference points at).
- Existing requirements this builds on: RUNS-001 (the four states), RUNS-002 (one run at a time, in
  order — a question queues like everything else), RUNS-003 (a failure blocks the next run until
  acknowledged), RUNS-006 (no agent outlives its run, and a start-up kills what it finds alive),
  RUNS-010 (a run's figures are kept current), GUARD-001 (a run gets only its grant), GUARD-003 (the
  grant is recorded — it hangs on the run itself, so it stays recorded without a record file),
  GUARD-004 (both ceilings), ACCESS-005 (cost is shown against its ceiling and never bare, and a
  figure changing must not move what is around it), WIKI-001 (the sections-and-pages shape a
  reference addresses).
- **Four things this feature contradicts, all the owner's decision:**
  - **RUNS-005** ends a run `done` only when `log.md` holds an entry for it. A question writes
    nothing in the wiki, so it can never satisfy that. The done-condition has to become a property
    of runs that change the wiki rather than of every run.
  - **RUNS-007** says *every* run must have exactly one record of its own, and **RUNS-008/009**
    describe what that record holds. A question has no record. All three have to become properties
    of runs that are handed over rather than of every run. ACCESS-006, which is about reading a
    *submission's* record, is untouched by this.
  - **GUARD-002** is the grant for an ingest run and says what that one allows. A question needs a
    grant of its own, read-only: no page written, no index written, no log appended.
  - **DEC-032** makes reading a run a page that polls. Polling is replaced by the browser being
    sent what happens, on the chat page **and on the two pages that poll today**. The wording of
    ACCESS-005 and ACCESS-006 does not change — both already ask for figures that follow a run and
    for lines that arrive as they are appended; what changes is how, and both are proven again.

## 6. Plan notes (input for /speckit-plan only)

- Constraints:
  - **Each question is its own run**, with its own ceilings and its own grant. A chat is a sequence
    of runs, not one long-lived agent. What the agent knows of the conversation is handed to it at
    dispatch, like the purpose description is.
  - **No polling anywhere when this feature is done.** The chat, the submissions list and the run
    record view are all sent what happens. DEC-032 is superseded and says so in `docs/decisions.md`.
  - **Server-Sent Events are the owner's expectation, SignalR was weighed and is the fallback.**
    SignalR's server half is in the framework, but its browser client means npm or a vendored
    script in `wwwroot/`, which DEC-019 rules out; `EventSource` is native, needs nothing, and is
    exactly one-directional server→browser, which is the direction here. The question itself goes in
    by POST. If SSE turns out not to carry this, say why in the new DEC.
  - `AgentTranscript` stays the only reader of the CLI protocol (V.2). The chain is CLI stdout →
    `AgentTranscript` → hub → browser; the CLI already streams, so nothing new is parsed.
  - `InstructionLoader` stays the only thing that puts text into a prompt (V.1). A question is a
    second instruction beside `instructions/ingest.md`, and changing either is an owner decision
    named in the PR.
  - **A question leaves no record, but its run still needs bookkeeping.** RUNS-006 has start-up kill
    the process of every run it finds as in progress; a run with nothing on disk would leave an
    orphaned `claude` after a crash. So the run's row survives — pid, start time, grant, figures —
    while the chat does not. That is also where the answer's cost comes from (DEC-030), so no new
    counting is needed and RUNS-010 is untouched.
  - Two new start-up inputs for the Obsidian link: the vault's name and the directory the in-vault
    paths are relative to. The link form is `obsidian://open?vault=<name>&file=<path relative to
    that directory>`. The absolute-path form was rejected: the owner wants the directory to be
    theirs to define.
  - The queue is untouched (RUNS-002, RUNS-003). A question waits behind whatever is running and
    behind whatever is waiting, and an unacknowledged failure blocks it as it blocks an ingest.
  - Nothing in the spec may promise reasoning or thinking. RUNS-009's measurement stands.
- Ideas:
  - The chat holds a question, the answer forming under it, and the steps between — all of it in
    memory for as long as the hub runs, because section 4 says a restart empties it.
  - The references: the agent writes ordinary relative Markdown links (WIKI-001, OKF §6.1) and the
    browser rewrites them to `obsidian://` when it renders. Nothing new goes into a wiki page.
  - The chat's total is a sum over that chat's questions and has **no ceiling behind it** — each
    question carries its own — so it must not be dressed as `x / y`.
- Open questions for the plan:
  - **What happens to a question's run when its reader leaves** — the tab closes, the connection
    drops. A chat is a direct interaction, so a run nobody is reading produces nothing anyone will
    see, and there is no record to catch it. Whether it runs to its ceiling, or is stopped, is a
    real question and the owner has not decided it. (Grimoire has no way to stop a run today; only a
    ceiling ends one.)
  - **What the chat shows when the connection broke mid-answer.** With no record behind it there is
    nothing to re-read, so either something is held for a reconnect or the answer is admitted lost.
    Say which; do not build both.
  - What a question's run is to the submissions list. It is a run, but the list is headed "What
    became of each submission" and a question is not a submission. Whether questions appear there,
    in a list of their own, or nowhere is a design question the owner has not decided.
  - How much of the previous turns a follow-up's run is given, and what happens when that grows past
    what a dispatch can carry. No mechanism for later — just say what the first version does.
  - What the chat shows when the vault inputs are missing at start-up. Ingest refuses a submission
    when its two inputs are missing (INGEST-003); a missing vault name need not be that severe,
    because the answer is still an answer without a clickable link.
  - Whether a reference the agent writes to a page that does not exist is Grimoire's business. OKF
    requires readers to tolerate a broken link and nothing in Grimoire checks links today
    (`docs/capabilities/wiki.md`).
- Done spikes: none.
