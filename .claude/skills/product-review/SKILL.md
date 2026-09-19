---
name: product-review
description: Review docs/product.md section by section against its own rules. Run before
  /speckit-specify, when a feature closes, and whenever docs/product.md was edited. Reports
  findings; never edits docs/product.md.
user-invocable: true
disable-model-invocation: false
---

Read docs/product.md, README.md and the design invariants in .specify/memory/constitution.md.
Answer every question below with yes/no and the offending sentence. Propose wording, but do not
edit docs/product.md; it is owner-written.

## 1. Goal
- Who can do what afterwards that they could not before, in one sentence, without a mechanism?
- Is there exactly one goal? Where "and" joins two benefits: which one would be dropped first?
- Is there a success criterion, and could the owner tell on a given day whether it is met?
- Does every noun survive a complete rewrite of the implementation?
- External standards: pinned version, and scope delegated to a capability?

## 2. User and context
- Who exactly, how many, in which situation, how often?
- What does the user do while a run is in progress, and what afterwards?
- What does the user do themselves, and what never?
- What does Grimoire assume about its environment?
- Classify each sentence: fact about user or environment (stays), agent behaviour (belongs in
  an instruction file), mechanism (belongs in a plan).
- Is each sentence true regardless of any outcome's status?

## 3. Core loop
- Three to five steps, each from the user's perspective?
- Is the last step better because of the first? If not, nothing compounds.
- Can a step be removed with the product still being this product? Then remove it.
- Does a step name a file, a component, or a number?

## 4. Non-goals
- Would an agent plausibly propose this? If not, it is noise.
- Is it truly Never? If a condition exists under which the owner wants it, it is a Later
  outcome with a trigger.
- Does the reason say what happens instead?
- Does a non-goal forbid something a safety mechanism needs (limits, timeouts, isolation)?
- Does a non-goal contradict the goal or an outcome?

## 5. Capabilities
- Is it a domain area a user would recognise, not a technical layer or cross-cutting concern?
- Can three requirements be named that live here and nowhere else?
- Does at least one outcome reference it, and do all requirements of every outcome have a home?
- Would the owner keep this name forever? Requirement IDs are permanent.
- Does a description name files or mechanisms?

## 6. Outcomes
- "I can ...", observable by the user without reading code or logs?
- Loop column filled: which step does it enable, deepen, widen, or protect?
- Deliverable in one or few specs of at most three user stories? If not, split.
- Exactly one Now, and is it the thinnest slice that still shows a result?
- Is the row order the owner's real priority today?
- Does it name an ability, not a mechanism?
- Capabilities column complete: where will its requirements land?
- IDs: zero-padded, none reused, none renumbered?

## 7. Promotion triggers
- Does every Later outcome have one, and does every trigger reference an existing ID?
- Is it an observable event or a count, not a feeling or a date?
- Is it consistent with the usage described in section 2?
- Is every safety outcome triggered before the outcome that creates its risk?

## 8. Open questions
- Does each carry `(blocks OUT-NN)` with an existing ID? A question that blocks nothing goes.
- Does one block the Now outcome? Then it is answered now: the answer moves into the section
  it belongs to and the question is deleted.
- Is it a real unknown, or a decision being avoided?
- Does it contain a solution? Solutions do not belong here.

## Whole document
- At most one page?
- Do references point one way only: sections 6-8 refer to 1-5, never the reverse?
- Technologies named: only those that define the product?
- Present tense only for what holds at the Now outcome?

## Across documents
- Is the content boundary (what Grimoire writes, what the agent writes, what the user decides)
  stated identically in the non-goal, the README roles paragraph, and design invariant 1?
- Does README.md contain status, roadmap, or features beyond the three operations?
- Does any capability or outcome ID in README.md or the triggers not exist in the tables?
