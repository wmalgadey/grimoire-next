# Test-Audit — 004-ask-the-wiki nach Phase 6

**Stand**: `968f5f1` auf `004-ask-the-wiki-phase-6-conversation`, 2026-09-27.
**Art**: Nur Lesen und Messen. Code und Tests sind unverändert, Mutation-Testing wurde nicht verwendet.
**Maschine** für alle lokalen Messungen: Intel Core i7-6820HQ, 4 Kerne / 8 Threads, 16 GB, macOS, .NET 10 x64.

**Methode**

- **Testkatalog**: aus den Quelltexten gelesen, pro Methode mit Suite, Klasse, Datei:Zeile, `level`/`req`/sonstigen Traits und Theory-Zeilen.
  - Gegen `--list-tests` der drei gebauten Test-Executables abgeglichen: 465 Methoden, 517 Runner-Einträge, keine Abweichung in beide Richtungen.
- **Laufzeit**: `dotnet test` pro Level, je dreimal, mit CTRF-Report für Zeiten pro Test.
- **Inhaltliche Fragen (4–7)**: Alle Testkörper wurden gelesen. Die Behauptungen, die in die Empfehlungen eingehen, wurden stichprobenartig gegen den Code geprüft.
- **Zählungen an älteren Commits** (Abschnitt 2): statisch aus `git show`; die Regex-Zählung zählt pro Commit höchstens eine Methode zu viel.

---

## 1. Zählweise

### Ergebnis

**trace-check zählt Testmethoden, der Runner zählt Testfälle.** Eine `[Theory]` ist für trace-check eine Methode (`tools/Grimoire.Trace/TestCatalogue.cs`: `IsTest` prüft `[Fact]` und Ableitungen, eine Zeile pro `MethodInfo`). Für den Runner ist jede `[InlineData]`-Zeile ein eigener Test.

| | Fast | Contract | E2E | Σ |
| --- | ---: | ---: | ---: | ---: |
| Methoden (= trace-check) | 381 | 48 | 36 | **465** |
| Runner-Testfälle (mit Theory-Zeilen) | 429 | 52 | 36 | **517** |
| Runner ohne `requires=signin` (CI und deine Zählung) | 429 | 48 | 36 | **513** |
| Methoden ohne `requires=signin` | 381 | 44 | 36 | 461 |

Belege:

- trace-check: `dotnet run --project tools/Grimoire.Trace -- check` → `trace-check: 39 requirements, 465 tests, no violations`
- Runner: `Grimoire.<Suite>.Tests --list-tests` → `found 429` / `found 52` / `found 36`
- CI-Lauf 36345078963 (Commit `968f5f1`): Fast `total: 429`, Contract `total: 48`, E2E `total: 36`

### Die Differenz 513 ↔ 464, Posten für Posten

1. **Deine zwei Zahlen stammen aus zwei Commits.** `968f5f1` (HEAD) fügt `ChatTests.NewChat_StartsNoQuestionThatWasStillWaitingWhenItWentAway` hinzu (`tests/Grimoire.Fast.Tests/ChatTests.cs`, +1 Methode, +1 Testfall).
   - trace-check zählt am Vorgänger `33ef523` 464 und an HEAD 465.
   - Fast zählt an HEAD 429 und am Vorgänger 428.
   - Auf demselben Commit lautet der Vergleich 513 ↔ 465.
2. **Theory-Zeilen: +52 Runner-Testfälle über 29 Theories.**
   - Fast: 27 Theories bringen 75 Testfälle für 27 Methoden (+48).
   - Contract: `FileSystemWikiStoreTests.WritePage_IsRefused_WhenThePathLeavesTheWiki` (4 Zeilen) und `ReadPage_IsRefused_WhenThePathLeavesTheWiki` (2 Zeilen) bringen +4.
   - Alle Theories nutzen `InlineData`, keine `MemberData`, daher gibt es keine nicht aufgezählten Theories.
3. **Sign-in-Ausschluss: −4.** Die vier Methoden von `HarnessProcessTests` (Klassen-Trait `requires=signin`, `HarnessProcessTests.cs:28`) sind in 465 enthalten, in 513 nicht.
4. **Ausgeschlossene Projekte: keine.** trace-check liest jedes Projekt unter `tests/` mit genau einer `.csproj` (`RepositoryLayout.TestAssemblies`). `tools/Grimoire.Trace` hat keine eigenen Tests; seine Tests liegen im Fast-Suite-Projekt und werden mitgezählt.
5. **Doppelzählung: keine.** trace-check zählt jede Methode einmal, gleich wie viele `req`-Traits sie trägt.
   - 109 Methoden tragen mehr als eine ID. Deshalb summieren sich die Spalten in Abschnitt 2 auf 551 Test↔Requirement-Verknüpfungen, nicht auf 465.
6. **Fehlende Requirement-Traits: keine Auswirkung auf die Zahl.** trace-check zählt Tests ohne `req` mit, verlangt `req` aber nur für E2E/Deploy.

Rechnung: 465 Methoden + 52 Theory-Zeilen = 517; − 4 Sign-in = 513. Oder in deinen Zahlen: 464 + 1 (HEAD) + 48 (Fast-Theory-Zeilen) + 4 (Contract-Theory-Zeilen) − 4 (Sign-in) = 513.

### Tests ohne Requirement-Trait: 39 Methoden, 51 Testfälle

| Datei:Zeile | Test | Testfälle | Grund |
| --- | --- | ---: | --- |
| `tests/Grimoire.Fast.Tests/CapabilityRegistryTests.cs:36–150` | 14 Methoden `Read_*` / `Capability_*` | 26 | beweisen Constitution IV.3 (Trace-Tool) |
| `tests/Grimoire.Fast.Tests/TestCatalogueTests.cs:44–111` | 12 Methoden `Read_*` / `Describe_*` | 12 | dito |
| `tests/Grimoire.Fast.Tests/TraceCheckTests.cs:20–88` | 7 Methoden `Check_*` / `CompleteCheck_*` | 7 | dito |
| `tests/Grimoire.Fast.Tests/TraceSummaryTests.cs:19` | `Summarise_CountsTheActiveRequirements` | 1 | dito |
| `tests/Grimoire.Fast.Tests/AgentTranscriptTests.cs:353` | `Line_SaysNothing_WhenItIsNotJson` | 1 | Robustheit des Adapters; keine Regel im Protokoll-Contract |
| `tests/Grimoire.Fast.Tests/AgentTranscriptTests.cs:357` | `Line_SaysNothing_WhenTheHubReadsNothingFromIt` | 1 | DEC-028; dupliziert `RunNarrativeTests.cs:115` |
| `tests/Grimoire.Fast.Tests/AgentTranscriptTests.cs:364` | `Line_SaysNothing_WhenTheSystemMessageIsNotAnInit` | 1 | Robustheit; keine Regel |
| `tests/Grimoire.Fast.Tests/SubmissionStateTests.cs:161` | `Report_CarriesNoRunIdentifier_WhileAFailureIsUnacknowledged` | 1 | Designeigenschaft aus `access.md:28–31` |
| `tests/Grimoire.Contract.Tests/FileSystemWikiStoreTests.cs:104` | `ReadPage_FindsNothing_WhenTheFileIsNotThere` | 1 | Darauf stützt sich RUNS-005 („kein Log = kein Eintrag“); könnte `RUNS-005` tragen |

34 der 39 gehören zum Trace-Tool und tragen zu Recht keine ID. Die übrigen 5 sind Produkttests ohne ID.

---

## 2. Verteilung

39 registrierte Requirements: 35 aktiv mit Nachweis `test`, 2 aktiv mit Nachweis `review` (QUERY-004, WIKI-001) und 2 zurückgezogen (ACCESS-002, INGEST-005). `trace-check` zählt alle 39, `summary` nur die 37 aktiven.

Methoden je Level. Ein Test mit mehreren IDs zählt bei jeder seiner IDs. 🔝 markiert die Top 5 nach Testanzahl, ⚠ markiert 0 oder 1 Test.

| Requirement | Nachweis | Fast | Contract | E2E | Σ Methoden | Σ Testfälle |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| ACCESS-001 ⚠ | test | 0 | 0 | 1 | 1 | 1 |
| ACCESS-002 (zurückgezogen) ⚠ | test | 0 | 0 | 0 | 0 | 0 |
| ACCESS-003 | test | 7 | 0 | 3 | 10 | 10 |
| ACCESS-004 | test | 8 | 1 | 6 | 15 | 15 |
| ACCESS-005 | test | 21 | 0 | 6 | 27 | 30 |
| ACCESS-006 🔝3 | test | 23 | 1 | 10 | 34 | 34 |
| ACCESS-007 🔝2 | test | 26 | 0 | 12 | 38 | 46 |
| ACCESS-008 | test | 1 | 0 | 2 | 3 | 3 |
| ACCESS-009 | test | 6 | 0 | 4 | 10 | 18 |
| ACCESS-010 | test | 0 | 0 | 3 | 3 | 3 |
| GUARD-001 | test | 14 | 9 | 0 | 23 | 27 |
| GUARD-002 | test | 4 | 2 | 0 | 6 | 10 |
| GUARD-003 ⚠ | test | 1 | 0 | 0 | 1 | 1 |
| GUARD-004 🔝1 | test | 44 | 2 | 0 | 46 | 48 |
| GUARD-005 | test | 6 | 2 | 1 | 9 | 9 |
| INGEST-001 | test | 4 | 0 | 0 | 4 | 4 |
| INGEST-002 | test | 4 | 0 | 0 | 4 | 4 |
| INGEST-003 | test | 6 | 0 | 0 | 6 | 8 |
| INGEST-004 | test | 4 | 0 | 0 | 4 | 8 |
| INGEST-005 (zurückgezogen) ⚠ | test | 0 | 0 | 0 | 0 | 0 |
| QUERY-001 | test | 7 | 0 | 0 | 7 | 7 |
| QUERY-002 | test | 8 | 0 | 0 | 8 | 8 |
| QUERY-003 | test | 14 | 0 | 1 | 15 | 21 |
| QUERY-004 ⚠ | review | 0 | 0 | 0 | 0 | 0 |
| QUERY-005 🔝5 | test | 24 | 0 | 4 | 28 | 28 |
| QUERY-006 | test | 10 | 0 | 1 | 11 | 11 |
| RUNS-001 | test | 7 | 0 | 0 | 7 | 9 |
| RUNS-002 | test | 21 | 0 | 1 | 22 | 22 |
| RUNS-003 | test | 18 | 0 | 1 | 19 | 19 |
| RUNS-004 | test | 9 | 16 | 1 | 26 | 26 |
| RUNS-005 | test | 25 | 1 | 0 | 26 | 28 |
| RUNS-006 | test | 14 | 6 | 0 | 20 | 20 |
| RUNS-007 🔝4 | test | 17 | 10 | 2 | 29 | 29 |
| RUNS-008 | test | 20 | 2 | 0 | 22 | 22 |
| RUNS-009 | test | 21 | 0 | 1 | 22 | 24 |
| RUNS-010 | test | 14 | 4 | 0 | 18 | 18 |
| WIKI-001 ⚠ | review | 0 | 0 | 0 | 0 | 0 |
| WIKI-002 | test | 19 | 3 | 0 | 22 | 27 |
| WIKI-003 | test | 3 | 2 | 0 | 5 | 5 |

