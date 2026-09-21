# Mutation testing — second measurement

<!-- Read out of the reports `./scripts/mutation.sh` produces. Nothing here is a gate. -->

The measurement of feature `001-first-ingest`, taken again on 2026-09-22 after acting on the
first one. There is still no threshold, no CI job and no score anyone has to reach. What it is
for is the last column: one reading per mutant that lived, of what the suite does not say.

No mutant is disabled anywhere, and no behaviour any requirement describes was changed.

## What changed since the first measurement

| | Then | Now |
| --- | --- | --- |
| Verdicts | five of 781 mutants changed verdict between identical runs | one of 635 |
| Coverage analysis | `perTest` | `perTestInIsolation` |
| The CLI stream parser | inside `HarnessProcess.cs`, reached by no Fast test | `AgentTranscript.cs`, read against the lines the R-11 spike recorded |
| Scoping that parser for Stryker | two character spans computed from the file | a plain file entry |
| Reading a capability file | one method, filesystem and parsing together | `Read` finds the files, `Parse` takes their text |
| The `level` and `req` traits | read by no test | read off this test assembly itself |

## How to repeat it

```
./scripts/mutation.sh
```

Four Stryker runs, one per project in scope. **About nine minutes** in total (2:12 + 2:12 +
2:19 + 2:17 of Stryker time, plus build). `perTestInIsolation` is most of that: it was about
three minutes under `perTest`, and the difference buys verdicts that hold still. The reports
land in `StrykerOutput/<project>/reports/mutation-report.json`, which is git-ignored.

## What was mutated, and with what

Stryker.NET 5.0.0 through the Microsoft Testing Platform runner (`"test-runner": "mtp"`),
which is the runner xunit v3 uses on .NET 10, with
`"coverage-analysis": "perTestInIsolation"`. Scope is the code that holds decisions of ours.
`Grimoire.Hub` is the composition root and is left out, and so is every `Adapters/` folder,
except `AgentTranscript` — which lives in one because the CLI's protocol may not appear outside
an adapter (Constitution V.2), and which starts no process and touches no file.

| In scope | How it is selected |
| --- | --- |
| `src/Grimoire.Runs` | whole project |
| `src/Grimoire.Agent`, without `Adapters/` | `Ceilings.cs`, `IAgentHarness.cs`, `ToolGrant.cs` |
| the CLI stream parser | `**/Adapters/AgentTranscript.cs` |
| `src/Grimoire.Wiki`, without `Adapters/` | `!**/Adapters/**` |
| `tools/Grimoire.Trace` | whole project |

## Validity

158 tests, one test assembly — the Fast suite — in every run.

| Project | Created | Tested | Ignored | Not covered | Compile errors |
| --- | ---: | ---: | ---: | ---: | ---: |
| Grimoire.Runs | 67 | 43 | 18 | 3 | 3 |
| Grimoire.Agent | 210 → **211** | 16 → **51** | 96 → **115** | 28 → **0** | 70 → **45** |
| Grimoire.Wiki | 154 | 89 → **93** | 51 | 10 → **6** | 4 |
| Grimoire.Trace | 350 → **354** | 76 → **104** | 42 → **44** | 205 → **179** | 27 |

`a → b` is the first measurement's figure and this one's. `Tested` is killed plus survived plus
timed out. `Ignored` is Stryker's own two filters: the block-already-covered filter, and — for
`Grimoire.Agent` and `Grimoire.Wiki` — the mutate filter that holds the run to the scope above.
`Compile errors` are mutants Stryker generated and could not build; they count towards nothing.
Every project in scope has tested mutants, and no file with logic is missing below.

### Files actually mutated

| Project | File | Created | Tested | Ignored | Not covered | Compile errors |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Grimoire.Runs | `src/Grimoire.Runs/RunStateMachine.cs` | 17 | 15 | 2 | 0 | 0 |
| Grimoire.Runs | `src/Grimoire.Runs/Submission.cs` | 21 | 10 | 8 | 2 | 1 |
| Grimoire.Runs | `src/Grimoire.Runs/SubmissionBoard.cs` | 29 | 18 | 8 | 1 | 2 |
| Grimoire.Agent | `src/Grimoire.Agent/Adapters/AgentTranscript.cs` | 80 | 35 | 2 | 0 | 43 |
| Grimoire.Agent | `src/Grimoire.Agent/Ceilings.cs` | 9 | 9 | 0 | 0 | 0 |
| Grimoire.Agent | `src/Grimoire.Agent/ToolGrant.cs` | 7 | 7 | 0 | 0 | 0 |
| Grimoire.Wiki | `src/Grimoire.Wiki/IWikiStore.cs` | 1 | 0 | 0 | 1 | 0 |
| Grimoire.Wiki | `src/Grimoire.Wiki/OkfFrontmatter.cs` | 69 | 54 | 12 | 0 | 3 |
| Grimoire.Wiki | `src/Grimoire.Wiki/ProvenanceStamp.cs` | 48 | 39 | 4 | 5 | 0 |
| Grimoire.Trace | `tools/Grimoire.Trace/CapabilityRegistry.cs` | 56 | 37 | 12 | 6 | 1 |
| Grimoire.Trace | `tools/Grimoire.Trace/Program.cs` | 70 | 0 | 0 | 54 | 16 |
| Grimoire.Trace | `tools/Grimoire.Trace/RepositoryLayout.cs` | 50 | 0 | 6 | 44 | 0 |
| Grimoire.Trace | `tools/Grimoire.Trace/Requirement.cs` | 1 | 1 | 0 | 0 | 0 |
| Grimoire.Trace | `tools/Grimoire.Trace/TestCatalogue.cs` | 48 | 32 | 11 | 3 | 2 |
| Grimoire.Trace | `tools/Grimoire.Trace/TraceCheck.cs` | 46 | 34 | 9 | 0 | 3 |
| Grimoire.Trace | `tools/Grimoire.Trace/TraceDocument.cs` | 83 | 0 | 6 | 72 | 5 |

`src/Grimoire.Agent/IAgentHarness.cs` is in scope and carries no mutant: it is an interface and
two records. `src/Grimoire.Wiki/Adapters/FileSystemWikiStore.cs` and the process half of
`HarnessProcess.cs` are absent because the scope excludes them, not because they were missed.

