# Phase 1 Data Model: The First Ingest

**Feature**: `001-first-ingest` · **Date**: 2026-09-20

Entities come from the spec's "Key Entities". Validation rules cite the requirement that imposes
them; nothing here adds behaviour (Constitution IV.6).

Scope note: the split holds back RUNS-002, RUNS-003, RUNS-004 and ACCESS-003, so there is no queue,
no acknowledgement and no surviving a restart in this feature. Fields that exist only to serve those
requirements are **not** modelled here — they arrive with the follow-up feature.

---

## Grimoire's own state (in memory, owned by the RUNS context)

There is no store, no port and no database. `SubmissionBoard` is a plain object holding the
submissions and their states for as long as the process runs; the Fast tests use the real object
(research.md R-08). A stop loses everything below — the spec says so, and the owner accepted it
until RUNS-004.

### Submission

A text the user handed to Grimoire, together with its state.

| Field | Type | Rules |
| --- | --- | --- |
| `Id` | GUID | Assigned by Grimoire on acceptance |
| `Text` | string | Non-empty after trimming — a text that is empty or only whitespace is refused and stored not at all (INGEST-004) |
| `SubmittedAt` | UTC instant | Assigned on acceptance; what the browser lists it by |
| `State` | `SubmissionState` | Exactly one at a time (RUNS-001) |

A refused submission is not a Submission: INGEST-003, INGEST-004 and INGEST-005 refuse before
anything is stored, so no state ever describes a refusal — including the refusal of a submission
made while a run is in progress.

### SubmissionState

`Submitted` · `Running` · `Done` · `Failed` — the four the spec names, no more (RUNS-001).

Transitions in this feature:

```
Submitted ──dispatch──▶ Running ──┬── agent stopped, both ceilings clear, log entry present ──▶ Done
                                  │                                                   (RUNS-005)
                                  ├── agent stopped inside both ceilings, no log entry ──▶ nudged once,
                                  │   still Running; entry present at the next stop ─────▶ Done
                                  │                                                   (RUNS-005)
                                  └── every other ending ─────────────────────────────▶ Failed
                                                                            (RUNS-005, GUARD-004)
```

`Done` and `Failed` are terminal. There is no transition out of `Failed` in this feature —
acknowledgement is RUNS-003, held back by the split. `Submitted` is momentary: a submission is
accepted only when no run is in progress (INGEST-005), so it is dispatched at once.

### Run

One attempt to work one submission into the wiki.

| Field | Type | Rules |
| --- | --- | --- |
| `Id` | GUID | Handed to the agent as part of what a run is given (INGEST-002); names the run in the generation record (WIKI-002) and in the log entry (WIKI-001) |
| `SubmissionId` | GUID | Exactly one submission per run |
| `StartedAt` | UTC instant | Read from `TimeProvider`; start of the elapsed-time ceiling (research.md R-12) |
| `Grant` | `ToolGrant` | Recorded for every run (GUARD-003) |
| `ElapsedCeiling` | duration | Fixed value, not a setting (GUARD-004) |
| `TokenCeiling` | token count | Fixed value; cost counted in model tokens, not currency (GUARD-004) |
| `TokensUsed` | token count | Running total during the run, from the streamed `usage`; the `result` message's authoritative count at the end (R-04) |
| `Outcome` | `Done` \| `Failed` | How it ended |
| `LogEntryNudged` | bool | Whether Grimoire has already told the agent once that the log entry is missing — the "once" in RUNS-005 |

`LogEntryNudged` exists solely to make RUNS-005's single nudge decidable; it is consumed in this
feature (II.1).

### ToolGrant

The list of tools a given run may use, recorded with the run (GUARD-003).

| Field | Type | Rules |
| --- | --- | --- |
| `ToolNames` | ordered set of string | For an ingest run: read anything in the wiki, create and change pages, indexes and the log — and nothing else. Deleting and moving are not in it (GUARD-002) |
| `RecordedAt` | UTC instant | Recorded when the run is dispatched |