### Die auffälligen Enden

- **Top 5:**
  - GUARD-004: 46, davon 44 Fast
  - ACCESS-007: 38
  - ACCESS-006: 34
  - RUNS-007: 29
  - QUERY-005: 28
  - ACCESS-005 folgt knapp mit 27.
- **Nur ein Test:**
  - ACCESS-001: einzig `E2E SubmitTextTests.cs:14`.
  - GUARD-003: einzig `ToolGrantTests.Grant_IsRecordedWithTheRun`. Für Fragen beweisen `ChatTests.cs:148` und `SqliteSubmissionStoreTests.cs:263` die Aufzeichnung, tragen die ID aber nicht (siehe 7).
- **Null Tests:** nur `review`-Requirements und zurückgezogene IDs. Das ist korrekt.
- **Dünn für ihren Umfang:**
  - ACCESS-008: 3 Tests; die Summe über mehrere Fragen ist ungetestet, siehe 7.
  - ACCESS-010: 3 Tests, nur E2E.
- **ACCESS-007 ist künstlich hoch.** Der Klassen-Trait auf `ChatStreamTests.cs:38` vererbt die ID an reine Vault-Pfad-Funktionen (`:341–380`) und Quittierungstests (`:518–549`).

### Vergleich mit 001

Die Referenz „16 Requirements / 108 Fast“ ist **nicht** der Stand beim Merge von 001:

- 108 ist die Testfall-Zahl der ersten Mutationsmessung (`eb9c2e5`, `specs/001-first-ingest/mutation.md`: „108 tests, one test assembly — the Fast suite“), zwölf Commits vor dem Merge.
- Beim Merge (`9b0a039`) waren es 16 Requirements, 152 Fast-Methoden / 183 Fast-Testfälle.

| Stand | aktive Req. | Fast Meth. / Testfälle | Contract | E2E | Σ Methoden | Methoden je aktivem Req. | Fast-Testfälle je aktivem Req. |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 001 1. Mutationsmessung `eb9c2e5` | 16 | 86 / 108 | 10 | 3 | 99 | 6,2 | 6,8 |
| 001 Merge `9b0a039` | 16 | 152 / 183 | 16 | 3 | 171 | 10,7 | 11,4 |
| 002 Merge `d9ac188` | 21 | 190 / 221 | 28 | 5 | 223 | 10,6 | 10,5 |
| 003 Merge `77bbacb` | 26 | 252 / 285 | 41 | 18 | 311 | 12,0 | 11,0 |
| main `4c94bd8` | 26 | 258 / 292 | 42 | 18 | 318 | 12,2 | 11,2 |
| 004 nach Phase 5 `8202ae2` | 36 | 355 / 403 | 48 | 32 | 435 | 12,1 | 11,2 |
| **004 HEAD** `968f5f1` | 37 | 381 / 429 | 48 | 36 | 465 | 12,6 | 11,6 |

**Die Dichte je Requirement ist seit dem Merge von 001 fast konstant** (10,5–12,6 Methoden je aktivem Requirement). Der Anstieg von 108 auf 429 hat drei Ursachen:

- 001 hat seine Tests nach der ersten Mutationsmessung fast verdoppelt.
- Es kamen 21 Requirements dazu.
- E2E ist gewachsen: 3 → 5 → 18 → 36.

**Welche Spec die Dichte treibt**, gezählt als heutige Test-Verknüpfungen je Requirement, gruppiert nach der Spec, die es eingeführt hat:

| Eingeführt von | Req. | Verknüpfungen heute (F / C / E) | je Req. |
| --- | ---: | --- | ---: |
| 001 | 16 (davon 2 zurückgezogen, 1 review) | 155 (135 / 19 / 1) | 9,7 |
| 002 | 6 | 112 (77 / 23 / 12) | 18,7 |
| **003** | 6 | 152 (116 / 17 / 19) | **25,3** |
| 004 | 11 (davon 1 review) | 132 (102 / 2 / 28) | 12,0 |

- **Am dichtesten getestet sind die Requirements aus 003** (RUNS-007…010, ACCESS-005/006).
- **Am meisten neue Tests bringt 004.** Seit `main` kamen 149 Methoden hinzu (123 Fast, 19 E2E, 7 Contract); 2 wurden umbenannt.
  - **43 der 149** tragen ausschließlich IDs aus 001–003: `LiveUpdatesTests` 9, `RecordStreamTests` 9, `RunBoardTests` 8, `SubmissionStreamTests` 5, `RunOutcomeTests` 4, `RunRecordTests` 2, `SqliteSubmissionStoreTests` 5, E2E 1.
  - Ursache: Die Umstellung von Polling auf Push (DEC-032 wird ersetzt) und die gemeinsame Queue für Fragen und Einreichungen haben bestehende Requirements neu getestet.
- **004 ist auch die Spec, die E2E verdoppelt hat:** von 18 auf 36.

---

## 3. Laufzeit

### Wall-Clock je Suite: `dotnet test` mit Level-Filter, je 3 Läufe

| Suite | Konfiguration | Filter | Wall-Clock (3 Läufe) | Runner-„Dauer“ | Load avg (1 min) |
| --- | --- | --- | --- | --- | --- |
| Fast | Debug | `level=fast`, `--timeout 15s` | **14,93 / 15,09 / 14,56 s** | 12,96 s (Lauf 1) | 2,4 |
| Contract | Debug | `level=contract`, ohne `requires=signin` | 6,82 / 7,17 / 6,86 s | 4,91 s | – |
| E2E | Debug | `level=e2e` | 35,85 / 35,81 / 37,02 s | 33,80 s | stieg während E2E auf 54 |
| Fast | Release | wie oben | 15,07 / 15,56 / 15,10 s | 13,21 / 13,73 / 13,17 s | 3,1–4,6 |
| Contract | Release | wie oben | 6,50 / 6,69 / 6,44 s | 4,67 / 4,89 / 4,67 s | 3,1–4,6 |
| Fast | Release, unter Last (3 Analyse-Agenten parallel) | wie oben | 17,53 / 17,73 / 16,30 s | 15,52 / 15,73 / 14,44 s | 10–15 |
| **CI** (ubuntu-latest, Release), Lauf 36345078963 | | | Step 4 s / 3 s / 10 s | **Fast 3,52 s**, Contract 1,81 s, E2E 9,36 s | – |

Release ist lokal nicht schneller als Debug, und CI ist etwa 3,7× schneller. Die lokale Zeit hängt also an der Maschine, nicht an der Build-Konfiguration.

### Wo die Fast-Zeit steckt

Medianwerte aus drei Läufen:

- Die Summe der Einzeldauern beträgt 40,2 s bei 11 s Laufspanne; xunit führt Klassen parallel aus.
- 59 der 429 Testfälle (14 %) stecken in den **sechs Klassen, die einen echten Kestrel-`HostedHub` starten**. Auf sie entfallen **32,5 s der 40,2 s (81 %)**:

| Klasse | Testfälle | Σ Median ms | max ms |
| --- | ---: | ---: | ---: |
| `ChatStreamTests` | 32 | 9 901 | 2 206 |
| `SubmissionStreamTests` | 5 | 5 285 | 2 339 |
| `QuestionAskedOverHttpTests` | 7 | 5 188 | 1 293 |
| `RunRecordEndpointTests` | 5 | 5 008 | 2 607 |
| `RecordStreamTests` | 9 | 4 875 | 764 |
| `SubmissionListTests` | 1 | 2 206 | 2 206 |

**Gegenprobe:** Fast ohne diese sechs Klassen (`--filter-not-class`, 3 Läufe) braucht **5,70 / 5,73 / 5,71 s** Wall-Clock bei 3,9 s Runner-Dauer, also 370 Testfälle. Unmittelbar danach dauerte der volle Lauf 14,26 s Wall-Clock. Die sechs Klassen kosten lokal also rund **8,5 s von 14,3 s**.

Dazu passt: `HostedHub.cs:16–22` begründet den Hub-Server damit, dass nur „the one thing that needs a request to reach an endpoint at all — the record endpoint of ACCESS-006“ ihn brauche. Heute nutzen ihn sechs Klassen.

### Die 10 langsamsten Tests je Level

Median aus 3 Läufen, Debug.

**Fast**

| ms | Test |
| ---: | --- |
| 2 607 | `RunRecordEndpointTests.Record_IsNotFound_WhenTheSubmissionHasNoRunYet` |
| 2 339 | `SubmissionStreamTests.Stream_SendsTheFiguresTheListAnswersWith_AfterARunSpent` |
| 2 206 | `SubmissionListTests.List_SaysWhatARunHasSpentAndTheCeilingItIsHeldTo` |
| 2 206 | `ChatStreamTests.Stream_OffersTheAcknowledgement_WhileTheFailureIsUnacknowledged` |
| 1 293 | `QuestionAskedOverHttpTests.Question_IsRefusedWithOneReason_WhenThereIsNothingToAsk(text: "")` |
| 1 252 | `QuestionAskedOverHttpTests.Question_IsRefusedForTheQuestionInstruction_WhenItIsNotThere` |
| 1 126 | `SubmissionStreamTests.Stream_OpensWithTheListAsItStands` |
| 1 000 | `TestCatalogueTests.Read_FindsTheRequirementIdTheMethodCarries` |
| 908 | `ChatStreamTests.Stream_OpensWithTheVaultTheWikiIsReadIn_WhenGrimoireWasToldBoth` |
| 888 | `ChatTests.Chat_IsWrittenTo_WhileAnotherThreadHoldsTheBoard` (streut stark: 1 767 / 13 / 888) |

