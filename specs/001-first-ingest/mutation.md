# Mutation testing — first measurement

<!-- Read out of the reports `./scripts/mutation.sh` produces. Nothing here is a gate. -->

A measurement of feature `001-first-ingest`, taken on 2026-09-21 with Stryker.NET 5.0.0 against
the Fast suite. There is no threshold, no CI job and no score anyone has to reach. What it is
for is the last column: one reading per mutant that lived, of what the suite does not say.

No test and no line of production code was changed to take it, and no mutant is disabled.

## How to repeat it

```
./scripts/mutation.sh
```

Four Stryker runs, one per project in scope, about three minutes in total on the owner's
machine. The reports land in `StrykerOutput/<project>/reports/mutation-report.json`, which is
git-ignored.

## What was mutated, and with what

Stryker.NET 5.0.0 runs the suite through the Microsoft Testing Platform runner
(`"test-runner": "mtp"`), which is the runner xunit v3 uses on .NET 10. The runner is
Stryker's own preview feature; see the stability note below.

Scope is the code that holds decisions of ours. `Grimoire.Hub` is the composition root and is
left out, and so is every `Adapters/` folder — except the parser that turns the CLI's stream
lines into port events, which lives inside one. Stryker mutates one project under test per run,
so there are four runs; `Grimoire.Mutation.slnx` is `Grimoire.slnx` without the Contract and E2E
suites, which is what keeps the measurement to the Fast suite alone.

| In scope | How it is selected |
| --- | --- |
| `src/Grimoire.Runs` | whole project |
| `src/Grimoire.Agent`, without `Adapters/` | the three files outside `Adapters/` |
| the CLI stream parser, `src/Grimoire.Agent/Adapters/HarnessProcess.cs` | `SurfaceIsTheGrant`, and `ReadAsync` with every helper below it, as two character spans computed from the file |
| `src/Grimoire.Wiki`, without `Adapters/` | `!**/Adapters/**` |
| `tools/Grimoire.Trace` | whole project |

## Validity

108 tests, one test assembly — the Fast suite — in every run.

| Project | Created | Tested | Ignored | Not covered | Compile errors |
| --- | ---: | ---: | ---: | ---: | ---: |
| Grimoire.Runs | 67 | 43 | 18 | 3 | 3 |
| Grimoire.Agent | 210 | 16 | 96 | 28 | 70 |
| Grimoire.Wiki | 154 | 89 | 51 | 10 | 4 |
| Grimoire.Trace | 350 | 76 | 42 | 205 | 27 |

`Tested` is killed plus survived plus timed out. `Ignored` is Stryker's own two filters: the
block-already-covered filter, and — for `Grimoire.Agent` and `Grimoire.Wiki` — the mutate filter
that holds the run to the scope above. `Compile errors` are mutants Stryker generated and could
not build; they count towards nothing. Every project in scope has tested mutants.

### Files actually mutated

| Project | File | Created | Tested | Ignored | Not covered | Compile errors |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Grimoire.Runs | `src/Grimoire.Runs/RunStateMachine.cs` | 17 | 15 | 2 | 0 | 0 |
| Grimoire.Runs | `src/Grimoire.Runs/Submission.cs` | 21 | 10 | 8 | 2 | 1 |
| Grimoire.Runs | `src/Grimoire.Runs/SubmissionBoard.cs` | 29 | 18 | 8 | 1 | 2 |
| Grimoire.Agent | `src/Grimoire.Agent/Adapters/HarnessProcess.cs` | 194 | 0 | 96 | 28 | 70 |
| Grimoire.Agent | `src/Grimoire.Agent/Ceilings.cs` | 9 | 9 | 0 | 0 | 0 |
| Grimoire.Agent | `src/Grimoire.Agent/ToolGrant.cs` | 7 | 7 | 0 | 0 | 0 |
| Grimoire.Wiki | `src/Grimoire.Wiki/IWikiStore.cs` | 1 | 0 | 0 | 1 | 0 |
| Grimoire.Wiki | `src/Grimoire.Wiki/OkfFrontmatter.cs` | 69 | 50 | 12 | 4 | 3 |
| Grimoire.Wiki | `src/Grimoire.Wiki/ProvenanceStamp.cs` | 48 | 39 | 4 | 5 | 0 |
| Grimoire.Trace | `tools/Grimoire.Trace/CapabilityRegistry.cs` | 52 | 37 | 10 | 4 | 1 |
| Grimoire.Trace | `tools/Grimoire.Trace/Program.cs` | 70 | 0 | 0 | 54 | 16 |
| Grimoire.Trace | `tools/Grimoire.Trace/RepositoryLayout.cs` | 50 | 0 | 6 | 44 | 0 |
| Grimoire.Trace | `tools/Grimoire.Trace/Requirement.cs` | 1 | 1 | 0 | 0 | 0 |
| Grimoire.Trace | `tools/Grimoire.Trace/TestCatalogue.cs` | 48 | 4 | 11 | 31 | 2 |
| Grimoire.Trace | `tools/Grimoire.Trace/TraceCheck.cs` | 46 | 34 | 9 | 0 | 3 |
| Grimoire.Trace | `tools/Grimoire.Trace/TraceDocument.cs` | 83 | 0 | 6 | 72 | 5 |

One file in scope carries no mutant at all — `src/Grimoire.Agent/IAgentHarness.cs`, which is an
interface and two records. Every other file in scope that holds logic is in the table.
`src/Grimoire.Wiki/Adapters/FileSystemWikiStore.cs` and the process half of `HarnessProcess.cs`
are absent because the scope excludes them, not because they were missed.

One row is worth reading twice: the parser in `HarnessProcess.cs` has **no tested mutants at
all**. Every mutant in it is uncovered, filtered or a compile error, because the Fast suite
never reaches that file — `InMemoryAgentHarness` stands in for it and the Contract suite drives
the real one. The project it belongs to does have tested mutants, so the run is valid; but the
28 uncovered rows below say that against the Fast suite alone the parser is unmeasured, not that
it is unproven.

### Stability

The configuration was run three times. Five mutants out of 781 changed verdict between runs;
they are marked *unstable* in the table below, with the three verdicts they drew. The MTP runner
is a preview feature of Stryker and its own release notes carry fixes for coverage flakiness, so
read a single verdict as a reading and not as a fact. Eight further survivors — every mutant in
a `static readonly` initializer, in `StartUpInputs`, `ToolGrant`, `CapabilityRegistry` and
`TestCatalogue` — survive because Stryker cannot switch a mutant on before the type is
initialised, not because the suite is silent about them. They are the `(c)` rows that say so.

## Score

