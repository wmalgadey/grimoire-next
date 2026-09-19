---

description: "Task list template for feature implementation"
---

# Tasks: [FEATURE NAME]

**Input**: Design documents from `/specs/[###-feature-name]/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md,
data-model.md, contracts/

**Outcome advanced**: OUT-NN — [row wording, copied from the spec]

**Budget**: about 40 tasks (Constitution I.7). If the list exceeds it, go back to plan.md and split.

## Format

Implementation task:

`- [ ] T00N [P?] [US?] Description — **Req:** <CAPABILITY>-NNN | Principle <n>`

Test task:

`- [ ] T00N [P?] [US?] Description — **Req:** <CAPABILITY>-NNN | **Level:** Fast\|Contract\|E2E\|Deploy — **Why not lower:** [one line]`

- **[P]**: can run in parallel (different files, no dependencies)
- **[US?]**: the user story this task belongs to (US1, US2, US3)
- **Req** *(mandatory, every task)*: the requirement ID this task serves, or the constitution
  principle it follows (Constitution IV.5). No task without one.
- **Level** *(mandatory, every test task)*: the test's level (Constitution III.3).
- **Why not lower** *(mandatory, every test task)*: one line on why the level below cannot prove
  this requirement (Constitution III.6). "Convenience" is not a reason.
- Include exact file paths in descriptions.

**Levels** — Fast: in-process, state-based, real domain objects, in-memory adapters at owned ports.
Contract: one suite per adapter against the real external thing. E2E: real processes, at most two
scenarios per user story. Deploy: smoke checks on built artifacts, CI only, only
for a deployment outcome, never in the default run.

**Not tested** (Constitution III.8) — do not write tasks for: framework or library behaviour,
argument parsing as such, dependency wiring, static configuration or deployment content, generated
code. Such a test is deleted, not fixed.

**Evals** (Constitution III.10) — a requirement whose proof kind is `eval` gets an eval task in the
separate runner, referencing the same requirement ID. It is never a test task.

<!--
  The tasks below are SAMPLES. /speckit-tasks MUST replace them with real tasks derived from
  spec.md (user stories and their requirement IDs), plan.md, data-model.md and contracts/.
  Group by user story so each story is independently implementable and testable.
-->

## Phase 1: Setup (shared)

- [ ] T001 Create the project structure per plan.md — **Req:** Principle II.6
- [ ] T002 [P] Configure the analyzer the stack already provides — **Req:** Principle II.3

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: only what every user story below needs. Nothing here without a consumer in this
feature (Constitution II.1).

- [ ] T003 Register this feature's requirements in `docs/capabilities/<capability>.md`. This is the
      first implementation task: it comes before any test is written — **Req:** Principle IV.2
- [ ] T004 [Foundational item] — **Req:** <CAPABILITY>-NNN
- [ ] T005 [P] [Foundational item] — **Req:** Principle V.2

**Checkpoint**: foundation ready — user story work can begin.

---

## Phase 3: User Story 1 - [Title] (Priority: P1) 🎯 MVP

**Goal**: [What this story delivers]

**Independent Test**: [How to verify it on its own]

### Tests for User Story 1

> Write these first and see them fail before implementing.

- [ ] T005 [P] [US1] [Test description] in [path] — **Req:** CAP-001 | **Level:** Fast — **Why not lower:** [there is no lower level]
- [ ] T006 [US1] [Test description] in [path] — **Req:** CAP-002 | **Level:** Contract — **Why not lower:** [Fast cannot prove it because the real external thing decides the outcome]

### Implementation for User Story 1

- [ ] T007 [P] [US1] [Implementation task] in [path] — **Req:** CAP-001
- [ ] T008 [US1] [Implementation task] in [path] — **Req:** CAP-002

**Checkpoint**: User Story 1 is fully functional and testable on its own.

---

## Phase 4: User Story 2 - [Title] (Priority: P2)

**Goal**: [What this story delivers]

**Independent Test**: [How to verify it on its own]

### Tests for User Story 2

- [ ] T009 [P] [US2] [Test description] in [path] — **Req:** CAP-003 | **Level:** E2E — **Why not lower:** [only real processes exercise the path this requirement describes]

### Implementation for User Story 2

- [ ] T010 [US2] [Implementation task] in [path] — **Req:** CAP-003

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - [Title] (Priority: P3)

**Goal**: [What this story delivers]

**Independent Test**: [How to verify it on its own]

- [ ] T011 [US3] [Task] in [path] — **Req:** CAP-004

**Checkpoint**: all user stories are independently functional.

---

## Phase 6: Closing the feature

- [ ] T0NN Run the Contract and E2E suites; both must pass, Contract within its budget. The default
      run is Fast only, so this is the one place they are exercised before the PR — **Req:** Principle III.7
- [ ] T0NN Run `trace-check`; it passes — **Req:** Principle IV.3
- [ ] T0NN Regenerate and commit `docs/trace.md`, and set the outcome status and spec reference in
      `docs/product.md` — the only two edits an agent makes to that file — **Req:** Principle IV.4
- [ ] T0NN Walk `docs/review-checklist.md` — **Req:** Principle Gov.2
- [ ] T0NN Reconcile `docs/capabilities/<capability>.md` with this feature's requirements as added,
      changed, or removed; retired ones move under "Retired" keeping their IDs — **Req:** Principle IV.2
- [ ] T0NN Merge this feature's binding decisions into `docs/decisions.md` — **Req:** Principle II.6
- [ ] T0NN The owner reads what each review-proven requirement is about — **Req:** Principle I.9
- [ ] T0NN The owner exercises the outcome once with the real external systems in place, per the
      acceptance run in plan.md and `quickstart.md`. This is the last task of the feature; without
      it the feature is not done — **Req:** Principle I.9

---

## Dependencies & Execution Order

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; blocks all user stories.
- **User stories (Phase 3+)**: depend on Foundational; then parallel or in priority order
  P1 → P2 → P3.
- **Closing (last phase)**: depends on every story that ships in this feature.

### Within each user story

- Tests are written and fail before the implementation.
- Domain before adapters; adapters before the entry points that call them.
- A story is finished before the next priority starts.

### Parallel opportunities

- Tasks marked [P] touch different files and have no dependencies between them.
- Once Foundational completes, user stories can proceed in parallel.

---

## Implementation Strategy

1. Setup → Foundational.
2. User Story 1 → validate independently. This is the MVP.
3. Add stories in priority order, each validated on its own.
4. Closing phase: capability files, `trace-check`, `docs/trace.md`, `docs/product.md`, checklist,
   then the owner's acceptance run as the last task.

## Notes

- Every task names a requirement ID or a principle; every test task also names its level and why the
  level below cannot prove it.
- Commit after each task or logical group.
- A finding from review becomes a test only if it names a violated requirement ID (Governance 3);
  otherwise it becomes the smallest code change that resolves it, or is dropped.