- Median eines Fast-Testfalls: 3 ms, p90: 268 ms.
- 64 Testfälle brauchen ≥ 100 ms, 29 brauchen ≥ 500 ms.
- Die langsamsten Tests außerhalb der Kestrel-Klassen sind `TestCatalogueTests` (lädt Assembly-Metadaten, Σ 2,4 s) und `ChatTests.cs:319` (Nebenläufigkeit).

**Contract**

| ms | Test |
| ---: | --- |
| 2 236 | `WikiToolDoorTests.QuestionDoor_ServesTheTwoReadToolsAndNothingElse` |
| 888 | `MarkdownRunRecordTests.Read_IsAlwaysAWholeRecord_WhileTheRunIsStillAppendingToIt` |
| 394 | `WikiToolDoorTests.RunDoor_ServesTheFiveToolsAnIngestRunIsGranted` |
| 333 | `SqliteSubmissionStoreTests.FileWithoutThisVersionsColumns_ComesBackWithItsSubmissionsIntactAndItsFiguresAtZero` |
| 240 | `AgentProcessTests.Terminate_EndsTheProcess_WhenTheRecordedIdentityIsStillThatAgent` |
| 229 | `AgentProcessTests.Terminate_LeavesTheProcessAlone_WhenOnlyTheIdentifierMatches` |
| 227 | `FileSystemWikiStoreTests.WritePage_LandsOnDiskWithTheRecord` |
| 125 | `SqliteSubmissionStoreTests.Figures_RoundTripThroughTheFile` |
| 109 | `MarkdownRunRecordTests.Tail_IsTheWholeRecord_WhenNoHeadWasEverWritten` |
| 101 | `SqliteSubmissionStoreTests.RunWithoutASubmission_IsInNoListOfSubmissions` |

**E2E**

| ms | Test |
| ---: | --- |
| 13 092 | `AskingTheWikiTests.Cost_RisesBesideTheQuestionAgainstItsCeiling_WhileTheRunIsUnderWay` |
| 13 090 | `RunRecordViewTests.Run_IsOpenedFromItsRowAndReadInOrder` |
| 12 986 | `RestartTests.Restart_ShowsEverySubmission_WithTheRunThatWasInProgressFailed` |
| 12 591 | `AcknowledgementTests.Acknowledge_StartsTheWaitingSubmission_AndLeavesTheRunFailed` |
| 12 560 | `ChatLifetimeTests.Question_OffersTheOneControl_AfterItsRunGotNoAnswer` |
| 11 862 | `AnswerReferencesTests.Reference_IsPlainTextAndSaysOpeningIsNotSetUp_WithoutTheVault` |
| 11 862 | `SubmissionStatesTests.List_PutsANewSubmissionFirst_WhileThePageIsOpen` |
| 11 858 | `NavigationTests.Chat_IsReachedFromTheList` |
| 5 881 | `ChatLifetimeTests.Question_AppearsInEveryBrowserReadingTheChat` |
| 5 563 | `NavigationTests.ChatAndTheList_AreBothReachedFromARunsPage` |

Die acht Tests mit rund 12–13 s sind jeweils der erste Test einer Klasse. Sie tragen den parallelen Start von Browser-Kontext und Hub unter Konkurrenz; ein typischer E2E-Test braucht 1–5 s.

### Bewertung „Fast als Dev-Loop“, Referenz unter 20 s

- **Die Referenz hält knapp:** 14,6–15,6 s Wall-Clock auf dieser Maschine ohne Last, 16,3–17,7 s unter Last.
- **Das eigentliche Risiko ist das 15-s-Gate**, nicht die 20-s-Referenz. `--timeout 15s` begrenzt die Testsitzung; lokal liegt sie bei 12,4–13,7 s (83–91 %), unter Last bei 14,4–15,7 s.
  - Das Gate hat in keinem Lauf ausgelöst, auch nicht bei einer „Dauer“ von 15,5 s. Beobachtet: Die Zeile des Test-Hosts lautete im selben Lauf `erfolgreich (14s 593ms)`. Die naheliegende Erklärung ist, dass der Timeout die Host-Sitzung misst und „Dauer“ zusätzlich den Overhead von `dotnet test` enthält; aus dem Repo ist das nicht belegt.
  - Bei dem Tempo, in dem 004 Tests hinzufügt, wird es lokal reißen, lange bevor CI (3,5 s) es merkt.
- **Die Ursache ist eindeutig:** 14 % der Testfälle kosten rund 60 % der Wall-Clock. Ohne sie läge Fast lokal bei 5,7 s.

---

## 4. Contract-Tests

Definition (Constitution III.4, `.specify/memory/constitution.md:96–98`): *„Contract — one suite per adapter against the real external thing.“*

**Kein Test verwendet ein gefaktes `claude`.** Die Fast-Suite spielt aufgezeichnete Transkripte ab (`RecordedTranscript.cs`). `RealRun` (`tests/Grimoire.Contract.Tests/RealRun.cs:45–137`) baut den echten Hub in-process mit echten Adaptern und startet das echte CLI.

### Gegenseite je Test (48 Methoden / 52 Testfälle)

| Klasse | Methoden (Testfälle) | Gegenseite | Anmeldung | Urteil |
| --- | ---: | --- | --- | --- |
| `HarnessProcessTests` (`:37`, `:85`, `:124`, `:181`) | 4 | **echtes angemeldetes `claude`-CLI** (Abo) plus MCP-Tür des In-Process-Hubs | **ja** | passt |
| `AgentProcessTests` (`:48`, `:60`, `:73`) | 3 | echter OS-Prozess (`/bin/sleep 600`) | nein | passt |
| `SqliteSubmissionStoreTests` (`:70–356`) | 16 | echte SQLite-Datei | nein | passt |
| `MarkdownRunRecordTests` (`:30–290`) | 10 | echtes Dateisystem (auch verweigerndes, auch nebenläufig, ≈ 1 MB) | nein | passt |
| `FileSystemWikiStoreTests` (`:27–194`) | 13 (17) | echtes Dateisystem, Symlinks | nein | 8 passen, 4 grenzwertig, 1 → Fast |
| `WikiToolDoorTests` (`:30`, `:44`) | 2 | **eigener** MCP-Server über Loopback, in-process, gelesen mit dem SDK-`McpClient`; kein Agent | nein | → Fast |

Alle 48 Methoden wurden einzeln gelesen. Die Tabelle ist zu einer Zeile je Klasse verdichtet, weil innerhalb einer Klasse alle Methoden dieselbe Gegenseite haben. Die Ausnahmen sind unten einzeln mit Datei:Zeile genannt.

### Echte Anmeldung

**4 Tests**, alle in `HarnessProcessTests` (Klassen-Trait `:28`). CI schließt sie aus: `.github/workflows/ci.yml:36` mit `--filter-not-trait "requires=signin"`; kein anderer Workflow startet die Contract-Suite.

### Gilt „höchstens drei, nur lokal“ aus Plan 001 noch?

- **„Nur lokal“: ja.**
- **„Höchstens drei“: nein, es sind vier.** Die Regel steht in `specs/001-first-ingest/plan.md:55` (wiederholt in `:68`, `:253` und `research.md:496`).
  - Sie wurde durch **DEC-021** (`docs/decisions.md:172–178`) regelkonform auf vier angehoben, weil die Kosten von GUARD-004 gewichtet wurden (`Cost_WeighsAModelUsageAsTheCliBillsIt`).
  - `tasks.md` T084 sagt: „DEC-021's budget of four is spent“.
  - Der Stand ist also konform. Veraltet ist die Formulierung „drei“ an drei Stellen:
    - `CLAUDE.md:125` („there are at most three (DEC-021)“)
    - `.github/workflows/ci.yml:31–32` („the three HarnessProcess tests are excluded“)
    - `tests/Grimoire.Contract.Tests/AgentProcessTests.cs:16–17`
- **Hinweis zur Aussagekraft:** Zwei der vier Sign-in-Tests (`:37`, `:181`) starten `claude` über `RealRun.TranscriptAsync`, nicht über `HarnessProcess.DispatchAsync`. Sie beweisen das Verhalten des CLI für unsere Argumente, nicht die Stream-Behandlung des Adapters. Den Adapter selbst treiben nur `Nudge_…` (`:85`) und `Interrupt_…` (`:124`).

### Tests unter Contract, die nach Definition woanders hingehören

- **Nach Fast gehören 3 Tests:**
  - `WikiToolDoorTests.QuestionDoor_ServesTheTwoReadToolsAndNothingElse` (`:30`) und `RunDoor_ServesTheFiveToolsAnIngestRunIsGranted` (`:44`): Es gibt kein externes System. `HostedHub.cs:16–22` stuft genau dieses Setup (Loopback, in-process) selbst als Fast ein. Sie liegen wohl nur wegen des ≈ 2,2 s teuren ersten MCP-Handshakes in Contract.
  - `FileSystemWikiStoreTests.WritePage_IsRefused_WhenTheFrontmatterCannotBeRead` (`:43`): Er ruft nur `ProvenanceStamp.Apply` auf, das ist Domänenlogik. Der Adapter wird nie zum Schreiben aufgefordert, und die Prüfung „nichts auf der Platte“ ist trivial wahr.
- **Grenzwertig sind 4 Methoden (8 Testfälle):** `FileSystemWikiStoreTests` `:60` (4 Zeilen), `:73` (2), `:109`, `:123`. Die Verweigerung entscheidet unsere eigene Pfadlogik über Strings, nicht das Dateisystem. Vertretbar, weil sie den Adapter als Ganzes prüfen.
- **Nach E2E gehört keiner.**

---

## 5. E2E-Schnitt

**Aufbau**: `HubUnderTest` (`tests/Grimoire.E2E.Tests/HubUnderTest.cs:195–241`) betreibt den Hub in-process (Kestrel auf Loopback) mit echten Datei-, SQLite- und Record-Adaptern und einem In-Memory-`DrivableHarness` als Agent. Der einzige echte Fremdprozess ist Chromium.

**Warum es viele E2E-Tests gibt:** Die Fast-Suite lädt `wwwroot/` nie (`app.js` 264, `run.js` 635, `chat.js` 455 Zeilen). DEC-019 schließt npm und damit einen JS-Testrunner aus. Jede Entscheidung im JavaScript ist daher nur per E2E prüfbar. Das erklärt mehr E2E-Tests als trace-check.

