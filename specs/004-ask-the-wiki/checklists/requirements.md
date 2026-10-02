# Specification Quality Checklist: Ask the Wiki

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-09-27
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

## Grimoire-specific (template override)

- [x] Exactly one outcome ID named, with the row's wording from `docs/product.md` §7 (OUT-03, Now)
- [x] Blocking open questions checked against `docs/product.md` §9 — none blocks OUT-03
- [x] Every requirement carries a capability-scoped ID and exactly one proof kind
- [x] Every `review` requirement has a line under "Why review" saying why neither a test nor an
      eval can prove it (QUERY-004)
- [x] Every edge case names the requirement ID that carries its behaviour
- [x] Every success criterion names the requirement IDs it restates and adds no behaviour
- [x] Four lifecycle rows, each answered with a requirement ID
- [x] "Who writes what" present — the feature touches the wiki by reading it
- [x] Requirements whose wording changes keep their IDs, with the change recorded (RUNS-005,
      RUNS-007, RUNS-008, RUNS-009)
- [x] No new capability invented — QUERY is already named in `docs/product.md` §6
- [x] Section 6 of the brief (plan input) is not used and its content is not mentioned

## Notes

- Validation run 2026-09-27: all items pass on the first iteration.
- Re-validated 2026-09-27 after `/speckit-clarify`: 16/16 → 16/16, no state changed. Four
  clarifications were integrated into ACCESS-007, ACCESS-008, ACCESS-009, QUERY-005 and the
  assumptions; no requirement ID was added and none was renumbered.
- The clarify session asked about a run whose reader left, a lost connection, two browser tabs and
  where the answer ends. Some of those topics also appear in the brief's section 6 as open
  questions for the plan; the answers here are the owner's, given in the clarify session, and no
  content of section 6 was read into the spec — no mechanism, no start-up input and no protocol is
  named anywhere in it.
- Three judgment calls were resolved as documented assumptions rather than
  `[NEEDS CLARIFICATION]` markers, because the brief's sections 1–5 settle them by implication:
  questions do not appear in the submissions list (Clarifications, specify session); a failed
  question uses the acknowledgement that already exists (ACCESS-003); layout of the chat and the
  navigation is the implementing agent's, per `docs/ux.md`.
- Requirement IDs become permanent when they are registered in `docs/capabilities/`, which happens
  before this feature's first test (Constitution IV.2). Until then QUERY-001…006, GUARD-005 and
  ACCESS-007…010 may still change.
