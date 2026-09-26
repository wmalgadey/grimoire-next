# Mutation survivors — `003-live-run-record`

T053. The survivors of the mutation artifact of the PR to `main` (run `36266531986`, commit
`bf0edcb`), classified: per survivor, the test that should have killed it and does not, or the reason
none should.

**Nothing here is a threshold**, and nothing was run locally to produce the measurement — `mutation`
is a CI job on a `main`-targeting PR, and `scripts/mutation.sh` is a measurement rather than a gate
(Constitution III.1). A survivor became a test only where it named a requirement the suite does not
actually verify; that happened three times.

| Project | Created | Tested | Survived | Not covered |
| --- | ---: | ---: | ---: | ---: |
| Grimoire.Agent | 152 | 79 | 9 | 0 |
| Grimoire.Runs | 305 | 155 | 49 | 39 |
| Grimoire.Trace | 376 | 117 | 19 | 10 |
| Grimoire.Wiki | 126 | 105 | 14 | 1 |
| **Total** | **959** | **456** | **91** | **50** |

Stryker runs the **Fast suite only**. A mutant the Contract or E2E suites would kill still counts as
survived here, which is worth holding in mind below: several of these are covered at a level this
measurement cannot see.

Of the 91, **53 are in files this feature wrote or changed**. The other 38 are in `Grimoire.Trace`,
`Grimoire.Wiki` and `ToolGrant` — earlier features' code, untouched here, and not this feature's to
classify.

## Three became tests

Each was checked the only way that settles it: the mutation was made in the source and the Fast suite
run. All three passed, which is what makes them gaps rather than opinions — and all three fail now.

| Survivor | Why it is a gap | Test |
| --- | --- | --- |
| `RecordText.cs:81` — `2 + Depth` → `2 - Depth` | The heading level *is* the nesting. Mutated, a call is written `#` instead of `###` and an answer `` instead of `####`, so the record loses the structure that puts a call inside the turn that explains it — and `run.js` finds nothing. The Fast suite pinned `moment.Depth`, the model, and nothing pinned what is written. | `RecordTextTests.Moment_OpensAtTheHeadingLevelOfItsDepth`, a theory over the three depths |
| `RecordText.cs:71` — `moment.Tool ?? "a tool"` → `"a tool"` | A segment's first line says what it is, and for a result that is which call returned (contracts/run-record.md, rule 2). Mutated, every result reads `a tool returned`. Nothing in the Fast suite read a result's heading. | `RecordTextTests.Result_NamesTheToolThatReturned` |
| `RunStateMachine.cs:242` — `elapsed >= Ceilings.Elapsed` → `>` | A run standing exactly on the elapsed ceiling would be recorded as having reached the **cost** ceiling — the wrong one of RUNS-008's seven reasons. `Ceilings.ReachedBy` treats exactly-on as reached, and the reason has to agree. The elapsed-ceiling tests go through the timer, which names `TimeCeiling` directly and never reaches this line. | `RunOutcomeTests.Exit_SaysTheTimeCeiling_WhenTheRunStandsExactlyOnIt` |

## The rest, and why no test should kill them

**37 of the 53 are the record's wording** — 17 string mutations and 20 statement removals in
`RecordText.cs`, `Submission.cs` and the two ports' argument checks. Constitution III.8 says the
wording of the record's headings is not tested; what is tested is that each part is there, in order,
and whole. A test that pinned `"| Model | …"` or `"ended done — "` would be a test to delete, and
this feature already had one found by review and removed (T059). The three above are the exception
because they are not wording: a heading *level*, a tool *name* the reader finds the segment by, and a
comparison.

**`SubmissionBoard.cs:11` — `BothPresent` to `false`, twice.** Not a gap and not observable:
making that change in the source fails **109 Fast tests**. It is a `static readonly` initialiser,
evaluated before a mutant switch can take effect, so the measurement reports a survivor for a change
the suite kills outright. Recorded here so the next reader does not chase it.

**`Submission.cs:215` — `runId is null && state == Submitted` → `||`.** Unreachable behind the guard
above it: `TakeNext` refuses where anything `IsUnderWay` before it ever asks what `IsWaiting`, so a
submission that has a run and still reads `submitted` never reaches this. A test would have to reach
past the queue rule to see it.

**`RunStateMachine.cs:139` — `tokensPerModel.Count > 0` → `>= 0`.** The breakdown is replaced with an
empty one instead of kept. The tail then shows the total against the ceiling and no model rows, which
is exactly what `docs/capabilities/runs.md` says a run that never reported a breakdown holds — so the
difference is invisible to a run that did report one only at its last turn. Killing it needs a run
that reports a breakdown and then a streamed line after it, which no requirement asks for.

**`AgentTranscript.cs:290`, `:178` and the two statement removals at `:136`–`137`.** The reconciling
of the streamed figure against the last `result`. `AgentTranscriptTests` drives the recorded lines and
asserts the totals those lines produce; these mutants change intermediate state in ways the recorded
sequence cannot distinguish. Pinning them would mean asserting the arithmetic rather than the figure,
and the figure is what GUARD-004 counts.

**`RecordText.cs:206` — `elapsed.TotalMinutes >= 1` and its two conditionals.** How a duration reads,
`3 min 12 s` against `192 s`. Wording again (III.8).

**`IRunRecord.cs:69`, `ISubmissionStore.cs:54`, `SubmissionBoard.cs:141`/`:222`,
`RunStateMachine.cs:58`/`:59`/`:167`** — removals of `ArgumentNullException.ThrowIfNull`. A null
reaching there is a programming error inside this process, not a behaviour any requirement names, and
the call would fail a line later. These are guards, not decisions.

## What the measurement says about this feature

`Grimoire.Runs` carries 39 of the 50 mutants **no test reaches at all**, and most of those are in
`SqliteSubmissionStore` — the adapter the Fast suite deliberately does not touch, because its port has
an in-memory double and the real file is the Contract suite's (Constitution III.4, III.9). That is the
measurement seeing the test pyramid rather than a hole in it.

The three gaps it did find are all in `RecordText` and `Run` — the two places this feature put a
decision into code that renders or judges, and both are exercised end to end by suites Stryker does
not run. That is the useful reading: **E2E coverage hid three unpinned decisions from the Fast suite**,
and the measurement is what surfaced them.
