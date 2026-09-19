# Implementation Plan: [FEATURE]

**Branch**: `[###-feature-name]` | **Date**: [DATE] | **Spec**: [link]

**Input**: Feature specification from `/specs/[###-feature-name]/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the
execution workflow.

## Summary

[Primary requirement from the spec + the approach in two or three sentences]

**Outcome advanced**: OUT-NN — [row wording, copied from the spec]

**Slice addition**: [exactly one of: a new operation | a new user interaction | a new external
system]. [What it is.] *(Constitution I.6 — never two; the skeleton feature is exempt from
"never two", not from the budget.)*

## Technology decisions *(mandatory)*

<!--
  REQUIRED (Constitution II.6). Technology choices are made here and recorded here. A later plan
  that departs from an earlier decision states why. The FIRST feature's plan must additionally
  decide the five skeleton items marked ★, which are what the two gates rest on.
-->

Read `docs/decisions.md` first; list here only what this plan decides or departs from.

| Decision | Choice | Reason | Binds later features (yes / no) | Departs from (DEC-NNN, or none) |
| --- | --- | --- | --- | --- |
| ★ Stack | [choice] | [reason] | yes \| no | none |
| ★ How a test carries its level | [mechanism] | [reason] | yes \| no | none |
| ★ How a test carries its requirement ID | [mechanism] | [reason] | yes \| no | none |
| ★ How `trace-check` reads the level and the requirement ID off a test | [mechanism] | [reason] | yes \| no | none |
| ★ How `time-budget` is enforced and fails the run | [simplest means the stack offers] | [reason] | yes \| no | none |
| [Other decision] | [choice] | [reason] | yes \| no | [DEC-NNN, or none] |

A reason names the constraint or evidence. "Owner decision" is not a reason — name the owner's
constraint. A decision binds later features when a later plan would have to know it in order not to
contradict it.

**Test time budget** *(Constitution III.7, gate `time-budget`)*: Fast under 15 s, Contract under
90 s, execution only, measured in CI. The default test run executes Fast only and fails when Fast
exceeds its budget; the Contract run fails when it exceeds its own.
Enforced by: [means]. No purpose-built tooling.

## Technical Context

**Storage**: [if applicable, or N/A]

**Target Platform**: [e.g. self-hosted single instance, or NEEDS CLARIFICATION]

**Project Type**: [e.g. service with a browser front end, or NEEDS CLARIFICATION]

**Constraints**: [domain-specific, or N/A]

**Scale/Scope**: [domain-specific, or N/A]

## Constitution Check *(mandatory)*

*Complete before design, re-check after design. One line per principle, "touched" or
"not touched". A cross-cutting concern binds this feature only where it is touched
(Constitution II.5).*

| Principle | Touched? | How this plan satisfies it / why it is not touched |
| --- | --- | --- |
| I. Purpose and Focus | touched \| not touched | [one line] |
| II. Simplicity | touched \| not touched | [one line] |
| III. Testing | touched \| not touched | [one line] |
| IV. Visibility | touched \| not touched | [one line] |
| V. Design Invariants | touched \| not touched | [one line] |
| Governance | touched \| not touched | [one line] |

## Budget and split decision *(mandatory)*

<!--
  REQUIRED (Constitution I.7). Budget: 3 user stories, about 40 tasks. A plan over budget is split
  BEFORE /speckit-tasks.
-->

**User stories**: [n] of 3 · **Estimated tasks**: [n] of ~40

**Within budget?** [Yes → proceed to /speckit-tasks] \| [No → split, described below]

**Split** *(fill only if over budget)*: This feature keeps [scope]; [scope] moves to [follow-up
feature / a Later outcome proposed to the owner]. The cut runs along [boundary], so each half still
has a user-observable result.

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created by /speckit-plan)
```

### Source Code (repository root)

<!--
  Replace with the concrete layout. Bounded contexts declare their own ports and own their adapters;
  an external system is referenced only inside its adapter (Constitution V.2) — the tree must make
  that visible.
-->

```text
[real directories, one line each]
```

**Structure Decision**: [The chosen layout, and where each port and adapter lives]

## Quickstart — the owner's acceptance run *(mandatory)*

<!--
  REQUIRED (Constitution I.9). A feature is done when merged to main with its capability files
  updated AND after the owner has exercised its outcome once with the real external systems in
  place. Describe that run here, and carry it into quickstart.md: what the owner starts, which
  real external systems must be reachable, the one thing they do, and what they must see for the
  outcome to count as exercised. No stand-ins for the external systems.
-->

**Outcome exercised**: OUT-NN — [row wording, copied from the spec]

**Real external systems in place**: [each one the run touches, or "none — the outcome touches none"]

**Steps the owner runs**: [what they start, then the one thing they do]

**What the owner must see**: [the observable result that means the outcome was exercised]

## Complexity Tracking

> Fill ONLY if the Constitution Check has violations that must be justified. A rule that blocks
> needed work is amended first, not set aside (Governance 1) — an entry here is a stopgap, not an
> exception.

| Violation | Why needed | Simpler alternative rejected because |
| --- | --- | --- |
| [violation] | [current need] | [why the simpler path does not work] |
