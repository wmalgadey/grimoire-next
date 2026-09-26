# Grimoire — Product

## 1. Goal

I maintain a wiki by only deciding which sources go into it and which questions I ask. The maintenance work (classifying, linking, updating, spotting contradictions) is done by an LLM agent. The wiki remains an artifact that is readable to me and usable without Grimoire, one that gets better with every source instead of being recreated from raw material with every question. It follows the Open Knowledge Format ([OKF 0.2](https://github.com/GoogleCloudPlatform/open-knowledge-format/blob/ad30107c31c06aec8a7d5636e0d1058118604e6f/SPEC.md)); which of the optional fields apply is defined by the requirements under WIKI in `docs/capabilities/`.

The goal is reached when I trust the runs enough to let them run unattended and only review them afterwards.

## 2. User and context

One person, one wiki with a purpose described by the user, self-hosted, used in the browser. Sources are submitted in passing, several times a week. The user reviews the results afterwards. Submitting never requires waiting. The user often watches a run while it happens and returns to the results later; runs may also finish while nobody is looking. Reading and editing the wiki outside of Grimoire is possible at any time — that is a property of the artifact, not the intended way of working with it.

The user describes the purpose of the wiki in a hand-written file. Every run receives it. Grimoire neither creates nor modifies it. The file belongs to Grimoire, not to the wiki: it is not a wiki page, it carries no OKF metadata and the agent does not maintain it.

The wiki lives in a version-controlled repository that the user maintains. Grimoire neither commits nor reverts a run; versioning and undo stay with the user, and the history is their gate: it is where they roll a run back or keep it. Undo is therefore as fine-grained as the user's own commits — runs that follow one another without a commit in between land in one diff. What a run changed in the wiki and why is recorded by the agent in `log.md`; that file, not the version history, is what the user and the agent read to see how the wiki evolved. A run that fails can leave the wiki half-changed: whatever the agent had already written, pages and log entry alike, stays where it is. That a run failed is shown by Grimoire, not recorded in the wiki; removing what it left behind is the user's business, as undo is.

The agent sorts the pages into sections: one directory per section, exactly one level deep, and which sections exist is the agent's decision. Every section carries an `index.md` (the file name OKF reserves for it) listing its pages; the wiki's root `index.md` is the entry point and lists only the sections. The agent keeps both levels current whenever a run changes pages. Log and indexes exist from the first run on.

As long as one person uses Grimoire, it needs no access control of its own: it assumes it runs inside a network the user trusts (home network, VPN).

## 3. Core loop

1. I put a source into the wiki.
2. The wiki gets better as a result: new and updated, linked pages referencing the source.
3. I see what was done and why.
4. I ask a question and get an answer from the wiki that is better than before step 1.

## 4. Non-goals

Things that are never built. What must never break is in §5.

- No vector/embedding index: the readable wiki IS the product, RAG is the alternative to it.
- No wiki editor of our own: the wiki stays readable and editable with any tool.
- No approval before a run's changes land: trust is built by reviewing afterwards (§1), and the
  history is the gate (§2). Approval exists only where a run itself proposes instead of acts
  (OUT-08).
- No general execution access for agents: an agent's abilities are exposed as narrowly scoped
  tools, never as a shell.
- No configurable budgets or per-run tuning: the two ceilings (§5) are fixed; otherwise a run
  runs to completion. Trust rests on seeing what a run did, not on regulating it up front.
- Not multiple wikis per instance — a second wiki means a second instance.
- No plugin/extension system.
- No hosted service.

## 5. Invariants

These hold from OUT-01 on and no outcome may break them. Anything that need not hold from the
first feature is a Later outcome, not an invariant.

1. Grimoire decides no wiki content. Judgement lives only in the versioned instruction and the
   user-written purpose description, both handed to the agent at dispatch. Into wiki pages
   Grimoire writes nothing but facts about a run: who produced a page and when.
2. Every run is bounded. An agent receives only the tools granted for that dispatch, every grant
   is recorded, and the run ends at the first of its two fixed ceilings, wall-clock time and
   cost. There is no way to run an agent without both.
3. The wiki's history stays the user's gate. Every change a run makes is visible and reversible
   there; Grimoire never takes a run back on its own, not even a failed one.
4. The wiki stays an artifact that is readable and usable without Grimoire. No outcome may make
   a page depend on Grimoire to be understood.

## 6. Capabilities

| Capability | Covers                                                                                                                             |
| ---------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| INGEST     | Accept sources in their forms, work them into the wiki, update or withdraw them                                                    |
| QUERY      | Answer questions and requests from the wiki, feed insights back                                                                    |
| LINT       | Check structure, consistency and claims against their sources; propose or carry out changes                                        |
| WIKI       | What the wiki is about and how it is shaped: purpose description, format, linking, provenance, sections and their indexes, run log |
| RUNS       | Traceability: what a run did, why, how it ended, what it cost; approving lint proposals                                            |
| ACCESS     | Who uses Grimoire and through which door: the browser UI, further paths, further people                                            |
| GUARD      | What an agent may do and reach: tool grants, the safety ceilings, reach limits                                                     |
| OPS        | Setting up and running Grimoire continuously                                                                                       |

## 7. Outcomes

| ID     | I can ...                                                                                               | Status | Capabilities                      | Specs                              |
| ------ | ------------------------------------------------------------------------------------------------------- | ------ | --------------------------------- | ---------------------------------- |
| OUT-01 | submit a text in the browser and afterwards find new, linked pages including a source page in the wiki  | Done   | INGEST, WIKI, GUARD, ACCESS, RUNS | 001-first-ingest, 002-ingest-queue |
| OUT-02 | see for every run what it did, why it ended and what it cost                                            | Done   | RUNS                              | 003-live-run-record                |
| OUT-16 | watch what the agent is doing while a run is in progress                                                | Done   | RUNS                              | 003-live-run-record                |
| OUT-03 | ask a question and get an answer with references to wiki pages                                          | Now    | QUERY                             |                                    |
| OUT-04 | trust that an agent only reaches what I have allowed                                                    | Later  | GUARD                             |                                    |
| OUT-05 | submit a URL instead of a text                                                                          | Later  | INGEST, WIKI                      |                                    |
| OUT-22 | submit a chat transcript as a source                                                                    | Later  | INGEST                            |                                    |
| OUT-18 | follow every claim on a wiki page to the passage in its source page it rests on                         | Later  | WIKI, INGEST                      |                                    |
| OUT-06 | have a high-value answer become a new wiki page, so that knowledge compounds                            | Later  | QUERY, WIKI                       |                                    |
| OUT-19 | resubmit a changed source and see which pages derived from it are now stale                             | Later  | INGEST, LINT                      |                                    |
| OUT-07 | have the wiki checked — structure, consistency, orphans, pages without a source — and see the proposals | Later  | LINT                              |                                    |
| OUT-08 | approve or reject the consequential proposals, while uncritical ones are carried out without asking     | Later  | LINT, RUNS, WIKI                  |                                    |
| OUT-20 | withdraw a source and have a run remove what exists only because of it                                  | Later  | INGEST, GUARD                     |                                    |
| OUT-21 | have a sample of claims checked against their cited passages and see where they diverge                 | Later  | LINT                              |                                    |
| OUT-09 | let the agent research on the internet when needed                                                      | Later  | INGEST, QUERY                     |                                    |
| OUT-10 | run Grimoire permanently on my server                                                                   | Later  | OPS                               |                                    |
| OUT-11 | use Grimoire through a chat program                                                                     | Later  | ACCESS                            |                                    |
| OUT-17 | be told when a run ends, without opening Grimoire                                                       | Later  | RUNS                              |                                    |
| OUT-12 | share the wiki with further people                                                                      | Later  | ACCESS                            |                                    |
| OUT-13 | monitor the runs and the wiki's structural state over time in a dashboard                               | Later  | RUNS, LINT                        |                                    |
| OUT-14 | have Grimoire help me phrase the wiki's description, while I make the change myself                     | Later  | WIKI                              |                                    |
| OUT-15 | stop committing by hand, because a run puts its own changes into the wiki's history                     | Later  | WIKI, RUNS                        |                                    |

Against the core loop (§3): steps 1 and 2 are OUT-01, step 3 is OUT-02, step 4 is OUT-03. The loop closes once Next is done, not with Now alone.

## 8. Later: promotion triggers

A trigger moves an outcome from Later to Next.

- OUT-04: whenever one of OUT-05, OUT-09, OUT-20 or an unattended OUT-10 is pulled in, OUT-04 is built before it. Without one of those occasions it stays where it is.
- OUT-05/OUT-09: as soon as OUT-01 and OUT-02 have been used regularly for two weeks and pasting text is the bottleneck.
- OUT-06: as soon as I have carried an answer over into the wiki by hand for the second time.
- OUT-07/OUT-08: as soon as the wiki has > 100 pages or I find the first contradiction by hand. Before runs go unattended.
- OUT-10: as soon as I have carried out at least ten ingests.
- OUT-11: as soon as the browser on the go is demonstrably the reason that I submit nothing.
- OUT-12: as soon as a concrete second person wants to use it.
- OUT-13: as soon as I have needed to compare runs by hand more than twice.
- OUT-14: as soon as I set up a second wiki.
- OUT-15: as soon as ingest, query and the browser UI run stably and I trust the agent enough that I no longer need the history as my gate.
- OUT-17: as soon as I trust the process enough that I no longer watch the agent work.
- OUT-18: as soon as I find the first claim while reading that I cannot trace to a source. Before OUT-21.
- OUT-19: as soon as I resubmit a source for the first time because it changed.
- OUT-20: as soon as I remove the traces of a bad source by hand for the first time and it takes longer than 15 minutes.
- OUT-21: after OUT-18 and before runs go unattended. Not earlier.
- OUT-22: as soon as I paste chat excerpts as text for the third time in one week.

## 9. Open questions

- How does a run tell a consequential lint change from an uncritical one, and who decides the criterion? (blocks OUT-08)
- Who commits, the agent through a tool of its own or Grimoire at the run boundary; and does a run's commit land on the wiki's main history or on a branch the user merges? (blocks OUT-15)
- What counts as a "passage": line range, heading anchor or OKF footnote on `sources`? (blocks OUT-18)
- Who records which page derives from which source beyond the page's own `sources` list: the agent in the frontmatter, or Grimoire from the run's tool calls? (blocks OUT-19, OUT-20)