### Journeys

| # | Journey | Tests (Datei:Zeile) |
| --- | --- | --- |
| J1 | Text einreichen | `SubmitTextTests.cs:14` |
| J2 | Einreichungsliste beobachten (Zustände, Modell, Zahlen, Live-Änderungen) | `SubmissionStatesTests.cs:31`, `:65`, `:111`, `:131`, `:212` |
| J3 | Fehlschlag quittieren | `AcknowledgementTests.cs:23`, `SubmissionStatesTests.cs:178` |
| J4 | Hub neu starten, Queue wiederfinden | `RestartTests.cs:22` |
| J5 | Protokoll eines beendeten Laufs lesen | `RunRecordViewTests.cs:30`, `:80`, `:109`, `:131`, `:345` |
| J6 | Protokoll eines laufenden Laufs verfolgen | `RunRecordViewTests.cs:159`, `:284`, `:313` |
| J7 | Wiki fragen, Antwort wachsen sehen (Zustand, Kosten, Summe) | `AskingTheWikiTests.cs:34`, `:74`, `:113`, `:156`, `:188`; `ChatLifetimeTests.cs:104`, `:131` |
| J8 | Schritte unter der Antwort ansehen | `AnswerReferencesTests.cs:35`, `:76` |
| J9 | Seitenverweis aus der Antwort | `AnswerReferencesTests.cs:126`, `:159`, `:193`, `:224` |
| J10 | Ein Chat für alle Browser; neuen Chat beginnen | `ChatLifetimeTests.cs:45`, `:73` |
| J11 | Zwischen den drei Seiten wechseln | `NavigationTests.cs:26`, `:40`, `:60` |
| – | ohne Browser | `RunRecordViewTests.cs:236` (vergleicht HTTP-Bytes mit Dateibytes) |

- **Es sind 11 Journeys mit Browser**, zusammengefasst nach Seite eher 7: Liste, Protokoll, Chat, Navigation, Neustart, Einreichen, Mehrere Browser.
- **Die Nutzerinteraktion ist dünn:**
  - 21 der 36 Tests führen außer einem `GotoAsync` keine Nutzeraktion aus.
  - Nur 2 Tests benutzen ein Formular (`SubmitTextTests.cs:14`, `ChatLifetimeTests.cs:45`); alle anderen Einreichungen und Fragen gehen per `HttpClient` (`HubUnderTest.cs:248`, `:266`).
  - Die J9-Tests folgen **keinem** Link, sie lesen nur `href`.
- **III.4 („at most two scenarios per user story“) ist überschritten.** Das ist seit 003 als offene Owner-Entscheidung notiert (`specs/003-live-run-record/review-checklist-walk.md:53–95`). 004 fügt hinzu: US1 ≈ 7, US2 6, US3 2 plus 3 Navigationstests.

### Gleicher Pfad, anderes Requirement oder andere Assertion

| Gruppe | Tests | Gemeinsamer Pfad | Unterschied |
| --- | --- | --- | --- |
| Liste statisch | `SubmissionStatesTests.cs:31`, `:65`, `:111` | Einreichung per API → Lauf treiben → `/` → Zeile lesen | welche Felder |
| Liste live | `SubmissionStatesTests.cs:131`, `:178`, `:212` | `/` offen → serverseitige Änderung → dieselbe Zeile | Geometrie / Fokus / Reihenfolge |
| **Protokoll fertig** | `RunRecordViewTests.cs:80` und `:109` (**identisches Setup**, Zeilen 85–89 = 114–118), dazu `:131`, `:345` | `run.html?submission=` → `#record li` | Falten / Segmentzahl / Prosa / Zuordnung |
| **Antwort wächst** | `AskingTheWikiTests.cs:34` und `:74` (**identisches Setup**) | Frage → Chat → `Said` ×2 | Bounding-Box vs. Textknoten-Identität |
| Kosten | `AskingTheWikiTests.cs:113`, `:156` | Frage → Chat → `Spend` | Geometrie vs. „kein `/`, keine Währung“ |
| **Verweise** | `AnswerReferencesTests.cs:126`, `:159`, `:193`, `:224` | Hub-Variante → Frage → `Said` mit Link → `href` lesen | nur die Vault-Eingaben; `:126` und `:193` unterscheiden sich nur in `wikiPathInVault` |
| Schritte | `AnswerReferencesTests.cs:35`, `:76` | Frage → `Said` + `Called` → `details.steps` | offen vs. Scroll erhalten |
| **Navigation** | `NavigationTests.cs:26`, `:40`, `:60` | Link-Hüpfen zwischen drei Seiten | wäre ein Durchgang |

### Dienen E2E-Tests trace-check?

- **trace-check verlangt für kein Requirement speziell E2E.** Nur ACCESS-001 (1 Test) und ACCESS-010 (3 Tests) haben *ausschließlich* E2E-Tests.
- Urteil je Test:
  - **23** prüfen etwas, das nur ein Browser zeigt: Geometrie, DOM-Identität, Fokus, Scroll, Live-Push, Klicks, zwei Leser.
  - **10** prüfen JavaScript-Logik, für die es sonst keinen Runner gibt: `SubmissionStatesTests.cs:111`, `RunRecordViewTests.cs:109`, `:131`, `:345`, `AskingTheWikiTests.cs:156`, die vier Verweis-Tests und `ChatLifetimeTests.cs:104`.
  - **3 tragen nichts bei, was ein Browser beisteuert:**
    - `RunRecordViewTests.Record_ServedIsTheFileOnDisk_AndTheWikiHoldsNoneOfIt` (`:236`) benutzt keine `Page`. Er ist gedeckt durch Fast `RunRecordEndpointTests.Record_IsAnsweredWithItsBytesUnaltered` und Contract `MarkdownRunRecordTests.Record_IsAMarkdownFileNamedAfterTheRun`.
    - `AskingTheWikiTests.Wiki_IsByteForByteWhatItWas_AfterAQuestionWasAnswered` (`:188`) ist **faktisch leer**: `DrivableHarness` hat keinen Weg ins Wiki, das Wiki *kann* sich nicht ändern. GUARD-005 beweisen Contract `WikiToolDoorTests:30` und 6 Fast-Tests.
    - `RestartTests.cs:22`: Der Wert liegt in zwei Hubs über einem echten SQLite-Verzeichnis, der Browser rendert nur. RUNS-004 hat 9 Fast- und 16 Contract-Tests.

---

## 6. Redundanz in der Fast-Suite (ohne Mutation-Testing)

Grundlage: alle 40 Testklassen und 9 Fixture-/Double-Dateien gelesen, Produktionszweige hinter jeder Theory geprüft.

**Vorab – Tests, die kaum Produktionscode ausführen:**

- **Nur das Test-Double wird ausgeführt:**
  - `FailedRunTests.cs:26`, `:38` (WIKI-003): schreiben in `InMemoryWikiStore` und lesen zurück.
  - `RunRecordTests.cs:142`, `:169`: rufen `hub.Record.Ended/Append` direkt auf. Die geprüften Wächter liegen in `InMemoryRunRecord`, nicht im Produkt.
  - Teilweise auch `RunRecordTests.cs:121` und `:192`: `LostEntriesNotice` ist ein Typ des Doubles.
- **Nicht fehlschlagbar:**
  - `ToolGrantTests.cs:87`, `Assert.False(hub.Harness.ReportedIn)`: `ReportedIn` wird nur gesetzt, wenn der Test selbst `ReportIn()` aufruft (`InMemoryAgentHarness.cs:60`, `:152`), was er nie tut. Stichprobe bestätigt.
  - `SubmissionStateTests.cs:95–101`: `Assert.Single(Enum.GetValues<SubmissionState>(), s => s == submission.State)` ist immer wahr.
  - `ProvenanceStampTests.cs:81` prüft den eigenen Parameter.
  - `AgentTranscriptTests.cs:143` vergleicht zwei Fixture-Konstanten.
- **Erreichen den genannten Pfad nicht:**
  - `CeilingTests.cs:122` (`…WhenTheElapsedCeilingIsReached`): `Advance(Elapsed)` löst den Timer schon aus (`RunConductor.cs:186`), das folgende `Spend(1)` bewirkt nichts. Damit ist er identisch mit `:174`.
  - Ebenso `:149` ≡ `:204` und `:250` ≡ `:190`.

### a) Gleiches Arrange und Act, nur die Assertion unterscheidet sich — 24 Gruppen

Viele dieser Aufteilungen erzwingt die README-Regel „A name that needs And is two tests“. Diese Gruppen sind als *erzwungen* markiert.

