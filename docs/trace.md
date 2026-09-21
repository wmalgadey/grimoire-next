# Traceability

<!-- Written by `dotnet run --project tools/Grimoire.Trace -- write`. Do not edit by hand. -->

Every registered requirement, how it is proven, and what proves it. `trace-check` is the
gate; this file is the readable form of the same two inputs.

Status is `proven` when a requirement proven by test has at least one test carrying its id,
`unproven` when it has none, and `by review` or `by eval` for the other two proof kinds,
which no test can carry.

## ACCESS

| Requirement | Proof | Tests | Level | Status |
| --- | --- | --- | --- | --- |
| ACCESS-001 | test | `Grimoire.E2E.Tests.SubmitTextTests.Submit_ReportsTheSubmissionAccepted` | e2e | proven |
| ACCESS-002 | test | `Grimoire.E2E.Tests.SubmissionStatesTests.List_ShowsEachSubmissionInItsState`<br>`Grimoire.E2E.Tests.SubmissionStatesTests.List_ShowsNothingBeyondTheState`<br>`Grimoire.Fast.Tests.SubmissionStateTests.Report_CarriesNothingBeyondTheState`<br>`Grimoire.Fast.Tests.SubmissionStateTests.Report_NamesEachStateAsOneOfTheFour` | e2e, fast | proven |

## GUARD

| Requirement | Proof | Tests | Level | Status |
| --- | --- | --- | --- | --- |
| GUARD-001 | test | `Grimoire.Contract.Tests.FileSystemWikiStoreTests.ReadPage_IsRefused_WhenThePathLeavesTheWiki`<br>`Grimoire.Contract.Tests.FileSystemWikiStoreTests.WritePage_IsRefused_WhenThePathLeavesTheWiki`<br>`Grimoire.Contract.Tests.HarnessProcessTests.Run_ReachesTheWikiAndNothingElse`<br>`Grimoire.Fast.Tests.ToolGrantTests.AgentReportsASurfaceOutsideTheGrant_EndsTheRunBeforeItsFirstModelCall`<br>`Grimoire.Fast.Tests.ToolGrantTests.AgentReportsASurfaceOutsideTheGrant_EndsTheRunFailed`<br>`Grimoire.Fast.Tests.ToolGrantTests.Surface_IsNotTheGrant_WithAToolOutsideIt`<br>`Grimoire.Fast.Tests.ToolGrantTests.Surface_IsNotTheGrant_WithoutOneOfTheGrantedNames`<br>`Grimoire.Fast.Tests.ToolGrantTests.Surface_IsTheGrant_WhenItHoldsExactlyTheGrantedNames` | contract, fast | proven |
| GUARD-002 | test | `Grimoire.Contract.Tests.HarnessProcessTests.Run_ReachesTheWikiAndNothingElse`<br>`Grimoire.Fast.Tests.ToolGrantTests.Endpoint_HasNoHandler_ForANameOutsideTheGrant`<br>`Grimoire.Fast.Tests.ToolGrantTests.Endpoint_ServesExactlyTheGrantedNames`<br>`Grimoire.Fast.Tests.ToolGrantTests.Grant_AllowsReadingAndWritingPagesIndexesAndTheLog`<br>`Grimoire.Fast.Tests.ToolGrantTests.Grant_LeavesOutDeletingAndMoving` | contract, fast | proven |
| GUARD-003 | test | `Grimoire.Fast.Tests.ToolGrantTests.Grant_IsRecordedWithTheRun` | fast | proven |
| GUARD-004 | test | `Grimoire.Contract.Tests.HarnessProcessTests.Interrupt_EndsARunInFlight`<br>`Grimoire.Fast.Tests.CeilingTests.Ceilings_AreFixedValuesAndNotSettings`<br>`Grimoire.Fast.Tests.CeilingTests.Cost_CountsEveryModelTheRunTouched`<br>`Grimoire.Fast.Tests.CeilingTests.Cost_CountsNothing_WithoutAModelUsage`<br>`Grimoire.Fast.Tests.CeilingTests.Cost_CountsTheFourTokenFields`<br>`Grimoire.Fast.Tests.CeilingTests.Run_EndsFailed_AfterACeilingStopsIt`<br>`Grimoire.Fast.Tests.CeilingTests.Run_EndsFailed_WhenTheTokensItSpentReachTheCeiling`<br>`Grimoire.Fast.Tests.CeilingTests.Run_IsLeftAlone_WhileBothCeilingsAreClear`<br>`Grimoire.Fast.Tests.CeilingTests.Run_IsStoppedThroughThePort_WhenTheCostCeilingIsReached`<br>`Grimoire.Fast.Tests.CeilingTests.Run_IsStoppedThroughThePort_WhenTheElapsedCeilingIsReached`<br>`Grimoire.Fast.Tests.CeilingTests.Run_ReachesACeiling_WhenTheClockRunsOut`<br>`Grimoire.Fast.Tests.CeilingTests.Run_ReachesACeiling_WhenTheTokensRunOut`<br>`Grimoire.Fast.Tests.CeilingTests.Run_ReachesNoCeiling_WhileBothAreClear`<br>`Grimoire.Fast.Tests.CeilingTests.Run_RecordsWhatItSpends_AsTheCostArrives`<br>`Grimoire.Fast.Tests.RunOutcomeTests.AgentStops_EndsTheRunFailed_WhenTheElapsedCeilingIsReached`<br>`Grimoire.Fast.Tests.RunOutcomeTests.AgentStops_EndsTheRunFailed_WhenTheTokenCeilingIsReached` | contract, fast | proven |

