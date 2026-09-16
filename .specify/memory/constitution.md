<!--
SYNC IMPACT REPORT (scratch material for amendment review — remove before committing)

Version change: [CONSTITUTION_VERSION] (unpopulated template) → 1.0.0
Bump rationale: initial ratification. The prior file contained only unreplaced
template placeholders, so there is no prior governance to be backward compatible
with. First concrete constitution is 1.0.0.

Modified principles (template slot → ratified principle):
  [PRINCIPLE_1_NAME] → I. Judgment in Instructions, Control in Code
  [PRINCIPLE_2_NAME] → II. Reversibility and Containment
  [PRINCIPLE_3_NAME] → III. Testing
  [PRINCIPLE_4_NAME] → IV. Observability as Operator Loop
  [PRINCIPLE_5_NAME] → V. Architecture
  (added beyond template)  VI. ADRs
  (added beyond template)  VII. Simplicity

Added sections:
  Workflow Phases   (was [SECTION_2_NAME]) — defines specify/plan/tasks/implement/CI
                    inline so each principle's enforcement point is self-contained
  Quality Gates     (was [SECTION_3_NAME]) — consolidated CI checks
  Governance        — authority, change types, Instruction Change Workflow,
                      amendment procedure, versioning, non-retroactivity,
                      decision precedence

Removed sections: none

Follow-up TODOs: none. RATIFICATION_DATE set to the date of this initial adoption.
Note: this constitution deliberately defers three decisions to ADRs rather than
fixing them here — host trust boundary mechanism (II.6), frontend/hub contract
mechanism (V.6), and the per-method complexity threshold value, which lives in CI
configuration (VII.4).
-->

# Grimoire Constitution

Grimoire is a hub with a web frontend that dispatches LLM agents which maintain a
markdown wiki in git and expose every operation to the user as inspectable task
artifacts. The rules below are stated in full here; no rule in this document
depends on reading any other document.

## Core Principles

### I. Judgment in Instructions, Control in Code

**Rules**

1. All judgment about wiki content — what to write, how to word it, when to link,
   what to reorganize, what to leave alone — MUST live only in versioned
   instruction files that are loaded into the agent context at dispatch time.
   Judgment MUST NOT be expressed as harness branching, prompt fragments embedded
   in application code, or hard-coded content rules.
2. Exactly one component, the instruction loader, MUST compose the system prompt
   passed to the model port. An architecture test MUST assert that no namespace
   other than the instruction loader calls the model port with instruction text it
   built itself.
3. The harness MUST own dispatch, agent lifecycle, credentials, tool-boundary
   guardrails, task artifacts, and observability. The harness MUST NOT decide wiki
   content.
4. Every operation performed on the user's behalf MUST produce a task artifact
   recording the instruction-file version dispatched, the inputs, the tool calls
   made, and the resulting commits; that artifact MUST be inspectable by the user.
5. A change to an instruction file is a first-class change type and MUST follow the
   Instruction Change Workflow defined under Governance. It MUST NOT ride along as
   an incidental edit inside a feature or bug change.

**Rationale.** Behaviour that is judgment changes by editing text; behaviour that is
control changes by editing code under test. Collapsing the two means every wiki
convention change becomes a code deploy, and the reason the agent wrote what it
wrote stops being readable in one place. Keeping the model port behind a single
loader is what makes "what was this agent told?" answerable from an artifact.

**Enforced at:** specify (a spec states, per behaviour, whether it is judgment or
control), plan, implement, CI (architecture test for rule 2).

### II. Reversibility and Containment

**Rules**

1. Every wiki mutation MUST be a git commit that the harness can revert without
   manual intervention.
2. Agents MUST NOT write to the wiki outside the commit path. A filesystem write
   that bypasses commit is a defect, not a shortcut.
3. Tools are deny-by-default. An agent receives only the tools explicitly granted
   for that dispatch, and the granted set MUST be recorded in the task artifact.
4. A tool with irreversible external side effects MUST NOT be offered to an agent.
   Enabling one is an agent autonomy change and requires an ADR under VI.
5. A host trust boundary MUST exist between agent execution and the host, and a CI
   test MUST prove it holds independent of instruction-file content, task input,
   and user-supplied wiki content.
6. Which boundary is used — process, container, or network — is decided by ADR, not
   by this document. This document mandates that one exists and that its holding is
   proven.

**Rationale.** An agent that can only produce revertible commits is an agent whose
worst output costs a revert. Deny-by-default tools and a proven boundary keep the
blast radius of both a bad instruction and a hostile wiki page inside something the
operator can undo.

**Enforced at:** plan, implement, CI.

### III. Testing

**Rules**

1. Test-first: a failing test exists before the production code that satisfies it.
   Tests are classicist and state-based — they assert observable state and output,
   not interactions between our own collaborators.
2. Tests MUST use real infrastructure: real filesystem, real git repositories, real
   child processes, real HTTP hosting. The LLM is the single sanctioned test double;
   it is replaced behind the model port and driven by scripted responses.
