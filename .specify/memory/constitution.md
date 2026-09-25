# Grimoire Constitution

A self-operated system in which agents maintain a wiki while a human decides what goes in and what
is asked. Every rule below names how it is verified: `review` (an item in `docs/review-checklist.md`),
a required template field, or a gate. Two gates exist from the first feature: `trace-check` and
`time-budget`.

## Core Principles

### I. Purpose and Focus

1. `docs/product.md` is owner-written: goal, non-goals, the core loop, the capability names, and the
   ordered outcomes with status Now / Next / Later / Never / Done; exactly one outcome is Now. It
   outranks every spec. **Verified:** review
2. Outcomes carry stable IDs `OUT-NN`. They are never renumbered and never reused, row order is
   priority, and `OUT` and `DEC` are reserved and are not capability names. **Verified:** review
3. Every spec names the one outcome ID it advances. A feature may touch several capabilities; it
   advances one outcome. **Verified:** spec template field "Outcome advanced (OUT-NN)"
4. Work that advances no outcome is not specified. It is proposed to the owner as a Later outcome
   with the trigger that would promote it. **Verified:** spec template field "Outcome advanced (OUT-NN)"
5. No spec is started while an open question in `docs/product.md` blocks its outcome.
   **Verified:** spec template field "Blocking open questions (none, or stop)"
6. A feature is one vertical slice with a user-observable result and adds exactly one of: a new
   operation, a new user interaction, a new external system. Never two. The first feature establishes
   the skeleton and is exempt from "never two", nothing else. **Verified:** review
7. A feature has one acceptance scenario, the one the owner exercises by hand at close. Every user
   story advances that scenario; a story that needs its own scenario is a separate feature. A split
   is proposed in the plan with both scenarios named and is the owner's decision; no count of
   stories, requirements or tasks triggers a split. **Verified:** plan template field "Acceptance
   scenario"
8. Where the product conforms to an external standard, `docs/product.md` names it with a pinned
   version and the capability requirements state which parts apply. Nothing of the standard beyond
   those parts is built. **Verified:** review
9. A feature is merged to main at the end, and nothing of it reaches main before then. It is done
   when every task in `tasks.md` is complete, both gates are green, `docs/trace.md` is regenerated,
   its capability files are reconciled and `docs/decisions.md` is updated, the owner has read what
   its review-proven requirements are about, and the owner has exercised its outcome once with the
   real external systems in place. **Verified:** review
10. One feature branch off main. Each phase of `tasks.md` is a branch off the feature branch and is
    merged back into it when its PR is green and its review is closed (I.11), before the next phase
    starts. No PR is based on another open PR. The plan names the phases and their PRs before
    implementation starts. A PR stays a draft until its phase is complete and leaves the feature
    branch green; a step that cannot pass yet belongs in the PR that makes it pass.
    **Verified:** review
11. Every PR is reviewed before merge by a reviewer other than the agent that wrote it. Taking a PR
    out of draft starts the repository's automatic review; the implementing agent waits for it,
    classifies every finding per Governance 3, resolves what it accepts and answers what it rejects
    on the PR. After resolving, the agent decides whether a further round is needed — by the size
    and complexity of the change and whether the findings altered the design — and records that
    decision on the PR in one sentence. At most three rounds per PR. The agent stops and requests
    the owner's review, leaving the PR open, when findings remain after the third round, when a
    finding is an owner decision (Governance 3), when the change touches an instruction, a design
    invariant or a decision in `docs/decisions.md`, or when the agent judges a second opinion
    insufficient. A review round is counted when the agent pushes after findings. The agent merges
    a phase PR into the feature branch itself; the feature reaches main per I.9. **Verified:** review

### II. Simplicity

1. Nothing is built without a consumer in the same feature: no mechanism, abstraction, option, gate,
   or placeholder for later. **Verified:** review
