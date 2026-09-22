# Mutation testing — third measurement

<!-- Read out of the reports `./scripts/mutation.sh` produces. Nothing here is a gate. -->

The measurement of feature `001-first-ingest`, taken again on 2026-09-22 after acting on the
second one. There is still no threshold, no CI job and no score anyone has to reach. What it is
for is the last column: one reading per mutant that lived, of what the suite does not say.

No mutant is disabled anywhere, and no behaviour any requirement describes was changed.

## What changed since the second measurement

| | Then | Now |
| --- | --- | --- |
| `tools/Grimoire.Trace` scope | the whole project | without `Program.cs`, `RepositoryLayout.cs` and `TraceDocument.cs` |
| Rows in this report | 240 | 54 |
| The catalogue's reading of a trait | one method, reflection and judgment together | `Read` finds the methods, `Describe` reads what they carry |
| Quoting the actor's name | read as unreachable, and so untested | read back by a YAML reader, in three spellings |
| `SubmissionBoard.RunInProgress` | a property with no caller | removed (Principle II.1) |
| Kinds | (a) (b) (c) (d) | (a) (a') (b) (c) (d) — (a') is new |

## How to repeat it

```
./scripts/mutation.sh
```

Four Stryker runs, one per project in scope. **About ten minutes** in total: 2:06 + 2:11 + 2:46
+ 2:48 of Stryker time, plus `dotnet tool restore` and one build per run. The reports land in
`StrykerOutput/<project>/reports/mutation-report.json`, which is git-ignored.

`"coverage-analysis": "perTestInIsolation"` is set in `stryker-config.json`, and it is most of
that time — the same four runs took about three minutes under `perTest`. It is what makes the
verdicts hold still. Under `perTest` Stryker keeps one test host alive across mutants, and a type
initialiser runs once in such a host: a mutant inside a `static readonly` initializer therefore
takes effect only if it happens to be the one switched on when the type is first touched, and the
verdicts of every other mutant reached through that type move with it. The first measurement saw
five mutants change verdict between identical runs for that reason. `perTestInIsolation`, which
Stryker 5.0.0 added, gives each test its own process, and the flipping stops.

This measurement is one pass. The second one ran the same configuration twice and one mutant of
635 changed verdict; no second pass was taken here, so nothing below is a claim about this run's
repeatability beyond what that one established.

## What was mutated, and with what

Stryker.NET 5.0.0 through the Microsoft Testing Platform runner (`"test-runner": "mtp"`), which
is the runner xunit v3 uses on .NET 10. Scope is the code that holds decisions of ours.
`Grimoire.Hub` is the composition root and is left out, and so is every `Adapters/` folder,
except `AgentTranscript` — which lives in one because the CLI's protocol may not appear outside
an adapter (Constitution V.2), and which starts no process and touches no file.

| In scope | How it is selected |
| --- | --- |
| `src/Grimoire.Runs` | whole project |
| `src/Grimoire.Agent`, without `Adapters/` | `Ceilings.cs`, `IAgentHarness.cs`, `ToolGrant.cs` |
| the CLI stream parser | `**/Adapters/AgentTranscript.cs` |
| `src/Grimoire.Wiki`, without `Adapters/` | `!**/Adapters/**` |
| `tools/Grimoire.Trace`, without its wiring and its output | `!**/Program.cs`, `!**/RepositoryLayout.cs`, `!**/TraceDocument.cs` |

The three files now out of `Grimoire.Trace` are the ones that hold no decision of ours.
`Program.cs` is argument parsing and the two verbs wired together, `RepositoryLayout.cs` is where
the repository keeps the gate's two inputs, and `TraceDocument.cs` renders `docs/trace.md`.
Constitution III.8 does not test argument parsing as such, dependency wiring, or the static
content of a generated file, and a measurement that keeps reporting on them keeps proposing tests
the constitution says not to write. Their 203 mutants leave the report with them, the 44
`RepositoryLayout.cs` rows and their "write a test of `RepositoryLayout`" proposal included: that
proposal was the largest single (a) group in the second measurement, and it was asking for a test
III.8 rules out. What is left in `Grimoire.Trace` is what the gate actually decides — the
catalogue that reads the traits, the registry that reads the requirements, the check itself, and
the records the three pass around.