## INGEST

| Requirement | Proof | Tests | Level | Status |
| --- | --- | --- | --- | --- |
| INGEST-001 | test | `Grimoire.Fast.Tests.SubmissionAcceptanceTests.Submit_IsAccepted_AfterADispatchThatCouldNotStart`<br>`Grimoire.Fast.Tests.SubmissionAcceptanceTests.Submit_IsAccepted_WhenNoRunIsInProgress`<br>`Grimoire.Fast.Tests.SubmissionAcceptanceTests.Submit_IsAccepted_WithoutWaitingForTheRunToEnd`<br>`Grimoire.Fast.Tests.SubmissionAcceptanceTests.Submit_StartsARun_WithItsOwnIdentifier` | fast | proven |
| INGEST-002 | test | `Grimoire.Fast.Tests.DispatchPayloadTests.Dispatch_CarriesNothingBeyondThoseFour`<br>`Grimoire.Fast.Tests.DispatchPayloadTests.Dispatch_CarriesTheGrantTheRunRecorded`<br>`Grimoire.Fast.Tests.DispatchPayloadTests.Dispatch_CarriesTheInstructionThePurposeTheTextAndTheRunIdentifier`<br>`Grimoire.Fast.Tests.DispatchPayloadTests.Dispatch_RunsOnThePinnedModelTheHubWasStartedWith` | fast | proven |
| INGEST-003 | test | `Grimoire.Fast.Tests.SubmissionRefusalTests.Submit_IsNotStored_WhenRefused`<br>`Grimoire.Fast.Tests.SubmissionRefusalTests.Submit_IsRefused_WhenTheInstructionIsMissing`<br>`Grimoire.Fast.Tests.SubmissionRefusalTests.Submit_IsRefused_WhenThePurposeDescriptionIsMissing`<br>`Grimoire.Fast.Tests.SubmissionRefusalTests.Submit_NamesTheInstructionFirst_WhenSeveralAreMissing`<br>`Grimoire.Fast.Tests.SubmissionRefusalTests.Submit_StartsNoRun_WhenRefused` | fast | proven |
| INGEST-004 | test | `Grimoire.Fast.Tests.SubmissionRefusalTests.Submit_IsNotStored_WhenRefused`<br>`Grimoire.Fast.Tests.SubmissionRefusalTests.Submit_IsRefused_WhenTheTextIsEmptyOrWhitespace`<br>`Grimoire.Fast.Tests.SubmissionRefusalTests.Submit_NamesTheInstructionFirst_WhenSeveralAreMissing`<br>`Grimoire.Fast.Tests.SubmissionRefusalTests.Submit_StartsNoRun_WhenRefused` | fast | proven |
| INGEST-005 | test | `Grimoire.Fast.Tests.SubmissionAcceptanceTests.Submit_IsAccepted_AfterTheRunHasEnded`<br>`Grimoire.Fast.Tests.SubmissionAcceptanceTests.Submit_IsNotStored_WhenRefused`<br>`Grimoire.Fast.Tests.SubmissionAcceptanceTests.Submit_IsRefused_WhileRunInProgress`<br>`Grimoire.Fast.Tests.SubmissionAcceptanceTests.Submit_StartsNoRun_WhenRefused` | fast | proven |