| Project | Score |
| --- | ---: |
| Grimoire.Runs | 76.09 % |
| Grimoire.Agent | 29.55 % |
| Grimoire.Wiki | 64.65 % |
| Grimoire.Trace | 21.00 % |

## Every mutant that lived

One row per survived or uncovered mutant. `Tests` are the tests Stryker recorded as covering the
line; `Req` are the requirement ids those tests carry, from `docs/trace.md`. A proposal is a
reading, not a change: nothing here has been acted on.

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
| 76 | String mutation: `$"a submission reading {state} cannot start run…` → `$""` | survived | `SubmissionStateTests.Transition_IsRefused_WhenTheSubmissionIsAlreadyDoneOrFailed` | RUNS-001 | (b) no requirement asks for this behaviour — candidate for removal |
| 91 | Statement mutation: `throw new ArgumentOutOfRangeException(nameof(te…` → `;` | not covered | — | — | (a) sharpen `Grimoire.Fast.Tests.SubmissionStateTests.RunEnds_LeavesTheSubmissionDoneOrFailed` — RUNS-001 requires this behaviour |
| 91 | String mutation: `"a run ends done or failed"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 101 | String mutation: `$"{state} is terminal; a submission does not le…` → `$""` | survived | `SubmissionStateTests.Transition_IsRefused_WhenTheSubmissionIsAlreadyDoneOrFailed` | RUNS-001 | (b) no requirement asks for this behaviour — candidate for removal |

#### `src/Grimoire.Runs/SubmissionBoard.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 9 | Boolean mutation: `true` → `false` | survived | — | — | (c) equivalent mutant, because the mutation sits in a `static readonly` initializer, which runs before Stryker switches the mutant on, so the mutated value never reaches the code under test; the suite does assert this value |
| 9 | Boolean mutation: `true` → `false` | survived | — | — | (c) equivalent mutant, because the mutation sits in a `static readonly` initializer, which runs before Stryker switches the mutant on, so the mutated value never reaches the code under test; the suite does assert this value |
| 65 | Linq method mutation (Reverse() to AsEnumerable()): `Enumerable.Reverse` → `Enumerable.AsEnumerable` | survived | `SubmissionAcceptanceTests.Submit_IsAccepted_WhenNoRunIsInProgress`, `SubmissionAcceptanceTests.Submit_IsNotStored_WhenRefused`, `SubmissionRefusalTests.Submit_IsNotStored_WhenRefused` | INGEST-001, INGEST-003, INGEST-004, INGEST-005 | (b) no requirement asks for this behaviour — candidate for removal |
| 81 | Equality mutation: `s.State is SubmissionState.Submitted or Submiss…` → `s.State is not SubmissionState.Submitte…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
### Grimoire.Agent

#### `src/Grimoire.Agent/Adapters/HarnessProcess.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 105 | Statement mutation: `ArgumentNullException.ThrowIfNull(grant);` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 106 | Statement mutation: `ArgumentNullException.ThrowIfNull(reported);` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 109 | Conditional (true) mutation: `t.StartsWith(McpPrefix, StringComparison.Ordina…` → `(true?t[McpPrefix.Length..] :t)` | not covered | — | — | (a) sharpen `Grimoire.Contract.Tests.HarnessProcessTests.Run_ReachesTheWikiAndNothingElse` — GUARD-001 requires this behaviour |
| 109 | Conditional (false) mutation: `t.StartsWith(McpPrefix, StringComparison.Ordina…` → `(false?t[McpPrefix.Length..] :t)` | not covered | — | — | (a) sharpen `Grimoire.Contract.Tests.HarnessProcessTests.Run_ReachesTheWikiAndNothingElse` — GUARD-001 requires this behaviour |
| 317 | Block removal mutation: `{ return JsonNode.Parse(line) as JsonObject; }` → `{}` | not covered | — | — | (a) sharpen `Grimoire.Contract.Tests.HarnessProcessTests.Nudge_ContinuesTheSameRun_AfterTheAgentStops` — RUNS-005 requires this behaviour |
| 321 | Block removal mutation: `{ // A line that is not JSON is not a message; …` → `{}` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 332 | Logical mutation: `SurfaceIsTheGrant(grant, Strings(init["tools"])…` → `SurfaceIsTheGrant(grant, Strings(init["…` | not covered | — | — | (a) sharpen `Grimoire.Contract.Tests.HarnessProcessTests.Run_ReachesTheWikiAndNothingElse` — GUARD-001 requires this behaviour |
| 332 | Logical mutation: `SurfaceIsTheGrant(grant, Strings(init["tools"])…` → `SurfaceIsTheGrant(grant, Strings(init["…` | not covered | — | — | (a) sharpen `Grimoire.Contract.Tests.HarnessProcessTests.Run_ReachesTheWikiAndNothingElse` — GUARD-001 requires this behaviour |
| 332 | String mutation: `"tools"` → `""` | not covered | — | — | (a) sharpen `Grimoire.Contract.Tests.HarnessProcessTests.Run_ReachesTheWikiAndNothingElse` — GUARD-001 requires this behaviour |
| 333 | String mutation: `"mcp_servers"` → `""` | not covered | — | — | (a) sharpen `Grimoire.Contract.Tests.HarnessProcessTests.Run_ReachesTheWikiAndNothingElse` — GUARD-001 requires this behaviour |
| 334 | String mutation: `"capabilities"` → `""` | not covered | — | — | (a) sharpen `Grimoire.Contract.Tests.HarnessProcessTests.Interrupt_EndsARunInFlight` — GUARD-004 requires this behaviour |
| 337 | Linq method mutation (Any() to All()): `listed.Any` → `listed.All` | not covered | — | — | (a) sharpen `Grimoire.Contract.Tests.HarnessProcessTests.Run_ReachesTheWikiAndNothingElse` — GUARD-001 requires this behaviour |
| 341 | Equality mutation: `described["name"]?.GetValue<string>() == Server…` → `described["name"]?.GetValue<string>() !…` | not covered | — | — | (a) sharpen `Grimoire.Contract.Tests.HarnessProcessTests.Run_ReachesTheWikiAndNothingElse` — GUARD-001 requires this behaviour |
| 341 | String mutation: `"name"` → `""` | not covered | — | — | (a) sharpen `Grimoire.Contract.Tests.HarnessProcessTests.Run_ReachesTheWikiAndNothingElse` — GUARD-001 requires this behaviour |
| 342 | Equality mutation: `described["status"]?.GetValue<string>() == "con…` → `described["status"]?.GetValue<string>()…` | not covered | — | — | (a) sharpen `Grimoire.Contract.Tests.HarnessProcessTests.Run_ReachesTheWikiAndNothingElse` — GUARD-001 requires this behaviour |
| 342 | String mutation: `"status"` → `""` | not covered | — | — | (a) sharpen `Grimoire.Contract.Tests.HarnessProcessTests.Run_ReachesTheWikiAndNothingElse` — GUARD-001 requires this behaviour |
| 342 | String mutation: `"connected"` → `""` | not covered | — | — | (a) sharpen `Grimoire.Contract.Tests.HarnessProcessTests.Run_ReachesTheWikiAndNothingElse` — GUARD-001 requires this behaviour |
| 351 | String mutation: `"event"` → `""` | not covered | — | — | (a) sharpen `Grimoire.Fast.Tests.CeilingTests.Cost_CountsTheFourTokenFields` — GUARD-004 requires this behaviour |
| 351 | String mutation: `"usage"` → `""` | not covered | — | — | (a) sharpen `Grimoire.Fast.Tests.CeilingTests.Cost_CountsTheFourTokenFields` — GUARD-004 requires this behaviour |
| 356 | String mutation: `"input_tokens"` → `""` | not covered | — | — | (a) sharpen `Grimoire.Fast.Tests.CeilingTests.Cost_CountsTheFourTokenFields` — GUARD-004 requires this behaviour |
| 357 | String mutation: `"output_tokens"` → `""` | not covered | — | — | (a) sharpen `Grimoire.Fast.Tests.CeilingTests.Cost_CountsTheFourTokenFields` — GUARD-004 requires this behaviour |
| 358 | String mutation: `"cache_read_input_tokens"` → `""` | not covered | — | — | (a) sharpen `Grimoire.Fast.Tests.CeilingTests.Cost_CountsTheFourTokenFields` — GUARD-004 requires this behaviour |
| 359 | String mutation: `"cache_creation_input_tokens"` → `""` | not covered | — | — | (a) sharpen `Grimoire.Fast.Tests.CeilingTests.Cost_CountsTheFourTokenFields` — GUARD-004 requires this behaviour |
| 388 | Equality mutation: `result["terminal_reason"]?.GetValue<string>() i…` → `result["terminal_reason"]?.GetValue<str…` | not covered | — | — | (a) sharpen `Grimoire.Fast.Tests.RunOutcomeTests.AgentStops_EndsTheRunFailed_WhenItDidNotStopOfItsOwnAccord` — RUNS-005 requires this behaviour |
| 388 | String mutation: `"terminal_reason"` → `""` | not covered | — | — | (a) sharpen `Grimoire.Fast.Tests.RunOutcomeTests.AgentStops_EndsTheRunFailed_WhenItDidNotStopOfItsOwnAccord` — RUNS-005 requires this behaviour |
| 388 | String mutation: `"aborted_streaming"` → `""` | not covered | — | — | (a) sharpen `Grimoire.Fast.Tests.RunOutcomeTests.AgentStops_EndsTheRunFailed_WhenItDidNotStopOfItsOwnAccord` — RUNS-005 requires this behaviour |
| 389 | String mutation: `"subtype"` → `""` | not covered | — | — | (a) sharpen `Grimoire.Fast.Tests.RunOutcomeTests.AgentStops_EndsTheRunFailed_WhenItDidNotStopOfItsOwnAccord` — RUNS-005 requires this behaviour |
| 389 | String mutation: `"success"` → `""` | not covered | — | — | (a) sharpen `Grimoire.Fast.Tests.RunOutcomeTests.AgentStops_EndsTheRunFailed_WhenItDidNotStopOfItsOwnAccord` — RUNS-005 requires this behaviour |