## Validity

165 tests, one test assembly — the Fast suite — in every run.

| Project | Created | Tested | Ignored | Not covered | Compile errors |
| --- | ---: | ---: | ---: | ---: | ---: |
| Grimoire.Runs | 67 → **63** | 43 | 18 → **16** | 3 → **2** | 3 → **2** |
| Grimoire.Agent | 211 | 51 | 115 | 0 | 45 |
| Grimoire.Wiki | 154 | 93 → **98** | 51 | 6 → **1** | 4 |
| Grimoire.Trace | 354 | 104 | 44 → **214** | 179 → **9** | 27 |

`a → b` is the second measurement's figure and this one's; a single figure did not move. These
are Stryker's own per-run summaries. `Tested` is killed plus survived plus timed out. `Ignored`
is Stryker's two filters: the block-already-covered filter, and the mutate filter that holds each
run to the scope above. `Compile errors` are mutants Stryker generated and could not build; they
count towards nothing. Every project in scope has tested mutants, and no file with logic is
missing below.

`Grimoire.Runs` shrank by four mutants because `SubmissionBoard.RunInProgress` is gone — a
property no caller read (Principle II.1). One of the four was a survivor.

`Grimoire.Trace` moved 170 mutants from `Not covered` to `Ignored` and nothing else: the three
files above are now outside the mutate filter rather than inside it and untested. That is the
whole of its score change.

### Files actually mutated

| Project | File | Created | Tested | Ignored | Not covered | Compile errors |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Grimoire.Runs | `src/Grimoire.Runs/RunStateMachine.cs` | 17 | 15 | 2 | 0 | 0 |
| Grimoire.Runs | `src/Grimoire.Runs/Submission.cs` | 21 | 10 | 8 | 2 | 1 |
| Grimoire.Runs | `src/Grimoire.Runs/SubmissionBoard.cs` | 25 | 18 | 6 | 0 | 1 |
| Grimoire.Agent | `src/Grimoire.Agent/Adapters/AgentTranscript.cs` | 80 | 35 | 2 | 0 | 43 |
| Grimoire.Agent | `src/Grimoire.Agent/Ceilings.cs` | 9 | 9 | 0 | 0 | 0 |
| Grimoire.Agent | `src/Grimoire.Agent/ToolGrant.cs` | 7 | 7 | 0 | 0 | 0 |
| Grimoire.Wiki | `src/Grimoire.Wiki/IWikiStore.cs` | 1 | 0 | 0 | 1 | 0 |
| Grimoire.Wiki | `src/Grimoire.Wiki/OkfFrontmatter.cs` | 69 | 54 | 12 | 0 | 3 |
| Grimoire.Wiki | `src/Grimoire.Wiki/ProvenanceStamp.cs` | 48 | 44 | 4 | 0 | 0 |
| Grimoire.Trace | `tools/Grimoire.Trace/CapabilityRegistry.cs` | 56 | 37 | 12 | 6 | 1 |
| Grimoire.Trace | `tools/Grimoire.Trace/Requirement.cs` | 1 | 1 | 0 | 0 | 0 |
| Grimoire.Trace | `tools/Grimoire.Trace/TestCatalogue.cs` | 48 | 32 | 11 | 3 | 2 |
| Grimoire.Trace | `tools/Grimoire.Trace/TraceCheck.cs` | 46 | 34 | 9 | 0 | 3 |

These are the JSON report's per-file figures, and they do not add up to the project table above:
a file the mutate filter removes whole is sometimes written to the JSON report with its mutants
marked ignored and sometimes not written at all. `Program.cs` (70), `RepositoryLayout.cs` (50) and
`TraceDocument.cs` (83) are written; `Adapters/HarnessProcess.cs` and
`Adapters/FileSystemWikiStore.cs` are not. Either way the mutants are counted in Stryker's own
summary, which is what the project table reports. `src/Grimoire.Agent/IAgentHarness.cs` is in
scope and carries no mutant: it is an interface and two records.

