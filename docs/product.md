# Grimoire — Product

## 1. Goal

I maintain a wiki by only deciding which sources go into it and which questions I ask. The maintenance work (classifying, linking, updating, spotting contradictions) is done by an LLM agent. The wiki remains an artifact that is readable to me and usable without Grimoire, one that gets better with every source instead of being recreated from raw material with every question. It follows the Open Knowledge Format ([OKF 0.2](https://github.com/GoogleCloudPlatform/open-knowledge-format/blob/ad30107c31c06aec8a7d5636e0d1058118604e6f/SPEC.md)); which of the optional fields apply is defined by the requirements under WIKI.

The goal is reached when I trust the runs enough to let them run unattended and only review them afterwards.

## 2. User and context

One person, one wiki with a purpose described by the user, self-hosted, used in the browser. Sources are submitted in passing, several times a week. The user reviews the results afterwards. During a run they do not wait. Reading and editing the wiki outside of Grimoire is possible at any time — that is a property of the artifact, not the intended way of working with it.

The user describes the purpose of the wiki in a hand-written file inside the wiki itself (`purpose.md` in the wiki root). Every run receives it. Grimoire neither creates nor modifies it; it can help the user phrase the text, but the change is made by the user.

The wiki lives in a Git repository that the user versions. Grimoire never commits. What a run did is recorded by the run itself in `log.md`; that file, not the version history, is what the user and the agent read to see what happened.

Grimoire assumes it runs inside a network the user trusts (home network, VPN) and brings no access control of its own.

## 3. Core loop

1. I put a source into the wiki.
2. The wiki gets better as a result: new and updated, linked pages referencing the source.
3. I see what was done and why.
4. I ask a question and get an answer from the wiki that is better than before step 1.

## 4. Non-goals

- No vector/embedding index: the readable wiki IS the product, RAG is the alternative to it.
- No wiki editor of our own: the wiki stays readable and editable with any tool.
- No judgement of content by Grimoire: judgement about content rests with the agent and the
  user. Into wiki pages Grimoire writes only facts about the run (who, when).
  Which source carries which statement — and any provenance or confidence metadata — is
  recorded by the agent.
- Undo happens through the wiki's version control, not through Grimoire: the user versions the
  wiki repository, Grimoire never commits.
- No general execution access for agents: an agent's abilities are exposed as narrowly scoped
  tools, never as a shell.
- No budget, time or step limits: letting runs go unattended rests on seeing what a run did,
  not on regulating it up front.
- Not multiple wikis per instance — a second wiki means a second instance.
- No plugin/extension system.
- No hosted service.

## 5. Capabilities

| Capability | Covers                                                                                   |
| ---------- | ---------------------------------------------------------------------------------------- |
| INGEST     | Accept sources and work them into the wiki                                               |
| QUERY      | Answer questions and tasks from the wiki, feed insights back                             |
| LINT       | Check structure and consistency, propose or carry out changes                            |
| WIKI       | Shape of the wiki as an artifact: format, linking, provenance, entry point, purpose file |
| RUNS       | Traceability: what was done, why, cost — written to `log.md`; approving lint tasks       |
| ACCESS     | Access paths and users, the browser UI included                                          |
| OPS        | Continuous operation and limiting what an agent can reach                                |

## 6. Outcomes

<!-- IDs: OUT-NN, in order of assignment, never renumbered, never reused.
     Order of rows = priority. OUT is not a capability name. -->
| ID     | I can ...                                                                               | Status | Capabilities   | Specs |
| ------ | --------------------------------------------------------------------------------------- | ------ | -------------- | ----- |
| OUT-01 | submit a text and afterwards find new, linked pages including a source page in the wiki | Now    | INGEST,WIKI    |       |
| OUT-02 | see for every run what it did, why it ended and what it cost                            | Next   | RUNS           |       |
| OUT-03 | ask a question and get an answer with references to wiki pages                          | Next   | QUERY          |       |
| OUT-04 | submit a URL instead of a text                                                          | Later  | INGEST,WIKI    |       |
| OUT-05 | have insights from an answer taken over into the wiki                                   | Later  | QUERY,WIKI     |       |
| OUT-06 | have the wiki checked and see the proposals                                             | Later  | LINT           |       |
| OUT-07 | approve or reject proposals; uncritical ones happen by themselves                       | Later  | LINT,RUNS,WIKI |       |
| OUT-08 | let the agent research on the internet when needed                                      | Later  | INGEST,QUERY   |       |
| OUT-09 | run Grimoire permanently on my server                                                   | Later  | OPS            |       |
| OUT-10 | trust that an agent only reaches what I have allowed                                    | Later  | OPS            |       |
| OUT-11 | use Grimoire through a chat program                                                     | Later  | ACCESS         |       |
| OUT-12 | share the wiki with further people                                                      | Later  | ACCESS         |       |
| OUT-13 | monitor the runs incl. detailed logs and metrics over time in a dashboard               | Later  | OPS            |       |
| OUT-14 | have Grimoire help me phrase the wiki's description, while I make the change myself     | Later  | WIKI           |       |

## 7. Later: promotion triggers

- OUT-04/OUT-08: as soon as OUT-01 and OUT-02 have been used regularly for two weeks and pasting text is the bottleneck.
- OUT-06/OUT-07: as soon as the wiki has > 100 pages or I find the first contradiction by hand.
- OUT-09: as soon as I have carried out at least ten ingests.
- OUT-10: before OUT-04, before OUT-08 and before OUT-09 runs unattended. Not earlier.
- OUT-11: as soon as the browser on the go is demonstrably the reason that I submit nothing.
- OUT-12: as soon as a concrete second person wants to use it.
- OUT-14: as soon as I set up a second wiki.

## 8. Open questions

- Who rates the impact of a lint change, and based on what? (blocks OUT-07)
