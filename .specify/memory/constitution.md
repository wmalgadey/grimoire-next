# Grimoire Constitution

Grimoire is a hub with a web frontend that dispatches LLM agents which maintain a markdown wiki in
git. Every rule below names how it is verified; an unverifiable statement is Guidance, not a rule.

## Core Principles

### I. Purpose and Focus

1. `docs/product.md` is owner-written and holds the goal, the non-goals, and the ordered outcomes
   with status Now / Next / Later / Never; it outranks every spec.
   **Verified:** spec review checklist — a spec contradicting it is rejected.
2. Every spec names the one outcome from `docs/product.md` it advances. Work advancing none is not
   specified; it is appended to Later with the trigger that would promote it.
   **Verified:** spec template field "Outcome advanced"; empty or multi-valued fails spec review.
3. A feature is one vertical slice with a user-observable result, budgeted at 3 user stories and
   40 tasks. A plan that exceeds the budget is split before `/speckit-tasks`, not after.
   **Verified:** plan review checklist — story count, task estimate, and the split decision.
4. A feature is done when it is merged to main with its capability files updated (IV.2). No branch
   stack is deeper than one.
   **Verified:** PR review checklist — the base branch is main or one feature branch, no deeper.

### II. Simplicity

1. Nothing is built without a consumer in the same feature: no mechanism, abstraction,
   configuration option, gate, or placeholder for later, and no named slot for pending work.
   **Verified:** PR review checklist — each new type, option, and gate names its caller.
2. A quality gate counts only once shown failing on a real violation; a gate that cannot fail, or
   that checks nothing yet, is removed. **Verified:** the PR introducing a gate links its failing
   run.
3. No custom measurement or analysis tooling where an existing analyzer does the job; where none
   does, the concern goes to Later. **Verified:** plan review checklist — a proposed tool names
   the analyzers rejected and why.
4. An interface exists only at a port to something outside the process, or where two real
   implementations exist. **Verified:** test `InterfacesArePortsOrHaveTwoImplementations`.
5. A cross-cutting concern binds a feature only where the feature touches it. Deployment,
   container hardening, egress control, and multi-platform builds are outcomes in
   `docs/product.md` with their own specs, not obligations on every feature.
   **Verified:** plan constitution check — one line per principle, "touched" or "not touched".

### III. Testing

1. Tests verify requirements, not code. Every requirement — functional or success criterion — has
   at least one test, and every acceptance test carries the requirement ID it proves. A test with
   no requirement ID is a unit test of our own logic, or is deleted. **Verified:** `trace-check`.
2. Four levels, tagged as traits. **Fast:** in-process, state-based, real domain objects,
   in-memory adapters at our ports. **Contract:** one suite per adapter against the real thing:
   SQLite, git, child process, HTTP. **E2E:** real processes, two scenarios per story max.
   **Deploy:** smoke checks against built images, run in CI after the image build only and only
   for a feature whose outcome is deployment; never part of `dotnet test` or `make test`.
   **Verified:** an untagged test fails `trace-check`; `make test` selects the Fast trait only.
3. Each test is written at the lowest level that can prove its requirement, and `tasks.md` states
   the level of every test task with one line on why the level below cannot prove it. **Verified:**
   tasks template fields "Level" and "Why not lower"; either empty fails tasks review.
4. Time is a gate: the Fast suite fails above 15 s total, Contract above 90 s.
   **Verified:** gate `test-time-budget`, run on every PR.
5. Not tested: framework and library behaviour (Kestrel, YARP, the CLI parser, the model SDK, the
   JSON serializer), argument parsing as such, DI wiring, static content of configuration and
   deployment files, generated code. We test decisions we made, not ones a dependency made.
   **Verified:** PR review checklist — such a test is deleted, not fixed.
6. In-memory adapters at owned ports are the preferred double. Mocks of our own domain types are
   not allowed. **Verified:** architecture test `NoMocksOfDomainTypes`.
7. Agent judgment is measured by evals with thresholds in a separate runner, referencing the same
   requirement IDs and never part of the test suites. **Verified:** gate `evals-are-not-tests`.

### IV. Visibility