#### `src/Grimoire.Agent/ToolGrant.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 28 | String mutation: `"read_page"` → `""` | survived | — | — | (c) equivalent mutant, because the mutation sits in a `static readonly` initializer, which runs before Stryker switches the mutant on, so the mutated value never reaches the code under test; the suite does assert this value |
| 29 | String mutation: `"write_page"` → `""` | survived | — | — | (c) equivalent mutant, because the mutation sits in a `static readonly` initializer, which runs before Stryker switches the mutant on, so the mutated value never reaches the code under test; the suite does assert this value |
| 30 | String mutation: `"write_index"` → `""` | survived | — | — | (c) equivalent mutant, because the mutation sits in a `static readonly` initializer, which runs before Stryker switches the mutant on, so the mutated value never reaches the code under test; the suite does assert this value |
### Grimoire.Wiki

#### `src/Grimoire.Wiki/IWikiStore.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 8 | String mutation: `$"\"{path}\" is outside the wiki"` → `$""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |

#### `src/Grimoire.Wiki/OkfFrontmatter.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 59 | Logical mutation: `lines.Length == 0 \|\| lines[0].TrimEnd('\r') != …` → `lines.Length == 0 && lines[0].TrimEnd('…` | survived | 9 in `ProvenanceStampTests` | WIKI-002 | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_Fails_WhenThePageHasNoFrontmatterAtAll` — WIKI-002 requires this behaviour |
| 61 | String mutation: `"the page has no YAML frontmatter: it must open…` → `""` | survived | `ProvenanceStampTests.WritePage_Fails_WhenThePageHasNoFrontmatterAtAll` | WIKI-002 | (b) no requirement asks for this behaviour — candidate for removal |
| 66 | Equality mutation: `closing < 0` → `closing <= 0` | survived | 8 in `ProvenanceStampTests` | WIKI-002 | (c) equivalent mutant, because `Array.FindIndex` is started at index 1 here, so it returns -1 or an index of at least 1; 0 is not a value it can return |
| 68 | String mutation: `"the page's frontmatter is never closed: a line…` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 87 | UnaryMinusExpression to UnaryPlusExpression mutation: `-1` → `+1` | survived | 4 in `ProvenanceStampTests` | WIKI-002 | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_AddsTheRecord_WhenThePageHasNoPlaceForIt` — WIKI-002 requires this behaviour |
| 99 | Equality mutation: `at < 0` → `at <= 0` | survived | 4 in `ProvenanceStampTests` | WIKI-002 | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_ReplacesAgentValues_WhenTheRecordIsAlreadyThere` — WIKI-002 requires this behaviour |
| 104 | String mutation: `$"{GeneratedKey} was read from the frontmatter …` → `$""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 133 | String mutation: `"the page's frontmatter does not read as a mapp…` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 156 | UnaryMinusExpression to UnaryPlusExpression mutation: `-1` → `+1` | survived | 4 in `ProvenanceStampTests` | WIKI-002 | (c) equivalent mutant, because `end` is read only where `Span` returns a non-negative index, and on that path line 165 assigns it before it is read |
| 158 | Logical mutation: `at < 0 \|\| at >= lines.Length \|\| !lines[at].Trim…` → `at < 0 \|\| at >= lines.Length && !lines[…` | survived | 4 in `ProvenanceStampTests` | WIKI-002 | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_Fails_WhenTheRecordsPlaceCannotBeRead` — WIKI-002 requires this behaviour |
| 158 | Logical mutation: `at < 0 \|\| at >= lines.Length` → `at < 0 && at >= lines.Length` | survived | 4 in `ProvenanceStampTests` | WIKI-002 | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_Fails_WhenTheRecordsPlaceCannotBeRead` — WIKI-002 requires this behaviour |
| 158 | Equality mutation: `at < 0` → `at <= 0` | survived | 4 in `ProvenanceStampTests` | WIKI-002 | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_ReplacesAgentValues_WhenTheRecordIsAlreadyThere` — WIKI-002 requires this behaviour |
| 158 | Equality mutation: `at >= lines.Length` → `at > lines.Length` | survived | 4 in `ProvenanceStampTests` | WIKI-002 | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_Fails_WhenTheRecordsPlaceCannotBeRead` — WIKI-002 requires this behaviour |
| 158 | String mutation: `$"{GeneratedKey}:"` → `$""` | survived | 4 in `ProvenanceStampTests` | WIKI-002 | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_Fails_WhenTheRecordsPlaceCannotBeRead` — WIKI-002 requires this behaviour |
| 160 | UnaryMinusExpression to UnaryPlusExpression mutation: `-1` → `+1` | not covered | — | — | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_Fails_WhenTheRecordsPlaceCannotBeRead` — WIKI-002 requires this behaviour |