## RUNS

| Requirement | Proof | Tests | Level | Status |
| --- | --- | --- | --- | --- |
| RUNS-001 | test | `Grimoire.Fast.Tests.SubmissionStateTests.AgentReportsIn_TurnsSubmittedIntoRunning`<br>`Grimoire.Fast.Tests.SubmissionStateTests.RunEnds_IsIgnored_WhenTheRunHasAlreadyEnded`<br>`Grimoire.Fast.Tests.SubmissionStateTests.RunEnds_LeavesTheSubmissionDoneOrFailed`<br>`Grimoire.Fast.Tests.SubmissionStateTests.RunEnds_LeavesTheSubmissionFailed_WithoutTheAgentReportingIn`<br>`Grimoire.Fast.Tests.SubmissionStateTests.States_AreTheFourTheSpecNames`<br>`Grimoire.Fast.Tests.SubmissionStateTests.Transition_IsRefused_WhenTheSubmissionIsAlreadyDoneOrFailed`<br>`Grimoire.Fast.Tests.SubmissionStateTests.Transitions_LeaveExactlyOneStateAtATime` | fast | proven |
| RUNS-005 | test | `Grimoire.Contract.Tests.HarnessProcessTests.Nudge_ContinuesTheSameRun_AfterTheAgentStops`<br>`Grimoire.Fast.Tests.RunOutcomeTests.AgentStops_EndsTheRunDone_AfterTheNudgeBringsTheEntry`<br>`Grimoire.Fast.Tests.RunOutcomeTests.AgentStops_EndsTheRunDone_WithTheLogEntryPresent`<br>`Grimoire.Fast.Tests.RunOutcomeTests.AgentStops_EndsTheRunFailed_WhenItDidNotStopOfItsOwnAccord`<br>`Grimoire.Fast.Tests.RunOutcomeTests.AgentStops_EndsTheRunFailed_WhenTheElapsedCeilingIsReached`<br>`Grimoire.Fast.Tests.RunOutcomeTests.AgentStops_EndsTheRunFailed_WhenTheEntryIsStillMissingAfterTheNudge`<br>`Grimoire.Fast.Tests.RunOutcomeTests.AgentStops_EndsTheRunFailed_WhenTheTokenCeilingIsReached`<br>`Grimoire.Fast.Tests.RunOutcomeTests.AgentStops_NudgesOnce_WhenLogEntryMissing`<br>`Grimoire.Fast.Tests.RunOutcomeTests.AgentStops_RecordsWhatTheRunSpent`<br>`Grimoire.Fast.Tests.RunOutcomeTests.Log_DoesNotNameTheRun_WithoutALogAtAll`<br>`Grimoire.Fast.Tests.RunOutcomeTests.Log_DoesNotNameTheRun_WithoutTheIdentifierInIt`<br>`Grimoire.Fast.Tests.RunOutcomeTests.Log_NamesTheRun_WhenItHoldsTheIdentifierAsPlainText`<br>`Grimoire.Fast.Tests.RunOutcomeTests.Log_NamesTheRun_WithNoFormatImposedOnTheEntry` | contract, fast | proven |

## WIKI

