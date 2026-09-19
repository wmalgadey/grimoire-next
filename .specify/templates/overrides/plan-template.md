# Implementation Plan: [FEATURE]

**Branch**: `[###-feature-name]` | **Date**: [DATE] | **Spec**: [link]

**Input**: Feature specification from `/specs/[###-feature-name]/spec.md`

**Outcome advanced**: [copy from spec.md — the one outcome from docs/product.md]

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the
execution workflow.

## Summary

[The requirement this feature satisfies plus the technical approach, in a few sentences.]

## Technical Context

<!-- Only the fields that apply; delete the rest. -->

**Language/Version**: [e.g. C# / .NET 10, TypeScript 5.x or NEEDS CLARIFICATION]

**Primary Dependencies**: [e.g. ASP.NET Core, SvelteKit or NEEDS CLARIFICATION]

**Storage**: [git wiki repository, operational-state store, or N/A]

**Bounded context**: [the capability this feature belongs to — must match spec.md]

**Target Platform**: [e.g. Linux container on the deploy host or N/A]

**Constraints**: [domain-specific, e.g. single-operator, offline-capable, or N/A]

## Constitution Check

*GATE: must pass before Phase 0 research; re-check after Phase 1 design. Every principle gets
exactly one line: "touched" or "not touched" (II.5). A touched principle states how this plan
satisfies it. An untouched principle imposes no obligation on this feature.*

| Principle | Touched? | How this plan satisfies it / why untouched |
|-----------|----------|---------------------------------------------|
| I. Purpose and Focus | touched / not touched | |
| II. Simplicity | touched / not touched | |
| III. Testing | touched / not touched | |
| IV. Visibility | touched / not touched | |
| V. Design Invariants | touched / not touched | |

### Budget and split decision (I.3)

<!--
  Budget: 3 user stories, 40 tasks. A plan over budget is split BEFORE /speckit-tasks.
  If it is over, this section states the split, not a justification for exceeding it.
-->

**User stories**: [n] of 3 · **Estimated tasks**: [n] of 40

**Over budget?**: [no] / [yes — split as follows]

- **This feature keeps**: [stories / requirement IDs]
- **Deferred to a follow-up feature**: [stories / requirement IDs, with the outcome each advances]

### New gates introduced (II.2)

<!-- A gate counts only once shown failing on a real violation. Leave empty if none. -->

| Gate | What it rejects | Link to the run in which it failed |
|------|-----------------|------------------------------------|
| [name] | [the violation] | [run link, added before merge] |

### New tooling proposed (II.3)

<!-- Leave empty if none. Otherwise name the existing analyzers rejected and why. -->

| Proposed tool | Existing analyzers considered | Why each was rejected |
|---------------|-------------------------------|------------------------|
| [name] | [analyzer, analyzer] | [reason] |

## Test Strategy

<!--
  III.2/III.3: name the level of each planned suite and what it proves. Every requirement ID
  from spec.md appears at least once, or trace-check fails. Deploy appears only if the
  outcome advanced by this feature is deployment.
-->

| Level | Suite | Requirement IDs proved | Why the level below cannot prove it |
|-------|-------|------------------------|--------------------------------------|
| Fast | [name] | [CAP-NNN, ...] | — |
| Contract | [adapter] | [CAP-NNN, ...] | [reason] |
| E2E | [scenario] | [CAP-NNN] | [reason] |

**Evals (III.7)**: [judgment-dependent SC IDs and their thresholds, or "none"]

**Time budget (III.4)**: Fast ≤ 15 s, Contract ≤ 90 s. [Expected impact of this feature.]

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

<!--
  V.2: one directory per bounded context; each declares its own ports and owns its adapters.
  Replace the sketch below with the concrete layout; no technical-layer names at the top level.
-->

```text
src/[capability]/
├── [domain types]
├── Ports/               # interfaces to things outside the process (II.4)
└── Adapters/            # the only place the external library is referenced (V.2)

tests/[capability]/
├── Fast/
└── Contract/
```

**Structure Decision**: [the layout chosen and the real directories it creates]

### Capability file to update on close (IV.2)

`docs/capabilities/[capability].md` — [requirement IDs added / changed / removed]

## Complexity Tracking

> Fill ONLY if the Constitution Check above has a row that cannot be satisfied.

| Violation | Why needed | Simpler alternative rejected because |
|-----------|------------|---------------------------------------|
| [rule] | [current need] | [why the simpler option does not work] |
