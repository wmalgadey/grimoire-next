# Mutation testing — the first measurement of `002-ingest-queue`

<!-- Read out of the artifact CI's `mutation` job uploads. Nothing here is a gate. -->

The measurement of feature `002-ingest-queue`, taken on 2026-09-24 from the `mutation` job of the
pull request to `main` (#41, run 35930682619, 8 m 39 s). There is no threshold, no score anyone has
to reach, and no comparison this document is allowed to fail. What it is for is the last column:
one reading per mutant that lived, of what the suite does not say.

No mutant is disabled anywhere, and no behaviour any requirement describes was changed.

## What is different about this one

| | `001-first-ingest`, third measurement | This one |
| --- | --- | --- |
| Where it ran | a person's machine, `./scripts/mutation.sh` | CI, on the pull request to `main` (DEC-022's fourth badge) |
| `Grimoire.Runs` scope | the whole project | without `Adapters/` — this feature gave it its first one |
| What is read below | every row | the rows in the code this feature adds or changes |

**Why the scope moved.** `run Grimoire.Runs` carried no `--mutate` filter, which was harmless until
this feature added `Adapters/SqliteSubmissionStore.cs`. The measurement runs the **Fast suite
alone**, and an adapter is proven by a Contract suite against the real thing, so every mutant in one
comes back uncovered: it says nothing about the tests and buries the survivors that do.
`Grimoire.Wiki` already had that filter and `scripts/metrics.sh` already scopes coverage that way
for every project (`-classfilters:-*.Adapters.*`).

## How to repeat it

The job runs on every pull request to `main`, and on `workflow_dispatch`. It is not run locally and
this document is not written from a local run — CI's artifact `mutation-reports` is the measurement.
By hand, for the same four runs:

```
./scripts/mutation.sh
```

## Validity

221 tests, one test assembly — the Fast suite — in every run.

| Project | Created | Tested | Ignored | Not covered | Compile errors | Score |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Grimoire.Runs | 156 | 95 | 29 | 4 | 28 | 84.8% |
| Grimoire.Agent | 123 | 59 | 2 | 0 | 62 | 86.4% |
| Grimoire.Wiki | 126 | 105 | 17 | 1 | 3 | 85.8% |
| Grimoire.Trace | 376 | 117 | 221 | 10 | 28 | 78.0% |

Stryker's own per-run summaries. `Tested` is killed plus survived plus timed out; `Ignored` is the
block-already-covered filter and the mutate filter that holds each run to its scope; `Compile
errors` are mutants Stryker generated and could not build, and count towards nothing. Every project
in scope has tested mutants, and the CI job's own two checks — a project with no tested mutant, and
a `--mutate` entry naming a file no report lists — both passed.

### What this feature put into the Fast suite

The 221 above are the whole suite. **39 of them are this feature's**, and what they carry is what
gives the figures above their meaning: a score over a project whose new code no test names would be
a number about the old code.

| Requirement | Fast tests | Where |
| --- | ---: | --- |
| RUNS-002 | 9 | `QueueTests` (7), `RestartTests` (2) |
| RUNS-003 | 10 | `AcknowledgementTests` (8), `RestartTests` (2) |
| RUNS-004 | 6 | `RestartTests` |
| RUNS-006 | 8 | `AgentLifetimeTests` (7), `SubmissionAcceptanceTests` (1) |
| ACCESS-003 | 1 | `SubmissionStateTests` |
| ACCESS-004 | 7 | `SubmissionExcerptTests` |
| ACCESS-002 | 1 | `SubmissionStateTests` — that no run identifier reaches the browser |
| INGEST-001 | 1 | `SubmissionAcceptanceTests` — a rename, not a new test: the scenario `WhenNoRunIsInProgress` stopped distinguishing anything once INGEST-005 was retired |

43 rows over 39 methods: three tests carry more than one requirement, because a restart that keeps
the queue's order and holds it at a failure is one observation of RUNS-002, RUNS-003 and RUNS-004 at
once, and splitting it would assert the same state three times. Every one of the 39 is a `[Fact]`,
so methods and cases are the same count here.