The parser is no longer the hole it was. In the first measurement `HarnessProcess.cs` had **no
tested mutants at all** — 194 created, every one of them uncovered, filtered or a compile error.
`AgentTranscript.cs` now has 35 tested of 80 created, and `Grimoire.Agent` has no uncovered
mutant left.

### Stability

The configuration was run twice, 635 mutants each time. **One** drew a different verdict:
`TraceCheck.cs` line 34, `tests.OrderBy` → `OrderByDescending`, survived the first pass and was
killed by the second. It is listed below as it came back in the reported pass. Every fixture
`TraceCheckTests` builds carries the same suite name, so that key is constant and `ThenBy` on
the display name decides the order either way — the mutant cannot really be told apart by the
tests as they stand, and the kill was the tool's, not the suite's. Sharpening it is the
proposal against it.

The first measurement ran three times and five mutants changed verdict between runs. The cause
was not the suite: run on its own, the Fast suite came back identical twelve times out of
twelve. It was `"coverage-analysis": "perTest"`, which lets Stryker keep one test host alive
across mutants. A type initialiser runs once in such a host, so a mutant inside a
`static readonly` initializer takes effect only if it happens to be the one switched on when
the type is first touched — and the verdicts of other mutants reached through the same type
move with it. `perTestInIsolation`, which Stryker 5.0.0 added, gives each test its own process
and the verdicts stop moving. It costs about six minutes a run.

Eleven survivors still sit in `static readonly` initializers and are marked `(c)` below: Stryker
cannot switch a mutant on before the type is initialised, so these cannot be killed from a test
at all. They are the tool's limit, not the suite's silence — the suite does assert every one of
those values.

## Score

| Project | First measurement | Now |
| --- | ---: | ---: |
| Grimoire.Runs | 76.09 % | 76.09 % |
| Grimoire.Agent | 29.55 % | 82.35 % |
| Grimoire.Wiki | 64.65 % | 75.76 % |
| Grimoire.Trace | 21.00 % | 30.74 % |

The second pass came back with the same four figures but for `Grimoire.Trace`, where the one
mutant above put it at 31.10 %.

## Every mutant that lived

One row per survived or uncovered mutant. `Tests` are the tests Stryker recorded as covering
the line; `Req` are the requirement ids those tests carry, from `docs/trace.md`. A proposal is
a reading, not a change.

The four kinds:

- **(a)** sharpen a named test — a registered requirement, or Principle IV.3 in
  `tools/Grimoire.Trace`, asks for the behaviour the mutant changes.
- **(b)** no requirement asks for this behaviour — candidate for removal.
- **(c)** equivalent mutant.
- **(d)** `tools/Grimoire.Trace` only: no test is asked for here. Kind (b) is not used in
  `Grimoire.Trace`, because no registered requirement names it — it is the traceability gate
  itself, asked for by Constitution IV.3 and IV.4 — and "candidate for removal" would be the
  wrong reading of that. (d) says what is true instead: the owner leaves `Program.cs` and the
  `docs/trace.md` writer untested, and the rest of these are argument guards and orderings no
  rule of IV.3 names.

### Grimoire.Runs

#### `src/Grimoire.Runs/RunStateMachine.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 40 | Statement mutation: `ArgumentNullException.ThrowIfNull(grant);` → `;` | survived | 39 in `CeilingTests`, `DispatchPayloadTests`, `RunOutcomeTests`, `SubmissionAcceptanceTests`, `SubmissionStateTests`, `ToolGrantTests` | ACCESS-002, GUARD-001, GUARD-004, INGEST-001, INGEST-002, INGEST-005, RUNS-001, RUNS-005 | (b) no requirement asks for this behaviour — candidate for removal |
| 41 | Statement mutation: `ArgumentNullException.ThrowIfNull(ceilings);` → `;` | survived | 39 in `CeilingTests`, `DispatchPayloadTests`, `RunOutcomeTests`, `SubmissionAcceptanceTests`, `SubmissionStateTests`, `ToolGrantTests` | ACCESS-002, GUARD-001, GUARD-004, INGEST-001, INGEST-002, INGEST-005, RUNS-001, RUNS-005 | (b) no requirement asks for this behaviour — candidate for removal |
| 92 | Statement mutation: `ArgumentNullException.ThrowIfNull(stop);` → `;` | survived | 11 in `CeilingTests`, `RunOutcomeTests`, `SubmissionStateTests` | GUARD-004, RUNS-001, RUNS-005 | (b) no requirement asks for this behaviour — candidate for removal |

#### `src/Grimoire.Runs/Submission.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 76 | String mutation: `$"a submission reading {state} cannot start…` → `$""` | survived | `SubmissionStateTests.Transition_IsRefused_WhenTheSubmissionIsAlreadyDoneOrFailed` | RUNS-001 | (b) no requirement asks for this behaviour — candidate for removal |
| 91 | Statement mutation: `throw new ArgumentOutOfRangeException(nameo…` → `;` | not covered | — | — | (a) sharpen `Grimoire.Fast.Tests.SubmissionStateTests.RunEnds_LeavesTheSubmissionDoneOrFailed` — RUNS-001 requires this behaviour |
| 91 | String mutation: `"a run ends done or failed"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 101 | String mutation: `$"{state} is terminal; a submission does no…` → `$""` | survived | `SubmissionStateTests.Transition_IsRefused_WhenTheSubmissionIsAlreadyDoneOrFailed` | RUNS-001 | (b) no requirement asks for this behaviour — candidate for removal |

