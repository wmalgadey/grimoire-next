# Specification Quality Checklist: The Ingest Queue

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-09-23
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Project-specific gates (Grimoire constitution)

- [x] I.3 — exactly one outcome ID named (OUT-01), with the row's wording from `docs/product.md` §6
- [x] I.5 — no open question in `docs/product.md` §8 blocks OUT-01
- [x] I.6 — one vertical slice adding exactly one new user interaction (acknowledging a failed run);
      no new operation, no new external system. RUNS-006 reuses GUARD-004's stop; ACCESS-004 adds to
      a list the browser already draws
- [x] I.7 — three user stories
- [x] III.1 — every requirement declares a proof kind; all four are `test`, so no "Why review"
      section is required and none is present
- [x] IV.1 — IDs are `<CAPABILITY>-NNN`; four taken over unchanged from `001-first-ingest` and two
      newly allocated at the next free number in their capability; nothing renumbered, nothing reused
- [x] IV.2 — the retired requirement (INGEST-005) keeps its ID and is recorded under "Retired in this
      feature"; the changed one (INGEST-001) is recorded under "Changed in this feature"; the two new
      ones (RUNS-006, ACCESS-004) are registered in the requirements table
- [x] IV.6 — every edge case, success criterion and assumption names requirement IDs, or says
      explicitly that it adds no behaviour
- [x] Four lifecycle rows, each answered with requirement IDs

## Notes

- Verified against the four requirements as `001-first-ingest` wrote them in its "Moved to the
  follow-up feature" table: wording is character-for-character unchanged for RUNS-002, RUNS-003,
  RUNS-004 and ACCESS-003.
- The "Who writes what" section is kept although this feature writes nothing into the wiki, so that
  the record says so explicitly rather than by omission.
- Success criteria numbering continues at SC-009 because `001-first-ingest` ended at SC-008 and
  stated that its dropped numbers (SC-006, SC-008) are not reused.
- All items passed on the first validation pass, before clarification.

### Re-validated after the clarify session of 2026-09-23

- Three questions asked and four answers accepted. Two requirements were added as a result —
  RUNS-006 (a stop takes the run's agent with it) and ACCESS-004 (the browser shows the opening of a
  submitted text and when it was made). Both are owner decisions recorded under Clarifications.
- Re-checked the three items most exposed by those additions and all three still pass: no
  implementation detail leaked (RUNS-006 names no process mechanism, ACCESS-004 fixes no length),
  every requirement still declares `test`, and every new edge case names requirement IDs.
- Re-checked that nothing now contradicts ACCESS-002: the run identifier the acknowledgement names is
  held by the browser and not shown, and what ACCESS-004 shows is about the submission. Three places
  that said "nothing further" were reworded so the spec does not contradict itself — Story 1's third
  scenario, the waiting-submission edge case, and SC-013.
- All items still pass. No item regressed.

### Re-validated after the owner's review of the plan, 2026-09-23

- Three changes, none of them adding a requirement ID: the acknowledgement addresses the
  **submission** (so no run identifier reaches the browser and `001-first-ingest`'s HTTP contract is
  left as written); ACCESS-004 was **checked** rather than assumed and is registered with proof
  `test`; and RUNS-006 was **widened** to record a run's agent process and terminate it at start-up.
- Re-checked IV.6 on the widening: every new behaviour it brings carries RUNS-006 — the recording,
  the termination, its ordering before the run reads failed and before anything starts, and the
  refusal to terminate a process that is no longer that run's agent. Three edge cases and SC-014
  were updated to match; one Assumption was withdrawn and two written.
- Re-checked III.1 on the widening: still proof `test`. It gains a Contract test against a real
  process, which needs no sign-in and runs in CI.
- Re-checked that ACCESS-002 is now untouched in every direction: nothing about a run reaches the
  browser at all. The paragraph that had argued an identifier is "carried but not shown" is gone.
- All items still pass. No item regressed. Six requirements, six proofs.