3. Harness contracts MUST be tested exhaustively and hermetically — no third-party
   network, no shared mutable state, deterministic ordering, safe in parallel.
4. Agent judgment is NEVER a CI gate. No test asserts that the model produced
   particular wiki prose, and no build fails because model output changed.
5. For a feature whose value depends on agent judgment, the spec's success-criteria
   section MUST be filled with operator-observable outcomes, each stating three
   things: the named user-facing surface where the outcome is observed, the
   observability signal read there, and the instruction file an operator edits when
   that signal is bad. Such a criterion MUST NOT be written as a deterministic
   assertion over model output.
6. Every assertion MUST be able to fail because of a change to our own code. An
   assertion that only a dependency upgrade could turn red is that dependency's test
   and does not belong here. The single permitted exception is one minimal,
   intent-named wire-up test per load-bearing framework registration.

**Rationale.** Real infrastructure is what makes the harness contracts trustworthy,
and the LLM is the only collaborator whose output we genuinely cannot pin. Gating CI
on judgment would either freeze the instructions or train us to delete the assertion;
rule 5 replaces that dead end with the loop that actually works.

**Enforced at:** specify (rule 5), tasks (test tasks precede their implementation
tasks), implement, CI.

### IV. Observability as Operator Loop

**Rules**

1. Every plan MUST declare the metrics, log events, and trace spans it introduces or
   changes, by name, each with the operator decision it supports.
2. Each declared signal MUST have a test that exercises the production composition
   root and asserts the signal is emitted. A unit test of the emitter in isolation
   does not satisfy this.
3. Each declared signal MUST be reachable by an operator on a named user-facing
   surface, and the plan MUST name that surface.
4. A signal no surface exposes MUST NOT be added, and a surface MUST NOT be claimed
   for a signal it does not actually show.
5. Task artifacts are an observability surface: every dispatch MUST be
   reconstructible after the fact from its artifact alone.

**Rationale.** Because agent judgment is never a CI gate (III.4), observe → edit
instructions → re-dispatch is the only correction mechanism this system has.
Observability is therefore the control system, not diagnostics, and a signal wired
only in test composition is a control that is not connected.

**Enforced at:** plan, implement, CI.

### V. Architecture

**Rules**

1. The first-level directory order MUST be vertical slices named after domain
   concepts. Technical-layer names — controllers, services, utils, helpers, models —
   MUST NOT appear at the first level.
2. Every external system — model provider, git, filesystem, clock, process spawning,
   network — MUST be reached only through a port declared at a slice boundary.
3. Adapters MUST be confined: an architecture test MUST assert that no namespace
   outside a port's adapter references that external system's library directly.
4. Ports are the sole sanctioned abstraction layer. An interface with one
   implementation and no external system behind it MUST NOT be introduced.
5. The frontend and the hub MUST communicate only through an explicit API contract
   versioned in this repository, and runtime behaviour MUST derive from the
   committed contract so that every contract change is visible in the PR diff.
6. The contract mechanism — schema language, generation, validation — is decided by
   ADR, not by this document.

**Rationale.** Slices named after the domain keep a feature's code in one place;
ports keep the untestable parts at the edges where the LLM double already lives. One
sanctioned abstraction layer is what stops "port" from becoming a habit of wrapping
everything. A contract that only exists at runtime is a contract nobody reviews.

**Enforced at:** plan, implement, CI.

### VI. ADRs

**Rules**

1. An ADR MUST record exactly one decision aspect. A record covering several
   aspects MUST be split.
2. An ADR is superseded whole, never edited in place: a changed decision is a new
   ADR naming the one it supersedes, and the superseded record is marked as such and
   kept.
3. Every ADR MUST use the same fixed template, with these headings in this order:
   Title; Status (Proposed | Accepted | Superseded by <id>); Context; Decision;
   Consequences; Alternatives Considered.
4. An ADR is required only for: technology choices, external ports, security
   boundaries, and agent autonomy changes.
5. Everything else is an elaboration of an already-accepted decision and MUST NOT
   get an ADR.
6. When a plan makes a decision of one of the four required kinds, its ADR MUST be
   merged in the same PR as the plan.

**Rationale.** Decisions worth recording are the ones that are expensive to reverse
and invisible in the diff. Restricting ADRs to four kinds keeps the set small enough
to actually read; superseding whole keeps the history of why honest rather than
retconned.

**Enforced at:** plan, implement (PR review), CI (template and status lint).

### VII. Simplicity

**Rules**

1. Frameworks MUST NOT be wrapped. Use the framework's own API at the point of use.
2. One model per concept: each domain concept has exactly one in-repo
   representation. Parallel duplicates of the same concept MUST NOT be introduced;
   translation happens only at a port boundary where an external format genuinely
   differs.
3. Speculative layers MUST NOT be added. No extension point, configuration toggle,
   or indirection without a caller that needs it today.
4. CI MUST run a per-method complexity regression gate. The threshold value lives in
   CI configuration, not in this document.
5. Only regressions fail the build. A method already over the threshold that did not
   get worse does not fail; a new violation or a worsened one does.

