# Mutation — `004-ask-the-wiki`

T091 classifies the survivors of a local run of `./scripts/mutation.sh` here — local, because since
T109 the `mutation` job no longer runs on the PR to `main` — under "The survivors" below. This page
begins before that, with two changes to what the measurement can see: `Grimoire.Hub` is in scope for the
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
Stryker into safe mode, so no method comes back into the measurement; the table below was taken
before them and is left as it was. They are rewritten so that the rule holds without exceptions a
reader would have to know about. They did lower the Hub's compile errors one mutant at a time,
which the T091 run shows (under "The survivors").

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

## The survivors (T091)

One local run of `./scripts/mutation.sh` at `fd824fd`, alone on the machine (load 2.68 at the
start), Fast suite at 442 tests, the same 4-core i7-6820HQ. The whole script took **22 minutes**,
not the 45 to 60 its header estimates; `Grimoire.Hub` took seven of them. `StrykerOutput/` is kept,
one `mutation-report.json` per project with `killedBy` for every mutant, for the deletions under
"Later" in `test-audit.md` §6.

| Project | Created | Tested | Killed | Timeout | Survived | Not covered | Compile errors | Score |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Grimoire.Agent | 168 | 116 | 103 | 0 | 13 | 1 | 39 | 88.03 % |
| Grimoire.Runs | 411 | 281 | 199 | 1 | 81 | 16 | 40 | 67.34 % |
| Grimoire.Hub | 760 | 468 | 299 | 12 | 157 | 69 | 51 | 57.91 % |
| Grimoire.Wiki | 126 | 105 | 90 | 1 | 14 | 1 | 3 | 85.85 % |
| Grimoire.Trace | 376 | 117 | 97 | 0 | 20 | 10 | 28 | 76.38 % |

"Tested" is killed, timed out and survived; the score is Stryker's, over tested and not covered.

**Overall**, the number the badge shows, computed the way `ci.yml`'s `mutation-badge` job computes
it over all five reports: (788 killed + 14 timed out) / (1087 tested + 97 not covered) = 802 / 1184
= **67.7 %**.

**Created is more than tested, not covered and compile errors together** — by 12 (Agent), 74
(Runs), 172 (Hub), 17 (Wiki) and 221 (Trace). Those are the reports' `Ignored` mutants, which
Stryker leaves out of the score; the reports give two reasons. *Removed by block already covered
filter* (Agent 12, Runs 74, Hub 167, Wiki 17, Trace 34): Stryker drops the mutant that empties a
block where that block already carries mutants of its own. *Removed by mutate filter* (Hub 5, Trace
187): files the `--mutate` filter excludes but Stryker still lists — the Hub's `Program.cs`, and
Trace's `Program.cs`, `RepositoryLayout.cs` and `TraceDocument.cs`. Files the other three projects
exclude (their `Adapters/`) do not appear in their reports at all.

**This is the state after pruning.** The tests that could not fail or proved only the double went
in `fcd408c` (2026-09-29, test-audit.md recommendation 2), before this run (2026-10-01); there is no
run from before it to compare against. The pruning by `killedBy` — a test removed where the reports
show another test kills every mutant it kills (test-audit.md §6 a–e) — is still to come; it is under
"Later" in `tasks.md`.

The Hub's **compile errors are 51**, against the 97 of the reference above. The 46 fewer are in two
files: `StartUp.cs` (32 → 1) and `Api/RunRecordEndpoint.cs` (20 → 5) — the two Hub files of the
rewrite that followed the reference (`b0dcc9f`). Their guards never threw Stryker into safe mode,
but every mutant of such a condition left its variable unassigned and was dropped on its own as a
compile error; written out, those mutants compile. Every other file reads as in the reference, and
the 760 mutants against the reference's 753 are the lines written since. A fall, not the rise that
would mean a pattern-variable guard came back.

**The first CI run with the Hub in scope** — run 36951410197, job `mutation`, on `f027ee9`, which
already holds the eighteen tests below — took **31 min 36 s** and published **70.4 %**: Runs
68.69 % (7 min 26 s), Agent 88.03 % (4 min 14 s), Wiki 85.85 % (4 min 1 s), Hub 62.73 %
(11 min 39 s), Trace 77.17 % (4 min 2 s).

**Two "Safe Mode!" lines**, neither of them the guard CLAUDE.md forbids:

- `RecordText.Counts` (`Grimoire.Runs`): a string mutation inside the interpolation handed to
  `string.Create` breaks the handler's `ref` argument (CS1620), which Stryker cannot pin on one
  mutant. The method is one line of wording (III.8), so nothing measurable is lost; it is a limit of
  Stryker's string mutator on `string.Create`, not a construction of ours to rewrite.