| # | Tests | Gemeinsames Arrange/Act | Assertions | erzwungen |
| --- | --- | --- | --- | --- |
| a1 | `DispatchPayloadTests.cs:15`, `:29`, `:47`, `:57` | `AcceptedAsync` → `Dispatched.Single()` | Inhalt / Gleichheit / Modell / Grant | ja |
| a2 | `QuestionPromptTests.cs:27`, `:48`, `:60`, `:70` | ein `AskedAsync` | Reihenfolge / Ende / Modell / kein „Asked:“ (`:48` und `:70` prüfen beide EndsWith) | ja |
| a3 | `ToolGrantTests.cs:75`, `:87` | Surface + „bash“, `AcceptedAsync` | Failed / `ReportedIn` (leer) | ja |
| a4 | `SubmissionRefusalTests.cs:57`, `:74` | `Refusing(text)` → `SubmitAsync` | Board leer / Dispatch leer | ja |
| a5 | `QuestionRefusalTests.cs:76`, `:92`, `:105` | `Refusing(text)` → `AskAsync` | Chat / Dispatch / Store leer | ja |
| a6 | `QuestionAcceptanceTests.cs:79`, `:93` | Einreichung, dann Frage | im Chat / wartet | ja |
| a7 | `QuestionAcceptanceTests.cs:34`, `:59` | `AskedAsync` + `Handed` | Turn-Felder / Chat-Antwort | teils |
| a8 | `AcknowledgementTests.cs:68`, `:81` | `AFailureWithTwoWaiting` + Quittung | Dispatch-Reihenfolge / bleibt Failed | ja |
| a9 | `RunOutcomeTests.cs:41`, `:55` | Fragelauf `AgentStopped(false)` | Done / kein Nudge (Done schließt Nudge aus) | ja |
| a10 | `RunOutcomeTests.cs:178`, `:190` | `IsNamedIn(...)` | Klartext / plus Großschreibung | nein |
| a11 | `ProvenanceStampTests.cs:22`, `:33` | dasselbe `Apply(...)` | Record ergänzt / Rest bytegleich | ja |
| a12 | `CeilingTests.cs:174`, `:190` | Accept, ReportIn, `Advance(Elapsed)` | `Stopped` / Failed | ja |
| a13 | `CeilingTests.cs:108`, `:204` | Accept, `Spend(Cost)` | `Stopped` / Failed | ja |
| a14 | `CeilingTests.cs:122` ≡ `:174` | s. o. | identisch | nein |
| a15 | `SubmissionAcceptanceTests.cs:19`, `:49` | ein `SubmitAsync` | Felder / Zustand | ja |
| a16 | `SubmissionAcceptanceTests.cs:33`, `:83` | `DispatchFailure`, Submit | `Stopped` / Failed + weiter | teils |
| a17 | `RunEndingTests.cs:33`, `:58` | Eintrag, `StoppedAsync` | Done / `ToldNothingFurther` (`:33` impliziert `:58`) | nein |
| a18 | `ChatTests.cs:364`, `:400` | Ask, Said, Called, `Chat.Start()` | leer / keine `Changes` | ja |
| a19 | `ChatTests.cs:123`, `:148` | Ask | nichts gespeichert / Laufzeile | teils |
| a20 | `RecordStreamTests.cs:105` ⊂ `:127` | Kopf, `FailWrites`, ein Call | `:127` ist Obermenge | nein |
| a21 | `QueueTests.cs:33`, `:47` | 1. gemeldet, 2. eingereicht | Zustand / Dispatch | ja |
| a22 | `QueueTests.cs:61` ⊂ `:91` | zwei wartend, erster endet | Obermenge | nein |
| a23 | `FailedQuestionTests.cs:98` ⊂ `:114` | Frage scheitert, Einreichung wartet | Präfix | nein |
| a24 | `AgentLifetimeTests.cs:74` ⊂ `:89` | Neustart mit Agent | Obermenge | nein |

Die README-Regel wird uneinheitlich angewandt. Diese bestehenden Namen enthalten „And“ im Ergebnis:

- `Dispatch_CarriesTheInstructionThePurposeTheTextAndTheRunIdentifier`
- `Figures_RiseWithTheRunAndNeverGoBackwards`
- `Grant_LeavesOutDeletingAndMoving`

### b) Einzige Assertion ist eine Interaktion — 25 Tests

Die Suite nutzt keine Mocking-Bibliothek; „Verify“ heißt hier: Zähler oder Liste am In-Memory-Double.

- **Legitim** sind die Fälle, in denen die Interaktion selbst das Requirement ist: Stop geht über den Port (GUARD-004/DEC-016), Agent wird beendet (RUNS-006), Dispatch-Reihenfolge ist die Queue (RUNS-002).
- **Fragwürdig** sind die Fälle, in denen ein beobachtbarer Zustand (`State`, `RunId`, Status) existiert und ungeprüft bleibt:

| Datei:Zeile | Assertion | Urteil |
| --- | --- | --- |
| `SubmissionAcceptanceTests.cs:33` | `Assert.Single(hub.Harness.Stopped)` | legitim (RUNS-006) |
| `ToolGrantTests.cs:87` | `Assert.False(hub.Harness.ReportedIn)` | **leer** |
| `SubmissionRefusalTests.cs:74` | `Assert.Empty(hub.Harness.Dispatched)` | legitim, aber a4 |
| `QuestionRefusalTests.cs:92` | `Assert.Empty(hub.Harness.Dispatched)` | legitim, aber a5 |
| `QuestionRefusalTests.cs:105` | `Store.Load()` / `LoadRunsWithoutASubmission()` leer | legitim, aber a5 |
| `QuestionAcceptanceTests.cs:108` | Store enthält den Text nicht | ⊂ `ChatTests.cs:123` |
| `QueueTests.cs:47`, `:75`, `:107`, `:91`, `:126` | Dispatch-Reihenfolge / `RunUnderWay` des Doubles | Reihenfolge legitim; `RunUnderWay` ist Buchhaltung des Doubles |
| `AcknowledgementTests.cs:94` | Dispatch-Reihenfolge | legitim |
| `FailedQuestionTests.cs:114` | Dispatch-Reihenfolge | legitim |
| `RestartTests.cs:177` | Dispatch-Reihenfolge ×2 | legitim |
| `AgentLifetimeTests.cs:74`, `:89`, `:114`, `:129` | `Terminated` / Journal-Reihenfolge | legitim (RUNS-006); `:74` ⊂ `:89` |
| `RunEndingTests.cs:58` | `ToldNothingFurther` | redundant zu `:33` |
| `CeilingTests.cs:108`, `:122`, `:162`, `:174` | `Harness.Stopped` | legitim; `:122` ≡ `:174` |
| `RunFiguresTests.cs:109` | Zahl der Store-Schreibvorgänge unverändert | legitim (RUNS-010 „nur wo gestiegen“) |
| `RunRecordTests.cs:221` | `Wiki.Asked` = [append log, read log] | legitim (RUNS-005 „nichts sonst lesen“), trägt aber nur RUNS-007 |

### c) Abhängigkeit von interner Struktur

- **`InternalsVisibleTo`** gibt es nur in `tools/Grimoire.Trace/Grimoire.Trace.csproj:15`, zugunsten von `Grimoire.Fast.Tests`. In `src/` gibt es keines; kein Produkttest kann `internal`-Mitglieder erreichen.
- **Trace-Tool-Tests (34 Methoden):** legitim. `TestCatalogue.Read` liest absichtlich Assembly-Metadaten.
- **Produkttests: 20 Treffer.**

| Datei:Zeile | Abhängigkeit |
| --- | --- |
| `FailedRunTests.cs:49` | Reflection: `typeof(IWikiStore).GetMethods()`-Namen gleich fester Liste |
| `RunFrameTests.cs:198` | `Enum.GetValues<RunEndedBecause>()` – Mitglieder und Deklarationsreihenfolge |
| `SubmissionStateTests.cs:23`, `:95` | `Enum.GetValues<SubmissionState>()` (`:95` tautologisch) |
| `CeilingTests.cs:21` | Literal-Konstanten 15 min / 2 000 000; III.8: statische Konfiguration wird nicht getestet |
| `QuestionGrantTests.cs:55` | Routen-Segment-Konstanten `"questions"` / `"runs"` |
| `ToolGrantTests.cs:38`, `:50`; `QuestionGrantTests.cs:69`, `:81`, `:124` | `WikiToolsServer.ServedNames` / `WikiReadToolsServer.ServedNames`: öffentliche statische Properties **ohne Produktions-Konsumenten** (Stichprobe: nur die Definitionen in `src/Grimoire.Hub/Mcp/*.cs:60`, `:67`); per Reflection über `[McpServerTool]` |
| `QuestionGrantTests.cs:98` | `HarnessProcess.ArgumentsFor`, Substring-Suche im argv |
| `AgentTranscriptTests.cs:27` | Adapter-Konstante `AgentTranscript.McpPrefix` |
| `RunRecordTests.cs:97`, `:192` | `Assert.IsType<RunFrameHead/RunMoment/RunFrameTail>` auf der Liste des Doubles; `LostEntriesNotice` (Typ des Doubles) |
| `CeilingTests.cs:135`, `:204`; `RunBoardTests.cs:186`, `:215` | `hub.Conductor.Of(...)` – Überwachungstabelle des Conductors statt `Status` |
| `RunOutcomeTests.cs:41`, `:55` | Flags der Zustandsmaschine `IsToChangeTheWiki`, `LogEntryNudged` |
| `SubmissionStateTests.cs:72` | `Board.ReportedIn` / `Board.Ended` direkt, Exception-Typ |
| `RunFiguresTests.cs:166` | `Board.RunFiguresAre(...)` direkt |
| `ChatTests.cs:381`, `:400` | `Chat.Generation`, `Snapshot().Changes` – Änderungsbuchhaltung |
| `ChatTests.cs:319`; `RunBoardTests.cs:215` | `Chat.AgentSaid`, `Board.Ask` + `Chat.Ask` direkt; absichtlich (Lock-Reihenfolge, Store-Fehler-Naht) |

### d) Theories mit Zeilen auf demselben Codepfad

| # | Theory (Datei:Zeile) | Zeilen | Codepfad | Urteil |
| --- | --- | ---: | --- | --- |
| 1 | `CapabilityRegistryTests.cs:54` | 5 | eine Verzweigung, aber jede Zeile bricht eine andere Regex-Klausel | behalten |
| 2 | `CapabilityRegistryTests.cs:109` | 4 | 3 Switch-Arme + Groß/Klein | behalten |
| 3 | `CapabilityRegistryTests.cs:132` | 2 | ein `Contains`, je Element eine Zeile | behalten (aggressiv 1) |
| 4 | `CapabilityRegistryTests.cs:139` | 3 | INGEST ≡ GUARD; OUTFLOW ist die Grenze | 3→2 |
| 5 | `CapabilityRegistryTests.cs:146` | 3 | INGEST-001 ≡ OUT-01 | 3→2 |
| 6 | `ChatStreamTests.cs:341` | 4 | drei Zeilen ein Zweig, "" ein zweiter | 4→3 (aggr. 2) |
| 7 | `ChatStreamTests.cs:355` | 3 | alle ergeben `..` auf Unix; `IsPathRooted` dort unerreichbar | 3→1 |
| 8 | `ChatStreamTests.cs:380` | 4 | drei Zeilen treffen den Namens-Operanden | 4→2 |
| 9 | `ProvenanceStampTests.cs:74` | 3 | Skalar ≡ Liste (`is not YamlMappingNode`) | 3→2 |
| 10 | `ProvenanceStampTests.cs:118` | 2 | beide `Documents.Count == 0` | 2→1 |
| 11 | `ProvenanceStampTests.cs:189` | 3 | Doppelpunkt vs. Erstzeichen; Anführungszeichen prüft zusätzlich Escaping | behalten (aggr. 2) |
| 12 | `QuestionAskedOverHttpTests.cs:56` | 2 | ein `IsNullOrWhiteSpace`; **jede Zeile startet Kestrel** | 2→1 |
| 13 | `QuestionRefusalTests.cs:51` | 3 | ein `IsNullOrWhiteSpace` (`RunBoard.cs:316`) | 3→1 |
| 14–16 | `QuestionRefusalTests.cs:76`, `:92`, `:105` | je 2 | Zeile „Text“ nimmt dieselben Zweige wie „""“ | je 2→1, zusammenlegbar |
| 17 | `RecordTextTests.cs:208` | 3 | ein Ausdruck `2 + Clamp(depth,0,2)`; Grenzen nie getroffen | 3→2 |
| 18–19 | `RunOutcomeTests.cs:135`, `:144` | je 2 | Deckel-Prüfung vor Log-Prüfung; `logEntry` irrelevant | je 2→1 |
| 20 | `SubmissionRefusalTests.cs:45` | 3 | ein `IsNullOrWhiteSpace` (`RunBoard.cs:261`) | 3→1 |
| 21–22 | `SubmissionRefusalTests.cs:57`, `:74` | je 2 | wie 14 | je 2→1, zusammenlegbar |
| 23 | `SubmissionStateTests.cs:45` | 2 | zwei Ergebnisse | behalten |
| 24 | `SubmissionStateTests.cs:72` | 2 | ein Wächter (`Queued.cs:151`) | 2→1 |
| 25 | `SubmissionStateTests.cs:242` | 4 | 4 Switch-Arme | behalten |
| 26 | `ToolGrantTests.cs:33` | 3 | vollständig durch die Gleichheit in `:23` gedeckt | streichen (Achtung: `TestCatalogueTests.cs:65` referenziert ihn per `nameof`) |
| 27 | `ToolGrantTests.cs:50` | 3 | vollständig durch die Gleichheit in `:38` gedeckt | streichen |