#### `src/Grimoire.Runs/SubmissionBoard.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 9 | Boolean mutation: `true` → `false` | survived | 30 in `CeilingTests`, `DispatchPayloadTests`, `SubmissionAcceptanceTests`, `SubmissionRefusalTests`, `SubmissionStateTests`, `ToolGrantTests` | ACCESS-002, GUARD-001, GUARD-004, INGEST-001, INGEST-002, INGEST-003, INGEST-004, INGEST-005, RUNS-001 | (c) equivalent mutant, because the mutation sits in a `static readonly` initializer, which runs once per test host and before Stryker switches the mutant on, so the mutated value never reaches the code under test; the suite does assert this value |
| 9 | Boolean mutation: `true` → `false` | survived | 30 in `CeilingTests`, `DispatchPayloadTests`, `SubmissionAcceptanceTests`, `SubmissionRefusalTests`, `SubmissionStateTests`, `ToolGrantTests` | ACCESS-002, GUARD-001, GUARD-004, INGEST-001, INGEST-002, INGEST-003, INGEST-004, INGEST-005, RUNS-001 | (c) equivalent mutant, because the mutation sits in a `static readonly` initializer, which runs once per test host and before Stryker switches the mutant on, so the mutated value never reaches the code under test; the suite does assert this value |
| 65 | Linq method mutation (Reverse() to AsEnumerable()): `Enumerable.Reverse` → `Enumerable.AsEnumerable` | survived | `SubmissionAcceptanceTests.Submit_IsAccepted_WhenNoRunIsInProgress`, `SubmissionAcceptanceTests.Submit_IsNotStored_WhenRefused`, `SubmissionRefusalTests.Submit_IsNotStored_WhenRefused` | INGEST-001, INGEST-003, INGEST-004, INGEST-005 | (b) no requirement asks for this behaviour — candidate for removal |
| 81 | Equality mutation: `s.State is SubmissionState.Submitted or Sub…` → `s.State is not SubmissionState.Subm…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
### Grimoire.Agent

#### `src/Grimoire.Agent/Adapters/AgentTranscript.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 85 | Statement mutation: `ArgumentNullException.ThrowIfNull(grant);` → `;` | survived | 6 in `AgentTranscriptTests` | GUARD-001, GUARD-004 | (b) no requirement asks for this behaviour — candidate for removal |
| 86 | Statement mutation: `ArgumentNullException.ThrowIfNull(reported);` → `;` | survived | 6 in `AgentTranscriptTests` | GUARD-001, GUARD-004 | (b) no requirement asks for this behaviour — candidate for removal |
| 127 | Block removal mutation: `{ // A line that is not JSON is not a messa…` → `{}` | survived | `AgentTranscriptTests.Line_SaysNothing_WhenItIsNotJson` | — | (b) no requirement asks for this behaviour — candidate for removal |
| 143 | Linq method mutation (Any() to All()): `listed.Any` → `listed.All` | survived | 4 in `AgentTranscriptTests` | GUARD-001, GUARD-004 | (a) sharpen `Grimoire.Fast.Tests.AgentTranscriptTests.Init_RefusesTheSurface_WithoutTheWikiServerConnected` — GUARD-001 requires this behaviour |

#### `src/Grimoire.Agent/ToolGrant.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 27 | String mutation: `"list_pages"` → `""` | survived | 68 in `AgentTranscriptTests`, `CeilingTests`, `DispatchPayloadTests`, `RunOutcomeTests`, `SubmissionAcceptanceTests`, `SubmissionStateTests`, `ToolGrantTests` | ACCESS-002, GUARD-001, GUARD-002, GUARD-003, GUARD-004, INGEST-001, INGEST-002, INGEST-005, RUNS-001, RUNS-005 | (c) equivalent mutant, because the mutation sits in a `static readonly` initializer, which runs once per test host and before Stryker switches the mutant on, so the mutated value never reaches the code under test; the suite does assert this value |
| 28 | String mutation: `"read_page"` → `""` | survived | 68 in `AgentTranscriptTests`, `CeilingTests`, `DispatchPayloadTests`, `RunOutcomeTests`, `SubmissionAcceptanceTests`, `SubmissionStateTests`, `ToolGrantTests` | ACCESS-002, GUARD-001, GUARD-002, GUARD-003, GUARD-004, INGEST-001, INGEST-002, INGEST-005, RUNS-001, RUNS-005 | (c) equivalent mutant, because the mutation sits in a `static readonly` initializer, which runs once per test host and before Stryker switches the mutant on, so the mutated value never reaches the code under test; the suite does assert this value |
| 29 | String mutation: `"write_page"` → `""` | survived | 68 in `AgentTranscriptTests`, `CeilingTests`, `DispatchPayloadTests`, `RunOutcomeTests`, `SubmissionAcceptanceTests`, `SubmissionStateTests`, `ToolGrantTests` | ACCESS-002, GUARD-001, GUARD-002, GUARD-003, GUARD-004, INGEST-001, INGEST-002, INGEST-005, RUNS-001, RUNS-005 | (c) equivalent mutant, because the mutation sits in a `static readonly` initializer, which runs once per test host and before Stryker switches the mutant on, so the mutated value never reaches the code under test; the suite does assert this value |
| 30 | String mutation: `"write_index"` → `""` | survived | 68 in `AgentTranscriptTests`, `CeilingTests`, `DispatchPayloadTests`, `RunOutcomeTests`, `SubmissionAcceptanceTests`, `SubmissionStateTests`, `ToolGrantTests` | ACCESS-002, GUARD-001, GUARD-002, GUARD-003, GUARD-004, INGEST-001, INGEST-002, INGEST-005, RUNS-001, RUNS-005 | (c) equivalent mutant, because the mutation sits in a `static readonly` initializer, which runs once per test host and before Stryker switches the mutant on, so the mutated value never reaches the code under test; the suite does assert this value |
| 31 | String mutation: `"append_log"` → `""` | survived | 68 in `AgentTranscriptTests`, `CeilingTests`, `DispatchPayloadTests`, `RunOutcomeTests`, `SubmissionAcceptanceTests`, `SubmissionStateTests`, `ToolGrantTests` | ACCESS-002, GUARD-001, GUARD-002, GUARD-003, GUARD-004, INGEST-001, INGEST-002, INGEST-005, RUNS-001, RUNS-005 | (c) equivalent mutant, because the mutation sits in a `static readonly` initializer, which runs once per test host and before Stryker switches the mutant on, so the mutated value never reaches the code under test; the suite does assert this value |
### Grimoire.Wiki

Nineteen of the twenty-four rows here are `ProvenanceStamp.Scalar`, which quotes the actor's name where plain YAML would not read it back. The hub's actor is `grimoire/<model>` and that is the only actor a run has, so none of those branches is reachable from anything Grimoire writes: WIKI-002 asks for the record, and nothing asks for this. That is why they read (b) and not (a) — they are the owner's call, not a test gap.

#### `src/Grimoire.Wiki/IWikiStore.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 8 | String mutation: `$"\"{path}\" is outside the wiki"` → `$""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |

#### `src/Grimoire.Wiki/OkfFrontmatter.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 66 | Equality mutation: `closing < 0` → `closing <= 0` | survived | 12 in `ProvenanceStampTests` | WIKI-002 | (c) equivalent mutant, because `Array.FindIndex` is started at index 1 here, so it returns -1 or an index of at least 1; 0 is not a value it can return |
| 87 | UnaryMinusExpression to UnaryPlusExpression mutation: `-1` → `+1` | survived | 4 in `ProvenanceStampTests` | WIKI-002 | (c) equivalent mutant, because this is `GeneratedEnd`, and `GeneratedEnd` is read only where `GeneratedAt` is not negative — which on this path it is |
| 156 | UnaryMinusExpression to UnaryPlusExpression mutation: `-1` → `+1` | survived | 6 in `ProvenanceStampTests` | WIKI-002 | (c) equivalent mutant, because `end` is read only where `Span` returns a non-negative index, and on that path it is assigned again before it is read |
| 158 | Logical mutation: `at < 0 \|\| at >= lines.Length` → `at < 0 && at >= lines.Length` | survived | 6 in `ProvenanceStampTests` | WIKI-002 | (c) equivalent mutant, because `at` is the parser's own line within the frontmatter it was given, so neither half of this guard is reachable and joining them differently changes nothing |
| 158 | Equality mutation: `at >= lines.Length` → `at > lines.Length` | survived | 6 in `ProvenanceStampTests` | WIKI-002 | (c) equivalent mutant, because `at` is the parser's own line within the frontmatter it was given, so it never reaches `lines.Length` and no page arrives at the boundary this moves |

#### `src/Grimoire.Wiki/ProvenanceStamp.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 39 | Statement mutation: `ArgumentNullException.ThrowIfNull(page);` → `;` | survived | 14 in `ProvenanceStampTests` | WIKI-002 | (b) no requirement asks for this behaviour — candidate for removal |
| 40 | Statement mutation: `ArgumentNullException.ThrowIfNull(record);` → `;` | survived | 14 in `ProvenanceStampTests` | WIKI-002 | (b) no requirement asks for this behaviour — candidate for removal |
| 81 | Logical mutation: `value.Length > 0 && !char.IsWhiteSpace(valu…` → `value.Length > 0 && !char.IsWhiteSp…` | survived | 8 in `ProvenanceStampTests` | WIKI-002 | (b) no requirement asks for this behaviour — candidate for removal |
| 81 | Logical mutation: `value.Length > 0 && !char.IsWhiteSpace(valu…` → `value.Length > 0 && !char.IsWhiteSp…` | survived | 8 in `ProvenanceStampTests` | WIKI-002 | (b) no requirement asks for this behaviour — candidate for removal |
| 81 | Logical mutation: `value.Length > 0 && !char.IsWhiteSpace(valu…` → `value.Length > 0 && !char.IsWhiteSp…` | survived | 8 in `ProvenanceStampTests` | WIKI-002 | (b) no requirement asks for this behaviour — candidate for removal |
| 81 | Logical mutation: `value.Length > 0 && !char.IsWhiteSpace(valu…` → `value.Length > 0 && !char.IsWhiteSp…` | survived | 8 in `ProvenanceStampTests` | WIKI-002 | (b) no requirement asks for this behaviour — candidate for removal |
| 81 | Logical mutation: `value.Length > 0 && !char.IsWhiteSpace(valu…` → `value.Length > 0 && !char.IsWhiteSp…` | survived | 8 in `ProvenanceStampTests` | WIKI-002 | (b) no requirement asks for this behaviour — candidate for removal |
| 81 | Logical mutation: `value.Length > 0 && !char.IsWhiteSpace(valu…` → `value.Length > 0 && !char.IsWhiteSp…` | survived | 8 in `ProvenanceStampTests` | WIKI-002 | (b) no requirement asks for this behaviour — candidate for removal |
| 81 | Logical mutation: `value.Length > 0 && !char.IsWhiteSpace(valu…` → `value.Length > 0 \|\| !char.IsWhiteSp…` | survived | 8 in `ProvenanceStampTests` | WIKI-002 | (b) no requirement asks for this behaviour — candidate for removal |
| 81 | Equality mutation: `value.Length > 0` → `value.Length >= 0` | survived | 8 in `ProvenanceStampTests` | WIKI-002 | (b) no requirement asks for this behaviour — candidate for removal |
| 88 | Equality mutation: `"-?:,[]{}#&*!\|>'\"%@`".IndexOf(value[0]) < 0` → `"-?:,[]{}#&*!\|>'\"%@`".IndexOf(valu…` | survived | 8 in `ProvenanceStampTests` | WIKI-002 | (b) no requirement asks for this behaviour — candidate for removal |
| 88 | String mutation: `"-?:,[]{}#&*!\|>'\"%@`"` → `""` | survived | 8 in `ProvenanceStampTests` | WIKI-002 | (b) no requirement asks for this behaviour — candidate for removal |
| 90 | Conditional (true) mutation: `plain ? value : $"\"{value.Replace("\\", "\…` → `(true?value :$"\"{value.Replace("\\…` | survived | 8 in `ProvenanceStampTests` | WIKI-002 | (b) no requirement asks for this behaviour — candidate for removal |
| 90 | String mutation: `$"\"{value.Replace("\\", "\\\\", StringComp…` → `$""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 90 | String mutation: `"\\"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 90 | String mutation: `"\\\\"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 90 | String mutation: `"\""` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 90 | String mutation: `"\\\""` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
### Grimoire.Trace

No registered requirement names `Grimoire.Trace`: it is the traceability gate itself, asked for by Constitution IV.3 and IV.4 rather than by a capability. Kind (a) therefore cites IV.3 here, and kind (b) is not used at all.

#### `tools/Grimoire.Trace/CapabilityRegistry.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 47 | Equality mutation: `dash < 0` → `dash <= 0` | survived | 17 in `CapabilityRegistryTests`, `TraceCheckTests` | — | (c) equivalent mutant, because a dash at index 0 would mean an id beginning with `-`, which the registered-id shape does not admit |
| 56 | LogicalNotExpression to un-LogicalNotExpression mutation: `!Directory.Exists(capabilitiesDirectory)` → `Directory.Exists(capabilitiesDirect…` | not covered | — | — | (a) write a test of `CapabilityRegistry.Read` against a directory — IV.3 requires this behaviour |
| 58 | Statement mutation: `throw new TraceInputException($"no capabili…` → `;` | not covered | — | — | (a) write a test of `CapabilityRegistry.Read` against a directory — IV.3 requires this behaviour |
| 58 | String mutation: `$"no capability files: {capabilitiesDirecto…` → `$""` | not covered | — | — | (a) write a test of `CapabilityRegistry.Read` against a directory — IV.3 requires this behaviour |
| 61 | Linq method mutation (Order() to OrderDescending()): `Directory.GetFiles(capabilitiesDirectory, "…` → `Directory.GetFiles(capabilitiesDire…` | not covered | — | — | (d) no test is asked for here: no rule of IV.3 names this |
| 61 | String mutation: `"*.md"` → `""` | not covered | — | — | (a) write a test of `CapabilityRegistry.Read` against a directory — IV.3 requires this behaviour |
| 72 | Statement mutation: `ArgumentNullException.ThrowIfNull(files);` → `;` | survived | 10 in `CapabilityRegistryTests` | — | (d) no test is asked for here: no rule of IV.3 names this |
| 92 | Boolean mutation: `false` → `true` | survived | 10 in `CapabilityRegistryTests` | — | (a) sharpen `Grimoire.Fast.Tests.CapabilityRegistryTests.Read_RegistersTheRequirement_WhenTheRowReadsInFull` — IV.3 requires this behaviour |
| 104 | Statement mutation: `continue;` → `;` | survived | 10 in `CapabilityRegistryTests` | — | (c) equivalent mutant, because a line beginning with `#` matches neither row pattern, so continuing and falling through end the same way |
| 137 | Conditional (false) mutation: `columns.Length <= 2 ? string.Empty : string…` → `(false?string.Empty :string.Join('\|…` | survived | 8 in `CapabilityRegistryTests` | — | (c) equivalent mutant, because `TextColumn` is reached only for a row that already matched, and such a row always has three columns, so the branch this forces is the branch taken |
| 137 | Equality mutation: `columns.Length <= 2` → `columns.Length < 2` | survived | 8 in `CapabilityRegistryTests` | — | (c) equivalent mutant, because `TextColumn` is reached only for a row that already matched, and such a row always has more than two columns |
| 138 | String mutation: `string.Empty` → `"Stryker was here!"` | not covered | — | — | (c) equivalent mutant, because the same: the empty-text branch is not reachable from a row that matched |

