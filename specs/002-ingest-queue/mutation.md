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

The five of `001-first-ingest/mutation.md`, and one this measurement had to add:

- **(a)** sharpen a named test — a registered requirement asks for the behaviour the mutant changes,
  and a test that already exists is the one that should have caught it.
- **(a')** no test reaches the line at all. The same reading, but the answer is a new test rather
  than a sharper one, so the row names the level it would sit at.
- **(b)** no requirement asks for this behaviour — candidate for removal.
- **(b′)** *new here.* A guard on an invariant the type's own callers already keep, unreachable
  through the public surface. It reads like (b) — no requirement asks for it — but "candidate for
  removal" is the wrong half of (b) to apply: removing it removes an assertion, not a behaviour, and
  no test can kill it without first putting the object into a state its only caller forbids.
- **(c)** equivalent mutant.
- **(d)** `tools/Grimoire.Trace` only; no row here.

`Tests` is how many tests Stryker recorded as covering the line. A proposal is a reading, not a
change.

## Every mutant that lived, in this feature's code

### Kind (a'), a line no test reaches

| File | Line | Mutation | Tests | A new test would sit at | Why |
| --- | ---: | --- | ---: | --- | --- |
| `Submission.cs` | 247 | `throw new ArgumentOutOfRangeException(nameof(terminal)…` → `;` | 0 | Fast | The guard on `Ended`: a terminal that is neither `Done` nor `Failed` is refused. RUNS-001 says a run ends done or failed, and no test asks what the submission does when a third state arrives. Reaching it needs no adapter, no clock and no file — a Fast test calling `board.Ended(id, SubmissionState.Running)` is the whole of it. **This row is inherited**: `001-first-ingest/mutation.md` proposed exactly this test at line 91 of the same file and it was never written. It is not new work this feature created; it is work this feature did not do either. |
| `Submission.cs` | 247 | `"a run ends done or failed"` → `""` | 0 | Fast | The same line and the same test. The message is the only thing that says which of the four the caller broke. |

### Kind (b), no requirement asks for it

| File | Line | Mutation | Status | Tests | Reading |
| --- | ---: | --- | --- | ---: | --- |
| `SubmissionBoard.cs` | 139 | `ArgumentNullException.ThrowIfNull(newRun);` → `;` | survived | 88 | An argument guard on `TakeNext`. No requirement names it, and **the build does not require it either** — measured, not assumed: removing it and building `Grimoire.Runs` produces no error, so CA1062 is not what put it there. It is the same reading `001-first-ingest` gave the three `ThrowIfNull` guards of `RunStateMachine.cs`. |
| `SubmissionBoard.cs` | 214 | `ArgumentNullException.ThrowIfNull(stored);` → `;` | survived | 88 | The same, on `Restore`. |
| `ISubmissionStore.cs` | 33 | `ArgumentNullException.ThrowIfNull(run);` → `;` | survived | 73 | The same, on `StoredRun.Of`. |
| `Submission.cs` | 232 | `$"a submission reading {state} cannot start running"` → `$""` | survived | 2 | The message of the `ReportedIn` guard. `SubmissionStateTests.Transition_IsRefused_WhenTheSubmissionIsAlreadyDoneOrFailed` reaches it and asserts the exception's type, which is what RUNS-001 asks; no requirement asks what it says. Carried over from `001-first-ingest` (line 76 of the same file), unchanged by this feature. |
| `Submission.cs` | 255 | `$"{state} is terminal; a submission does not leave it"` → `$""` | survived | 2 | The same, for the terminal-transition guard. Carried over (line 101). |

**None of these five is proposed for removal**, which is worth saying because that is (b)'s own
wording. A message that no test reads is still what a person reads when the guard fires, and the
three `ThrowIfNull`s turn a `NullReferenceException` somewhere later into a named argument at the
boundary. What (b) records is the true half: no registered requirement asks for them, so no test
here is missing.

### Kind (b′), a guard its only caller already keeps

| File | Line | Mutation | Status | Tests | Reading |
| --- | ---: | --- | --- | ---: | --- |
| `Submission.cs` | 216 | `throw new InvalidOperationException($"submission {Id} already has run {runId}")` → `;` | not covered | 0 | `HandedTo` refusing a second run. Its only caller is `SubmissionBoard.TakeNext`, which reaches it *after* returning null for any submission that `IsUnderWay` — so handing the same submission out twice is a state the board forbids one line earlier. `HandedTo` is `internal` and the Fast suite is another assembly, so no test can call it directly either. RUNS-002 is what the guard defends, and the test that proves RUNS-002 is `QueueTests.RunEnds_StartsExactlyOneRun_WithSeveralSubmissionsWaiting` — proving it through the board, which is where it is decidable. |
| `Submission.cs` | 216 | `$"submission {Id} already has run {runId}"` → `$""` | not covered | 0 | The same line. |

### Kind (c), equivalent

| File | Line | × | Mutation | Reading |
| --- | ---: | ---: | --- | --- |
| `SubmissionBoard.cs` | 11 | 2 | `true` → `false` in `StartUpInputs.BothPresent` | The mutation sits in a `static readonly` initializer. Stryker cannot switch a mutant on before the type is initialised, so the mutated value never reaches the code under test. The suite does assert both values — `SubmissionRefusalTests` refuses on each missing input. The same row `001-first-ingest` read at line 9; this feature moved it by two lines and nothing else. |
| `Submission.cs` | 194 | 1 | `runId is null && state == Submitted` → `runId is null \|\| state == Submitted` | `IsWaiting`'s two clauses differ in exactly one state — a submission that has a run and whose agent has not reported in, which still reads `Submitted` (research.md R-04). `TakeNext` returns null for that submission at its **first** clause, `IsUnderWay`, before `IsWaiting` is ever evaluated. So the mutant changes what `IsWaiting` answers and cannot change what the board does, and no test can tell them apart without a board state `TakeNext` forbids. The redundancy is deliberate: `IsWaiting` means what its name says on its own, rather than only in the order its caller happens to ask. |

## What follows from this measurement

**No test is added by it, and no code is changed by it.** The one row that names a missing test —
`Submission.cs` 247, the `Ended` guard — is a row `001-first-ingest` had already read and proposed
the same Fast test for, at a time when that file had the same guard three lines from the same place.
It was not written then and it is not written here. Governance 3 is why: a review finding becomes a
test only where it names a requirement the suite does not actually verify, and RUNS-001 *is*
verified — `SubmissionStateTests` proves all four states and both terminal transitions. What the
guard refuses is a fifth state that no caller in the process can produce, because `RunConductor` is
the only caller and it passes `Done` or `Failed`.

Recorded rather than acted on, so that the next measurement finds the same reading instead of
proposing the same test a third time.