Von 27 Theories haben 21 Zeilen auf einem gemeinsamen Pfad. Wegfallen könnten 14 (konservativ) bis 21 (aggressiv) Testfälle.

### e) Dasselbe Verhalten in zwei Klassen

| Verhalten | Doppelt in (Datei:Zeile) |
| --- | --- |
| Surface ≠ Grant | `AgentTranscriptTests.cs:38`, `:45` vs. `ToolGrantTests.cs:65`, `:70` (Transkript delegiert an `grant.IsTheSurface`) |
| Dispatch-Modell | `QuestionPromptTests.cs:60` vs. `DispatchPayloadTests.cs:47` (ein Pfad, `RunQueue.cs:238`) |
| Kostendeckel beendet Lauf | `CeilingTests.cs:149` ≡ `:204` ≈ `RunFrameTests.cs:138` ≈ `RunFiguresTests.cs:74` ≈ `FailedQuestionTests.cs:45` |
| Zeitdeckel beendet Lauf | `CeilingTests.cs:250` ≡ `:190` ≈ `:174` ≈ `RunFrameTests.cs:122` ≈ `FailedQuestionTests.cs:34` |
| Laufende auf Hub-Ebene | `RunEndingTests.cs:33`/`:43`/`:92` ≈ `RunFrameTests.cs:93`/`:169`/`:106`; `AgentLifetimeTests.cs:26` ≈ `RunFrameTests.cs:186` – gleiches Arrange/Act, einmal `State`, einmal `EndedBecause` |
| Chat-Domäne vs. Chat-Stream | `ChatTests.cs:70` vs. `ChatStreamTests.cs:418`; `:45` vs. `:392`; `:96`/`:110` vs. `:178` – die Stream-Version deckt die Domäne mit ab, kostet aber je einen Kestrel-Start |
| Record NotFound | `RunRecordEndpointTests.cs:63`/`:73`/`:89` vs. `RecordStreamTests.cs:179`/`:189`/`:206` |
| Liste Deckel/Zahlen | `SubmissionListTests.cs:21` ⊂ `SubmissionStreamTests.cs:48` + `SubmissionStateTests.cs:180` (2,2 s für einen Test) |
| LiveUpdates | `LiveUpdatesTests.cs:30` ⊂ `:56`/`:93`; `:42` ⊂ `:73` |
| Katalog | `TestCatalogueTests.cs:44` ⊂ `:48` |
| Auszug | `SubmissionExcerptTests.cs:54` ⊂ `:45` |
| **Nicht redundant** | `SubmissionRefusalTests` vs. `QuestionRefusalTests`: `Accept` und `Ask` sind getrennter, parallel kopierter Produktionscode (`RunBoard.cs:247`/`:302`). Die Dopplung liegt im Produkt, nicht in den Tests. |

### Schätzung: ohne Verlust eines bewiesenen Verhaltens entfernbar oder zusammenlegbar (Fast)

| Kategorie | konservativ (Methoden / Testfälle) | aggressiv |
| --- | --- | --- |
| a) gleiches Arrange/Act | 6 / 11 | 25 / 30 (bricht die README-„And“-Regel) |
| b) reine Interaktion, schon gedeckt | 2 / 2 | 3 / 3 |
| c) interne Struktur | 2 / 2 | 6 / 6 |
| d) Theory-Zeilen auf einem Pfad | 0 / 14 | 0 / 21 |
| e) klassenübergreifend / Obermenge | 14 / 18 | 38 / 42 |
| **Σ** | **24 / 47 (≈ 6 % / 11 %)** | **72 / 102 (≈ 19 % / 24 %)** |

Vorbehalte:

- Die Kategorien sind überschneidungsfrei gezählt.
- Tests der Kategorie c und die Double-only-Tests tragen IDs (RUNS-001, RUNS-008, GUARD-004, RUNS-007, WIKI-003). `trace-check --complete` braucht vor dem Streichen jeweils einen anderen Träger.

---

## 7. Die richtigen Dinge?

### Entscheidungen (`docs/decisions.md`)

| DEC | Kern | Test | Urteil |
| --- | --- | --- | --- |
| 001 | Abo-Anmeldung, `ANTHROPIC_API_KEY` wird dem Kind entzogen | keiner. `HarnessProcess.cs:128` entfernt den Schlüssel; der Sign-in-Test startet über `RealRun` und entfernt ihn selbst (`RealRun.cs:97`) | **Lücke** |
| 002 | nur .NET, kein npm | – | nicht testbar (CI-Regel „kein `package.json`“ wäre die Prüfung; gibt es nicht) |
| 003/004/005 | `level`/`req`-Traits; Metadaten statt Runner | `TestCatalogueTests`, `TraceCheckTests` | geprüft |
| 006 | Namenskonvention | – | Review; siehe 8 |
| 007/008 | CA1502 ≤ 15; Budgets 15 s / 90 s | – | per Build bzw. CI erzwungen |
| 009 | `claude`-CLI starten | `HarnessProcessTests.cs:85`, `:124` | geprüft (nur lokal) |
| 010 | gepinntes `--model`, keine Aliase | `DispatchPayloadTests.cs:47`, `QuestionPromptTests.cs:60` prüfen nur `dispatch.Model`; kein Test prüft das argv | **nur indirekt**; Aliase verweigert nur `scripts/run-hub.sh:104–108`, der Hub (`Program.cs:86`) nicht. Die Aussage in `CLAUDE.md` („aliases are refused“) stimmt für den Hub nicht |
| 011 | Deny-by-default | `AgentTranscriptTests.cs:22–96`, `WikiToolDoorTests`, `HarnessProcessTests.cs:37` | geprüft |
| 012 | `--setting-sources ""`, eigenes Arbeitsverzeichnis | keiner | **Lücke** (Fast über `HarnessProcess.ArgumentsFor` möglich) |
| 013 | MCP je Lauf | `QuestionGrantTests.cs:98`, `WikiToolDoorTests` | geprüft |
| 014 | ohne Auth, nur Loopback | Loopback-Ablehnung (`Program.cs:99`) ungetestet | nur indirekt |
| 015 | gewichtete Kosten, zwei Deckel | `CeilingTests`, `AgentTranscriptTests.cs:104–274`, `HarnessProcessTests.cs:181` | geprüft |
| 016 | erst Interrupt, dann Kill | Interrupt ja (`HarnessProcessTests.cs:124`, `CeilingTests.cs:108`); **Kill nach 10 s (`HarnessProcess.cs:276–292`) ungetestet** | teilweise |
| 017 | Nudge über stdin | `HarnessProcessTests.cs:85`, `RunEndingTests.cs:92` | geprüft |
| 018–020 | TimeProvider, statische Seiten, Playwright | – | Werkzeug / nicht testbar |
| 021 | ≤ 4 Sign-in-Tests | genau 4 | nicht erzwungen (trace-check-Regel denkbar) |
| 022 | drei Metriken, **kein** Mutations-Badge | – | **Drift**: `ci.yml:303–330` und `README.md:6`, `:57` veröffentlichen ein viertes Badge |
| 023–030 | SQLite hinter Port; PID + Startzeit; Record-Port; Kopf/Anhängen/Schluss; Transkript; Fence; Zahlen als Spalten | `SqliteSubmissionStoreTests`, `AgentProcessTests`, `MarkdownRunRecordTests`, `RunNarrativeTests`, `RecordTextTests`, `RunFiguresTests` | geprüft |
| 031 | ältere Datei bekommt Spalten per `ALTER` | `SqliteSubmissionStoreTests.cs:356` prüft `ALTER` – **aber `:332` prüft, dass eine ältere Datei abgelehnt wird** („OWNER DECISION: the file goes“, im Testkommentar). Der Entscheidungstext (`decisions.md:258`: „asking them to delete it throws their list away“) widerspricht dem | **Test und Entscheidung widersprechen sich** |
| 032 | Polling | im Code ersetzt (`LiveUpdatesTests`, `*StreamTests`) | veraltet; T088 verschiebt ihn |

### Requirements Klausel für Klausel

Alle aktiven `test`-Requirements haben mindestens einen Test, der sie tatsächlich prüft. **Lücken oder nur scheinbar geprüfte Klauseln:**