1. Requirement IDs are capability-scoped, stable, and never reused: `<CAPABILITY>-NNN`, for example
   `INGEST-004`. Feature-local `FR-001` numbering is not used.
   **Verified:** `trace-check` rejects an ID off the pattern or one already retired.
2. `docs/capabilities/<capability>.md` is the as-is description of the system, one file per bounded
   context; `specs/NNN-*` are change records. A feature is not done until its requirements are
   merged into the capability files as added, changed, or removed.
   **Verified:** PR review checklist — the feature's closing diff touches `docs/capabilities/`.
3. One deterministic test reads the IDs from `docs/capabilities/` and the requirement traits from
   the test assemblies, fails on a requirement without a test or a test with an unknown ID, and
   writes `docs/trace.md` (requirement, tests, level, status). It is built in the first feature,
   shown failing once, and is never produced by an agent prompt.
   **Verified:** it is itself the gate `trace-check`, run on every PR.
4. `docs/product.md` outcome status is updated when a feature closes. With `docs/capabilities/`
   and `docs/trace.md` it answers what exists, what is specified, what is proven; no other status
   document exists. **Verified:** PR review checklist — a new status document is rejected.

### V. Design Invariants

1. Judgment about wiki content lives only in versioned instruction files loaded into the agent
   context at dispatch. The harness owns dispatch, agent lifecycle, credentials, tool grants, and
   task artifacts, and decides no wiki content.
   **Verified:** architecture test `SystemPromptIsBuiltOnlyByTheInstructionLoader`.
2. Each bounded context declares its own ports and owns its adapters. No namespace outside an
   adapter references that external system's library, and no context reaches into another
   context's adapter. **Verified:** architecture test `AdaptersAreConfinedToTheirContext`.
3. Tools are deny-by-default: an agent receives only the tools granted for that dispatch, the
   granted set is recorded in the task artifact, and no tool with irreversible external effects is
   offered. **Verified:** contract tests `DispatchGrantsOnlyRequestedTools`, `ArtifactRecordsGrant`.

## Guidance

Not rules; no gate depends on them and no review is blocked by them.

- Prefer the smallest change that resolves a finding over the most general one.
- When a requirement is withdrawn, delete its test rather than weakening it.
- Name a slice after the domain concept it serves, not after a technical layer.

## Template Synchronisation

Templates are overridden in `.specify/templates/overrides/`; core templates are never edited.

- spec template: required fields "Outcome advanced (docs/product.md)" and "Out of scope";
  capability-scoped requirement IDs.
- plan template: a constitution check with one line per principle, "touched" or "not touched", and
  the split decision when the I.3 budget is exceeded.
- tasks template: every test task carries "Level" and "Why not lower"; no task without a
  requirement ID or a named principle.

**Verified:** an amendment changing a rule above without changing the template carrying its field
is rejected in review.

## Governance

**Authority.** This constitution supersedes other practice here; where a practice conflicts with a
rule, the rule wins. If a rule blocks work that should happen, amend it first and work under the
amended rule.

**Convergence.** `/speckit-converge` output is a proposal and runs once per feature. Each finding is
classified before it is acted on: a code defect becomes a task, a spec defect goes to
`/speckit-clarify`, anything else is dropped.
**Verified:** the converge PR lists every finding with its classification.

**Review findings.** A finding, human or bot, becomes a test only if it names a requirement ID that
is violated. Otherwise it gets the smallest code change that resolves it, or it is dropped. A
finding whose answer is a new mechanism is a decision for the repository owner.
**Verified:** PR review checklist — a new test cites the requirement ID it proves.

**Amendments.** An amendment is its own PR and changes only this file and the templates under
`.specify/templates/overrides/`. It states the rules added, changed, or removed and the version bump
with its reasoning: MAJOR for an incompatible removal or redefinition, MINOR for a new rule or
section, PATCH for wording. Nothing is grandfathered; an amendment binds all work from its merge
date. **Verified:** PR review checklist — the amendment diff touches no other path.

**Version**: 1.0.0 | **Ratified**: 2026-09-19 | **Last Amended**: 2026-09-19
