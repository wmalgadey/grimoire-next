# Grimoire — Product

## 1. Goal

I maintain a wiki by only deciding which sources go into it and which questions I ask. The maintenance work (classifying, linking, updating, spotting contradictions) is done by an LLM agent. The wiki remains an artifact that is readable to me and usable without Grimoire, one that gets better with every source instead of being recreated from raw material with every question. It follows the Open Knowledge Format ([OKF 0.2](https://github.com/GoogleCloudPlatform/open-knowledge-format/blob/ad30107c31c06aec8a7d5636e0d1058118604e6f/SPEC.md)); which of the optional fields apply is defined by the requirements under WIKI (`docs/capabilities/`, still to be written).

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

- No vector/embedding index: the readable wiki IS the product, RAG is the alternative to it.
- No wiki editor of our own: the wiki stays readable and editable with any tool.
- No judgement of content by Grimoire: judgement about content rests with the agent and the
  user. Into the wiki Grimoire writes only facts about the run: who produced a page and when.
- Undo happens through the wiki's version control, not through Grimoire: Grimoire brings no undo
  of its own and never takes a run back, a failed one included. How far back the user can go is a
  property of the wiki's history, not a feature of Grimoire.
- No general execution access for agents: an agent's abilities are exposed as narrowly scoped
  tools, never as a shell.
- No configurable budgets or per-run tuning: a run has two fixed ceilings, wall-clock time and
  cost, and ends at whichever it reaches first; otherwise it runs to completion. Trust rests on
  seeing what a run did, not on regulating it up front.
- Not multiple wikis per instance — a second wiki means a second instance.
- No plugin/extension system.
- No hosted service.

## 5. Capabilities

| Capability | Covers                                                                                                                             |
| ---------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| INGEST     | Accept sources and work them into the wiki                                                                                         |
| QUERY      | Answer questions and requests from the wiki, feed insights back                                                                    |
| LINT       | Check structure and consistency, propose or carry out changes                                                                      |
| WIKI       | What the wiki is about and how it is shaped: purpose description, format, linking, provenance, sections and their indexes, run log |
| RUNS       | Traceability: what a run did, why, how it ended, what it cost; approving lint proposals                                            |
| ACCESS     | Who uses Grimoire and through which door: the browser UI, further paths, further people                                            |
| GUARD      | What an agent may do and reach: tool grants, the safety ceilings, reach limits                                                     |
| OPS        | Setting up and running Grimoire continuously                                                                                       |

## 6. Outcomes

<!-- IDs: OUT-NN, in order of assignment. They are never renumbered and
     never reused; Order of rows = priority. OUT is not a capability name.
     Status: Now = gets specified next, Next = after that, without a condition,
     Later = needs a trigger from §7. -->
| ID     | I can ...                                                                                              | Status | Capabilities                  | Specs            |
| ------ | ------------------------------------------------------------------------------------------------------ | ------ | ----------------------------- | ---------------- |
| OUT-01 | submit a text in the browser and afterwards find new, linked pages including a source page in the wiki | Now    | INGEST,WIKI,GUARD,ACCESS,RUNS | 001-first-ingest, 002-ingest-queue |
| OUT-02 | see for every run what it did, why it ended and what it cost                                           | Next   | RUNS                          |                  |
| OUT-16 | I can watch what the agent is doing while a run is in progress                                         | Next   |                               | RUNS             |
| OUT-03 | ask a question and get an answer with references to wiki pages                                         | Next   | QUERY                         |                  |
| OUT-04 | trust that an agent only reaches what I have allowed                                                   | Later  | GUARD                         |                  |
| OUT-05 | submit a URL instead of a text                                                                         | Later  | INGEST,WIKI                   |                  |
| OUT-06 | have a high-value answer become a new wiki page, so that knowledge compounds                           | Later  | QUERY,WIKI                    |                  |
| OUT-07 | have the wiki checked and see the proposals                                                            | Later  | LINT                          |                  |
| OUT-08 | approve or reject the consequential proposals, while uncritical ones are carried out without asking    | Later  | LINT,RUNS,WIKI                |                  |
| OUT-09 | let the agent research on the internet when needed                                                     | Later  | INGEST,QUERY                  |                  |
| OUT-10 | run Grimoire permanently on my server                                                                  | Later  | OPS                           |                  |
| OUT-11 | use Grimoire through a chat program                                                                    | Later  | ACCESS                        |                  |
| OUT-17 | I am told when a run ends, without opening Grimoire                                                    | Later  |                               | RUNS             |
| OUT-12 | share the wiki with further people                                                                     | Later  | ACCESS                        |                  |
| OUT-13 | monitor the runs incl. detailed logs and metrics over time in a dashboard                              | Later  | RUNS                          |                  |
| OUT-14 | have Grimoire help me phrase the wiki's description, while I make the change myself                    | Later  | WIKI                          |                  |
| OUT-15 | stop committing by hand, because a run puts its own changes into the wiki's history                    | Later  | WIKI,RUNS                     |                  |

Against the core loop (§3): steps 1 and 2 are OUT-01, step 3 is OUT-02, step 4 is OUT-03. The loop closes once Next is done, not with Now alone.

## 7. Later: promotion triggers

A trigger moves an outcome from Later to Next.

- OUT-04: whenever one of OUT-05, OUT-09 or an unattended OUT-10 is pulled in, OUT-04 is built before it. Without one of those occasions it stays where it is.
- OUT-05/OUT-09: as soon as OUT-01 and OUT-02 have been used regularly for two weeks and pasting text is the bottleneck.
- OUT-06: as soon as I have carried an answer over into the wiki by hand for the second time.
- OUT-07/OUT-08: as soon as the wiki has > 100 pages or I find the first contradiction by hand.
- OUT-10: as soon as I have carried out at least ten ingests.
- OUT-11: as soon as the browser on the go is demonstrably the reason that I submit nothing.
- OUT-12: as soon as a concrete second person wants to use it.
- OUT-13: as soon as I have needed to compare runs by hand more than twice.
- OUT-14: as soon as I set up a second wiki.
- OUT-15: as soon as ingest, query and the browser UI run stably and I trust the agent enough that I no longer need the history as my gate.
- OUT-17: as soon as I have enough confidence in the process, so I do not need to watch the agents worl.

## 8. Open questions

- How does a run tell a consequential lint change from an uncritical one, and who decides the criterion? (blocks OUT-08)
- Who commits, the agent through a tool of its own or Grimoire at the run boundary? (blocks OUT-15)