| Req | Klausel | Befund |
| --- | --- | --- |
| ACCESS-004 | „when the submission was made“ | Fast prüft nur, dass das Feld existiert (`SubmissionStateTests.cs:211`); kein Test prüft, dass die Zeit gezeichnet wird (`app.js:87–88`) |
| ACCESS-006 | Wiederverbindung | Browser-Hälfte (`run.js:600–613`) ungetestet |
| ACCESS-007 | „Where the browser's connection … is lost and made again, … show the chat as it then stands“ | nur Fast (`ChatStreamTests.cs:219`); **das Zusammenführen in `chat.js:335–380` ist ungetestet** |
| ACCESS-008 | „for the chat what its questions have spent altogether“ | **nur mit einer Frage getestet.** Die Summe über mehrere Turns und die Kosten einer gescheiterten Frage (`Chat.cs:252`) sind ungetestet; der Inhalt des `question`-Events ebenfalls |
| ACCESS-009 | „without that opening changing anything in the wiki“ | kein Test; kein Link wird verfolgt |
| ACCESS-010 | „start a new chat from the chat“ | bewiesen von `ChatLifetimeTests.cs:73`, der die ID **nicht** trägt |
| GUARD-005 | „allow reading anything inside the wiki“ | nur Namen geprüft (`QuestionGrantTests.cs:40`, `:124`, `WikiToolDoorTests.cs:30`); kein Test ruft `read_page`/`list_pages` an `/mcp/questions/{id}` auf. `E2E AskingTheWikiTests.cs:188` prüft den Grant nicht (s. 5) |
| QUERY-002 | „in the same chat“ | dass ein Dispatch nach „neuer Chat“ nichts vom alten Chat trägt, ist ungetestet; `QuestionPromptTests.cs:154` prüft nur das Fehlen der Teilantwort, nicht der gescheiterten Frage |
| QUERY-005 | übersteht keinen Stopp | `RunBoardTests.cs:154` ist in diesem Punkt tautologisch: `FastHub` baut beim Neustart einen neuen `Chat` (`FastHub.cs:48`, `:109`). Echte Beweise: `ChatTests.cs:123`, `QuestionAcceptanceTests.cs:108` |
| QUERY-006 | „MUST NOT present anything the run had produced as its answer“ | nur im Browser (E2E `ChatLifetimeTests.cs:131`, hängt an CSS) |
| RUNS-003 | „a question's failure goes with the chat that held it when Grimoire stops“ | getestet nur für eine *laufende* Frage beim Stopp (`RunBoardTests.cs:154`); **eine vor dem Stopp gescheiterte, unquittierte Frage ist ungetestet** |
| RUNS-006 | Fragelauf beim Neustart als beendet markiert, zweiter Neustart beendet nichts | nur auf Adapter-Ebene (`SqliteSubmissionStoreTests.cs:294`); Reihenfolge für Fragen nicht geprüft |
| WIKI-002 | „the agent MUST be told why“ | die Ablehnung im Tool (`WikiToolsServer.cs:82–104`) ist ungetestet; `FileSystemWikiStoreTests.cs:43` ruft den Store nicht auf |
| (query.md:35) | Einreichung wird ohne Frage-Instruktion angenommen | kein Submit-Test setzt `QuestionInstructionPresent: false` |

### Verhalten aus product.md / Spec 004 ohne Test

| Quelle | Verhalten | Level |
| --- | --- | --- |
| `spec.md:261`, `:470–473` | Summe enthält die Kosten einer gescheiterten Frage | Fast |
| `spec.md:260`, `access.md:83` | Summe über mehrere Turns | Fast |
| `contracts/hub-http-api.md:105` | Felder des `question`-Events (`costSpent`, `total`, `awaitingAcknowledgement`) | Fast |
| `hub-http-api.md:25` | keine Run-ID im Chat-Stream (nur an der POST-Antwort geprüft, `QuestionAskedOverHttpTests.cs:31`) | Fast |
| `spec.md:254` | nach Neustart liest der laufende Fragelauf „failed“ | Fast |
| `runs.md:15` | vor dem Stopp gescheiterte, unquittierte Frage hält die Queue nicht | Fast |
| `query.md:35` | Einreichung ohne Frage-Instruktion wird angenommen | Fast |
| QUERY-002 × `spec.md:232` | Folgefrage nach neuem Chat trägt nichts vom alten | Fast |
| `spec.md:266` | Chat nach Wiederverbindung korrekt | E2E |
| `spec.md:203` | Klick auf Verweis ändert nichts im Wiki | E2E (geringer Wert, Obsidian öffnet) |
| `spec.md:256`, `:230` | Antwort ohne Seite; Folgefrage live mit eigenen Verweisen | E2E (gering) |

Die Erfolgskriterien SC-025 bis SC-037 (`spec.md:395–427`) enthalten keine Zeitgrenzen. Ihre „100 %“- und „every“-Aussagen misst keine automatisierte Prüfung, nur Einzelfalltests und der Abnahmelauf (T095).

### Umgekehrt: Tests, deren Verhalten nirgends steht oder deren ID nicht passt

- **Ohne Grundlage:** `AgentTranscriptTests.cs:353` (Nicht-JSON-Zeile) und `:364` (System-Nachricht ≠ init) – im Protokoll-Contract `specs/001-first-ingest/contracts/agent-cli-protocol.md` ist beides nicht geregelt.
- **Gegen oder über eine Entscheidung hinaus:**
  - `SqliteSubmissionStoreTests.cs:332` widerspricht DEC-031.
  - `RunOutcomeTests.cs:70` schreibt `StoppedWithItsLogEntry` für einen Fragelauf fest, der gar keinen Log-Eintrag hat. Das Ende-Grund-Label ist irreführend.
  - `ChatTests.cs:319` prüft Deadlock-Freiheit, die kein Requirement nennt, trägt aber ACCESS-007.
- **ID passt nicht zum Körper:**
  - Klassen-Trait ACCESS-007 auf `ChatStreamTests.cs:38`, vererbt an die Vault-Tests `:341–380` und die Quittungstests `:518–549`.
  - `ChatTests.cs:96` zeigt ACCESS-008-Verhalten ohne ACCESS-008.
  - `AgentTranscriptTests.cs:298–327` tragen GUARD-004, prüfen aber RUNS-005 „stopped on its own“.
  - `QuestionGrantTests.cs:124` behauptet „same arguments, same answers“, vergleicht aber nur Namen.
- **Fehlende IDs:**
  - `ChatLifetimeTests.cs:73` (ACCESS-010)
  - `RunRecordTests.cs:221` (RUNS-005)
  - `ChatTests.cs:148` (GUARD-003)
  - `FileSystemWikiStoreTests.cs:104` (RUNS-005)
- **`docs/trace.md` ist auf diesem Branch veraltet:** keine QUERY-Sektion, keine Zeilen für ACCESS-007…010, und in `:17` steht noch der umbenannte `Row_IsNotRebuiltUnderTheUser_WhileTheListPolls`. T087 regeneriert sie.

---

## 8. Namenskonvention

**Welches Schema geprüft wurde:** Das Repo definiert nicht „Subjekt_Aufgabe_Ergebnis[_Szenario]“ im Methodennamen, sondern Subjekt = Klasse (`<Subject>Tests`) und Methode = `<Action>_<Result>[_<Scenario>]` (`tests/README.md`, „How a test is named“). Geprüft wurde das Schema des Repos:

- 2 oder 3 Teile, PascalCase, genau ein Unterstrich als Trenner.
- Das Szenario beginnt mit When, While, With, Without oder After.
- Kein „And“, das zwei Ergebnisse oder zwei Bedingungen verbindet.
- Kein Works/Succeeds/Correctly/AsExpected als Ergebnis.
- Keine Statuscodes.
- Vokabular der Spec, nicht der Implementierung.

Die Prüfung ist heuristisch. Sie erkennt zum Beispiel nicht, dass in `ChatTests.NewChat_StartsNoQuestionThatWasStillWaitingWhenItWentAway` das Szenario ins Ergebnis gerutscht ist.

| | Methoden | Anteil |
| --- | ---: | ---: |
| erfüllt die harten Regeln (Teile, PascalCase, Szenario-Wort, „And“) | 381 / 465 | **81,9 %** |
| … und zusätzlich kein Implementierungswort | 352 / 465 | 75,7 % |

### Abweichungen

**Szenario beginnt nicht mit einem Bedingungswort: 46.** Verwendet werden „For“, „Where“, „Before“, „In“, „Once“, „Only“, „As“, „Until“, „Across“, „Beside“, „But“, „Because“, „From“, „Oldest“, „Even“ und „And“. Beispiele:

- `CeilingTests.Run_IsNotStoppedByTheClock_BeforeTheCeiling` (`:220`)
- `ChatStreamTests.Vault_IsNothing_WhereTheWikiIsNotInsideIt` (`:355`)
- `RestartTests.Restart_LeavesTheWaitingSubmissionsWaiting_UntilTheFailureIsAcknowledged` (`:152`)
- `RunFiguresTests.TerminalState_AndTheFinalFigures_ArePublishedTogether` (`:130`; Ergebnis steht im dritten Teil)
- `SqliteSubmissionStoreTests.Load_ReturnsSubmissions_OldestFirst` (`:165`)
- `WikiFileTests.Index_IsTheWholeName_AndNotAnEndingOfIt` (`:27`)
- E2E:
  - `AcknowledgementTests.cs:23`
  - `AnswerReferencesTests.cs:159`, `:193`
  - `AskingTheWikiTests.cs:156`
  - `RunRecordViewTests.cs:236`, `:313`
  - `SubmissionStatesTests.cs:65`, `:111`

„Where“ (8×) und „For“ (8×) sind die häufigsten. „Where“ ist faktisch zum sechsten Bedingungswort geworden, und `tests/README.md` sollte es entweder aufnehmen oder verbieten.

**„And“ im Szenario (zwei Bedingungen): 15.** Unter anderem:

- `CeilingTests.cs:190`, `:204` (`…AndTheAgentNeverReportsAgain`)
- `RunEndingTests.cs:33`, `:43`
- `RunOutcomeTests.cs:70`
- `QuestionPromptTests.cs:115`
- `AgentLifetimeTests.cs:89`
- `SqliteSubmissionStoreTests.cs:70`, `:86`, `:97`
- E2E `AcknowledgementTests.cs:23`, `AskingTheWikiTests.cs:156`, `RunRecordViewTests.cs:236`

**„And“ im Ergebnis (zwei Ergebnisse): 29.** Unter anderem:

- `DispatchPayloadTests.cs:15`
- `RecordTextTests.cs:82`, `:132`
- `RunFiguresTests.cs:31`, `:74`
- `RunFrameTests.cs:52`
- `RunRecordTests.cs:192` und `MarkdownRunRecordTests.cs:91` (`IsCountedAndDoesNotThrow`)
- `SubmissionExcerptTests.cs:45`
- `ToolGrantTests.cs:23`, `:33`
- `HarnessProcessTests.cs:37`
- `WikiToolDoorTests.cs:30`
- E2E `AnswerReferencesTests.cs:224`, `RunRecordViewTests.cs:30`, `:80`, `SubmissionStatesTests.cs:65`

