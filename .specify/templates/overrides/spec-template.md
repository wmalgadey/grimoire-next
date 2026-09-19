# Feature Specification: [FEATURE NAME]

**Feature Branch**: `[###-feature-name]`

**Created**: [DATE]

**Status**: Draft

**Input**: User description: "$ARGUMENTS"

## Outcome advanced (docs/product.md) *(mandatory)*

<!--
  Constitution I.2: name EXACTLY ONE outcome from docs/product.md that this feature advances,
  quoted verbatim, with its status. Empty or naming more than one fails spec review.
  If no outcome fits, do not write this spec: append the work to Later in docs/product.md
  together with the trigger that would promote it, and stop.
-->

**Outcome**: [verbatim outcome line from docs/product.md]

**Status in docs/product.md**: [Now | Next]

**Why this feature advances it**: [one or two sentences]

## Out of scope *(mandatory)*

<!--
  Constitution II.1 and II.5. Name what this feature deliberately does not build.
  Anything listed here must not appear in plan.md or tasks.md.
-->

- [thing not built, and where it lives instead — a Later entry, another capability, or nowhere]
- [cross-cutting concern this feature does not touch, e.g. "deployment: not touched (II.5)"]

## User Scenarios & Testing *(mandatory)*

<!--
  Constitution I.3: at most THREE user stories. Each is independently testable — implementing
  just one still delivers a user-observable result. Priorities P1..P3, P1 most critical.
  A feature needing a fourth story is two features; split it now, not after /speckit-tasks.
-->

### User Story 1 - [Brief Title] (Priority: P1)

[Describe this user journey in plain language]

**Why this priority**: [the value it delivers and why it comes first]

**Independent Test**: [how this is verified on its own, and what result the user observes]

**Acceptance Scenarios**:

1. **[CAP-NNN]** **Given** [initial state], **When** [action], **Then** [expected outcome]
2. **[CAP-NNN]** **Given** [initial state], **When** [action], **Then** [expected outcome]

---

### User Story 2 - [Brief Title] (Priority: P2)

[Describe this user journey in plain language]

**Why this priority**: [the value it delivers]

**Independent Test**: [how this is verified on its own]

**Acceptance Scenarios**:

1. **[CAP-NNN]** **Given** [initial state], **When** [action], **Then** [expected outcome]

---

### User Story 3 - [Brief Title] (Priority: P3)

[Describe this user journey in plain language]

**Why this priority**: [the value it delivers]

**Independent Test**: [how this is verified on its own]

**Acceptance Scenarios**:

1. **[CAP-NNN]** **Given** [initial state], **When** [action], **Then** [expected outcome]

---

### Edge Cases

- **[CAP-NNN]** What happens when [boundary condition]?
- **[CAP-NNN]** How does the system handle [error scenario]?

## Requirements *(mandatory)*

<!--
  Constitution IV.1: requirement IDs are capability-scoped, stable and NEVER reused —
  <CAPABILITY>-NNN, e.g. INGEST-004. Feature-local FR-001 numbering is not used.
  The capability is the bounded context whose docs/capabilities/<capability>.md this
  feature will update on close (IV.2). Continue that file's numbering; never restart at 001.
  Every ID below must end up with at least one test (III.1) or trace-check fails.
-->

**Capability**: [capability name] → `docs/capabilities/[capability].md`

**Highest ID currently in that file**: [CAP-NNN]

### Functional Requirements

- **[CAP-NNN]**: [System MUST ... — one capability, observable from outside]
- **[CAP-NNN]**: [System MUST ...]
- **[CAP-NNN]**: [Users MUST be able to ...]

*Marking an unclear requirement:*

- **[CAP-NNN]**: System MUST [NEEDS CLARIFICATION: what is undecided and why it matters]

### Changed or removed requirements

<!-- IV.2: a change record names what it changes in the capability file, not only what it adds. -->

- **[CAP-NNN]** (changed): [old behaviour] → [new behaviour]
- **[CAP-NNN]** (removed): [why it no longer holds; its tests are deleted, not weakened]

### Key Entities *(include if the feature involves data)*

- **[Entity]**: [what it represents, key attributes, no implementation detail]

## Success Criteria *(mandatory)*

<!--
  Technology-agnostic and measurable. Each SC carries a capability-scoped ID and gets at
  least one test (III.1). A criterion that depends on agent judgment is measured by an eval
  (III.7), not by a test — mark it "(eval)".
-->

- **[CAP-NNN]**: [measurable outcome, e.g. "a run of 50 pages completes in under 2 minutes"]
- **[CAP-NNN]** (eval): [judgment-dependent outcome with its threshold]

## Assumptions

- [assumption about users, scope, data or environment made where the description was silent]