#### `src/Grimoire.Wiki/ProvenanceStamp.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 39 | Statement mutation: `ArgumentNullException.ThrowIfNull(page);` → `;` | survived | 9 in `ProvenanceStampTests` | WIKI-002 | (b) no requirement asks for this behaviour — candidate for removal |
| 40 | Statement mutation: `ArgumentNullException.ThrowIfNull(record);` → `;` | survived | 9 in `ProvenanceStampTests` | WIKI-002 | (b) no requirement asks for this behaviour — candidate for removal |
| 51 | Equality mutation: `frontmatter.GeneratedAt < 0` → `frontmatter.GeneratedAt <= 0` | survived | 7 in `ProvenanceStampTests` | WIKI-002 | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_ReplacesAgentValues_WhenTheRecordIsAlreadyThere` — WIKI-002 requires this behaviour |
| 81 | Logical mutation: `value.Length > 0 && !char.IsWhiteSpace(value[0]…` → `value.Length > 0 && !char.IsWhiteSpace(…` | survived | 7 in `ProvenanceStampTests` | WIKI-002 | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_AddsTheRecord_WhenTheFrontmatterIsEmpty` — WIKI-002 requires this behaviour |
| 81 | Logical mutation: `value.Length > 0 && !char.IsWhiteSpace(value[0]…` → `value.Length > 0 && !char.IsWhiteSpace(…` | survived | 7 in `ProvenanceStampTests` | WIKI-002 | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_AddsTheRecord_WhenTheFrontmatterIsEmpty` — WIKI-002 requires this behaviour |
| 81 | Logical mutation: `value.Length > 0 && !char.IsWhiteSpace(value[0]…` → `value.Length > 0 && !char.IsWhiteSpace(…` | survived | 7 in `ProvenanceStampTests` | WIKI-002 | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_AddsTheRecord_WhenTheFrontmatterIsEmpty` — WIKI-002 requires this behaviour |
| 81 | Logical mutation: `value.Length > 0 && !char.IsWhiteSpace(value[0]…` → `value.Length > 0 && !char.IsWhiteSpace(…` | survived | 7 in `ProvenanceStampTests` | WIKI-002 | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_AddsTheRecord_WhenTheFrontmatterIsEmpty` — WIKI-002 requires this behaviour |
| 81 | Logical mutation: `value.Length > 0 && !char.IsWhiteSpace(value[0]…` → `value.Length > 0 && !char.IsWhiteSpace(…` | survived | 7 in `ProvenanceStampTests` | WIKI-002 | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_AddsTheRecord_WhenTheFrontmatterIsEmpty` — WIKI-002 requires this behaviour |
| 81 | Logical mutation: `value.Length > 0 && !char.IsWhiteSpace(value[0]…` → `value.Length > 0 && !char.IsWhiteSpace(…` | survived | 7 in `ProvenanceStampTests` | WIKI-002 | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_AddsTheRecord_WhenTheFrontmatterIsEmpty` — WIKI-002 requires this behaviour |
| 81 | Logical mutation: `value.Length > 0 && !char.IsWhiteSpace(value[0])` → `value.Length > 0 \|\| !char.IsWhiteSpace(…` | survived | 7 in `ProvenanceStampTests` | WIKI-002 | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_AddsTheRecord_WhenTheFrontmatterIsEmpty` — WIKI-002 requires this behaviour |
| 81 | Equality mutation: `value.Length > 0` → `value.Length >= 0` | survived | 7 in `ProvenanceStampTests` | WIKI-002 | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_AddsTheRecord_WhenTheFrontmatterIsEmpty` — WIKI-002 requires this behaviour |
| 88 | Equality mutation: `"-?:,[]{}#&*!\|>'\"%@`".IndexOf(value[0]) < 0` → `"-?:,[]{}#&*!\|>'\"%@`".IndexOf(value[0]…` | survived | 7 in `ProvenanceStampTests` | WIKI-002 | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_AddsTheRecord_WhenTheFrontmatterIsEmpty` — WIKI-002 requires this behaviour |
| 88 | String mutation: `"-?:,[]{}#&*!\|>'\"%@`"` → `""` | survived | 7 in `ProvenanceStampTests` | WIKI-002 | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_AddsTheRecord_WhenTheFrontmatterIsEmpty` — WIKI-002 requires this behaviour |
| 90 | Conditional (true) mutation: `plain ? value : $"\"{value.Replace("\\", "\\\\"…` → `(true?value :$"\"{value.Replace("\\", "…` | survived | 7 in `ProvenanceStampTests` | WIKI-002 | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_AddsTheRecord_WhenTheFrontmatterIsEmpty` — WIKI-002 requires this behaviour |
| 90 | String mutation: `$"\"{value.Replace("\\", "\\\\", StringComparis…` → `$""` | not covered | — | — | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_AddsTheRecord_WhenTheFrontmatterIsEmpty` — WIKI-002 requires this behaviour |
| 90 | String mutation: `"\\"` → `""` | not covered | — | — | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_AddsTheRecord_WhenTheFrontmatterIsEmpty` — WIKI-002 requires this behaviour |
| 90 | String mutation: `"\\\\"` → `""` | not covered | — | — | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_AddsTheRecord_WhenTheFrontmatterIsEmpty` — WIKI-002 requires this behaviour |
| 90 | String mutation: `"\""` → `""` | not covered | — | — | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_AddsTheRecord_WhenTheFrontmatterIsEmpty` — WIKI-002 requires this behaviour |
| 90 | String mutation: `"\\\""` → `""` | not covered | — | — | (a) sharpen `Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_AddsTheRecord_WhenTheFrontmatterIsEmpty` — WIKI-002 requires this behaviour |
### Grimoire.Trace

No registered requirement names `Grimoire.Trace`: it is the traceability gate itself, asked for by Constitution IV.3 and IV.4 rather than by a capability. Proposal (a) is therefore unavailable here by construction, and (b) should be read as what it says — no registered requirement asks for this — and not as a recommendation to delete the gate.

#### `tools/Grimoire.Trace/CapabilityRegistry.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 30 | String mutation: `"DEC"` → `""` | survived | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 38 | Conditional (false) mutation: `dash < 0 ? requirementId : requirementId[..dash]` → `(false?requirementId :requirementId[..d…` | survived | 9 in `CapabilityRegistryTests`, `TraceCheckTests` | — | (c) equivalent mutant, because every id that reaches `CapabilityOf` comes from the registered-id shape or from a `req` trait carrying one, so it holds a dash and the false branch is the branch taken |
| 38 | Equality mutation: `dash < 0` → `dash <= 0` | survived *(unstable: survived, killed, survived)* | 9 in `CapabilityRegistryTests`, `TraceCheckTests` | — | (c) equivalent mutant, because a dash at index 0 would mean an id beginning with `-`, which the registered-id shape does not admit |
| 45 | Statement mutation: `throw new TraceInputException($"no capability f…` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 45 | String mutation: `$"no capability files: {capabilitiesDirectory} …` → `$""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 50 | Linq method mutation (Order() to OrderDescending()): `Directory.GetFiles(capabilitiesDirectory, "*.md…` → `Directory.GetFiles(capabilitiesDirector…` | survived | 5 in `CapabilityRegistryTests` | — | (b) no requirement asks for this behaviour — candidate for removal |
| 50 | String mutation: `"*.md"` → `""` | survived | 5 in `CapabilityRegistryTests` | — | (b) no requirement asks for this behaviour — candidate for removal |
| 52 | Boolean mutation: `false` → `true` | survived | 5 in `CapabilityRegistryTests` | — | (b) no requirement asks for this behaviour — candidate for removal |
| 63 | Statement mutation: `continue;` → `;` | survived | 5 in `CapabilityRegistryTests` | — | (c) equivalent mutant, because a line beginning with `#` matches neither `RequirementRow` nor `RequirementRowOpening`, so continuing and falling through end the same way |
| 105 | Conditional (true) mutation: `columns.Length <= 2 ? string.Empty : string.Joi…` → `(true?string.Empty :string.Join('\|', co…` | survived | `CapabilityRegistryTests.Read_Fails_WhenAnIdIsRegisteredTwice`, `CapabilityRegistryTests.Read_MarksTheRequirementRetired_WhenTheRowSitsUnderRetired`, `CapabilityRegistryTests.Read_RegistersTheRequirement_WhenTheRowReadsInFull` | — | (b) no requirement asks for this behaviour — candidate for removal |
| 105 | Conditional (false) mutation: `columns.Length <= 2 ? string.Empty : string.Joi…` → `(false?string.Empty :string.Join('\|', c…` | survived | `CapabilityRegistryTests.Read_Fails_WhenAnIdIsRegisteredTwice`, `CapabilityRegistryTests.Read_MarksTheRequirementRetired_WhenTheRowSitsUnderRetired`, `CapabilityRegistryTests.Read_RegistersTheRequirement_WhenTheRowReadsInFull` | — | (b) no requirement asks for this behaviour — candidate for removal |
| 105 | Equality mutation: `columns.Length <= 2` → `columns.Length > 2` | survived | `CapabilityRegistryTests.Read_Fails_WhenAnIdIsRegisteredTwice`, `CapabilityRegistryTests.Read_MarksTheRequirementRetired_WhenTheRowSitsUnderRetired`, `CapabilityRegistryTests.Read_RegistersTheRequirement_WhenTheRowReadsInFull` | — | (b) no requirement asks for this behaviour — candidate for removal |
| 105 | Equality mutation: `columns.Length <= 2` → `columns.Length < 2` | survived | `CapabilityRegistryTests.Read_Fails_WhenAnIdIsRegisteredTwice`, `CapabilityRegistryTests.Read_MarksTheRequirementRetired_WhenTheRowSitsUnderRetired`, `CapabilityRegistryTests.Read_RegistersTheRequirement_WhenTheRowReadsInFull` | — | (b) no requirement asks for this behaviour — candidate for removal |
| 106 | String mutation: `string.Empty` → `"Stryker was here!"` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 113 | String mutation: `"eval"` → `""` | survived | `CapabilityRegistryTests.Read_Fails_WhenAnIdIsRegisteredTwice`, `CapabilityRegistryTests.Read_MarksTheRequirementRetired_WhenTheRowSitsUnderRetired`, `CapabilityRegistryTests.Read_RegistersTheRequirement_WhenTheRowReadsInFull` | — | (b) no requirement asks for this behaviour — candidate for removal |
| 114 | String mutation: `"review"` → `""` | survived | `CapabilityRegistryTests.Read_Fails_WhenAnIdIsRegisteredTwice`, `CapabilityRegistryTests.Read_MarksTheRequirementRetired_WhenTheRowSitsUnderRetired`, `CapabilityRegistryTests.Read_RegistersTheRequirement_WhenTheRowReadsInFull` | — | (b) no requirement asks for this behaviour — candidate for removal |
| 116 | String mutation: `$"{id} in {Path.GetFileName(file)} declares pro…` → `$""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |

#### `tools/Grimoire.Trace/Program.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 15 | Conditional (true) mutation: `args.Length > 0 ? args[0] : null` → `(true?args[0] :null)` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 15 | Conditional (false) mutation: `args.Length > 0 ? args[0] : null` → `(false?args[0] :null)` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 15 | Equality mutation: `args.Length > 0` → `args.Length < 0` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 15 | Equality mutation: `args.Length > 0` → `args.Length >= 0` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 17 | Equality mutation: `verb is not ("check" or "write")` → `verb is ("check" or "write")` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 17 | Logical mutation: `"check" or "write"` → `"check" and "write"` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 17 | String mutation: `"check"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 17 | String mutation: `"write"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 19 | Statement mutation: `Console.Error.WriteLine(Usage);` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 29 | Boolean mutation: `false` → `true` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 31 | Equality mutation: `at < args.Length` → `at > args.Length` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 31 | Equality mutation: `at < args.Length` → `at <= args.Length` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 31 | PostIncrementExpression to PostDecrementExpression mutation: `at++` → `at--` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 35 | String mutation: `"--complete"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 35 | Equality mutation: `verb == "check"` → `verb != "check"` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 35 | String mutation: `"check"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 36 | Boolean mutation: `true` → `false` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 40 | Statement mutation: `Console.Error.WriteLine("trace-check: --complet…` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 40 | String mutation: `"trace-check: --complete belongs to `check`; `w…` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 41 | Statement mutation: `Console.Error.WriteLine(Usage);` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 44 | String mutation: `"--configuration"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 44 | Logical mutation: `at + 1 < args.Length && !args[at + 1].StartsWit…` → `at + 1 < args.Length \|\| !args[at + 1].S…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 44 | Equality mutation: `at + 1 < args.Length` → `at + 1 > args.Length` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 44 | Equality mutation: `at + 1 < args.Length` → `at + 1 <= args.Length` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 44 | Arithmetic mutation: `at + 1` → `at - 1` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 44 | LogicalNotExpression to un-LogicalNotExpression mutation: `!args[at + 1].StartsWith("--", StringComparison…` → `args[at + 1].StartsWith("--", StringCom…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 44 | Arithmetic mutation: `at + 1` → `at - 1` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 44 | String mutation: `"--"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 45 | PreIncrementExpression to PreDecrementExpression mutation: `++at` → `--at` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 49 | Statement mutation: `Console.Error.WriteLine("trace-check: --configu…` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 49 | String mutation: `"trace-check: --configuration needs a value, De…` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 50 | Statement mutation: `Console.Error.WriteLine(Usage);` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 54 | Statement mutation: `Console.Error.WriteLine($"trace-check: unrecogn…` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 54 | String mutation: `$"trace-check: unrecognised argument \"{args[at…` → `$""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 55 | Statement mutation: `Console.Error.WriteLine(Usage);` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 64 | Equality mutation: `verb == "write"` → `verb != "write"` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 64 | String mutation: `"write"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 66 | Statement mutation: `File.WriteAllText(layout.TraceDocument, TraceDo…` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 67 | Statement mutation: `Console.WriteLine($"wrote {Path.GetRelativePath…` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 67 | String mutation: `$"wrote {Path.GetRelativePath(layout.Root, layo…` → `$""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 72 | Equality mutation: `violations.Count > 0` → `violations.Count >= 0` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 72 | Negate expression: `violations.Count > 0` → `!(violations.Count > 0)` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 74 | Statement mutation: `Console.Error.WriteLine($"trace-check: {violati…` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 74 | String mutation: `$"trace-check: {violations.Count} violation(s)"` → `$""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 77 | Statement mutation: `Console.Error.WriteLine($" {violation}");` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 77 | String mutation: `$" {violation}"` → `$""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 83 | Statement mutation: `Console.WriteLine( $"trace-check{(complete ? " …` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 84 | String mutation: `$"trace-check{(complete ? " --complete" : strin…` → `$""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 84 | Conditional (true) mutation: `complete ? " --complete" : string.Empty` → `(true?" --complete" :string.Empty)` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 84 | Conditional (false) mutation: `complete ? " --complete" : string.Empty` → `(false?" --complete" :string.Empty)` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 84 | String mutation: `" --complete"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 84 | String mutation: `string.Empty` → `"Stryker was here!"` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 89 | Statement mutation: `Console.Error.WriteLine($"trace-check: {failure…` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 89 | String mutation: `$"trace-check: {failure.Message}"` → `$""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |

#### `tools/Grimoire.Trace/RepositoryLayout.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 18 | String mutation: `"docs"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 18 | String mutation: `"capabilities"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 20 | String mutation: `"docs"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 20 | String mutation: `"trace.md"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 24 | Equality mutation: `directory is not null` → `directory is null` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 26 | Negate expression: `File.Exists(Path.Combine(directory.FullName, So…` → `!(File.Exists(Path.Combine(directory.Fu…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 27 | Block removal mutation: `{ return new RepositoryLayout(directory.FullNam…` → `{}` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 32 | Statement mutation: `throw new TraceInputException($"no {SolutionFil…` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 32 | String mutation: `$"no {SolutionFile} in {Environment.CurrentDire…` → `$""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 42 | Linq method mutation (Order() to OrderDescending()): `Directory.GetDirectories(Path.Combine(Root, "te…` → `Directory.GetDirectories(Path.Combine(R…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 42 | String mutation: `"tests"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 43 | Equality mutation: `Directory.GetFiles(d, "*.csproj").Length == 1` → `Directory.GetFiles(d, "*.csproj").Lengt…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 43 | String mutation: `"*.csproj"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 49 | Equality mutation: `suites.Length == 0` → `suites.Length != 0` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 51 | Statement mutation: `throw new TraceInputException($"no test project…` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 51 | String mutation: `$"no test projects under {Path.Combine(Root, "t…` → `$""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 51 | String mutation: `"tests"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 54 | Conditional (true) mutation: `configuration is null ? (string[])["Release", "…` → `(true?(string[])["Release", "Debug"] :[…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 54 | Conditional (false) mutation: `configuration is null ? (string[])["Release", "…` → `(false?(string[])["Release", "Debug"] :…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 54 | Equality mutation: `configuration is null` → `configuration is not null` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 54 | String mutation: `"Release"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 54 | String mutation: `"Debug"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 57 | Negate expression: `Array.TrueForAll(found, f => f.Path is not null)` → `!(Array.TrueForAll(found, f => f.Path i…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 57 | Equality mutation: `f.Path is not null` → `f.Path is null` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 58 | Block removal mutation: `{ return [.. found.Select(f => new TestAssembly…` → `{}` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 63 | String mutation: `", "` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 63 | Equality mutation: `Locate(s, configuration ?? "Debug") is null` → `Locate(s, configuration ?? "Debug") is …` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 63 | Null coalescing mutation (remove left): `configuration ?? "Debug"` → `"Debug"` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 63 | String mutation: `"Debug"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 64 | Statement mutation: `throw new TraceInputException( $"not built: {mi…` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 65 | String mutation: `$"not built: {missing}. Run `dotnet build {Solu…` → `$""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 70 | String mutation: `"tests"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 70 | String mutation: `"bin"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 71 | LogicalNotExpression to un-LogicalNotExpression mutation: `!Directory.Exists(output)` → `Directory.Exists(output)` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 72 | Block removal mutation: `{ return null; }` → `{}` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 77 | Linq method mutation (Order() to OrderDescending()): `Directory.GetFiles(output, $"{suite}.dll", Sear…` → `Directory.GetFiles(output, $"{suite}.dl…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 77 | String mutation: `$"{suite}.dll"` → `$""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 81 | Conditional (true) mutation: `assemblies.Length == 1 ? assemblies[0] : assemb…` → `(true?assemblies[0] :assemblies.Length …` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 81 | Conditional (false) mutation: `assemblies.Length == 1 ? assemblies[0] : assemb…` → `(false?assemblies[0] :assemblies.Length…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 81 | Equality mutation: `assemblies.Length == 1` → `assemblies.Length != 1` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 83 | Conditional (true) mutation: `assemblies.Length == 0 ? null : throw new Trace…` → `(true?null :throw new TraceInputExcepti…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 83 | Conditional (false) mutation: `assemblies.Length == 0 ? null : throw new Trace…` → `(false?null :throw new TraceInputExcept…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 83 | Equality mutation: `assemblies.Length == 0` → `assemblies.Length != 0` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 84 | String mutation: `$"{suite} is built for more than one target fra…` → `$""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |

#### `tools/Grimoire.Trace/TestCatalogue.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 16 | String mutation: `"contract"` → `""` | survived | — | — | (c) equivalent mutant, because the mutation sits in a `static readonly` initializer, which runs before Stryker switches the mutant on, so the mutated value never reaches the code under test; the suite does assert this value |
| 16 | String mutation: `"e2e"` → `""` | survived | — | — | (c) equivalent mutant, because the mutation sits in a `static readonly` initializer, which runs before Stryker switches the mutant on, so the mutated value never reaches the code under test; the suite does assert this value |
| 24 | Statement mutation: `tests.AddRange(ReadOne(assembly));` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 36 | Linq method mutation (Concat() to Except()): `Directory.GetFiles(directory, "*.dll") .Concat` → `Directory.GetFiles(directory, "*.dll") …` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 36 | String mutation: `"*.dll"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 37 | String mutation: `"*.dll"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 39 | Linq method mutation (First() to FirstOrDefault()): `g.First` → `g.FirstOrDefault` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 49 | Bitwise mutation: `BindingFlags.Public \| BindingFlags.NonPublic \| …` → `BindingFlags.Public \| BindingFlags.NonP…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 49 | Bitwise mutation: `BindingFlags.Public \| BindingFlags.NonPublic \| …` → `BindingFlags.Public \| BindingFlags.NonP…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 49 | Bitwise mutation: `BindingFlags.Public \| BindingFlags.NonPublic \| …` → `BindingFlags.Public \| BindingFlags.NonP…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 49 | Bitwise mutation: `BindingFlags.Public \| BindingFlags.NonPublic` → `BindingFlags.Public & BindingFlags.NonP…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 52 | LogicalNotExpression to un-LogicalNotExpression mutation: `!IsTest(attributes)` → `IsTest(attributes)` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 54 | Statement mutation: `continue;` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 57 | Linq method mutation (Concat() to Except()): `typeTraits.Concat` → `typeTraits.Except` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 59 | Statement mutation: `yield return new TestMethod( assembly.Suite, ty…` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 61 | Null coalescing mutation (remove left): `type.FullName ?? type.Name` → `type.Name` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 64 | Linq method mutation (Order() to OrderDescending()): `traits.Where(t => t.Name.Equals("req", StringCo…` → `traits.Where(t => t.Name.Equals("req", …` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 64 | String mutation: `"req"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 74 | Linq method mutation (Any() to All()): `attributes.Any` → `attributes.All` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 78 | Equality mutation: `type is not null` → `type is null` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 80 | Equality mutation: `type.FullName == FactAttribute` → `type.FullName != FactAttribute` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 82 | Boolean mutation: `true` → `false` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 86 | Boolean mutation: `false` → `true` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 96 | Block removal mutation: `{ return type.BaseType; }` → `{}` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 100 | Block removal mutation: `{ return null; }` → `{}` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 107 | Linq method mutation (FirstOrDefault() to First()): `traits .Where(t => t.Name.Equals("level", Strin…` → `traits .Where(t => t.Name.Equals("level…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 108 | String mutation: `"level"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 117 | Logical mutation: `a.AttributeType.FullName == TraitAttribute && a…` → `a.AttributeType.FullName == TraitAttrib…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 117 | Equality mutation: `a.AttributeType.FullName == TraitAttribute` → `a.AttributeType.FullName != TraitAttrib…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 119 | Null coalescing mutation (remove left): `a.ConstructorArguments[0].Value as string ?? st…` → `string.Empty` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 119 | String mutation: `string.Empty` → `"Stryker was here!"` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 120 | Null coalescing mutation (remove left): `a.ConstructorArguments[1].Value as string ?? st…` → `string.Empty` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 120 | String mutation: `string.Empty` → `"Stryker was here!"` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |

#### `tools/Grimoire.Trace/TraceCheck.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 25 | Linq method mutation (OrderBy() to OrderByDescending()): `active.Values.Where(r => r.Proof == ProofKind.T…` → `active.Values.Where(r => r.Proof == Pro…` | survived | 5 in `TraceCheckTests` | — | (b) no requirement asks for this behaviour — candidate for removal |
| 34 | Linq method mutation (OrderBy() to OrderByDescending()): `tests.OrderBy` → `tests.OrderByDescending` | survived *(unstable: survived, killed, survived)* | 7 in `TraceCheckTests` | — | (b) no requirement asks for this behaviour — candidate for removal |

#### `tools/Grimoire.Trace/TraceDocument.cs`

| Line | Mutation | Status | Tests | Req | Proposal |
| ---: | --- | --- | --- | --- | --- |
| 16 | Statement mutation: `document.AppendLine("# Traceability");` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 16 | String mutation: `"# Traceability"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 17 | Statement mutation: `document.AppendLine();` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 18 | Statement mutation: `document.AppendLine("<!-- Written by `dotnet ru…` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 18 | String mutation: `"<!-- Written by `dotnet run --project tools/Gr…` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 19 | Statement mutation: `document.AppendLine();` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 20 | Statement mutation: `document.AppendLine("Every registered requireme…` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 20 | String mutation: `"Every registered requirement, how it is proven…` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 21 | Statement mutation: `document.AppendLine("gate; this file is the rea…` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 21 | String mutation: `"gate; this file is the readable form of the sa…` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 22 | Statement mutation: `document.AppendLine();` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 23 | Statement mutation: `document.AppendLine("Status is `proven` when a …` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 23 | String mutation: `"Status is `proven` when a requirement proven b…` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 24 | Statement mutation: `document.AppendLine("`unproven` when it has non…` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 24 | String mutation: `"`unproven` when it has none, and `by review` o…` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 25 | Statement mutation: `document.AppendLine("which no test can carry.");` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 25 | String mutation: `"which no test can carry."` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 27 | Linq method mutation (Order() to OrderDescending()): `requirements.Select(r => r.Capability).Distinct…` → `requirements.Select(r => r.Capability).…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 29 | Statement mutation: `document.AppendLine();` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 30 | Statement mutation: `document.AppendLine(CultureInfo.InvariantCultur…` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 31 | Statement mutation: `document.AppendLine();` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 32 | Statement mutation: `document.AppendLine("\| Requirement \| Proof \| Te…` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 32 | String mutation: `"\| Requirement \| Proof \| Tests \| Level \| Status…` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 33 | Statement mutation: `document.AppendLine("\| --- \| --- \| --- \| --- \| …` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 33 | String mutation: `"\| --- \| --- \| --- \| --- \| --- \|"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 35 | Linq method mutation (ThenBy() to ThenByDescending()): `requirements .Where(r => r.Capability.Equals(ca…` → `requirements .Where(r => r.Capability.E…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 35 | Linq method mutation (OrderBy() to OrderByDescending()): `requirements .Where(r => r.Capability.Equals(ca…` → `requirements .Where(r => r.Capability.E…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 40 | Linq method mutation (OrderBy() to OrderByDescending()): `tests .Where(t => t.RequirementIds.Contains(req…` → `tests .Where(t => t.RequirementIds.Cont…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 45 | Statement mutation: `document.AppendLine(CultureInfo.InvariantCultur…` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 49 | Linq method mutation (ThenBy() to ThenByDescending()): `tests.Where(t => t.RequirementIds.Count == 0) .…` → `tests.Where(t => t.RequirementIds.Count…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 49 | Linq method mutation (OrderBy() to OrderByDescending()): `tests.Where(t => t.RequirementIds.Count == 0) .…` → `tests.Where(t => t.RequirementIds.Count…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 54 | Equality mutation: `unattached.Length > 0` → `unattached.Length < 0` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 54 | Equality mutation: `unattached.Length > 0` → `unattached.Length >= 0` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 54 | Negate expression: `unattached.Length > 0` → `!(unattached.Length > 0)` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 56 | Statement mutation: `document.AppendLine();` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 57 | Statement mutation: `document.AppendLine("## Tests carrying no requi…` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 57 | String mutation: `"## Tests carrying no requirement id"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 58 | Statement mutation: `document.AppendLine();` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 59 | Statement mutation: `document.AppendLine("Allowed at Fast and Contra…` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 59 | String mutation: `"Allowed at Fast and Contract (Constitution III…` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 60 | Statement mutation: `document.AppendLine();` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 61 | Statement mutation: `document.AppendLine("\| Test \| Suite \| Level \|");` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 61 | String mutation: `"\| Test \| Suite \| Level \|"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 62 | Statement mutation: `document.AppendLine("\| --- \| --- \| --- \|");` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 62 | String mutation: `"\| --- \| --- \| --- \|"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 66 | Statement mutation: `document.AppendLine(CultureInfo.InvariantCultur…` → `;` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 66 | Null coalescing mutation (remove left): `test.Level ?? "—"` → `"—"` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 66 | String mutation: `"—"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 76 | Conditional (true) mutation: `proving.Length == 0 ? "—" : string.Join("<br>",…` → `(true?"—" :string.Join("<br>", proving.…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 76 | Conditional (false) mutation: `proving.Length == 0 ? "—" : string.Join("<br>",…` → `(false?"—" :string.Join("<br>", proving…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 76 | Equality mutation: `proving.Length == 0` → `proving.Length != 0` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 76 | String mutation: `"—"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 76 | String mutation: `"<br>"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 76 | String mutation: `$"`{t.DisplayName}`"` → `$""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 80 | Linq method mutation (Order() to OrderDescending()): `proving.Select(t => t.Level ?? "—").Distinct(St…` → `proving.Select(t => t.Level ?? "—").Dis…` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 80 | Null coalescing mutation (remove left): `t.Level ?? "—"` → `"—"` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 80 | String mutation: `"—"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 81 | Conditional (true) mutation: `levels.Length == 0 ? "—" : string.Join(", ", le…` → `(true?"—" :string.Join(", ", levels))` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 81 | Conditional (false) mutation: `levels.Length == 0 ? "—" : string.Join(", ", le…` → `(false?"—" :string.Join(", ", levels))` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 81 | Equality mutation: `levels.Length == 0` → `levels.Length != 0` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 81 | String mutation: `"—"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 81 | String mutation: `", "` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 86 | Boolean mutation: `true` → `false` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 86 | String mutation: `"retired"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 87 | String mutation: `"by review"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 88 | String mutation: `"by eval"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 89 | Conditional (true) mutation: `proving.Length > 0 ? "proven" : "unproven"` → `(true?"proven" :"unproven")` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 89 | Conditional (false) mutation: `proving.Length > 0 ? "proven" : "unproven"` → `(false?"proven" :"unproven")` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 89 | Equality mutation: `proving.Length > 0` → `proving.Length < 0` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 89 | Equality mutation: `proving.Length > 0` → `proving.Length >= 0` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 89 | String mutation: `"proven"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |
| 89 | String mutation: `"unproven"` → `""` | not covered | — | — | (b) no requirement asks for this behaviour — candidate for removal |

### Marked unstable but killed in the reported run

| Project | File | Line | Mutation | Verdicts across three runs |
| --- | --- | ---: | --- | --- |
| Grimoire.Agent | `src/Grimoire.Agent/ToolGrant.cs` | 27 | String mutation: `"list_pages",` → `""` | killed, survived, survived |
| Grimoire.Agent | `src/Grimoire.Agent/ToolGrant.cs` | 31 | String mutation: `"append_log",` → `""` | killed, survived, survived |
| Grimoire.Trace | `tools/Grimoire.Trace/TestCatalogue.cs` | 16 | String mutation: `"fast", "contract", "e2e", "deploy"];` → `""` | killed, survived, survived |

## Proposals by kind

| Kind | Count |
| --- | ---: |
| (a) sharpen a test — a registered requirement asks for the behaviour | 52 |
| (b) no requirement asks for the behaviour — candidate for removal | 235 |
| (c) equivalent mutant | 12 |
| **Total** | **299** |