2. A gate is created by the first feature whose code could violate its rule, not before, and counts
   only after it has been shown failing on a real violation. Until a gate exists, its rule is verified
   in review; adding a further gate is an amendment. `trace-check` and `time-budget` are established
   by this document. **Verified:** review
3. No purpose-built measurement or analysis tooling where an existing analyzer does the job. Where
   none does, the concern is proposed as a Later outcome. **Verified:** review
4. An interface exists only at a port to something outside the process, or where two real
   implementations exist. **Verified:** review
5. A cross-cutting concern binds a feature only where the feature touches it. Deployment, hardening,
   network containment, and packaging are outcomes in `docs/product.md` with their own specs, never
   obligations on every feature. **Verified:** plan template field "Constitution Check"
6. Technology choices are made in a feature's plan and recorded there. The first feature's plan
   decides the stack, how tests carry level and requirement ID, how `trace-check` reads them, and how
   `time-budget` is enforced. A later plan that departs from an earlier technology decision
   states why. `docs/decisions.md` holds the decisions in force: one entry per decision with a
   stable ID `DEC-NNN`, the decision, its reason, and who or which plan made it. The owner may record
   constraints there before any plan exists, and every plan reads it first. A reason names the
   constraint or evidence behind the decision; "owner decision" is not a reason. Decisions that bind
   later features are merged in when the feature closes; a superseded decision moves under
   "Superseded" and keeps its ID; a plan that departs from one cites it. **Verified:** plan template
   field "Technology decisions"

### III. Testing

1. Tests verify requirements, not code. Every requirement declares how it is proven — `test`, `eval`,
   or `review` — and has at least one proof of that kind. `review` is allowed only where neither a
   test nor an eval can prove the requirement, and the spec says why. A requirement states behaviour
   of the system that can be observed from outside it. What an agent is asked to do is one requirement
   on the instruction it receives, proven by `review`; whether the agent does it is agent judgment,
   proven by `eval`. Neither is ever proven by matching the wording of a text. **Verified:** spec
   template field "Proof"
2. A requirement proven by `test` has at least one test carrying its requirement ID, and no test
   carries an unknown, retired, or reserved ID. **Verified:** `trace-check`
3. Every test is marked with one of the four levels. **Verified:** `trace-check`
4. The levels are: Fast — in-process, state-based, real domain objects, in-memory adapters at owned
   ports. Contract — one suite per adapter against the real external thing. E2E — real processes, at
   most two scenarios per user story. Deploy — smoke checks on built artifacts, run in CI only, only
   for a feature whose outcome is deployment, never part of the default test run. **Verified:** review
5. E2E and Deploy tests must carry the requirement ID they prove. Fast and Contract tests may.
   **Verified:** `trace-check`
6. Each test sits at the lowest level that can prove its requirement. **Verified:** tasks template
   field "Why not lower"
7. Time budget, test execution only, measured in CI: Fast under 15 s, Contract under 90 s. The
   default test run executes Fast only and fails when Fast exceeds its budget; the Contract run fails
   when it exceeds its own. Enforcement uses the simplest means the stack offers, not purpose-built
   tooling. **Verified:** `time-budget`
8. Not tested: framework and library behaviour, argument parsing as such, dependency wiring, static
   content of configuration and deployment files, generated code, the wording of instructions and
   prompts. We test decisions we made, not ones a dependency made; such a test is deleted, not fixed.
   **Verified:** review
9. Doubles are in-memory adapters at owned ports. Generated or framework-provided mocks of our own
   types are not used. **Verified:** review
10. Agent judgment is proven by evals with thresholds in a separate runner, referencing the same
    requirement IDs, never part of the test suites. **Verified:** review

### IV. Visibility

1. Requirement IDs are capability-scoped, stable, and never reused: `<CAPABILITY>-NNN`. Capability
   names come from `docs/product.md`; a new capability is an owner decision. Feature-local numbering
   is not used. An ID becomes permanent when its requirement is registered in `docs/capabilities/`;
   within a draft spec, IDs may still change. **Verified:** spec template field "Requirements"
