# Implementation Plan: [FEATURE]

**Branch**: `[###-feature-name]` | **Date**: [DATE] | **Spec**: [link]

**Input**: Feature specification from `/specs/[###-feature-name]/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

[Extract from feature spec: primary requirement + technical approach from research]

## Technical Context

<!--
  Replace with the technical details for this feature. Only the fields that
  apply; delete the rest. Any technology choice made here that is not already
  covered by an accepted ADR triggers VI below.
-->

**Language/Version**: [e.g., C# / .NET 10, TypeScript 5.x or NEEDS CLARIFICATION]

**Primary Dependencies**: [e.g., ASP.NET Core, SvelteKit or NEEDS CLARIFICATION]

**Storage**: [git wiki repository, operational-state store, or N/A]

**Testing**: [test framework and runner or NEEDS CLARIFICATION]

**Target Platform**: [e.g., Linux container on the deploy host or NEEDS CLARIFICATION]

**Constraints**: [domain-specific, e.g., single-operator, offline-capable, or N/A]

**Scale/Scope**: [domain-specific, e.g., wiki of ~1k pages, 1 concurrent run, or N/A]

## Constitution Check

*GATE: Every box is checked or its row appears in Complexity Tracking with a
justification. Must pass before Phase 0 research. Re-check after Phase 1 design.*

### I. Judgment in Instructions, Control in Code

- [ ] Every behaviour in the spec is classified as judgment (instruction file)
      or control (harness code); the classification is listed below.
- [ ] No harness code in this plan branches on wiki content or embeds prompt text.
- [ ] Instruction files touched: [list, or "none"]. If any, the expected
      behaviour change is stated in operator-observable terms.

| Behaviour | Judgment / Control | Lives in |
|-----------|--------------------|----------|
| [from spec] | | [instruction file or slice] |

### II. Reversibility and Containment

- [ ] Every wiki mutation this feature introduces goes through the single commit
      path; no new filesystem write to the wiki.
- [ ] Tool grants: [tools added or widened, or "unchanged"]. Any new tool is
      reversible, or this plan names the ADR that enables it.
- [ ] Trust-boundary impact: [none / describe]. If any, the CI containment test
      is extended in this feature's tasks.

### III. Testing

- [ ] Every harness contract in this plan has a hermetic test against real
      infrastructure, named in Test Strategy below.
- [ ] The LLM is the only double; the scripted responses this feature needs are
      listed in Test Strategy.
- [ ] No planned test asserts model output; judgment criteria from the spec are
      carried as observability rows in IV.

### IV. Observability

| Signal | Kind (metric / log / span) | Operator decision it supports | Surface |
|--------|----------------------------|-------------------------------|---------|
| | | | |

- [ ] Every row has a surface that actually shows it.
- [ ] Each judgment criterion from the spec (constitution III.5) maps to at
      least one row.
- [ ] Each row has a test through the production composition root in Test Strategy.

### V. Architecture

- [ ] Slices touched or created: [list]. No technical-layer directory at first level.
- [ ] External systems touched: [list]. Model port only where doubled; everything
      else adapter-contained, no new port.
- [ ] No new interface with a single implementation and no external system behind it.
- [ ] API contract change: [none / describe]. The committed contract is updated
      in this feature under `contracts/`, not derived at runtime.

### VI. ADRs

- [ ] Decision kinds in this plan: technology choice / external port / security
      boundary / agent autonomy change — [which, or "none"].
- [ ] For each: ADR drafted and included in this plan's PR: [ADR ids].
- [ ] Nothing else in this plan gets an ADR.

### VII. Simplicity

- [ ] No framework wrapper, no speculative extension point or toggle.
- [ ] One representation per domain concept; translation only at a port.
- [ ] Methods expected to exceed the complexity threshold: [none / list with reason].

## Test Strategy

<!--
  One row per harness contract this feature introduces or changes. The tasks
  phase derives its test-before-implementation ordering from this table.
  Observability rows from IV appear here too, each with the composition root
  as its infrastructure.
-->

| Contract | Real infrastructure used | Scripted LLM responses | Test location |
|----------|--------------------------|------------------------|---------------|
| | | | |

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output: the committed API contract change, if any (V.5)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

<!--
  Replace with the concrete layout for this feature. First-level directories
  are domain slices (constitution V.1). Technical-layer names (models,
  services, utils, helpers, controllers) MUST NOT appear at the first level.
  Ports and adapters live inside the slice that owns the external system.
  Delete directories this feature does not touch.
-->

```text
src/
├── <slice-a>/            # e.g. dispatch/, wiki/, tasks/
│   ├── ...               # slice code, named after domain concepts
│   └── adapters/         # only if this slice owns an external system
├── <slice-b>/
└── instructions/         # versioned instruction files (I.1)

frontend/
└── src/<surface>/        # one directory per user-facing surface

tests/
├── <slice-a>/            # tests mirror slices, not test kinds
└── architecture/         # constitution quality gates 1–5
```

**Structure Decision**: [Name the slices this feature lives in and reference the
real directories captured above]

## Complexity Tracking

> **Fill ONLY if Constitution Check has unchecked boxes that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., new port for X] | [which double or second adapter needs it] | [why adapter containment alone is insufficient] |
| [e.g., method over threshold] | [current need] | [why the split makes it less readable] |
| [e.g., ADR for an elaboration] | [why it is a decision of one of the four kinds after all] | [why treating it as elaboration loses something] |