# Mutation — `004-ask-the-wiki`

T091 classifies the survivors of the mutation artifact of the PR to `main` here. This page begins
before that, with two changes to what the measurement can see: `Grimoire.Hub` is in scope for the
first time, and the guards that hid thirteen of its methods from Stryker are written so that they
no longer do.

**Nothing here is a threshold** (Constitution III.1). The numbers below come from local runs with a
narrow `--mutate` filter, taken to count compile errors. Stryker compiles every mutant of the
project whatever the filter selects, so each run counts them all, whichever file it tests.

## `Grimoire.Hub` in scope

`Grimoire.Hub` was left out from 001 on as "the composition root" (`specs/001-first-ingest/mutation.md`).
It is now the project that decides everything (`CLAUDE.md`), and most of this feature's logic is in
it. `scripts/mutation.sh` mutates all of it except `Program.cs`, which is left with nothing but the
top-level statements that put the real adapters at their ports. The start-up refusals that used to
sit beside those statements are `StartUp.cs` now, read by `StartUpTests`.

## The guards Stryker could not mutate

Stryker puts every mutant into one compilation behind a switch. Where a mutant changes a condition
that declares a pattern variable — `if (Reporting(queuedId) is not { } watched) return;` — the
variable is no longer definitely assigned after the `if`, and its first use is a compile error
(CS0165). Stryker cannot pin that error on one mutant. It falls into *safe mode* and drops **every
mutant of the enclosing method** as a compile error, so the whole method goes unmeasured.

Thirteen methods went that way, every one of them on an early-return guard of this shape:

| File | Methods |
| --- | --- |
| `RunConductor.cs` | `CostSoFar`, `MomentHappened`, `AgentStoppedAsync`, `Ended`, `ElapsedCeilingReached` |
| `Chat.cs` | `AgentSaid`, `StepHappened`, `QuestionChanged` |
| `RunQueue.cs` | `StartWhatIsWaitingAsync`, `StartAsync` |
| `Api/ChatEndpoints.cs` | `Increments` |
| `Api/RunRecordEndpoint.cs` | `Framed` |
| `StartUp.cs` | `ResolvedWholly` |

Each guard is now written as a local and a null test — `var watched = Reporting(queuedId);
if (watched is null) return;` — which means the same thing and leaves nothing unassigned for a
mutant to break. The rewrite changes no behaviour, and the Fast suite is the proof: 441 of 441 before
and after. Patterns in a positive branch (`is { } run`) never broke the compilation and are left as
they were.

`RunConductor.cs` had six such guards, and all six were rewritten, but only five of its methods are
in the table. The sixth, `AgentExited`, was not in safe mode: two of its mutants were compile errors
before the rewrite and one after, while the rest of its mutants compiled. It was rewritten with the
other five because it is the same guard.

**The rest of the tree followed** once `CLAUDE.md` made this a rule rather than a fix for thirteen
methods. Every guard left that declared a variable in a condition controlling an early return
— `is not { } x`, `is not T x`, a property pattern, or an `out var` behind `||` — was written out the
same way: `StartUp.cs` (three), `Program.cs`, `Api/RunRecordEndpoint.cs` (two), `Queued.cs`,
`RecordText.cs`, `RunBoard.cs` (four) and `Adapters/AgentTranscript.cs` (six). None of them put
Stryker into safe mode, so the counts below do not move. They are rewritten so that the rule holds
without exceptions a reader would have to know about.

RunConductor was rewritten first, on its own, because it was the worst case. The rule was to carry
on only if its compile errors fell below 20; they fell to 12.

| File | Mutants | Compile errors before | after |
| --- | ---: | ---: | ---: |
| `RunConductor.cs` | 145 | 97 | 12 |
| `Chat.cs` | 59 | 29 | 5 |
| `RunQueue.cs` | 65 | 26 | 0 |
| `Api/RunRecordEndpoint.cs` | 40 | 34 | 20 |
| `Api/ChatEndpoints.cs` | 107 | 27 | 13 |
| `StartUp.cs` | 99 | 41 | 32 |
| every other file in scope | 238 | 15 | 15 |
| **In scope** (all but `Program.cs`) | **753** | **269** | **97** |

The 97 that remain are ordinary compile errors. Stryker identifies each one and drops only that
mutant — a string mutation of a constant, a mutated type that no longer fits — and none of them
takes a method with it. Stryker's log has no "Safe Mode!" line any more. A guard written back as a
pattern would bring one back, which is what `scripts/mutation.sh` tells a reader to look for.

Measured on a 4-core i7-6820HQ with the Fast suite at 441 tests. The RunConductor run then tested
91 mutants in 1:40, and killed 69.
