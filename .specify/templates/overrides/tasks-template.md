---
description: "Task list template for feature implementation"
---

# Tasks: [FEATURE NAME]

**Input**: Design documents from `/specs/[###-feature-name]/`

**Prerequisites**: plan.md (required), spec.md (required for user stories and requirement IDs)

**Outcome advanced**: [copy from spec.md]

**Budget (I.3)**: at most 3 user stories and 40 tasks. If the list below would exceed 40, stop
and split the feature in plan.md first.

## Format

`- [ ] T### [P?] [Story] [CAP-NNN] Description`

- **T###** — task id, sequential
- **[P]** — may run in parallel (different files, no dependency on another unfinished task)
- **[Story]** — US1 / US2 / US3, or SETUP / FOUND for shared work
- **[CAP-NNN]** — the requirement ID this task serves. Every task carries either a requirement
  ID or a named principle (e.g. `[V.2]`); a task with neither fails tasks review.

### Test tasks carry two extra fields (III.3)

A test task is written as:

```text
- [ ] T012 [P] [US1] [INGEST-004] <what the test proves> in tests/<path>
      Level: Fast | Contract | E2E | Deploy
      Why not lower: <one line — what the level below cannot reach>
```

Either field left empty fails tasks review. `Why not lower:` on a Fast task reads `—`.

## Path Conventions

Paths follow the structure decision in plan.md: `src/<capability>/` for the bounded context,
`tests/<capability>/<Level>/` for its suites. Every path below is concrete, not a placeholder.

---

## Phase 1: Setup

**Purpose**: only what a task in this feature consumes (II.1). No scaffolding for later.

- [ ] T001 [SETUP] [CAP-NNN] [concrete setup step]
- [ ] T002 [P] [SETUP] [II.1] [concrete setup step, naming its consumer task]

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: work every user story depends on. Keep this phase as small as it can be — anything
only one story needs belongs to that story.

- [ ] T003 [FOUND] [CAP-NNN] [shared type or port]
- [ ] T004 [FOUND] [V.2] [adapter, confined to this capability]

**Checkpoint**: user story work can begin.

---

## Phase 3: User Story 1 - [Title] (Priority: P1) 🎯 MVP

**Goal**: [the user-observable result this story delivers]

**Independent Test**: [how this story is verified on its own]

### Tests for User Story 1

- [ ] T005 [P] [US1] [CAP-NNN] [what it proves] in tests/[capability]/Fast/[name]
      Level: Fast
      Why not lower: —
- [ ] T006 [P] [US1] [CAP-NNN] [what it proves] in tests/[capability]/Contract/[name]
      Level: Contract
      Why not lower: [what the in-memory adapter cannot reach]

### Implementation for User Story 1

- [ ] T007 [P] [US1] [CAP-NNN] [concrete change] in src/[capability]/[file]
- [ ] T008 [US1] [CAP-NNN] [concrete change] in src/[capability]/[file] (depends on T007)

**Checkpoint**: User Story 1 is functional and testable on its own.

---

## Phase 4: User Story 2 - [Title] (Priority: P2)

**Goal**: [the user-observable result this story delivers]

**Independent Test**: [how this story is verified on its own]

### Tests for User Story 2

- [ ] T009 [P] [US2] [CAP-NNN] [what it proves] in tests/[capability]/Fast/[name]
      Level: Fast
      Why not lower: —

### Implementation for User Story 2

- [ ] T010 [P] [US2] [CAP-NNN] [concrete change] in src/[capability]/[file]

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - [Title] (Priority: P3)

**Goal**: [the user-observable result this story delivers]

**Independent Test**: [how this story is verified on its own]

### Tests for User Story 3

- [ ] T011 [P] [US3] [CAP-NNN] [what it proves] in tests/[capability]/Fast/[name]
      Level: Fast
      Why not lower: —

### Implementation for User Story 3

- [ ] T012 [P] [US3] [CAP-NNN] [concrete change] in src/[capability]/[file]

**Checkpoint**: all user stories are independently functional.

---

## Phase 6: Close the feature

<!-- I.4 and IV.2/IV.4: a feature is not done until these land in the same merge. -->

- [ ] T013 [CLOSE] [IV.2] Merge this feature's requirements into
      `docs/capabilities/[capability].md` — added, changed and removed
- [ ] T014 [CLOSE] [IV.4] Update the outcome status in `docs/product.md`
- [ ] T015 [CLOSE] [IV.3] `trace-check` is green and `docs/trace.md` is regenerated
- [ ] T016 [CLOSE] [III.4] Fast suite ≤ 15 s, Contract ≤ 90 s

---

## Dependencies

- Phase 2 blocks Phases 3–5.
- Within a story, its test tasks precede the implementation tasks they constrain.
- Phase 6 requires every preceding phase.

## Out of scope (from spec.md)

<!-- Restated so a task cannot quietly reintroduce it (II.1). -->

- [item from the spec's Out of scope section]
