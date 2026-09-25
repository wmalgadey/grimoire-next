# Review Checklist

The single review checklist. Every rule in `.specify/memory/constitution.md` that ends in
**Verified:** review is covered by exactly one item below. Each item cites the rules it verifies and
is answerable yes or no on a concrete PR in under a minute. Twelve items is the cap; a thirteenth
requires an amendment that retires one.

- [ ] **1. Product file integrity** — Exactly one outcome carries status Now, no `OUT-NN` was
      renumbered or reused, row order still reflects priority, no capability is named `OUT` or `DEC`,
      `docs/product.md` was otherwise not edited by an agent except for the outcome status and the
      spec reference, and no status document exists beyond it, `docs/trace.md`, and the capability
      files. If an outcome was set to Done: did the owner state it achieved after exercising it, and
      was no Done outcome reopened? *(I.1, I.2, IV.4)*

- [ ] **2. Slice shape** — The feature is one vertical slice with a user-observable result and adds
      exactly one of: a new operation, a new user interaction, a new external system. *(I.6)*

- [ ] **3. Standard scope and the instruction** — Where an external standard applies, only the
      parts the capability requirements name are built, against the version pinned in
      `docs/product.md`. And where a capability requirement places the shape of an artifact on the
      agent rather than on our code, does the instruction every run receives state that shape, in
      full, as the requirement lists it? *(I.8)*

- [ ] **4. Phase PRs and their review** — Was every phase PR merged into the feature branch before
      the next phase started, only once green and its review closed, and was no PR based on another
      open PR? Was every PR reviewed by someone other than its author, every finding answered on the
      PR, the round decision recorded in one sentence, no more than three rounds run, and the
      owner's review requested where I.11 requires it? *(I.10, I.11)*

- [ ] **5. Closing the feature** — Are all tasks complete, both gates green, and `docs/trace.md`
      regenerated? Are the capability files reconciled with this feature's requirements as added,
      changed, or removed, removed ones keeping their IDs under "Retired"? Does `docs/decisions.md`
      carry this feature's binding decisions, each with a reason? Has the owner read what each
      review-proven requirement of this feature is about? Has the owner exercised the outcome once
      with the real external systems in place? *(I.9, IV.2)*

- [ ] **6. Nothing built without a consumer, and interfaces** — Every mechanism, abstraction,
      option, gate, and placeholder added is consumed by this feature; any new gate was added by the
      first feature that could violate its rule, and the PR introducing `trace-check` and
      `time-budget` links a failing run of each; no measurement or analysis tooling was written where
      an existing analyzer does the job; and every interface added sits at a port to something
      outside the process, or has two real implementations. *(II.1, II.2, II.3, II.4)*

- [ ] **7. Test shape** — Each test matches its level's definition, E2E stays within two scenarios
      per user story, Deploy tests run only in CI and only for a deployment outcome, and agent
      judgment is proven by evals in the separate runner rather than in the test suites.
      *(III.4, III.10)*

- [ ] **8. Tests we do not write** — No test added covers framework or library behaviour, argument
      parsing as such, dependency wiring, static configuration or deployment content, or generated
      code, and any such test found was deleted rather than fixed; every double is an in-memory
      adapter at an owned port, with no generated or framework-provided mock of one of our own types.
      *(III.8, III.9)*

- [ ] **9. Judgment stays with the agent and the user** — Does anything outside the instruction
      loader put text into the agent's prompt? Does Grimoire write anything into the wiki beyond who
      produced a page and when? (both must be no) Does this PR change an instruction, and if so, is
      that named in the PR description as an owner decision? *(V.1)*

- [ ] **10. Ports, adapters, and tool grants** — Is every external library referenced only inside
      its adapter? Does every dispatch pass an explicit tool list, and is that grant recorded?
      *(V.2, V.3)*

- [ ] **11. Findings, converge and amendments** — Every review finding, human or bot, that became a
      test names the requirement ID it violates; the rest became the smallest code change that
      resolves them or were dropped, none silently — each is answered on the PR — and any finding
      whose answer is a new mechanism went to the owner. Each converge finding was classified before
      action, work blocked by a rule was unblocked by an amendment rather than by an exception, and
      any amendment was its own PR touching only the constitution, the template overrides, and this
      checklist. *(Gov. 1, 2, 3, 4)*

- [ ] **12. Requirement shape** — Is every requirement one observable behaviour, with the values it
      covers listed inside it and no two requirements differing only in a value? Did every
      clarification land in the acceptance criteria of an existing requirement, and was any that
      needed a new requirement ID put to the owner rather than created by the agent? *(IV.7, IV.8)*