| Requirement | Proof | Tests | Level | Status |
| --- | --- | --- | --- | --- |
| WIKI-001 | review | — | — | by review |
| WIKI-002 | test | `Grimoire.Contract.Tests.FileSystemWikiStoreTests.WritePage_IsRefused_WhenTheFrontmatterCannotBeRead`<br>`Grimoire.Contract.Tests.FileSystemWikiStoreTests.WritePage_LandsOnDiskWithTheRecord`<br>`Grimoire.Contract.Tests.HarnessProcessTests.Run_ReachesTheWikiAndNothingElse`<br>`Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_AddsTheRecord_WhenTheFrontmatterIsEmpty`<br>`Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_AddsTheRecord_WhenThePageHasNoPlaceForIt`<br>`Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_Fails_WhenThePageHasNoFrontmatterAtAll`<br>`Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_Fails_WhenTheRecordsPlaceCannotBeRead`<br>`Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_KeepsEverythingElseAsTheAgentWroteIt_WhenTheRecordIsAdded`<br>`Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_NamesTheUpdatingRun_WhenThePageIsUpdated`<br>`Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_ReplacesAgentValues_WhenCommentsSitAboveTheRecord`<br>`Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_ReplacesAgentValues_WhenTheFrontmatterIsIndented`<br>`Grimoire.Fast.Tests.ProvenanceStampTests.WritePage_ReplacesAgentValues_WhenTheRecordIsAlreadyThere` | contract, fast | proven |
| WIKI-003 | test | `Grimoire.Contract.Tests.FileSystemWikiStoreTests.AppendLog_KeepsWhatEarlierRunsWrote`<br>`Grimoire.Contract.Tests.FileSystemWikiStoreTests.RunFails_LeavesEveryFileItWroteOnDisk`<br>`Grimoire.Fast.Tests.FailedRunTests.RunFails_LeavesEveryPageItWroteInPlace`<br>`Grimoire.Fast.Tests.FailedRunTests.RunFails_LeavesTheLogEntryItWroteInPlace`<br>`Grimoire.Fast.Tests.FailedRunTests.WikiPort_OffersNoWayToRemoveRevertOrCommit` | contract, fast | proven |

## Tests carrying no requirement id

Allowed at Fast and Contract (Constitution III.5); an E2E or Deploy test here fails the gate.

| Test | Suite | Level |
| --- | --- | --- |
| `Grimoire.Contract.Tests.FileSystemWikiStoreTests.ReadPage_FindsNothing_WhenTheFileIsNotThere` | Grimoire.Contract.Tests | contract |
| `Grimoire.Fast.Tests.CapabilityRegistryTests.Read_Fails_WhenARowDoesNotReadAsARequirementInFull` | Grimoire.Fast.Tests | fast |
| `Grimoire.Fast.Tests.CapabilityRegistryTests.Read_Fails_WhenAnIdIsRegisteredTwice` | Grimoire.Fast.Tests | fast |
| `Grimoire.Fast.Tests.CapabilityRegistryTests.Read_MarksTheRequirementRetired_WhenTheRowSitsUnderRetired` | Grimoire.Fast.Tests | fast |
| `Grimoire.Fast.Tests.CapabilityRegistryTests.Read_PassesOverARowThatIsNotARequirement` | Grimoire.Fast.Tests | fast |
| `Grimoire.Fast.Tests.CapabilityRegistryTests.Read_RegistersTheRequirement_WhenTheRowReadsInFull` | Grimoire.Fast.Tests | fast |
| `Grimoire.Fast.Tests.TraceCheckTests.Check_Fails_WhenATestCarriesAnUnknownRetiredOrReservedId` | Grimoire.Fast.Tests | fast |
| `Grimoire.Fast.Tests.TraceCheckTests.Check_Fails_WhenATestCarriesNoLevel` | Grimoire.Fast.Tests | fast |
| `Grimoire.Fast.Tests.TraceCheckTests.Check_Fails_WhenAnE2EOrDeployTestCarriesNoRequirementId` | Grimoire.Fast.Tests | fast |
| `Grimoire.Fast.Tests.TraceCheckTests.Check_Passes_WhenATestRequirementHasNoTest` | Grimoire.Fast.Tests | fast |
| `Grimoire.Fast.Tests.TraceCheckTests.Check_Passes_WhenEveryTestMatchesTheRegistry` | Grimoire.Fast.Tests | fast |
| `Grimoire.Fast.Tests.TraceCheckTests.CompleteCheck_Fails_WhenATestRequirementHasNoTest` | Grimoire.Fast.Tests | fast |
| `Grimoire.Fast.Tests.TraceCheckTests.CompleteCheck_Passes_WhenAReviewRequirementHasNoTest` | Grimoire.Fast.Tests | fast |