Ein Teil davon ist eine Aufzählung („ReadingAndWriting…“) oder „…AndNothingElse“ (Vollständigkeit) statt zweier Ergebnisse. Die Zahl ist eine Obergrenze.

**Vier Teile: 1.** `SqliteSubmissionStoreTests.Load_ReturnsSubmissions_InTheOrderTheyWereAccepted_AfterTheClockWasPutBack` (`:184`).

**Verbotenes Ergebniswort: 0.** Die zwei Treffer auf „Succeeds“ stehen im Szenario („…WhenAWriteSucceedsAgain“), nicht im Ergebnis.

**Implementierungsvokabular: 33 Methoden.**

- 29× „Stream“ (`ChatStreamTests`, `SubmissionStreamTests`, `AgentTranscriptTests.StreamedUsage_*`)
- 2× „Endpoint“/„Handler“ (`ToolGrantTests.cs:38`, `:50`)
- 2× „Throw“ (`RunRecordTests.cs:192`, `MarkdownRunRecordTests.cs:91`)
- 1× „Adapter“ (`MarkdownRunRecordTests.cs:261`)
- 1× „Json“ (`AgentTranscriptTests.cs:353`)

„Stream“ ist diskutabel, weil die Spec von Live-Updates spricht.

**Klassennamen gleich einem Implementierungstyp** (README: „never a class or a method of the implementation“):

- Produkt:
  - `AgentTranscriptTests`, `RecordTextTests`, `LiveUpdatesTests`, `RunBoardTests`, `ProvenanceStampTests`, `WikiFileTests`, `RunRecordEndpointTests` – die Typen existieren in `src/`.
  - Die Adapter-Suiten `HarnessProcessTests`, `FileSystemWikiStoreTests`, `SqliteSubmissionStoreTests`, `MarkdownRunRecordTests`. Für Contract-Suiten („one suite per adapter“) ist der Adapter das Subjekt; dort ist das vertretbar.
- Trace-Tool: `CapabilityRegistryTests`, `TestCatalogueTests`, `TraceCheckTests`, `TraceSummaryTests`.
- `Submission`, `Run`, `Chat`, `ToolGrant` und `Ceiling` sind zugleich Typ und Spec-Vokabular; so nennt es die README selbst.

---

## Empfehlungen

Priorisiert nach Wirkung auf Dev-Loop-Zeit und Aussagekraft. Nur Vorschläge, nichts umgesetzt.

| # | Maßnahme | Wirkung | Tests betroffen |
| --- | --- | --- | --- |
| **1** | **Kestrel-Starts in Fast bündeln:** ein `HostedHub` je Klasse oder Collection (`IClassFixture`) statt je Test, oder die Stream-Tests, die nur Domänenverhalten prüfen, auf `FastHub` zurückführen (Chat vs. ChatStream, SubmissionList, Record-NotFound doppelt) | **Fast lokal ≈ 14,5 s → geschätzt 6–8 s.** Die Messung ohne die sechs Klassen ergibt 5,7 s. Das 15-s-Gate wäre wieder weit weg. | 59 Testfälle in 6 Klassen umbauen, davon etwa 8 streichbar (e: Chat/Stream 4, SubmissionList 1, Record-NotFound 2–3) |
| **2** | **Leere, tautologische und Double-only-Tests streichen oder reparieren:** `ToolGrantTests.cs:87`, `SubmissionStateTests.cs:95`, `CeilingTests.cs:122`/`:149`/`:250`, `RunEndingTests.cs:58`; `FailedRunTests.cs:26`/`:38` und `RunRecordTests.cs:142`/`:169` gegen den echten Adapter (Contract) statt gegen das Double; `E2E AskingTheWikiTests.cs:188` | Aussagekraft: Diese Tests zählen in trace.md, können aber nicht fehlschlagen oder prüfen das Double | ≈ 6 streichen, 4 verlegen, 1 E2E streichen oder so umbauen, dass der Harness wirklich schreiben *könnte* |
| **3** | **Theory-Zeilen auf einem Pfad zusammenlegen** (Abschnitt 6 d) | Zählung ehrlicher; `QuestionAskedOverHttpTests.cs:56` spart einen Kestrel-Start | −14 (konservativ) bis −21 Testfälle |
| **4** | **Obermengen und klassenübergreifende Doppelungen auflösen** (6 a/e, ohne die README-„And“-Regel zu brechen) | Wartung, Lesbarkeit | ≈ 14 Methoden / 18 Testfälle |
| **5** | **Contract bereinigen:** `WikiToolDoorTests` (2) nach Fast, sobald Punkt 1 den MCP-Handshake bezahlbar macht; `FileSystemWikiStoreTests.cs:43` durch einen echten Tool-Test ersetzen (siehe Lücke G4); veraltetes „drei“ in `CLAUDE.md:125`, `ci.yml:31`, `AgentProcessTests.cs:16` korrigieren | Level-Treue (III.4/III.6) | 3 Tests verlegen oder ersetzen |
| **6** | **E2E straffen:** Navigation 3→1 Durchgang; `RunRecordViewTests.cs:80`+`:109` und `AskingTheWikiTests.cs:34`+`:74` teilen sich je ein Setup; `RunRecordViewTests.cs:236` (ohne Browser) streichen; `AnswerReferencesTests.cs:126`/`:193` zusammenlegen. Die offene Owner-Entscheidung zu III.4 „≤ 2 Szenarien je Story“ treffen | E2E lokal ≈ 36 s → geschätzt 28–30 s; weniger Browser-Starts | 36 → etwa 29–31 |
| **7** | **Traits korrigieren**, vor T087: Klassen-Trait ACCESS-007 in `ChatStreamTests` auf Methoden verteilen; fehlende IDs ergänzen (`ChatLifetimeTests.cs:73` ACCESS-010, `RunRecordTests.cs:221` RUNS-005, `ChatTests.cs:148` GUARD-003, `ChatTests.cs:96` ACCESS-008, `FileSystemWikiStoreTests.cs:104` RUNS-005); `AgentTranscriptTests.cs:298–327` auf RUNS-005 | trace.md wird nicht gegen aufgeblähte Zahlen gelesen | ≈ 20 Trait-Änderungen, keine Teständerung |
| 8 | Namens-Abweichungen bei Gelegenheit; „Where“ in `tests/README.md` entscheiden | Lesbarkeit | 84 Methoden (hart), davon ≈ 30 Obergrenzen-Treffer bei „And“ |

**Summe:**

- Fast: konservativ −24 Methoden / −47 Testfälle, aggressiv −72 / −102.
- Contract: 3 Tests verlegen.
- E2E: −5 bis −7.
- Der größte Einzelgewinn für den Dev-Loop kommt aus Punkt 1, nicht aus dem Streichen: Er ändert keine Testanzahl, halbiert aber die Laufzeit.

### Lücken aus Abschnitt 7, die vor Phase 7 geschlossen gehören

**Warum vor Phase 7:** Phase 7 findet diese Lücken nicht.

- T083 (converge) sucht nach ungebauter Arbeit.
- T085 `--complete` ist grün, weil jedes Requirement mindestens einen Test hat.
- T091 (Mutation) mutiert laut `scripts/mutation.sh` `Grimoire.Hub` nicht. Genau dort liegt der größte Teil der 004-Logik: `Chat`, `ChatEndpoints`, `InstructionLoader`, `HubApplication.RestoreAfterAStop`, `WikiToolsServer`.

Priorität:

- **G1 – Fast:** ACCESS-008-Summe über ≥ 2 Fragen, eine davon gescheitert (`Chat.cs:252`), plus die Felder des `question`-Events, wenn eine Zahl steigt.
- **G2 – Fast:** RUNS-003-Schlussklausel, also ein Neustart mit einer *vor* dem Stopp gescheiterten, unquittierten Frage; dazu RUNS-006 mit doppeltem Neustart für einen Fragelauf.
- **G3 – Fast:**
  - QUERY-002: Folgefrage nach neuem Chat trägt nichts vom alten Chat.
  - Einreichung wird ohne Frage-Instruktion angenommen (`query.md:35`).
  - Keine Run-ID im Chat-Stream (`hub-http-api.md:25`).
- **G4 – Contract (läuft in CI):**
  - GUARD-005 „reading anything“: `read_page` über `/mcp/questions/{id}` tatsächlich aufrufen.
  - WIKI-002 „agent MUST be told why“: `write_page` mit unlesbarem Frontmatter liefert die Ablehnung, und nichts landet auf der Platte. Ersetzt `FileSystemWikiStoreTests.cs:43`.
  - Ob die Frage-Tür ohne registrierten Lauf liest, lässt sich aus dem Repo nicht entscheiden.
- **G5 – Dokumente, vor T088, keine Tests:**
  - DEC-031 ergänzen oder eine neue Entscheidung für die abgelehnte ältere Datei schreiben (derzeit nur ein Testkommentar, `research.md:149`, `data-model.md:125`).
  - `plan.md:36` korrigieren.
  - DEC-022-Badge-Drift auflösen.
  - `CLAUDE.md` „aliases are refused“ richtigstellen (gilt nur für `scripts/run-hub.sh`).
- **G6 – E2E:** Wiederverbindung in `chat.js:335–380` (ACCESS-007, letzte Klausel). Die einzige Lücke in einer 004-Klausel, die nur ein Browser zeigen kann.

**Kann warten**, ist nicht 004-Umfang:

- argv-Test für DEC-010/011/012 (Fast über `HarnessProcess.ArgumentsFor`).
- Stub-Executable-Contract-Test für DEC-001 (Schlüssel entfernt) und DEC-016 (Kill-Backstop).
- ACCESS-004-Zeitstempel im Browser.

### Aus dem Repo nicht beantwortbar

- Ob die vier Sign-in-Tests auf dem aktuellen Stand grün sind: Sie wurden nicht ausgeführt, eine Anmeldung ist nötig.
- Ob `/mcp/questions/{id}` ohne registrierten Lauf Lesezugriffe bedient.
- Wie viele der in Abschnitt 6 genannten Kandidaten die Mutationsmessung als tatsächlich überflüssig bestätigen würde. Das ist die Frage für nach Phase 7.