2. `docs/capabilities/<capability>.md` is the as-is description of the system; `specs/NNN-*` are
   change records. A feature registers its requirements in the capability files before its first
   test is written and reconciles them at close as added, changed, or removed; a removed requirement
   moves under a "Retired" heading and keeps its ID. **Verified:** review
3. `trace-check` is one deterministic check: it reads requirement IDs and proof kinds from
   `docs/capabilities/` and the requirement IDs attached to the tests, and fails on a `test`
   requirement without a test, a test carrying an unknown, retired, or reserved ID, a test without a
   level, or an E2E or Deploy test without a requirement ID. It writes nothing, is built in the first
   feature, and is shown failing once. It is code that runs deterministically; its result is never
   obtained by asking an agent. The first condition applies where a feature lands on main; the
   others apply on every push. What the check cannot read, it fails on rather than skips.
   **Verified:** `trace-check`
4. A single documented command writes `docs/trace.md` (requirement, proof kind, tests, level, status).
   It is committed when a feature closes, together with the outcome status and spec reference in
   `docs/product.md`; these two edits are the only ones an agent makes to `docs/product.md`. At
   feature close the agent sets the outcome to Done only when the owner, having exercised the
   outcome (I.9), states that it is achieved; otherwise it stays Now and the next spec advancing it
   follows. A Done outcome is never reopened; a further wish is a new outcome. Done keeps its
   `OUT-NN` and its row. These places and `docs/decisions.md` answer what is wanted, what exists,
   what is proven, and why it is built this way, and no other status document exists.
   **Verified:** review
5. Every task names the requirement ID it serves or the principle it follows. **Verified:** tasks
   template field "Requirement or principle"
6. Every behaviour a spec commits to carries a requirement ID. Edge cases, success criteria and
   assumptions refer to requirement IDs and add no behaviour of their own. **Verified:** spec
   template field "Edge cases"
7. A requirement is one observable behaviour. The values it covers — states, reasons, fields,
   messages — are a list inside that requirement, never one requirement per value. Two requirements
   that differ only in a value are merged. **Verified:** review
8. Clarification refines, it does not extend: what a clarification surfaces goes into the
   acceptance criteria of an existing requirement. A clarification that would need a new
   requirement ID is put to the owner; the agent does not create it. **Verified:** review

### V. Design Invariants

1. Judgment about wiki content lives only in versioned instruction files and the user-written purpose
   description, both given to the agent at dispatch. Grimoire decides no wiki content; into the wiki
   it writes only facts about the run: who produced a page and when. A change to an instruction is an
   owner decision and is named in the PR. **Verified:** review, until a feature adds a gate
2. Each bounded context declares its own ports and owns its adapters; an external system is referenced
   only inside its adapter. **Verified:** review, until a feature adds a gate
3. Agent tools are deny-by-default: an agent receives only the tools granted for that dispatch, and
   every grant is recorded. **Verified:** review, until a feature adds a gate

## Governance

1. This document outranks other practice. A rule that blocks needed work is amended first, not set
   aside. **Verified:** review
2. `/speckit-converge` runs once per feature and its output is a proposal. Each finding is classified
   before action: code defect → task; spec defect → `/speckit-clarify`; else dropped. **Verified:** review
3. A review finding, human or bot, becomes a test only if it names a violated requirement ID.
   Otherwise it gets the smallest code change that resolves it, or is dropped. A finding whose answer
   is a new mechanism is an owner decision. Findings are answered on the PR, not silently dropped.
   **Verified:** review
4. An amendment is its own PR, touches only this file, the template overrides, and the review
   checklist, and is not retroactive. Versioning is semantic: MAJOR removal or redefinition, MINOR new
   rule, PATCH wording. **Verified:** review

**Version**: 2.0.0 | **Ratified**: 2026-09-20 | **Last Amended**: 2026-09-25