- `HarnessProcess.Terminate` (`Grimoire.Agent`): `process` is assigned inside a `try` and read after
  it (CS0165). The file is an adapter outside the `--mutate` filter, so none of its mutants is
  measured in any case; the line only says that Stryker compiles every file of the project.

### What was read

Of the 337 mutants that survived or were not covered in `Runs`, `Agent` and `Hub`, **260** are read
here: every one on a line this feature wrote (155, by `git blame` against `main`), and every one in
`Grimoire.Hub`, which no measurement has read before (105 on lines older than this feature). The
other 77 are on lines of `Runs` and `Agent` that 001 to 003 wrote and classified; and `Wiki` and
`Trace` (45 more), which this feature did not touch, are not this feature's to classify — the same
cut `003-live-run-record/mutation.md` made.

Every claimed gap was checked the only way that settles it: the mutation made in the source and the
whole Fast suite run. Each of the ones below passed all 442 tests, which is what makes it a gap
rather than an opinion — and each fails now.

### Became tests — (a) and (a′)

Eighteen new Fast tests, for twenty-two survivors. Each was seen red against its mutant, made in
the source and taken out again; the `red against:` lines are in the commit message.

| Survivor | Why it is a gap | Test |
| --- | --- | --- |
| `RunConductor.cs:141` — `question ?` → `true ?` | A **submission's** run would be dispatched with the question's two reads at the `questions` door. Every test held the dispatched grant against the grant the run recorded, which agree with each other whichever was chosen. | `DispatchPayloadTests.Dispatch_CarriesTheIngestGrantAtItsDoor_ForASubmission` (GUARD-002) |
| `RunConductor.cs:398`, `:326` — a question's tool call not counted, its figures not raised | RUNS-010 says *every* run; only a submission's count was asserted. | `RunFiguresTests.ToolCalls_AreCounted_WhileAQuestionsRunIsInProgress` (RUNS-010) |
| `RunConductor.cs:639` `false`, `RunBoard.cs:600` — the lost entries left out of the final figures | `TerminalState_AndTheFinalFigures_ArePublishedTogether` asserts with `Assert.All` over a set both mutants leave empty, so it passed for the wrong reason. | `RunFiguresTests.EntriesLost_StandInTheFinalFigures_WhenTheTailCouldNotBeWritten` (RUNS-007) |
| `RunConductor.cs:505` — the nudge not published to an open record | The nudge reached a reader only when the agent next did something; no stream test nudged. | `RecordStreamTests.Record_CarriesGrimoiresNudge_WhenTheAgentStopsWithoutItsLogEntry` (ACCESS-006) |
| `RunConductor.cs:512`, `:353` — the depth after a nudge, and after a result while an earlier call waits | The nesting is in the file (003's `contracts/run-record.md`, rule 1), as 003's `RecordText.cs:81` was. | `RunNarrativeTests.Call_StaysAtTheTop_AfterGrimoiresNudge`, `…Result_IsNotPutUnderTheCallAbove_WhileAnEarlierCallStillWaits` |
| `RunBoard.cs:284` — no list event for a text accepted while another runs | Every stream test submitted before opening the stream. | `SubmissionStreamTests.Stream_SendsTheWholeList_WhenATextWasAcceptedWhileAnotherRuns` (ACCESS-005) |
| `RunBoard.cs:564` — no list event when the agent reports in | The test for a change hid it: the tool call after the report-in sends one anyway. | `SubmissionStreamTests.Stream_SendsTheRunningState_AfterTheAgentReportedIn` (ACCESS-005) |
| `RunBoard.cs:618` — a question's ended run never written to the store | The run stays in progress there, and the next start terminates a process number that may be another program's by then. Only a submission's run was restarted over. | `AgentLifetimeTests.Restart_TerminatesNothing_WhenAQuestionsRunHadAlreadyEnded` (RUNS-006) |
| `RunQueue.cs:75`, `:142` — the queue not closed at the stop, or a pump not stopped by it | The existing test stopped a run under way, whose failure holds the queue anyway (RUNS-003). | `AgentLifetimeTests.HubStops_StartsNothingAfterwards_WhenATextArrives` (RUNS-006) |
| `SubmissionsEndpoints.cs:229`, `:233`, `:237` — the three wire reasons (not covered at all) | No Fast test posted a refused submission; the board's `Refusal` was proven, the name the page reads was not — what `QuestionAskedOverHttpTests` closed for questions. | `SubmissionSubmittedOverHttpTests`, three methods (INGEST-003, INGEST-004) |
| `StartUp.cs:231`, `:238` — a link on an *ancestor* of `--state` never resolved | `--state <vault-link>/wiki/state` was accepted, inside the wiki. The link test linked straight to the wiki. | `StartUpTests.Start_IsRefused_WhenTheStateDirectoryReachesIntoTheWikiThroughALinkAboveIt` (RUNS-007) |
| `WikiToolsServer.cs:44` — the actor without `grimoire/` | The stamp's own tests hand it an actor; no test read the actor `write_page` actually records. | `ProvenanceStampTests.WritePage_RecordsGrimoireAndTheModel_WhenARunWritesAPage` (WIKI-002) |
| `WikiToolsServer.cs:119`, `:128`, `:143` — `write_index` refusing every index or writing nothing, `append_log` appending nothing | No Fast or Contract test called either tool. | `WikiToolTests`, three methods (GUARD-002) |


### Proven at another level

Stryker runs the Fast suite alone; these are killed by a Contract or E2E test that exists.

- **The MCP doors** — `HubApplication.cs:243`, `:252`, `:346`–`:347` (the two `MapMcp` calls and
  their routes), `WikiToolSurfaces.cs:72` (both conditionals), `:92`, `:94`,
  `WikiReadToolsServer.cs:30` (two of three), `WikiToolsServer.cs:81`, `:94`, `:98`: Contract
  `WikiToolDoorTests` (GUARD-002, GUARD-005, WIKI-002), which opens a real MCP session at both doors
  without a sign-in. `WikiToolsServer.cs:103` (the page never written) is killed only by the
  sign-in test `HarnessProcessTests.Run_ReachesTheWikiAndNothingElse`.
- **The static pages** — `HubApplication.cs:261`–`:262`: every E2E test loads them.
- **The record's tail arriving live** — `RunConductor.cs:624`: E2E
  `RunRecordViewTests.Moments_ArriveBelowWhatIsThere_WithoutDisturbingIt`.
- **Restoring after a stop, called from `Build`** — `HubApplication.cs:349`: E2E `RestartTests`;
  the method itself is proven through `FastHub` (`RestartTests`, `AgentLifetimeTests`).

### (d) — not asserted by design

- **Argument guards** (`ArgumentNullException.ThrowIfNull` removed), 49 of them: `ChatEndpoints.cs`
  :60, :72, :274–:279; `Chat.cs:272`; `InstructionLoader.cs:72`, :131; `LiveUpdates.cs:168`–:169;
  `RunRecordEndpoint.cs:58`–:61; `SubmissionsEndpoints.cs:65`, :152–:154; `HubApplication.cs`
  :91–:95, :174–:175, :191–:193; `RunConductor.cs:133`, :158; `WikiToolSurfaces.cs:70`;
  `RunBoard.cs:249`, :304, :351–:352, :469, :695; `Submission.cs:227`; and `Queued.cs:122`, :146,
  which throw on a double hand-out and on an ending that is neither done nor failed — invariants the
  queue rule keeps, so (b′) rather than (d) for those two.
- **Wording** (III.8): the refusal and error messages — `ChatEndpoints.cs:114`, :116–:119 (why a
  question has no answer, as a sentence; `ChatChangeTests` asserts that a reason is there, which is
  the depth QUERY-006 asks for), :488, :499, :502; `SubmissionsEndpoints.cs:230`, :234, :238;
  `StartUp.cs:77`, :86, :116–:118, :131 (what a refused start prints; the refusal itself is
  asserted); `WikiReadToolsServer.cs:31`, :36 and `WikiToolsServer.cs:87`–:88, :108, :123, :133
  (the tools' refusal reasons and messages, read by the agent and named by no requirement);
  `Queued.cs:122`, :146 and `Submission.cs:260` (exception text). And the prompt's layout:
  `InstructionLoader.cs:104` (two blank lines before a first question), :134 (the separator between
  earlier turns), :137 (an answered turn whose answer is empty) — `QuestionPromptTests` asserts what
  the prompt holds and in which order, not its whitespace.
- **Argument parsing and static configuration** (III.8): `StartUp.cs:22`, :59 (twice), :60,
  :64–:65, :68–:69, :250, :255 (twice) — reading `--state`, `--instruction`, `--question-instruction`
  and their defaults; `SubmissionsEndpoints.cs:159` and `ChatEndpoints.cs:284`, a request body with
  no `text` field read as empty text — the same refusal follows, and the empty and whitespace cases
  are asserted over HTTP.
- **Wiring** (III.8): `HubApplication.cs:220`–:224, :231–:233 (console logging and its filters),
  :238–:241 and `WikiToolSurfaces.cs:53`–:54 (service registrations, which a Contract tool call fails
  without), :354 and :358 (the framework's start and stop hooks; what they call is proven through
  `FastHub`).

### (c) — equivalent

- **`ConfigureAwait(true)`**, 29 times across `ChatEndpoints`, `ChatIntake`, `HubApplication`,
  `LiveUpdates`, `RunConductor`, `RunQueue`, `SubmissionIntake`, `SubmissionsEndpoints` and the two
  tool servers: ASP.NET Core has no synchronisation context.
- **`static readonly` initialisers**, which Stryker's switch cannot reach: `RunBoard.cs:19` (three
  times — 003 recorded the same of `SubmissionBoard.cs:11`), `ToolGrant.cs:63`–:64 (the question
  grant's two names; changed in the source, `QuestionGrantTests` fails), `LiveUpdates.cs:98` (a
  channel's performance hint).
- **Redundant twice over**: `Question.cs:134`, :151 and `Queued.cs:157` (a reason is kept only for
  a failed ending, and only a failed question reads it); `Queued.cs:88` (003's `Submission.cs:215`,
  unreachable behind the queue rule); `Queued.cs:179`; `AgentTranscript.cs:163` (a null message
  already means a null type); `RunConductor.cs:535` (`Ended` repeats the check), :445, :639 `true`
  (a question's run has no record, so nothing is lost); `ChatEndpoints.cs:406`, :416, :420, :486
  (the changes and the turns come from one snapshot, so a change always finds its turn — the
  comment at :413 describes a race the single snapshot rules out), `ChatEndpoints.cs:99`, :507 and
  `SubmissionsEndpoints.cs:104`, :240 (the default arm of a switch over every enum value);
  `StartUp.cs:190`–:191, :216 (twice), :218, :238 `false` (the walk up always ends at the root, and
  `returnFinalTarget` resolves a chain in one pass).
- **A second event carrying the same state**: `Chat.cs:281` (the board's `changed` follows inside
  the same lock), `RunBoard.cs:283`, :330, `HubApplication.cs:293`; and `RunBoard.cs:493` and
  `RunConductor.cs:179` (nobody can be listening yet — a stream opens with a snapshot, and a record
  is 404 until its head exists).
- **Unobservable through any port**: `RunBoard.cs:563` (a row restored with a run reads failed
  whether it was stored submitted or running); `LiveUpdates.cs:110`, :200, :222, :224, :227, :234
  (a topic shared or a subscriber not removed costs wakes and memory, and every reader still reads
  past its own offset); `RunConductor.cs:600` (a leaked timer finds no run); `WikiReadToolsServer.cs:89`
  (both servers are sealed); `WikiToolSurfaces.cs:89`; `RunQueue.cs:94`, :147, :148, :151 (a nested
  pump dispatches the same runs; the board's lock keeps one at a time, RUNS-002);
  `ChatIntake.cs:40` and `SubmissionIntake.cs:31` (a refused input pumps a queue that has nothing
  new).
- **`Chat.cs:340` and :414** — a run under way when the chat is started again writes descriptors
  into the new chat's change log. They are never sent (`ChatEndpoints.cs:420` skips a change whose
  turn is not there) and `Turns` stays empty, which is what `NewChatTests` asserts; QUERY-005 is
  about what the user sees of the old chat, and nothing of it reaches them.
- **Only in a race** the Fast suite cannot order: `HubApplication.cs:177`–:179 (the stop's drain),
  `RunConductor.cs:250`, :260, :297, :308, :462, :581, :663 (a report, a moment or an ending for a
  run another thread has just ended), `RunQueue.cs:103`, :114, :178, :213, :215, :230, and
  `RunBoard.cs:594`, :621, :626 (ending what the board never handed out).
  `RunConductor.cs:392` (an agent text block with no text) is a malformed CLI line, which
  `AgentTranscript` never produces.

### (b) — candidate for removal

- **`WikiToolsServer.cs:25`, :27 — `RunAddress.RunId`.** It has no caller in `src/` or `tests/`:
  `WikiToolsServer` reads only `GeneratedBy`, and the door's run is the route's, read by
  `WikiToolSurfaces`. Its comment's claim that the identifier names the run in the generation record
  and the log is no longer true (`GeneratedBy`'s own remarks say why the run is not in the actor). With it would go the
  `IHttpContextAccessor` it reads and its registration at `HubApplication.cs:238`. Nothing built without a
  consumer (II.1); the deletion is not made here, because T091 classifies and this is a production
  change — it is an open question for the PR.

  **Closed when closing 004 (code)**: deleted. No consumer was found in `src/` or `tests/`, so
  `RunAddress.RunId`, the `IHttpContextAccessor` it read and its registration went (II.1). What was
  left — the generation record's actor — is `PageProducer`, which says what it is.
