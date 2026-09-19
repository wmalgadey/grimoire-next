# Feature Specification: [FEATURE NAME]

**Feature Branch**: `[###-feature-name]`

**Created**: [DATE]

**Status**: Draft

**Input**: User description: "$ARGUMENTS"

## Outcome advanced (OUT-NN) *(mandatory)*

<!--
  REQUIRED. Exactly one outcome ID from docs/product.md §6, with the row's wording.
  A feature may touch several capabilities, but it advances ONE outcome (Constitution I.3).
  If this work advances no outcome, do not write a spec: propose it to the owner as a Later
  outcome together with the trigger that would promote it, and stop here (Constitution I.4).
-->

**Outcome**: OUT-NN — [row wording from docs/product.md]

**Capabilities touched**: [CAP, CAP, ...]

## Blocking open questions (none, or stop) *(mandatory)*

<!--
  REQUIRED. Check docs/product.md §8. If any open question there blocks THIS outcome, write it
  here and stop — no spec is started (Constitution I.5). Otherwise write "None".
-->

**Blocking**: None

## Out of scope *(mandatory)*

<!--
  REQUIRED. What this feature deliberately does not do, and where it went instead (a Later
  outcome, a follow-up feature, a non-goal in docs/product.md §4).
-->

- [Excluded thing] → [where it went]

## User Scenarios & Testing *(mandatory)*

<!--
  Budget: at most 3 user stories (Constitution I.7). Each story is independently testable and
  delivers value on its own. Priorities P1, P2, P3 — P1 is the MVP slice.
-->

### User Story 1 - [Brief Title] (Priority: P1)

[Describe this user journey in plain language]

**Why this priority**: [Value, and why it ranks here]

**Independent Test**: [How this can be verified on its own]

**Acceptance Scenarios**:

1. **Given** [initial state], **When** [action], **Then** [expected outcome]
2. **Given** [initial state], **When** [action], **Then** [expected outcome]

---

### User Story 2 - [Brief Title] (Priority: P2)

[Describe this user journey in plain language]

**Why this priority**: [Value, and why it ranks here]

**Independent Test**: [How this can be verified on its own]

**Acceptance Scenarios**:

1. **Given** [initial state], **When** [action], **Then** [expected outcome]

---

### User Story 3 - [Brief Title] (Priority: P3)

[Describe this user journey in plain language]

**Why this priority**: [Value, and why it ranks here]

**Independent Test**: [How this can be verified on its own]

**Acceptance Scenarios**:

1. **Given** [initial state], **When** [action], **Then** [expected outcome]

---

### Edge Cases

<!--
  REQUIRED (Constitution IV.6). Every edge case names the requirement ID that carries its expected
  behaviour. An edge case adds no behaviour of its own: if there is no ID for it, add the
  requirement first. A case without a requirement ID is not allowed.
-->

| Case | Expected behaviour | Requirement ID |
| --- | --- | --- |
| [boundary condition] | [what the system does] | CAP-00N |
| [error scenario] | [what the system does] | CAP-00N |

## Requirements *(mandatory)*

<!--
  REQUIRED per requirement: a capability-scoped ID and a proof kind.

  ID       <CAPABILITY>-NNN, capability name from docs/product.md §5. Stable, never reused,
           never feature-local. A new capability is an owner decision (Constitution IV.1).
           `OUT` is reserved and is not a capability name.
  Proof    exactly one of: test | eval | review (Constitution III.1).
             test   — proven by at least one test carrying this ID; trace-check enforces it.
             eval   — agent judgment; proven by an eval with a threshold in the separate runner.
             review — proven by an item in docs/review-checklist.md. ALLOWED ONLY where neither a
                      test nor an eval can prove the requirement. Every requirement whose proof is
                      `review` gets one line under "Why review" below saying why both are
                      impossible. No line, no `review`.

  Mark anything undecided as [NEEDS CLARIFICATION: question] and run /speckit-clarify.
-->

### Functional Requirements

System behaviour only. Everything asked of the agent is one requirement on the instruction, proof
`review`. Whether the agent does it is proof `eval`.

| ID | Requirement | Proof |
| --- | --- | --- |
| CAP-001 | System MUST [specific, observable capability] | test |
| CAP-002 | Users MUST be able to [key interaction] | test |
| CAP-003 | The agent MUST [judgment-bearing behaviour] | eval |
| CAP-004 | [Structural or design property] | review |

### Why review *(one line per `review` requirement; omit only if there are none)*

<!--
  REQUIRED wherever a requirement above is proven by `review` (Constitution III.1). State why a
  test cannot prove it AND why an eval cannot either. "Hard to test" is not a reason.
-->

| ID | Why neither a test nor an eval can prove it |
| --- | --- |
| CAP-004 | [why a test cannot reach it, and why an eval cannot judge it] |

### Retired in this feature *(omit if none)*

| ID | Was | Why retired |
| --- | --- | --- |
| CAP-0NN | [prior requirement text] | [reason] |

### Key Entities *(include if feature involves data)*

- **[Entity 1]**: [What it represents, key attributes, no implementation]
- **[Entity 2]**: [What it represents, relationships to other entities]

## Who writes what *(mandatory whenever the feature touches anything in the wiki)*

<!--
  REQUIRED wherever this feature touches the wiki (Constitution V.1). One row per artifact. Grimoire
  decides no wiki content; into the wiki it writes only facts about the run. If the last column says
  anything beyond that, the feature violates V.1.
-->

user = the person using this wiki; owner = whoever ships Grimoire.

| Artifact | Written by (agent / Grimoire / user / owner) | What Grimoire adds, if anything |
| --- | --- | --- |
| [artifact] | [agent \| Grimoire \| user \| owner] | [facts about the run, or nothing] |

## Lifecycle questions *(mandatory)*

<!--
  REQUIRED. Four fixed rows, no more and no fewer. Each is answered with a requirement ID, or with
  "not applicable, because ..." — never left blank and never answered with new behaviour that has
  no ID (Constitution IV.6).
-->

| Question | Answer (requirement ID, or "not applicable, because ...") |
| --- | --- |
| Stopping and starting again | [CAP-00N \| not applicable, because ...] |
| A run or operation ending partway | [CAP-00N \| not applicable, because ...] |
| Concurrent use | [CAP-00N \| not applicable, because ...] |
| A missing input | [CAP-00N \| not applicable, because ...] |

## Success Criteria *(mandatory)*

<!--
  REQUIRED (Constitution IV.6). A success criterion restates requirements that already exist; it
  adds no behaviour of its own. Each one names the requirement IDs it restates.
-->

### Measurable Outcomes

- **SC-001**: [Measurable, technology-agnostic outcome] — **Restates:** CAP-00N[, CAP-00N]
- **SC-002**: [Measurable, technology-agnostic outcome] — **Restates:** CAP-00N[, CAP-00N]

## Assumptions

- [Assumption about users, scope, data, or environment]
- [Dependency on an existing part of the system]
