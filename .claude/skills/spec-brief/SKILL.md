---
name: spec-brief
description: Prepare a one-page feature brief before running /speckit-specify in a Spec-Kit project (Grimoire). Use this whenever the user wants to start a new feature, pick the next slice of an outcome, "write the spec prompt", "prepare the spec", "what should the next feature be", or asks how to get their UX / visual / technical ideas into a spec. Also use it when the user says /spec-brief. The skill finds the slice from docs/product.md + capabilities + trace, interviews the owner for the walkthrough and wishes, writes docs/briefs/SLUG.md, and hands over the exact /speckit-specify and /speckit-plan inputs. Do not run /speckit-specify yourself — this skill ends before it.
---

# spec-brief

Spec-Kit takes a feature description and produces spec → plan → tasks. The weak point is the input: if the owner mixes UX, visual wishes and technical solution into one prompt, the spec absorbs implementation details, `/speckit-clarify` argues about them, and `/speckit-analyze` demands splits nobody understands. This skill produces a **brief** — one page, fixed sections — that separates the three concerns and gives each its own address:

| Concern | Goes to | Why |
|---|---|---|
| UX: flow, states, feedback, failure cases | Spec (walkthrough + acceptance scenarios) | It is behavior — WHAT the user experiences |
| Visual: look, layout, tone | `docs/ux.md` once, per-feature only deltas | Stable across features; repeated in a spec it becomes a mandate |
| Technical solution | `/speckit-plan` input, never the spec | Keeps clarify focused on behavior |

Converse and write the brief in the language the user uses. The section names of the template stay as they are so later commands can reference them.

## Repo layout (defaults — verify, adjust if the repo differs)

- `docs/product.md` — product vision, outcomes `OUT-xx` with status
- `docs/decisions.md` — decisions in force, `DEC-xxx`
- `docs/capabilities/*.md` — registered requirements per capability (`INGEST-003`, `RUNS-002`, …)
- `docs/trace.md` — generated trace: which requirements exist, tested, implemented
- `docs/ux.md` — design principles (create on first use, see step 4)
- `docs/briefs/<slug>.md` — where the brief is written; moved into `specs/NNN-<slug>/brief.md` after `/speckit-specify` created the feature directory
- `.specify/memory/constitution.md`

Do not put the brief directly under `specs/` before `/speckit-specify` runs: the feature-numbering script scans `specs/` and a stray directory can confuse it.

## Workflow

### 1. Find the slice

Read `docs/product.md`, `docs/capabilities/*.md`, `docs/trace.md` and `docs/decisions.md`. Then answer, for the outcome the user named (or the next outcome in `docs/product.md` that is not Done):

- What does the outcome need that no requirement covers yet? (trace + capabilities tell you; this is the point of `docs/trace.md`)
- Which requirements were deferred to a follow-up in earlier specs? (look for "follow-up", "deferred", IDs kept for later)
- What already exists and must not be re-specified?

Propose **2–3 candidate slices**, each as one sentence of user value plus the capabilities it touches. Recommend one and say why (usually: the one that closes the outcome's most visible gap with the fewest new capabilities). Let the user pick or correct. If the user already named the slice, skip the proposal and just confirm scope in one paragraph.

### 2. Interview — only what is missing

Extract everything you can from the conversation and the repo first. Then ask for what is genuinely missing, in this order, one question at a time:

1. **Walkthrough**: "Tell me the story: you open Grimoire, then what, until the moment you got what you wanted?" — you want a first-person narrative, 5–10 sentences. If the user gives a list, rewrite it as narrative and read it back.
2. **Wishes**: what they want to see; accept references ("like a GitHub Actions job log"), sketch photos, ASCII. 3–5 bullets max.
3. **Not in this feature**: what they are tempted to include but won't. This is the split brake — get at least two items.
4. **Technical notes**: solution ideas, spikes done, things forbidden. Only for section 6.

Do not ask about acceptance criteria, edge cases or error handling in detail — `/speckit-clarify` does that. Failure cases that belong to the story ("after 15 minutes the run stops and I see why") go into the walkthrough.

### 3. Size check before writing

The walkthrough is the size gauge:

- More than ~10 sentences, or two separate moments of value → two features. Say so and propose the cut.
- A sentence that starts with "and also…" is usually section 4 material.
- If the walkthrough needs a capability that does not exist and is not the point of the feature → probably out of scope.

Do not apply task counts or story counts as limits; the owner removed those from the constitution on purpose. The judgment is "one path to one moment of value".

### 4. Ensure `docs/ux.md` exists

If missing, create it from `assets/ux-md-template.md` and fill it from what the user said (ask two questions at most: overall feel, and one reference product). It must contain the stance that screen descriptions and wishes in briefs and specs are the owner's concerns, and the implementing agent decides the layout — without this, `/speckit-specify` turns every sketch into a mandated layout.

If it exists, only add a delta when the current feature introduces something the principles don't cover.

### 5. Write the brief

Use `assets/brief-template.md`. Write to `docs/briefs/<slug>.md`, slug = kebab-case, ≤4 words, no number. One page. Rules:

- Section 1 names the outcome ID and the one sentence the owner can say afterwards. Not a feature title.
- Section 2 is the walkthrough verbatim from the interview, cleaned up, first person, present tense.
- Section 3 wishes are wishes. No "must", "exactly", "always". Reference products beat prose; one ASCII wireframe per screen at most.
- Section 4 lists exclusions with the reason "later" or "never" and, where known, the ID kept for later.
- Section 5 references `DEC-xxx` and capability IDs; never restate their content.
- Section 6 is the only place for technology. Mark it clearly as plan input.

Read it back to the user in full before finishing. Fix wording they object to. See `references/example-brief.md` for a filled example.

### 6. Hand-off

End with the two commands the user will run, ready to paste:

```
/speckit-specify Build the spec from docs/briefs/<slug>.md. Sections 1–5 are the input. Section 6 is plan input — do not use it, do not mention its content in the spec. Section 3 items are the owner's wishes, not requirements; docs/ux.md says the implementing agent decides layout. After creating the feature directory, move docs/briefs/<slug>.md to specs/NNN-<slug>/brief.md.
```

```
/speckit-plan Technical context is section 6 of specs/NNN-<slug>/brief.md, plus docs/decisions.md. Constraints there are binding; ideas there are suggestions.
```

Do not run these yourself. The user reviews the brief, then runs them.

## What this skill is not

- Not a spec writer: it never produces user stories, requirement IDs or acceptance scenarios. Those come from `/speckit-specify` and `/speckit-clarify`.
- Not a planner: technical design belongs to `/speckit-plan`. Section 6 is notes, not a design.
- Not a scope police with numbers: no task limits, no story limits.