**Rationale.** Wrappers and speculative seams are how a codebase acquires rules
nobody can state. A regression-only gate makes complexity a ratchet that tightens
with the work actually being done, instead of a cleanup project that blocks
unrelated changes.

**Enforced at:** plan, implement, CI.

## Workflow Phases

These are the enforcement points referenced by each principle.

- **specify** — the spec states the user-facing outcome and its success criteria,
  separates judgment from control per I.1, and fills success criteria per III.5 when
  agent judgment is involved. No technology choices here.
- **plan** — the technical approach: slices and ports touched (V), declared
  observability signals and their surfaces (IV.1–IV.3), tool grants and boundary
  impact (II), and any ADR required by VI.4, merged with the plan.
- **tasks** — ordered, independently reviewable work items. A test task precedes the
  implementation task it constrains (III.1).
- **implement** — code written against the failing tests from the tasks phase, with
  review verifying the principles the PR touches.
- **CI** — the automated gates listed below, run on every PR.

## Quality Gates

CI MUST enforce, on every PR:

1. Architecture test: only the instruction loader composes system prompts for the
   model port (I.2).
2. Architecture test: external-system libraries are referenced only inside their
   port's adapter (V.3).
3. Architecture test: first-level directories are domain slices (V.1).
4. Trust-boundary test proving containment holds against adversarial instruction,
   task, and wiki content (II.5).
5. Revertibility test: every wiki mutation path produces a commit the harness can
   revert (II.1).
6. Observability test through the production composition root for each declared
   signal (IV.2).
7. Full test suite, hermetic and parallel-safe, with real filesystem, git, child
   processes, and HTTP hosting; the LLM is the only double (III.2, III.3).
8. Per-method complexity regression gate, failing only on regressions (VII.4–VII.5).
9. ADR lint: single aspect, required headings, valid status, supersession link
   resolves (VI.1–VI.3).

A gate MUST NOT be disabled to land a change. If a gate is wrong, the PR that fixes
the gate is a separate PR.

## Governance

**Authority.** This constitution supersedes other practices in this repository. Where
a practice conflicts with a rule here, the rule wins. If a rule blocks work that
genuinely should happen, amend the constitution first and do the work under the
amended rule — do not proceed around it.

**How these principles guide technical decisions.** When a decision is
underdetermined and two principles pull in different directions, resolve in this
precedence order: II (reversibility and containment) over I (judgment/control split)
over III (testing) over V (architecture) over VII (simplicity). IV and VI constrain
how a decision is recorded and observed rather than what it is, and apply regardless.
A tie that survives the precedence order becomes an ADR if it is one of the four
required kinds (VI.4); otherwise the plan states the trade-off and the choice made.

**Change types and their routes.**

- *Feature* — runs the SDD workflow: specify → plan → tasks → implement. Phases are
  not skipped, and implementation does not begin before its tasks exist.
- *Bug* — runs the bug extension workflow, beginning with a test that reproduces the
  defect and fails for the reason reported.
- *Instruction change* — runs the Instruction Change Workflow below.
- *Agent autonomy change* — a new tool, a widened tool grant, or a relaxed boundary.
  Requires an ADR (VI.4) and compliance with II, and MUST NOT be bundled with any
  other change.

**Instruction Change Workflow.** A change to a versioned instruction file:

1. Opens by citing the observed behaviour that motivates it, identified by task
   artifact id or by the observability surface and signal where it was seen.
2. Touches instruction files only. A PR that changes both instruction files and
   harness code MUST be split into two PRs.
3. States the expected behaviour change in operator-observable terms and names the
   surface on which the change will be visible.
4. Adds no CI assertion over model output (III.4).
5. Is reviewed for whether the behaviour belongs in instructions at all. If the
   desired behaviour is deterministic, it is harness control, and it goes through the
   SDD workflow as a feature instead.
6. Produces a new instruction-file version, which every subsequent dispatch records
   in its task artifact (I.4), so any behaviour change is attributable to a specific
   instruction revision.

**Amendment procedure.** An amendment is proposed as a PR that modifies this file and
nothing else. It states the principles affected, the rules added, changed, or
removed, the proposed version bump, and the reasoning for that bump. It requires
review approval and takes effect when merged.

**Versioning policy.** MAJOR for backward-incompatible governance or principle
removals and redefinitions. MINOR for a new principle or section, or materially
expanded guidance. PATCH for clarifications, wording, and non-semantic refinements.

**Amendments are never retroactive.** A new or changed rule binds work whose specify
phase begins on or after the amendment's merge date. Specs, plans, tasks, ADRs, and
code merged before that date MUST NOT be reworked solely to satisfy the new rule. When
pre-amendment code is modified for an independent reason, the parts actually changed
come into compliance; untouched parts stay as they are.

**Compliance review.** Every PR names the principles it touches, and review verifies
them against the rules above. The Quality Gates run on every PR. A gate failure is
fixed, not waived; where a waiver is genuinely warranted for a security boundary or
an autonomy change, it requires an ADR.

**Version**: 1.0.0 | **Ratified**: 2026-09-16 | **Last Amended**: 2026-09-16