The grant is what the MCP endpoint serves, and it is also the agent's *entire* tool surface: the
run is started with every built-in tool switched off, so a tool absent from the grant does not exist
for that run (GUARD-001; research.md R-03, R-11).

---

## Grimoire's own files (not the wiki, not written by any agent)

### Purpose description

The hand-written description of what the wiki is for. Belongs to Grimoire; every run receives it;
Grimoire neither creates nor changes it (INGEST-002). Its absence refuses every submission
(INGEST-003).

### Instruction

The versioned text every run receives alongside the purpose description, shipping with Grimoire in
its repository. It is where the wiki's shape is demanded of the agent (WIKI-001), and it is not a
wiki page. Changing it is an owner decision named in the PR (Constitution V.1).

Its provisions, from WIKI-001: a source page for the submitted text; a `type` on every page; named
sources referenced where a statement relies on them; links between pages; every page in exactly one
section, sections one level deep; a current index per section; a current root index declaring the
pinned OKF version and listing the sections; a log entry per run that identifies the run and says
what changed and why.

Together with the purpose description, this is the only text that reaches the agent's prompt. The
run is started with no settings loaded from the machine and in a working directory Grimoire owns, so
nothing in the wiki or on the host can add to it (Constitution V.1; research.md R-11).

---

## The wiki (written by the agent; Grimoire touches only what WIKI-002 names)

Field names are OKF 0.2 as pinned in `docs/product.md`; see research.md R-07 for which parts apply.

### Page

| Frontmatter | Written by | Rules |
| --- | --- | --- |
| `type` | agent | Grimoire neither supplies nor checks it (WIKI-001; RUNS-005 reads nothing but the log) |
| `sources` | agent | list; each entry requires `resource` |
| `generated` | **Grimoire** | `{ by, at }`. Grimoire writes both and replaces whatever the agent supplied. On an update the record names the updating run (WIKI-002) |
| everything else | agent | Opaque to Grimoire |

Body: the agent's, entirely. Grimoire does not place the submitted text into any page — that is the
agent's decision (spec Clarifications, 2026-09-20).

**The two write preconditions of WIKI-002**, and the only judgments Grimoire makes about a page:

| On write | Grimoire does |
| --- | --- |
| Frontmatter parses, no `generated` key | Adds the key; the write succeeds |
| Frontmatter parses, `generated` present in any form | Replaces it with Grimoire's values |
| Frontmatter does not parse, or `generated` is not a mapping | Write **fails**; the agent is told why |

Nothing else about the page is judged.

### Source page

The page the agent creates for a submitted text and that other pages name as their source. A Page
in every respect — Grimoire gives it no special handling and does not know which page it is.

### Section

One directory of pages, exactly one level deep, carrying an `index.md`. The agent decides which
sections exist.

### Root index

The wiki's `index.md`, declaring `okf_version: "0.2"` and listing the sections. Written by the
agent.

### Log

`log.md` — the wiki's record of what each run changed and why. Written by the agent.

**The one thing Grimoire reads in the wiki**: whether `log.md` holds an entry identifying a given
run. That single fact decides the run's state (RUNS-005). Nothing else in the wiki is read to
decide anything.

Indexes and the log are not pages: they carry no `type`, no `sources` and no `generated` record.

---

## Ownership summary

| Artifact | Written by | What Grimoire adds |
| --- | --- | --- |
| Source page, and any other page a run writes | agent | `generated: { by, at }` (WIKI-002) |
| Section index, root index, log entry | agent | nothing |
| Purpose description | user | nothing |
| Instruction | owner, versioned in Grimoire's repository | nothing |
| Submission, Run, ToolGrant | Grimoire | — (in memory, not in the wiki) |

A run that ends failed leaves everything it had already written in place; Grimoire removes, reverts
and commits none of it (WIKI-003). With RUNS-003 held back, the next run may start on top of what a
failed run left — stated under Assumptions in the spec and accepted until the follow-up feature.