All six requirements this feature registers are proven in the Fast suite, and four of them also
above it: RUNS-004 and RUNS-006 carry Contract tests — against a real SQLite file and a real
process — and RUNS-003, RUNS-004, ACCESS-003 and ACCESS-004 carry E2E scenarios. RUNS-004 is the
one proven at all three levels, which is what a restart is: the rule is the board's, the file is the
adapter's, and only a hub started twice over one store shows that the two are wired to each other.
`docs/trace.md` is the full account.

**These figures are not comparable row for row with `001-first-ingest/mutation.md`.** That
measurement was local and taken before `main` moved (#33, #34, #35, #39); this is the first taken in
CI. Where a count differs in code this feature does not touch, the difference belongs to whatever
changed it, not to this feature.

## What this document reads, and what it does not

66 mutants lived across the four projects — 51 survived, 15 reached by no test. They fall in two
groups:

| | Rows | Read here |
| --- | ---: | --- |
| `src/Grimoire.Runs/Submission.cs`, `SubmissionBoard.cs`, `ISubmissionStore.cs` — the files this feature adds or changes | **12** | yes, every one |
| `RunStateMachine.cs`, `ToolGrant.cs`, `AgentTranscript.cs`, all of `Grimoire.Wiki` and `Grimoire.Trace` — code this feature does not touch | 54 | no |

The 54 are not skipped silently and they are not this feature's to answer for: they sit in code no
commit of `002-ingest-queue` changes, and `001-first-ingest/mutation.md` is the reading of that
code. Re-reading them here would put the same rows in two documents and make it unclear which one is
current. `src/Grimoire.Agent/IAgentHarness.cs` is changed by this feature and carries no surviving
mutant: what was added to it is a record and two members of an interface.

## The kinds

Defined once, in [`tests/README.md`](../../tests/README.md) under "How a surviving mutant is read":
**(a)** sharpen a named test · **(a')** no test reaches the line · **(b)** no requirement asks for
it, candidate for removal · **(b′)** a guard on an invariant its callers already keep · **(c)**
equivalent · **(d)** not asserted by design.

They are not restated here. A kind redefined per feature makes two measurements incomparable, which
is what happened to (d) across `001-first-ingest`'s three readings; `tests/README.md` records that
history and is what this measurement used.

`Tests` is how many tests Stryker recorded as covering the line. A proposal is a reading, not a
change.

## Every mutant that lived, in this feature's code

### Kind (a'), a line no test reaches

None. The one row that stood here — the `Ended` guard at `Submission.cs` 247 — was read as (a') in
this document's first draft and by `001-first-ingest` before it. It is **(b′)** below, by the
owner's decision recorded there.

### Kind (d), not asserted by design

| File | Line | Mutation | Status | Tests | Reading |
| --- | ---: | --- | --- | ---: | --- |
| `SubmissionBoard.cs` | 139 | `ArgumentNullException.ThrowIfNull(newRun);` → `;` | survived | 88 | An argument guard on `TakeNext`. No requirement names it, and **the build does not require it either** — measured, not assumed: removing it and building `Grimoire.Runs` produces no error, so CA1062 is not what put it there. It is the same reading `001-first-ingest` gave the three `ThrowIfNull` guards of `RunStateMachine.cs`. |
| `SubmissionBoard.cs` | 214 | `ArgumentNullException.ThrowIfNull(stored);` → `;` | survived | 88 | The same, on `Restore`. |
| `ISubmissionStore.cs` | 33 | `ArgumentNullException.ThrowIfNull(run);` → `;` | survived | 73 | The same, on `StoredRun.Of`. |
| `Submission.cs` | 232 | `$"a submission reading {state} cannot start running"` → `$""` | survived | 2 | The message of the `ReportedIn` guard. `SubmissionStateTests.Transition_IsRefused_WhenTheSubmissionIsAlreadyDoneOrFailed` reaches it and asserts the exception's type, which is what RUNS-001 asks; no requirement asks what it says. Carried over from `001-first-ingest` (line 76 of the same file), unchanged by this feature. |
| `Submission.cs` | 255 | `$"{state} is terminal; a submission does not leave it"` → `$""` | survived | 2 | The same, for the terminal-transition guard. Carried over (line 101). |

### Kind (b′), a guard its only caller already keeps

| File | Line | Mutation | Status | Tests | Reading |
| --- | ---: | --- | --- | ---: | --- |
| `Submission.cs` | 216 | `throw new InvalidOperationException($"submission {Id} already has run {runId}")` → `;` | not covered | 0 | `HandedTo` refusing a second run. Its only caller is `SubmissionBoard.TakeNext`, which reaches it *after* returning null for any submission that `IsUnderWay` — so handing the same submission out twice is a state the board forbids one line earlier. `HandedTo` is `internal` and the Fast suite is another assembly, so no test can call it directly either. RUNS-002 is what the guard defends, and the test that proves RUNS-002 is `QueueTests.RunEnds_StartsExactlyOneRun_WithSeveralSubmissionsWaiting` — proving it through the board, which is where it is decidable. |
| `Submission.cs` | 216 | `$"submission {Id} already has run {runId}"` → `$""` | not covered | 0 | The same line. |
| `Submission.cs` | 247 | `throw new ArgumentOutOfRangeException(nameof(terminal)…` → `;` | not covered | 0 | The guard on `Ended`: a terminal that is neither `Done` nor `Failed` is refused. **OWNER DECISION, 2026-09-24: the guard stays, as the idiom for exhaustiveness over the four states, and no test is asked for.** It is reachable — `SubmissionBoard.Ended` is public and a test could pass `Running` — which is why `001-first-ingest` read it as (a') and proposed a Fast test, and why this document's first draft repeated that proposal. The decision ends it: what the guard asserts is that `SubmissionState` has four members and two of them are terminal, which is RUNS-001 itself and is proven where it is decidable, in `SubmissionStateTests`. A test that handed the method a fifth state would be a test of the idiom rather than of the requirement. |
| `Submission.cs` | 247 | `"a run ends done or failed"` → `""` | not covered | 0 | The same line, and the same decision. |

### Kind (c), equivalent

| File | Line | × | Mutation | Reading |
| --- | ---: | ---: | --- | --- |
| `SubmissionBoard.cs` | 11 | 2 | `true` → `false` in `StartUpInputs.BothPresent` | The mutation sits in a `static readonly` initializer. Stryker cannot switch a mutant on before the type is initialised, so the mutated value never reaches the code under test. The suite does assert both values — `SubmissionRefusalTests` refuses on each missing input. The same row `001-first-ingest` read at line 9; this feature moved it by two lines and nothing else. |
| `Submission.cs` | 194 | 1 | `runId is null && state == Submitted` → `runId is null \|\| state == Submitted` | `IsWaiting`'s two clauses differ in exactly one state — a submission that has a run and whose agent has not reported in, which still reads `Submitted` (research.md R-04). `TakeNext` returns null for that submission at its **first** clause, `IsUnderWay`, before `IsWaiting` is ever evaluated. So the mutant changes what `IsWaiting` answers and cannot change what the board does, and no test can tell them apart without a board state `TakeNext` forbids. The redundancy is deliberate: `IsWaiting` means what its name says on its own, rather than only in the order its caller happens to ask. |

## What follows from this measurement

**No test is added by it, no code is changed by it, and nothing is proposed for removal.** Every
row in this feature's code is (b′), (c) or (d), and none of those three asks for one. There is no
(a) row and no (a') row: no registered requirement names behaviour the suite leaves unasserted.

The row that had been proposed twice — the `Ended` guard at line 247 — is settled rather than
carried forward again. `001-first-ingest` read it as (a') and asked for a Fast test; this document's
first draft repeated the proposal with a reason for not acting on it, which would have left the next
measurement free to propose it a third time. The owner's decision above is what closes it: the guard
is the idiom for exhaustiveness over the four states, RUNS-001 is proven where it is decidable, and
a measurement that finds this row again should read it as (b′) and move on.

The two claims this reading rests on were measured rather than assumed: that the build does not
require the `ThrowIfNull` guards — removing one and building `Grimoire.Runs` produces no error, so
CA1062 is not what put them there — and that the `IsWaiting` mutant cannot be told apart through the
board, because `TakeNext` returns null at its first clause for the only state in which the two
readings differ.