#### `tools/Grimoire.Trace/Program.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 15 | Conditional (true) mutation: `args.Length > 0 ? args[0] : null` → `(true?args[0] :null)` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 15 | Conditional (false) mutation: `args.Length > 0 ? args[0] : null` → `(false?args[0] :null)` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 15 | Equality mutation: `args.Length > 0` → `args.Length < 0` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 15 | Equality mutation: `args.Length > 0` → `args.Length >= 0` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 17 | Equality mutation: `verb is not ("check" or "write")` → `verb is ("check" or "write")` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 17 | Logical mutation: `"check" or "write"` → `"check" and "write"` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 17 | String mutation: `"check"` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 17 | String mutation: `"write"` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 19 | Statement mutation: `Console.Error.WriteLine(Usage);` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 29 | Boolean mutation: `false` → `true` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 31 | Equality mutation: `at < args.Length` → `at > args.Length` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 31 | Equality mutation: `at < args.Length` → `at <= args.Length` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 31 | PostIncrementExpression to PostDecrementExpression mutation: `at++` → `at--` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 35 | String mutation: `"--complete"` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 35 | Equality mutation: `verb == "check"` → `verb != "check"` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 35 | String mutation: `"check"` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 36 | Boolean mutation: `true` → `false` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 40 | Statement mutation: `Console.Error.WriteLine("trace-check: --com…` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 40 | String mutation: `"trace-check: --complete belongs to `check`…` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 41 | Statement mutation: `Console.Error.WriteLine(Usage);` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 44 | String mutation: `"--configuration"` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 44 | Logical mutation: `at + 1 < args.Length && !args[at + 1].Start…` → `at + 1 < args.Length \|\| !args[at + …` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 44 | Equality mutation: `at + 1 < args.Length` → `at + 1 > args.Length` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 44 | Equality mutation: `at + 1 < args.Length` → `at + 1 <= args.Length` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 44 | Arithmetic mutation: `at + 1` → `at - 1` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 44 | LogicalNotExpression to un-LogicalNotExpression mutation: `!args[at + 1].StartsWith("--", StringCompar…` → `args[at + 1].StartsWith("--", Strin…` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 44 | Arithmetic mutation: `at + 1` → `at - 1` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 44 | String mutation: `"--"` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 45 | PreIncrementExpression to PreDecrementExpression mutation: `++at` → `--at` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 49 | Statement mutation: `Console.Error.WriteLine("trace-check: --con…` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 49 | String mutation: `"trace-check: --configuration needs a value…` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 50 | Statement mutation: `Console.Error.WriteLine(Usage);` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 54 | Statement mutation: `Console.Error.WriteLine($"trace-check: unre…` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 54 | String mutation: `$"trace-check: unrecognised argument \"{arg…` → `$""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 55 | Statement mutation: `Console.Error.WriteLine(Usage);` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 64 | Equality mutation: `verb == "write"` → `verb != "write"` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 64 | String mutation: `"write"` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 66 | Statement mutation: `File.WriteAllText(layout.TraceDocument, Tra…` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 67 | Statement mutation: `Console.WriteLine($"wrote {Path.GetRelative…` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 67 | String mutation: `$"wrote {Path.GetRelativePath(layout.Root, …` → `$""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 72 | Equality mutation: `violations.Count > 0` → `violations.Count >= 0` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 72 | Negate expression: `violations.Count > 0` → `!(violations.Count > 0)` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 74 | Statement mutation: `Console.Error.WriteLine($"trace-check: {vio…` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 74 | String mutation: `$"trace-check: {violations.Count} violation…` → `$""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 77 | Statement mutation: `Console.Error.WriteLine($" {violation}");` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 77 | String mutation: `$" {violation}"` → `$""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 83 | Statement mutation: `Console.WriteLine( $"trace-check{(complete …` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 84 | String mutation: `$"trace-check{(complete ? " --complete" : s…` → `$""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 84 | Conditional (true) mutation: `complete ? " --complete" : string.Empty` → `(true?" --complete" :string.Empty)` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 84 | Conditional (false) mutation: `complete ? " --complete" : string.Empty` → `(false?" --complete" :string.Empty)` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 84 | String mutation: `" --complete"` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 84 | String mutation: `string.Empty` → `"Stryker was here!"` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 89 | Statement mutation: `Console.Error.WriteLine($"trace-check: {fai…` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 89 | String mutation: `$"trace-check: {failure.Message}"` → `$""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |

#### `tools/Grimoire.Trace/RepositoryLayout.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 18 | String mutation: `"docs"` → `""` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 18 | String mutation: `"capabilities"` → `""` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 20 | String mutation: `"docs"` → `""` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 20 | String mutation: `"trace.md"` → `""` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 24 | Equality mutation: `directory is not null` → `directory is null` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 26 | Negate expression: `File.Exists(Path.Combine(directory.FullName…` → `!(File.Exists(Path.Combine(director…` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 27 | Block removal mutation: `{ return new RepositoryLayout(directory.Ful…` → `{}` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 32 | Statement mutation: `throw new TraceInputException($"no {Solutio…` → `;` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 32 | String mutation: `$"no {SolutionFile} in {Environment.Current…` → `$""` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 42 | Linq method mutation (Order() to OrderDescending()): `Directory.GetDirectories(Path.Combine(Root,…` → `Directory.GetDirectories(Path.Combi…` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 42 | String mutation: `"tests"` → `""` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 43 | Equality mutation: `Directory.GetFiles(d, "*.csproj").Length ==…` → `Directory.GetFiles(d, "*.csproj").L…` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 43 | String mutation: `"*.csproj"` → `""` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 49 | Equality mutation: `suites.Length == 0` → `suites.Length != 0` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 51 | Statement mutation: `throw new TraceInputException($"no test pro…` → `;` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 51 | String mutation: `$"no test projects under {Path.Combine(Root…` → `$""` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 51 | String mutation: `"tests"` → `""` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 54 | Conditional (true) mutation: `configuration is null ? (string[])["Release…` → `(true?(string[])["Release", "Debug"…` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 54 | Conditional (false) mutation: `configuration is null ? (string[])["Release…` → `(false?(string[])["Release", "Debug…` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 54 | Equality mutation: `configuration is null` → `configuration is not null` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 54 | String mutation: `"Release"` → `""` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 54 | String mutation: `"Debug"` → `""` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 57 | Negate expression: `Array.TrueForAll(found, f => f.Path is not …` → `!(Array.TrueForAll(found, f => f.Pa…` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 57 | Equality mutation: `f.Path is not null` → `f.Path is null` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 58 | Block removal mutation: `{ return [.. found.Select(f => new TestAsse…` → `{}` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 63 | String mutation: `", "` → `""` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 63 | Equality mutation: `Locate(s, configuration ?? "Debug") is null` → `Locate(s, configuration ?? "Debug")…` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 63 | Null coalescing mutation (remove left): `configuration ?? "Debug"` → `"Debug"` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 63 | String mutation: `"Debug"` → `""` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 64 | Statement mutation: `throw new TraceInputException( $"not built:…` → `;` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 65 | String mutation: `$"not built: {missing}. Run `dotnet build {…` → `$""` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 70 | String mutation: `"tests"` → `""` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 70 | String mutation: `"bin"` → `""` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 71 | LogicalNotExpression to un-LogicalNotExpression mutation: `!Directory.Exists(output)` → `Directory.Exists(output)` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 72 | Block removal mutation: `{ return null; }` → `{}` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 77 | Linq method mutation (Order() to OrderDescending()): `Directory.GetFiles(output, $"{suite}.dll", …` → `Directory.GetFiles(output, $"{suite…` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 77 | String mutation: `$"{suite}.dll"` → `$""` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 81 | Conditional (true) mutation: `assemblies.Length == 1 ? assemblies[0] : as…` → `(true?assemblies[0] :assemblies.Len…` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 81 | Conditional (false) mutation: `assemblies.Length == 1 ? assemblies[0] : as…` → `(false?assemblies[0] :assemblies.Le…` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 81 | Equality mutation: `assemblies.Length == 1` → `assemblies.Length != 1` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 83 | Conditional (true) mutation: `assemblies.Length == 0 ? null : throw new T…` → `(true?null :throw new TraceInputExc…` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 83 | Conditional (false) mutation: `assemblies.Length == 0 ? null : throw new T…` → `(false?null :throw new TraceInputEx…` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 83 | Equality mutation: `assemblies.Length == 0` → `assemblies.Length != 0` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |
| 84 | String mutation: `$"{suite} is built for more than one target…` → `$""` | not covered | — | — | (a) write a test of `RepositoryLayout`, which finds the gate's two inputs — IV.3 requires this behaviour |