`ProvenanceStamp.cs` has no uncovered mutant left. Its five were the escaping branch — the one
reached only by an actor's name that has to be quoted — and a test that writes such a name now
reaches them.

## Score

| Project | First | Second | Now |
| --- | ---: | ---: | ---: |
| Grimoire.Runs | 76.09 % | 76.09 % | **77.78 %** |
| Grimoire.Agent | 29.55 % | 82.35 % | **84.31 %** |
| Grimoire.Wiki | 64.65 % | 75.76 % | **87.88 %** |
| Grimoire.Trace | 21.00 % | 30.74 % | **78.76 %** |

Stryker's own figure: killed over killed plus survived plus not-covered. `Grimoire.Trace` moved
because its scope did, and not because anything about it is better tested than it was; the other
three moved because mutants died.

## Every mutant that lived

54 rows, down from 240. The five kinds:

- **(a)** sharpen a named test — a registered requirement, or Principle IV.3 in
  `tools/Grimoire.Trace`, asks for the behaviour the mutant changes, and a test that already
  exists is the one that should have caught it.
- **(a')** no test reaches the line at all. The same reading as (a) — something asks for the
  behaviour — but the answer is a new test rather than a sharper one, so the row names the level
  that test would have to sit at (Constitution III.4, III.6).
- **(b)** no requirement asks for this behaviour — candidate for removal.
- **(c)** equivalent mutant.
- **(d)** `tools/Grimoire.Trace` only: no test is asked for here. Kind (b) is not used in
  `Grimoire.Trace`, because no registered requirement names it — it is the traceability gate
  itself, asked for by Constitution IV.3 and IV.4 — and "candidate for removal" would be the
  wrong reading of that. (d) says what is true instead: these are argument guards and orderings
  no rule of IV.3 names.

`Tests` are the tests Stryker recorded as covering the line; `Req` are the requirement ids those
tests carry, from `docs/trace.md`. A proposal is a reading, not a change.

### Kind (a), by the test to sharpen

| Test to sharpen | Asked for by | Mutants | Where |
| --- | --- | ---: | --- |
| `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_WritesAnActorAYamlReaderReadsBack_WhenPlainYamlWouldNot` | WIKI-002 | 4 | `ProvenanceStamp.cs` 81 ×3, 90 |
| `Grimoire.Fast.Tests.CapabilityRegistryTests.Read_RegistersTheRequirement_WhenTheRowReadsInFull` | IV.3 | 1 | `CapabilityRegistry.cs` 92 |
| `Grimoire.Fast.Tests.TraceCheckTests.CompleteCheck_Fails_WhenATestRequirementHasNoTest` | IV.3 | 1 | `TraceCheck.cs` 25 |
| `Grimoire.Fast.Tests.TraceCheckTests.Check_Fails_WhenATestCarriesAnUnknownRetiredOrReservedId` | IV.3 | 1 | `TraceCheck.cs` 34 |

The four `ProvenanceStamp.cs` rows are the ones the new test does not yet reach: three at line 81
need an actor whose name is empty or opens or closes with a space, and the one at line 90 needs
one with a backslash in it. The test writes a colon, a leading quote and a leading `-`; these are
the same property in spellings it does not carry.

The two `TraceCheck.cs` rows are orderings. Every fixture `TraceCheckTests` builds carries one
suite name and a handful of ids, so reversing the order the violations are collected in changes
nothing either test asserts on. IV.3 asks the gate to name what it found, and a reader who has to
guess the order is not being told; that is what sharpening these would say.

### Kind (a'), a line no test reaches

| File | Line | Mutation | A new test would sit at | Why the level |
| --- | ---: | --- | --- | --- |
| `src/Grimoire.Runs/Submission.cs` | 91 | Statement mutation: `throw new ArgumentOutOfRangeException(nameof…` → `;` | Fast | The guard on `Ended`: a terminal state that is neither `Done` nor `Failed` is refused. Reaching it means handing a third state to a domain object in process — no adapter, no clock, no file. RUNS-001 says a run ends done or failed, and no test asks what the submission does when something else arrives. |
| `tools/Grimoire.Trace/CapabilityRegistry.cs` | 56 | LogicalNotExpression to un-LogicalNotExpression mutation: `!Directory.Exists(capabilitiesDirectory)` → `Directory.Exists(capabilitiesDirectory)` | Contract | `CapabilityRegistry.Read` is the half of the registry that touches the filesystem. A test of it is a test against a real directory, which is what Contract is for (III.4); the half that judges what the files say is already proven at Fast, given text. |
| `tools/Grimoire.Trace/CapabilityRegistry.cs` | 58 | Statement mutation: `throw new TraceInputException($"no capabilit…` → `;` | Contract | Same half, same reason. IV.3 says what the check cannot read it fails on rather than skips, and a missing capabilities directory is the plainest case of that. |
| `tools/Grimoire.Trace/CapabilityRegistry.cs` | 58 | String mutation: `$"no capability files: {capabilitiesDirector…` → `$""` | Contract | Same half. The refusal is asked for and so is the reason: a gate that fails without saying what it could not read is a gate no one can act on. |
| `tools/Grimoire.Trace/CapabilityRegistry.cs` | 61 | String mutation: `"*.md"` → `""` | Contract | Same half. Which files count as capability files is the registry's own decision, and no test of it has ever seen a directory. |

### Kind (b), outside `tools/Grimoire.Trace`

Every (b) row in the run. There are none in `tools/Grimoire.Trace` by definition.

| File | Line | Mutation | Status | Tests | Req |
| --- | ---: | --- | --- | --- | --- |
| `src/Grimoire.Runs/RunStateMachine.cs` | 40 | Statement mutation: `ArgumentNullException.ThrowIfNull(grant);` → `;` | survived | 39 in `CeilingTests`, `DispatchPayloadTests`, `RunOutcomeTests`, `SubmissionAcceptanceTests`, `SubmissionStateTests`, `ToolGrantTests` | ACCESS-002, GUARD-001, GUARD-004, INGEST-001, INGEST-002, INGEST-005, RUNS-001, RUNS-005 |
| `src/Grimoire.Runs/RunStateMachine.cs` | 41 | Statement mutation: `ArgumentNullException.ThrowIfNull(ceilings);` → `;` | survived | 39 in `CeilingTests`, `DispatchPayloadTests`, `RunOutcomeTests`, `SubmissionAcceptanceTests`, `SubmissionStateTests`, `ToolGrantTests` | ACCESS-002, GUARD-001, GUARD-004, INGEST-001, INGEST-002, INGEST-005, RUNS-001, RUNS-005 |
| `src/Grimoire.Runs/RunStateMachine.cs` | 92 | Statement mutation: `ArgumentNullException.ThrowIfNull(stop);` → `;` | survived | 11 in `CeilingTests`, `RunOutcomeTests`, `SubmissionStateTests` | GUARD-004, RUNS-001, RUNS-005 |
| `src/Grimoire.Runs/Submission.cs` | 76 | String mutation: `$"a submission reading {state} cannot start …` → `$""` | survived | `SubmissionStateTests.Transition_IsRefused_WhenTheSubmissionIsAlreadyDoneOrFailed` | RUNS-001 |
| `src/Grimoire.Runs/Submission.cs` | 91 | String mutation: `"a run ends done or failed"` → `""` | not covered | — | — |
| `src/Grimoire.Runs/Submission.cs` | 101 | String mutation: `$"{state} is terminal; a submission does not…` → `$""` | survived | `SubmissionStateTests.Transition_IsRefused_WhenTheSubmissionIsAlreadyDoneOrFailed` | RUNS-001 |
| `src/Grimoire.Runs/SubmissionBoard.cs` | 65 | Linq method mutation (Reverse() to AsEnumerable()): `Enumerable.Reverse` → `Enumerable.AsEnumerable` | survived | 3 in `SubmissionAcceptanceTests`, `SubmissionRefusalTests` | INGEST-001, INGEST-005 |
| `src/Grimoire.Agent/Adapters/AgentTranscript.cs` | 85 | Statement mutation: `ArgumentNullException.ThrowIfNull(grant);` → `;` | survived | 6 in `AgentTranscriptTests` | GUARD-001, GUARD-004 |
| `src/Grimoire.Agent/Adapters/AgentTranscript.cs` | 86 | Statement mutation: `ArgumentNullException.ThrowIfNull(reported);` → `;` | survived | 6 in `AgentTranscriptTests` | GUARD-001, GUARD-004 |
| `src/Grimoire.Agent/Adapters/AgentTranscript.cs` | 127 | Block removal mutation: `{ // A line that is not JSON is not a messag…` → `{}` | survived | `AgentTranscriptTests.Line_SaysNothing_WhenItIsNotJson` | — |
| `src/Grimoire.Wiki/IWikiStore.cs` | 8 | String mutation: `$"\"{path}\" is outside the wiki"` → `$""` | not covered | — | — |
| `src/Grimoire.Wiki/ProvenanceStamp.cs` | 39 | Statement mutation: `ArgumentNullException.ThrowIfNull(page);` → `;` | survived | 15 in `ProvenanceStampTests` | WIKI-002 |
| `src/Grimoire.Wiki/ProvenanceStamp.cs` | 40 | Statement mutation: `ArgumentNullException.ThrowIfNull(record);` → `;` | survived | 15 in `ProvenanceStampTests` | WIKI-002 |

Eleven of the thirteen are argument guards and exception texts. Nothing registered asks for either,
and the tests that cover those lines cover them on the way to something else; a test written to
kill one of these would be a test of the guard rather than of a requirement. They are the owner's
call, which is what (b) means.

### Kinds (c) and (d)

Not listed row by row: neither kind proposes anything. What follows is why each group lived.

| Where | Rows | Kind | Why |
| --- | ---: | --- | --- |
| `SubmissionBoard.cs` 9, `ToolGrant.cs` 27–31, `TestCatalogue.cs` 26 | 11 | (c) | The mutation sits in a `static readonly` initializer. Stryker cannot switch a mutant on before the type is initialised, so the mutated value never reaches the code under test and no test can tell it apart. The suite does assert every one of these values. |
| `OkfFrontmatter.cs` 66 | 1 | (c) | `Array.FindIndex` is started at index 1 here, so it returns -1 or an index of at least 1; 0 is not a value it can return, and `< 0` and `<= 0` cannot differ. |
| `OkfFrontmatter.cs` 87, 156 | 2 | (c) | Both are a `-1` that is read only on a path where it has already been assigned again, or where the value it guards is not read. |
| `OkfFrontmatter.cs` 158 ×2 | 2 | (c) | `at` is the parser's own line within the frontmatter it was given, so neither half of the guard is reachable; joining them differently, or moving the bound by one, changes nothing. |
| `CapabilityRegistry.cs` 47 | 1 | (c) | A dash at index 0 would mean an id beginning with `-`, which the registered-id shape does not admit. |
| `CapabilityRegistry.cs` 104 | 1 | (c) | A line beginning with `#` matches neither row pattern, so continuing and falling through end the same way. |
| `CapabilityRegistry.cs` 137 ×2, 138 | 3 | (c) | `TextColumn` is reached only for a row that already matched, and such a row always has three columns; the empty-text branch is not reachable from it. |
| `TestCatalogue.cs` 46 | 1 | (c) | `"*.dll"` → `""`. Measured: an empty search pattern is .NET's *every file*, not none — 44 entries instead of 27 in the Fast suite's output folder. The extra entries are `.pdb`, `.json` and the apphost, and none of them outranks the `.dll` that shares its `GroupBy` key, so `PathAssemblyResolver` is handed the same assemblies and `trace-check` reads the same 146 tests either way. This rests on the folder as it stands: `Directory.GetFiles` does not guarantee an order, so the mutant is equivalent today rather than by construction. The second measurement read this row as (a); that reading was wrong. |
| `TestCatalogue.cs` 49 | 1 | (c) | `GroupBy` yields no empty group, so `First` and `FirstOrDefault` return the same element. |
| `TestCatalogue.cs` 139, 140 | 2 | (c) | A `[Trait]` carries two string arguments, so the fallback for an argument that is not a string is not reachable. |
| `CapabilityRegistry.cs` 61 | 1 | (d) | The order the capability files are read in. No rule of IV.3 names it. |
| `CapabilityRegistry.cs` 72 | 1 | (d) | An argument guard. No rule of IV.3 names it. |
| `TestCatalogue.cs` 87 | 1 | (d) | The order a test's requirement ids come back in. No rule of IV.3 names it. |
| `TestCatalogue.cs` 120 | 1 | (d) | The `catch` that ends the base-type walk when a dependency is not beside the assembly. Reached only by an assembly built that way, and no rule of IV.3 names it. |

## Proposals by kind

| Kind | Count |
| --- | ---: |
| (a) sharpen a test | 7 |
| (a') no test reaches the line | 5 |
| (b) no requirement asks for the behaviour — candidate for removal | 13 |
| (c) equivalent mutant | 25 |
| (d) no test is asked for here (`tools/Grimoire.Trace`) | 4 |
| **Total** | **54** |

## What this pull request acted on

Each of these was verified one at a time: the mutation applied by hand, the Fast suite run, the
test seen to fail, the mutation reverted.

| Test | Asked for by | Mutants it killed |
| --- | --- | --- |
| `ProvenanceStampTests.WritePage_WritesAnActorAYamlReaderReadsBack_WhenPlainYamlWouldNot` (new) | WIKI-002 | 12 in `ProvenanceStamp.cs` — five of the eight at line 81, both at 88, five of the six at 90: an actor's name written so that a YAML reader gives back something else, or nothing |
| `AgentTranscriptTests.Init_RefusesTheRun_WithoutTheWikiServerConnected` (a second fixture) | GUARD-001 | `AgentTranscript.cs` 143, `Any` → `All` — a run refused because some other MCP server was down |
| `TestCatalogueTests.Read_PassesOverAnAttributeThatIsNotATrait` (new) | IV.3 | `TestCatalogue.cs` 117, `&&` → `\|\|` — a requirement id read off an attribute that is not a trait |
| `TestCatalogueTests.Describe_SaysTheTestHasNoLevel_WhenItCarriesNoTraitAtAll`, `…_WhenNoTraitNamesOne`, `Describe_ReadsTheFirstLevelThatIsOneOfTheFour` (new) | IV.3 | `TestCatalogue.cs` 107, `FirstOrDefault` → `First` — the catalogue throwing on the one thing the gate's third condition is written about |

The last of those needed the catalogue split before it could be written at all. A test with no
level cannot be put in the Fast suite: the gate reads that assembly and fails on exactly that
(`trace-check: … carries no level`), and xunit will not take a non-public test class either. So
`TestCatalogue` was split the way `CapabilityRegistry` already is — `Read` finds the methods and
their trait data, `Describe` turns a suite, a type, a method and its traits into a `TestMethod` —
and `Describe` is asked directly, with no traits and with traits that name no level. The gate's
own rules are now provable without an assembly that breaks the gate.

`SubmissionBoard.RunInProgress` was removed in the same pull request. It had no caller
(Principle II.1); four mutants went with it, one of them a survivor this report no longer carries.

## After the measurement

The review of this pull request asked for three changes to `AgentTranscript.cs`, and they were
made after the run above. Nothing here is a measurement; it is what the tables no longer describe.

- `TranscriptSays.SurfaceIsNotTheGrant` is now `InitIsNotAcceptable`. The event was always raised
  for three different things, the surface being one of them, and the name named only that one.
- `Strings` returned the string elements of an array and dropped the rest. A `tools` of the
  granted names and a `null` beside them therefore read as the grant. It now returns nothing at
  all for an array that is not all names, and `system/init` is refused on it — two Fast tests,
  `Init_RefusesTheSurface_WhenTheToolsAreNotAllNames` and `…_WithoutAToolsArrayAtAll`.
- The prefix-ownership remark in `ToolGrant.cs` still named `HarnessProcess`; the mapping moved
  to `AgentTranscript` in this pull request.

The line numbers in every table above are the measured ones and predate these three. The Fast
suite is 167 tests, not the 165 the Validity section counts.
