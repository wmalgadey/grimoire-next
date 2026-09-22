# The test suites

Three suites, one per level that runs here: `Grimoire.Fast.Tests`, `Grimoire.Contract.Tests`,
`Grimoire.E2E.Tests`. What each level means, and which one a test belongs at, is Constitution III;
this file is only the conventions every test in this repository follows.

## The two traits `trace-check` reads

`trace-check` reads these off the **built** test assemblies as metadata — no test is run and no
runner is involved (research.md R-05). A test that does not follow them fails the gate.

### `level`

Every test class carries `[Trait("level", "fast" | "contract" | "e2e" | "deploy")]`.

The trait goes on the **class**, not the method: a suite is a level, and a class-level trait
reaches every test in it, so a new method cannot be added without one. A test with no level — a
misspelt value included, since only those four count — fails the gate on every push
(Constitution III.3).

### `req`

A test that proves a requirement carries `[Trait("req", "<CAPABILITY>-NNN")]`, on the class where
the whole class proves it and on the method otherwise. The attribute repeats, so a test proving
two requirements carries two.

The id must be registered in `docs/capabilities/`: an unknown, retired or reserved one fails the
gate. E2E and Deploy tests **must** carry it; Fast and Contract tests may (Constitution III.5).
The tests that prove a principle rather than a requirement — the gate's own tests, for instance —
carry none.

## How a test is named

A test name is read in `docs/trace.md` and in CI failure output, where one glance has to show
which behaviour broke and under which condition.

**A test class is `<Subject>Tests`.** The subject is an entity from the spec's vocabulary —
`Submission`, `Run`, `GenerationRecord`, `ToolGrant`, `Ceiling` — never a class or a method of the
implementation. A rename in the code then cannot make a test name wrong.

**A test method is `<Action>_<Result>[_<Scenario>]`.** Each part is PascalCase and exactly one
underscore separates them. The scenario is present whenever the result depends on a condition, and
it starts with a condition word: **When**, **While**, **With**, **Without**, **After**.

**Names use the spec's vocabulary, never the implementation's.** No HTTP status codes, no method
names, no type names — the spec says "refused", not "422"; "a run is in progress", not
"RunInProgress == true". A name that points at the implementation ties the test to the code
instead of to the requirement, which is the thing III.1 says tests verify.

**The result is an observable outcome.** Never `Works`, `Succeeds`, `Correctly`, `AsExpected` — a
reader of a failure line learns nothing from those. Say what was observed: `IsRefused`,
`LeavesTheSubmissionFailed`, `CarriesNothingBeyondTheState`.

**A name that needs `And` is two tests.** If the result joins two outcomes, split it; if the
scenario joins two conditions, one of them belongs in the action or in a second test.

Requirement ids live in the `req` trait and never appear in a name.

```
SubmissionTests.Submit_IsRefused_WhileRunInProgress
GenerationRecordTests.WritePage_ReplacesAgentValues
RunTests.AgentStops_NudgesOnce_WhenLogEntryMissing
```

`tests/.editorconfig` switches CA1707 — "identifiers should not contain underscores" — off for
these projects, and only these: the underscores are what separate the three parts, and the rule is
about identifiers an API exposes, whereas a test method is called by the runner and by nobody
else. It stays on everywhere else in the tree.