#### `tools/Grimoire.Trace/TestCatalogue.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 16 | String mutation: `"fast"` → `""` | survived | 9 in `TestCatalogueTests`, `TraceCheckTests` | — | (c) equivalent mutant, because the mutation sits in a `static readonly` initializer, which runs once per test host and before Stryker switches the mutant on, so the mutated value never reaches the code under test; the suite does assert this value |
| 16 | String mutation: `"contract"` → `""` | survived | 9 in `TestCatalogueTests`, `TraceCheckTests` | — | (c) equivalent mutant, because the mutation sits in a `static readonly` initializer, which runs once per test host and before Stryker switches the mutant on, so the mutated value never reaches the code under test; the suite does assert this value |
| 16 | String mutation: `"e2e"` → `""` | survived | 9 in `TestCatalogueTests`, `TraceCheckTests` | — | (c) equivalent mutant, because the mutation sits in a `static readonly` initializer, which runs once per test host and before Stryker switches the mutant on, so the mutated value never reaches the code under test; the suite does assert this value |
| 16 | String mutation: `"deploy"` → `""` | survived | 9 in `TestCatalogueTests`, `TraceCheckTests` | — | (c) equivalent mutant, because the mutation sits in a `static readonly` initializer, which runs once per test host and before Stryker switches the mutant on, so the mutated value never reaches the code under test; the suite does assert this value |
| 36 | String mutation: `"*.dll"` → `""` | survived | 8 in `TestCatalogueTests` | — | (a) sharpen `Grimoire.Fast.Tests.TestCatalogueTests` — IV.3 requires this behaviour |
| 39 | Linq method mutation (First() to FirstOrDefault()): `g.First` → `g.FirstOrDefault` | survived | 8 in `TestCatalogueTests` | — | (c) equivalent mutant, because `GroupBy` yields no empty group, so `First` and `FirstOrDefault` return the same element |
| 64 | Linq method mutation (Order() to OrderDescending()): `traits.Where(t => t.Name.Equals("req", Stri…` → `traits.Where(t => t.Name.Equals("re…` | survived | 8 in `TestCatalogueTests` | — | (d) no test is asked for here: no rule of IV.3 names this |
| 100 | Block removal mutation: `{ return null; }` → `{}` | not covered | — | — | (d) no test is asked for here: no rule of IV.3 names this |
| 107 | Linq method mutation (FirstOrDefault() to First()): `traits .Where(t => t.Name.Equals("level", S…` → `traits .Where(t => t.Name.Equals("l…` | survived | 8 in `TestCatalogueTests` | — | (a) sharpen `Grimoire.Fast.Tests.TestCatalogueTests` — IV.3 requires this behaviour |
| 117 | Logical mutation: `a.AttributeType.FullName == TraitAttribute …` → `a.AttributeType.FullName == TraitAt…` | survived | 8 in `TestCatalogueTests` | — | (a) sharpen `Grimoire.Fast.Tests.TestCatalogueTests` — IV.3 requires this behaviour |
| 119 | String mutation: `string.Empty` → `"Stryker was here!"` | not covered | — | — | (c) equivalent mutant, because a `[Trait]` carries two string arguments, so the fallback for an argument that is not a string is not reachable |
| 120 | String mutation: `string.Empty` → `"Stryker was here!"` | not covered | — | — | (c) equivalent mutant, because a `[Trait]` carries two string arguments, so the fallback for an argument that is not a string is not reachable |

#### `tools/Grimoire.Trace/TraceCheck.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 25 | Linq method mutation (OrderBy() to OrderByDescending()): `active.Values.Where(r => r.Proof == ProofKi…` → `active.Values.Where(r => r.Proof ==…` | survived | 5 in `TraceCheckTests` | — | (a) sharpen `Grimoire.Fast.Tests.TraceCheckTests.CompleteCheck_Fails_WhenATestRequirementHasNoTest` — IV.3 requires this behaviour |
| 34 | Linq method mutation (OrderBy() to OrderByDescending()): `tests.OrderBy` → `tests.OrderByDescending` | survived | 7 in `TraceCheckTests` | — | (a) sharpen `Grimoire.Fast.Tests.TraceCheckTests.Check_Fails_WhenATestCarriesAnUnknownRetiredOrReservedId` — IV.3 requires this behaviour |

#### `tools/Grimoire.Trace/TraceDocument.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 16 | Statement mutation: `document.AppendLine("# Traceability");` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 16 | String mutation: `"# Traceability"` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 17 | Statement mutation: `document.AppendLine();` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 18 | Statement mutation: `document.AppendLine("<!-- Written by `dotne…` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 18 | String mutation: `"<!-- Written by `dotnet run --project tool…` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 19 | Statement mutation: `document.AppendLine();` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 20 | Statement mutation: `document.AppendLine("Every registered requi…` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 20 | String mutation: `"Every registered requirement, how it is pr…` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 21 | Statement mutation: `document.AppendLine("gate; this file is the…` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 21 | String mutation: `"gate; this file is the readable form of th…` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 22 | Statement mutation: `document.AppendLine();` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 23 | Statement mutation: `document.AppendLine("Status is `proven` whe…` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 23 | String mutation: `"Status is `proven` when a requirement prov…` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 24 | Statement mutation: `document.AppendLine("`unproven` when it has…` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 24 | String mutation: `"`unproven` when it has none, and `by revie…` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 25 | Statement mutation: `document.AppendLine("which no test can carr…` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 25 | String mutation: `"which no test can carry."` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 27 | Linq method mutation (Order() to OrderDescending()): `requirements.Select(r => r.Capability).Dist…` → `requirements.Select(r => r.Capabili…` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 29 | Statement mutation: `document.AppendLine();` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 30 | Statement mutation: `document.AppendLine(CultureInfo.InvariantCu…` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 31 | Statement mutation: `document.AppendLine();` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 32 | Statement mutation: `document.AppendLine("\| Requirement \| Proof …` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 32 | String mutation: `"\| Requirement \| Proof \| Tests \| Level \| St…` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 33 | Statement mutation: `document.AppendLine("\| --- \| --- \| --- \| --…` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 33 | String mutation: `"\| --- \| --- \| --- \| --- \| --- \|"` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 35 | Linq method mutation (ThenBy() to ThenByDescending()): `requirements .Where(r => r.Capability.Equal…` → `requirements .Where(r => r.Capabili…` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 35 | Linq method mutation (OrderBy() to OrderByDescending()): `requirements .Where(r => r.Capability.Equal…` → `requirements .Where(r => r.Capabili…` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 40 | Linq method mutation (OrderBy() to OrderByDescending()): `tests .Where(t => t.RequirementIds.Contains…` → `tests .Where(t => t.RequirementIds.…` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 45 | Statement mutation: `document.AppendLine(CultureInfo.InvariantCu…` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 49 | Linq method mutation (ThenBy() to ThenByDescending()): `tests.Where(t => t.RequirementIds.Count == …` → `tests.Where(t => t.RequirementIds.C…` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 49 | Linq method mutation (OrderBy() to OrderByDescending()): `tests.Where(t => t.RequirementIds.Count == …` → `tests.Where(t => t.RequirementIds.C…` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 54 | Equality mutation: `unattached.Length > 0` → `unattached.Length < 0` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 54 | Equality mutation: `unattached.Length > 0` → `unattached.Length >= 0` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 54 | Negate expression: `unattached.Length > 0` → `!(unattached.Length > 0)` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 56 | Statement mutation: `document.AppendLine();` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 57 | Statement mutation: `document.AppendLine("## Tests carrying no r…` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 57 | String mutation: `"## Tests carrying no requirement id"` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 58 | Statement mutation: `document.AppendLine();` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 59 | Statement mutation: `document.AppendLine("Allowed at Fast and Co…` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 59 | String mutation: `"Allowed at Fast and Contract (Constitution…` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 60 | Statement mutation: `document.AppendLine();` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 61 | Statement mutation: `document.AppendLine("\| Test \| Suite \| Level…` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 61 | String mutation: `"\| Test \| Suite \| Level \|"` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 62 | Statement mutation: `document.AppendLine("\| --- \| --- \| --- \|");` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 62 | String mutation: `"\| --- \| --- \| --- \|"` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 66 | Statement mutation: `document.AppendLine(CultureInfo.InvariantCu…` → `;` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 66 | Null coalescing mutation (remove left): `test.Level ?? "—"` → `"—"` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 66 | String mutation: `"—"` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 76 | Conditional (true) mutation: `proving.Length == 0 ? "—" : string.Join("<b…` → `(true?"—" :string.Join("<br>", prov…` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 76 | Conditional (false) mutation: `proving.Length == 0 ? "—" : string.Join("<b…` → `(false?"—" :string.Join("<br>", pro…` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 76 | Equality mutation: `proving.Length == 0` → `proving.Length != 0` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 76 | String mutation: `"—"` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 76 | String mutation: `"<br>"` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 76 | String mutation: `$"`{t.DisplayName}`"` → `$""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 80 | Linq method mutation (Order() to OrderDescending()): `proving.Select(t => t.Level ?? "—").Distinc…` → `proving.Select(t => t.Level ?? "—")…` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 80 | Null coalescing mutation (remove left): `t.Level ?? "—"` → `"—"` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 80 | String mutation: `"—"` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 81 | Conditional (true) mutation: `levels.Length == 0 ? "—" : string.Join(", "…` → `(true?"—" :string.Join(", ", levels…` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 81 | Conditional (false) mutation: `levels.Length == 0 ? "—" : string.Join(", "…` → `(false?"—" :string.Join(", ", level…` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 81 | Equality mutation: `levels.Length == 0` → `levels.Length != 0` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 81 | String mutation: `"—"` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 81 | String mutation: `", "` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 86 | Boolean mutation: `true` → `false` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 86 | String mutation: `"retired"` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 87 | String mutation: `"by review"` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 88 | String mutation: `"by eval"` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 89 | Conditional (true) mutation: `proving.Length > 0 ? "proven" : "unproven"` → `(true?"proven" :"unproven")` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 89 | Conditional (false) mutation: `proving.Length > 0 ? "proven" : "unproven"` → `(false?"proven" :"unproven")` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 89 | Equality mutation: `proving.Length > 0` → `proving.Length < 0` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 89 | Equality mutation: `proving.Length > 0` → `proving.Length >= 0` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 89 | String mutation: `"proven"` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |
| 89 | String mutation: `"unproven"` → `""` | not covered | — | — | (d) no test is asked for here: the owner leaves the CLI entry point and the `docs/trace.md` writer untested |

## Kind (a), by the test to sharpen

| Test to sharpen | Asked for by | Mutants | Acted on here |
| --- | --- | ---: | --- |
| write a test of `RepositoryLayout`, which finds the gate's two inputs<br><sub>`RepositoryLayout.cs`</sub> | IV.3 | 44 | no |
| write a test of `CapabilityRegistry.Read` against a directory<br><sub>`CapabilityRegistry.cs`</sub> | IV.3 | 4 | no |
| `Grimoire.Fast.Tests.TestCatalogueTests`<br><sub>`TestCatalogue.cs`</sub> | IV.3 | 3 | no |
| `Grimoire.Fast.Tests.AgentTranscriptTests.Init_RefusesTheSurface_WithoutTheWikiServerConnected`<br><sub>`AgentTranscript.cs`</sub> | GUARD-001 | 1 | no |
| `Grimoire.Fast.Tests.CapabilityRegistryTests.Read_RegistersTheRequirement_WhenTheRowReadsInFull`<br><sub>`CapabilityRegistry.cs`</sub> | IV.3 | 1 | no |
| `Grimoire.Fast.Tests.SubmissionStateTests.RunEnds_LeavesTheSubmissionDoneOrFailed`<br><sub>`Submission.cs`</sub> | RUNS-001 | 1 | no |
| `Grimoire.Fast.Tests.TraceCheckTests.Check_Fails_WhenATestCarriesAnUnknownRetiredOrReservedId`<br><sub>`TraceCheck.cs`</sub> | IV.3 | 1 | no |
| `Grimoire.Fast.Tests.TraceCheckTests.CompleteCheck_Fails_WhenATestRequirementHasNoTest`<br><sub>`TraceCheck.cs`</sub> | IV.3 | 1 | no |

Three groups were acted on in this pull request and are therefore not in the table above,
because the mutants are dead:

| Test sharpened | Asked for by | Mutants it killed |
| --- | --- | --- |
| `AgentTranscriptTests.Result_SaysTheAgentDidNotStopOfItsOwnAccord_WhenTheStreamWasAborted` (new) | GUARD-004 | `AgentTranscript.cs` 194 ×2 — an interrupted turn read as a clean stop |
| `ProvenanceStampTests` — five new failure tests, each asserting the reason and not only the refusal | WIKI-002 | `OkfFrontmatter.cs` 59, 61, 68, 104, 133, 158 ×2, 160 — a page whose frontmatter cannot be read, written anyway or refused without a reason |
| `ProvenanceStampTests.WritePage_ReplacesAgentValues_WhenTheRecordOpensTheFrontmatter` (new) | WIKI-002 | `OkfFrontmatter.cs` 99, 158 and `ProvenanceStamp.cs` 51 — a record on the first frontmatter line, refused or doubled instead of replaced |

## Proposals by kind

| Kind | Count |
| --- | ---: |
| (a) sharpen a test | 56 |
| (b) no requirement asks for the behaviour — candidate for removal | 30 |
| (c) equivalent mutant | 24 |
| (d) no test is asked for here (`tools/Grimoire.Trace`) | 130 |
| **Total** | **240** |

