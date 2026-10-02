# Closing review — 004-ask-the-wiki

Stand: `70b17d2` (Spitze von `004-ask-the-wiki`, baumgleich mit `main` @ `f027ee9`). Geprüft am
2026-10-02, nur gelesen, nichts geändert. Alle Pfade relativ zur Repository-Wurzel.

> **Vorab — der Merge ist schon geschehen.** PR #66 (`004-ask-the-wiki` → `main`) wurde am
> 2026-10-02 um 01:32Z gemerged (`f027ee9`); `git log origin/main..origin/004-ask-the-wiki` ist leer.
> Ein neuer Merge-PR hätte keinen Diff. Diese Review kam also *nach* dem Merge; was unten
> „blockiert den Merge“ heißt, blockiert jetzt den **Abschluss** der Feature und gehört in einen
> Nachzieh-PR gegen `main`.

## 1. spec.md ↔ docs/trace.md ↔ Traits

**Hält:**
- `trace.md` ist aktuell: `Grimoire.Trace -- write` erzeugt die Datei byte-gleich neu.
- `check --complete` meldet „39 requirements, 500 tests, no violations“. Die 39 schließen die zwei
  retirierten IDs ACCESS-002 und INGEST-005 ein (`tools/Grimoire.Trace/Program.cs:98` zählt alle Zeilen,
  `TraceSummary.cs:37` nur die aktiven). Es bleiben 37 aktive: 35 per Test bewiesen (35/35 haben
  Träger), 2 per Review (WIKI-001, QUERY-004).
- Alle 11 neuen IDs (QUERY-001–006, GUARD-005, ACCESS-007–010) sind in `docs/capabilities/`
  registriert und wortgleich mit `spec.md`. Die umformulierten RUNS-IDs stimmen ebenfalls; RUNS-005
  weicht um ein Komma ab.
- Keine retirierte ID steht auf einem Test.
- Alle 36 E2E-Tests tragen `req`.

**Abweichungen:**

*IDs auf Tests, deren Körper sie nicht beweist* (Stichprobe: rund zehn Tests je Bereich QUERY, ACCESS,
GUARD, RUNS gelesen):

| Ort | ID | Grund |
| --- | --- | --- |
| `tests/Grimoire.Fast.Tests/QuestionCostTests.cs:117` | ACCESS-008 | Prüft nur `awaitingAcknowledgement`, nichts über Kosten. ACCESS-003 passt, ACCESS-008 nicht. |
| `tests/Grimoire.Fast.Tests/QuestionCostTests.cs:138` | ACCESS-008 | Dasselbe. |
| `tests/Grimoire.Fast.Tests/RunOutcomeTests.cs:67` | RUNS-008 | Der Lauf einer Frage hat keinen Record. RUNS-008 greift also nicht, und der Test pinnt `StoppedWithItsLogEntry` (:79) für einen Lauf ohne Log-Eintrag. RUNS-005 ist korrekt. |
| `tests/Grimoire.Fast.Tests/RunRecordTests.cs:23-24` | RUNS-008, RUNS-009 | Strittig: Der Test beweist RUNS-007 („kein Record“). Der Kommentar :35-39 begründet 008/009 als Folge daraus. |

*Nur am Rand berührt:*
- `tests/Grimoire.Fast.Tests/ChatTests.cs:325` (QUERY-005): prüft die Deadlock-Freiheit.
- `tests/Grimoire.Fast.Tests/ChatTests.cs:283` (QUERY-005): prüft die Reihenfolge, gehört zu RUNS-002.
- `tests/Grimoire.Fast.Tests/SubmissionAcceptanceTests.cs:32` (QUERY-003): Gegenstand ist eine
  Submission, gehört zu INGEST-003.
- `tests/Grimoire.Fast.Tests/ChatChangeTests.cs:135` (QUERY-001): beweist ACCESS-007.
- `tests/Grimoire.Fast.Tests/QuestionAcknowledgementTests.cs:54` (ACCESS-003).
- `tests/Grimoire.E2E.Tests/AnswerReferencesTests.cs:158` (ACCESS-009): Ein Ziel außerhalb des Wikis
  abzulehnen steht nicht in ACCESS-009.
- `tests/Grimoire.Fast.Tests/RunBoardTests.cs:153` (QUERY-005): Das Audit nennt diesen Bezug
  tautologisch.

*Dokument-Drift:*
- `specs/004-ask-the-wiki/spec.md:318` und `:489` sagen „four are reworded“ bzw. „All four“. Die
  Changed-Tabelle hat aber fünf Zeilen (RUNS-003 kam in Phase 3 hinzu). Dasselbe steht in
  `docs/capabilities/runs.md:24`.
- `docs/capabilities/access.md:21`: Eine Leerzeile trennt die Tabelle. ACCESS-007–010 werden deshalb
  nicht als Tabelle gerendert; das Gate liest zeilenweise und merkt es nicht.
- Veraltete Kommentare:
  - `tests/Grimoire.Fast.Tests/QuestionGrantTests.cs:15-16` nennt eine „E2E-Hälfte“ von GUARD-005, die
    es nicht gibt.
  - `tests/Grimoire.E2E.Tests/AskingTheWikiTests.cs:17` und `AnswerReferencesTests.cs:19` verweisen
    auf `ChatStreamTests`, die Klasse existiert nicht mehr.
  - `tests/Grimoire.Fast.Tests/QuestionGrantTests.cs:126` behauptet „same arguments and same
    answers“, der Test vergleicht aber nur Namen.

## 2. docs/decisions.md ↔ Code

| DEC | Umsetzung | Beweis | Abweichung |
| --- | --- | --- | --- |
| DEC-031 (`docs/decisions.md:256-270`) | `src/Grimoire.Runs/Adapters/SqliteSubmissionStore.cs`: Gate über `user_version` :124, NOT-NULL-Probe :129, Kopie per `VACUUM INTO` :131-136, Rebuild in einer Transaktion :162-178 (+ :562-580), Fehler nennt die Kopie :142-148, `user_version = 1` :151, Spalten-Ergänzung :219-262 | `tests/Grimoire.Contract.Tests/SqliteSubmissionStoreTests.cs:344` (002-Datei), `:376` (003-Datei), gemeinsame Asserts :409-417 | (a) Der Fehlerpfad „Fehler nennt die Kopie“ hat **keinen Test**. (b) Geprüft wird nur, dass die Kopie existiert, nicht ihr Inhalt. (c) Die Kopie entsteht nur `if (!File.Exists(copy))` (:133): Eine alte Kopie bleibt liegen, und die Fehlermeldung kann auf sie zeigen. (d) Die Entscheidung sagt „by that number, not by inspecting columns“; `BringTheRunTableUpToDate` inspiziert aber weiter Spalten (:241-262). |
| DEC-034 (`docs/decisions.md:282-290`) | `src/Grimoire.Hub/HubApplication.cs:199-224` (`CreateEmptyBuilder`, `UseKestrelCore`, `AddRouting`, `AddSimpleConsole`), `src/Grimoire.Hub/Program.cs:15,27`, `src/Grimoire.Hub/StartUp.cs:71-77` | keiner; nach III.8 (Framework-Verdrahtung) zulässig, implizit durch jeden Hub-Test | keine: kein `appsettings*`, kein `AddJsonFile`, `FileSystemWatcher` oder `IConfiguration` im Baum |
| DEC-022 (`docs/decisions.md:180-190`) | `.github/workflows/ci.yml:3-7` (Trigger), `:206-208` (`mutation` nur bei push auf main oder dispatch), `:305-307` (Badge nur auf main) | keiner; CI-Konfiguration (III.8) | `docs/decisions.md:186` sagt noch „The mutation score gets no badge“. Die Ergänzung :190 überstimmt das, ohne die alte Zeile zu markieren. |
| DEC-021 (`docs/decisions.md:172-178`) | `tests/Grimoire.Contract.Tests/HarnessProcessTests.cs:28` (Klassen-Trait), 4 `[Fact]` (:37, :85, :124, :181); CI schließt sie aus in `ci.yml:36` | Anzahl 4 = Grenze | Nichts erzwingt die Zahl. Weil der Trait auf der Klasse sitzt, würde ein fünfter Test still zum Sign-in-Test. |

**„Owner decision“ außerhalb von decisions.md und der Constitution** (Regel: Constitution II.7,
`.specify/memory/constitution.md:82-86`):

Verstöße:
- `tests/Grimoire.Fast.Tests/RunNarrativeTests.cs:49`: „the owner's decision in the spec's
  Clarifications“. Richtig wäre ein Verweis auf DEC-029.
- `specs/004-ask-the-wiki/spec.md:71`, `:75`, `:80`: „OWNER DECISION“ zu RUNS-005, RUNS-007/008/009
  und GUARD-002. Es gibt keinen DEC-Eintrag; der Inhalt steht als Umformulierung in
  `docs/capabilities/`.
- `specs/004-ask-the-wiki/spec.md:83`: „OWNER DECISION: DEC-032 is contradicted“, ohne Verweis auf
  DEC-035.
- `specs/004-ask-the-wiki/spec.md:107`: „OWNER DECISION: … one requirement, not one per state“. Dafür
  gibt es **keinen DEC-Eintrag**.
- `specs/004-ask-the-wiki/research.md:102`, `:259`, `:286`, `:375`: Restatements von DEC-037, -042,
  -041 und -044. research.md zitiert keine einzige der Entscheidungen DEC-035–045.
- `specs/004-ask-the-wiki/plan.md:30`, `:37`, `:42`, `:44` und der Abschnitt `:268-276` („Owner
  decisions taken for this plan“, samt verworfenen Alternativen, die in decisions.md fehlen).
- `specs/004-ask-the-wiki/contracts/hub-http-api.md:17`: „The owner's decision“, ohne Verweis auf
  DEC-035.
- Grenzfall: `specs/004-ask-the-wiki/tasks.md:258` formuliert DEC-044 neu.

Unbedenklich (Verweise auf die V.1-Regel oder künftige Bedingungen):
- `src/Grimoire.Hub/StartUp.cs:8`, `HubApplication.cs:22`, `InstructionLoader.cs:13`
- `specs/004-ask-the-wiki/spec.md:62`, `:376`, `:437`; `research.md:367`; `tasks.md:260`, `:547`,
  `:625-636`; `contracts/question-run.md:57`
- `specs/004-ask-the-wiki/test-audit.md:360` und `:742` nennen eine *offene* Owner-Entscheidung, siehe
  §3.

Hinweis: spec, research und plan sind vom 2026-09-27, älter als II.7 (2.1.0, 2026-10-01). T090
Punkt 11 hat sie „unless the owner says otherwise“ stehen lassen. Eine Antwort des Owners dazu ist
nirgends festgehalten.

## 3. tasks.md

**Hält:**
- T001–T114 sind alle abgehakt, ohne Lücken und ohne Dubletten.
- „Later“ (`specs/004-ask-the-wiki/tasks.md:845-860`): fünf Punkte, jeder mit Begründung.
- spec, plan und research enthalten kein TBD, TODO oder NEEDS CLARIFICATION. Die Clarifications sind
  beantwortet (`specs/004-ask-the-wiki/spec.md:85-144`).
- 16 Stichproben abgehakter Phase-7-Tasks: Artefakt vorhanden (T088, T091, T094, T096–T104,
  T106–T113).

**Offene Fragen ohne Antwort** (in Phase 7 aufgeworfen, nur in Commit-Messages; PR #67 hat keinen
Abschnitt mit offenen Fragen):
1. **T083 Q1** (Commits `3756359`, `856f27e`): `specs/004-ask-the-wiki/spec.md:173-174` (US1-AS4) und
   `contracts/hub-http-api.md:216` verlangen Seitennamen *in der Prosa* als Link.
   `src/Grimoire.Hub/wwwroot/chat.js:187-216` zeichnet sie aber in einer Zeile neben der Antwort.
   Spec und Code widersprechen sich.
2. **T083 Q2:** `src/Grimoire.Hub/wwwroot/run.js:631` versucht nach einem 404 per
   `setTimeout(watch, 1000)` erneut, also Polling. Das widerspricht `run.js:584` („Nothing polls“)
   und DEC-035.
3. **T083 Q3:** `src/Grimoire.Hub/InstructionLoader.cs:137-139` lässt eine *gescheiterte* Frage aus
   dem Prompt einer Folgefrage weg. `f405abc` hat nur den Fall „Antwort leer“ gelöst.
4. **T090 Punkt 7:** Die E2E-Szenarien je Story (5/6/4) überschreiten III.4, „at most two scenarios
   per user story“ (`.specify/memory/constitution.md:103`). Als „for the owner“ markiert, nicht
   entschieden.
5. **T090 Punkt 11:** die OWNER-DECISION-Passagen, siehe §2.
6. **`specs/004-ask-the-wiki/mutation.md:233-241`:** Ob `RunAddress.RunId` gelöscht werden soll
   (`src/Grimoire.Hub/Mcp/WikiToolsServer.cs:16-27`), ist als „an open question for the PR“
   markiert und unbeantwortet.
7. **T093:** Den OUT-24-Vorschlag gibt es nur in Commit `fd824fd`; `docs/product.md` kennt ihn
   nicht. Als Vorschlag zulässig, aber nirgends verfolgt.

## 4. test-audit.md

| Empfehlung | Status | Beleg |
| --- | --- | --- |
| 1 | zurückgenommen | `specs/004-ask-the-wiki/test-audit.md:737`, Commit `8eefbca` (Zeit lag im Watcher, DEC-034) |
| 2 | erledigt | `fcd408c`. Das Dokument selbst markiert es nicht. |
| 3, 4 | Later | `specs/004-ask-the-wiki/tasks.md` Later, „Deletions from audit §6 a–e“ |
| 5 | erledigt bzw. Later | T104 (`8a6b075`), T110 (`4be4475`); WikiToolDoorTests → Fast steht in Later |
| 6 | **ohne Status** | `NavigationTests` hat noch drei Tests (:26, :40, :60), `AnswerReferencesTests:126`/`:193` sind nicht zusammengelegt, und der browserlose Test `RunRecordViewTests:242` besteht noch. Hängt an der III.4-Frage (§3 Nr. 4). |
| 7 | erledigt, mit Resten | `c606e9b`, `eb71e38`. Reste: `ChatTests.cs:325`, `RunBoardTests.cs:153`, `QuestionGrantTests.cs:126`, `AgentTranscriptTests.cs:353`/`:364` („ohne Grundlage“, unverändert) |
| 8 | **ohne Status** | `tests/README.md:41` listet weiter fünf Szenario-Wörter; die Entscheidung über „Where“ fehlt. |
| „Kann warten“: DEC-001-Stub-Test (`ANTHROPIC_API_KEY` entfernt) | **verloren** | weder erledigt noch in Later |

**§7-Lücken:** Nur drei Zeilen verweisen auf den schließenden Task: DEC-031 → T106/T107 (:582),
DEC-032 → T088 (:583), trace.md → T087 (:643). Die übrigen sind nur einseitig verknüpft: tasks.md
zitiert G1–G6 (T096–T111), das Audit verweist nicht zurück. In Code und Tests geschlossen sind sie
alle (verifiziert, siehe §3).

Ohne Task-Verweis geschlossen:
- DEC-014 Loopback, jetzt `tests/Grimoire.Fast.Tests/StartUpTests.cs:53` (`3201f57`). Das Audit
  sagt bei :570 noch „nur indirekt“.
- ACCESS-010 fehlende ID (`c606e9b`).

**Ohne Task und ohne Later-Eintrag:**
- DEC-001 (:564)
- DEC-021, nicht erzwungen (:578)
- ACCESS-006, Reconnect im Browser (`src/Grimoire.Hub/wwwroot/run.js:600-613`; :589)
- ACCESS-009, „Öffnen ändert nichts am Wiki“ (:591, :619)
- QUERY-006, nur per E2E/CSS bewiesen (:595)
- spec.md:256/:230 (:620)

## 5. CLAUDE.md und README.md

**Hält:**
- Weder CLAUDE.md noch README noch `tests/README.md` nennen Testanzahlen oder gemessene Laufzeiten.
  Die Budgets 15 s / 90 s (`CLAUDE.md:47-49`, `:133`) werden klar eingehalten (§7).
- Sign-in-Tests: „at most four“ (`CLAUDE.md:134`); es sind genau vier.
- Alias-Ablehnung: `scripts/run-hub.sh:106-109` lehnt `opus|sonnet|haiku|fable|default|""` ab, der
  Hub lehnt keinen ab.
- Badges: `README.md:3-6`, vier JSONs auf `origin/badges`; Trigger wie in `README.md:57` beschrieben
  (`ci.yml:110`, `:208`, `:307`).
- Requirements-Badge: „37 · 35/35“, stimmt.
- Alle genannten Pfade und Dateien existieren, ebenso die Architekturdateien.

**Abweichungen:**
- `CLAUDE.md:143`: „read the current plan at specs/003-live-run-record/plan.md“. Veraltet, richtig wäre
  004 (vgl. `CLAUDE.md:22`).
- `CLAUDE.md:95`: „the hub itself (`Program.cs`) refuses none“. Die Aussage stimmt, die Argumente
  liest aber `src/Grimoire.Hub/StartUp.cs` (:35, :57-59).
- `CLAUDE.md:114`: „the only file in the tree that names SQLite“. Wörtlich falsch
  (`src/Grimoire.Hub/Program.cs:9,31`, `ISubmissionStore.cs:111`, `Grimoire.Runs.csproj:11,14`);
  gemeint ist „die einzige Stelle, die die SQLite-API benutzt“, und das stimmt.
- Mutation-Badge auf `origin/badges` zeigt **82 %**. Der Wert ist aus einem Lauf vor der Aufnahme des
  Hubs weitergetragen. Nach T091 ergibt sich gesamt (788 + 14) / (1087 + 97) ≈ **67,7 %**. Der erste
  CI-Lauf mit Hub (Run 36951410197, Job `mutation`, auf `f027ee9`) lief bei Redaktionsschluss noch.
- `README.md:57` und `docs/decisions.md:188` nennen „twenty to thirty minutes“. Das ist eine
  Schätzung; gemessen sind lokal 22 min (mutation.md, T091), eine CI-Dauer gibt es noch nicht.
- `scripts/mutation.sh` ist veraltet:
  - :2 und :11 nennen 001-first-ingest;
  - :19-25 nennen 15–20 / 45–60 min und 441 Tests;
  - :75 nennt „97 of the 753“, T091 maß 51 von 760.
- Nebenbefund: Die Alias-Liste in `scripts/run-hub.sh:106-109` kennt `opusplan` und `sonnet[1m]`
  nicht.

## 6. mutation.md

Der Hub ist enthalten:
- `Grimoire.Mutation.slnx:10`
- `stryker-config.json:4`
- `scripts/mutation.sh:83` (ohne `Program.cs`)

**Nach T091** (Lauf auf `fd824fd`, 442 Fast-Tests, `specs/004-ask-the-wiki/mutation.md:89-95`):

| Projekt | Score | CompileError |
| --- | ---: | ---: |
| Agent | 88,03 % | 39 |
| Runs | 67,34 % | 40 |
| Hub | 57,91 % | 51 |
| Wiki | 85,85 % | 3 |
| Trace | 76,38 % | 28 |

Laufzeit: 22 min gesamt, davon Hub 7 min. Andere Projekte haben keine Einzelzeiten.

**Abweichungen:**
- **Kein Stand „nach Pruning“.** Das Test-Pruning (`fcd408c`, in PR #63, 2026-09-29) liegt *vor* dem
  T091-Lauf (2026-10-01). Die T091-Zahlen sind damit schon der Stand nach Pruning; mutation.md sagt
  das aber nirgends, und einen Lauf vor dem Pruning zum Vergleich gibt es nicht. Ein späterer
  „Pruning-PR“ mit eigener Messung ist im Log nicht zu finden; falls ein anderer PR gemeint war,
  fehlt dessen Messung.
- Kein Gesamtscore (die Zahl, die das Badge zeigt).
- Interne Rechenfehler:
  - Der Abschnitt „What was read“ nennt 335 Überlebende/Nicht-abgedeckte in Runs, Agent und Hub; die
    Tabelle ergibt 337.
  - „35 more“ für Wiki und Trace; die Tabelle ergibt 45.
- „Created“ minus (Tested + NC + CE) bleibt ungeklärt: Agent 12, Runs 74, Hub 172, Wiki 17,
  Trace 221 (vermutlich ignoriert oder gefiltert, aber nicht gesagt).
- Warum die CompileErrors des Hubs zwischen Referenzlauf und T091 von 97 auf 51 fielen, ist nicht
  erklärt.

## 7. CI und Laufzeiten

- **PR-Lauf auf `70b17d2`** (Run 36951187397, 2026-10-02 01:29Z): grün. trace-check, build-and-test
  und e2e grün; mutation, metrics und mutation-badge wurden planmäßig übersprungen.
  - Fast: 464 Tests, **3 s 781 ms**
  - Contract: 51 Tests, 2 s 378 ms
  - E2E: 36 Tests, 9 s 077 ms
- **Push-Lauf auf `main` @ `f027ee9`** (Run 36951410197): build-and-test, e2e, trace-check und
  metrics grün; `mutation` lief bei Redaktionsschluss noch.
- **Fast lokal, 2026-10-02** (`70b17d2`, `--no-build`): 3 s 781 ms, 3 s 838 ms, 3 s 335 ms, jeweils
  464/464 grün. Contract lokal: 51 Tests, 3 s 635 ms. Budget 15 s bzw. 90 s.

---

## Abweichungen, sortiert

### Blockiert den Merge (hier: den Abschluss von 004, da #66 schon gemerged ist)

1. **Prozess:** #66 wurde gemerged, bevor diese Closing-Review vorlag.
2. **Spec ↔ Code:** US1-AS4 „Seitennamen in der Prosa als Link“
   (`specs/004-ask-the-wiki/spec.md:173-174`, `contracts/hub-http-api.md:216`) gegenüber der Zeile
   neben der Antwort (`src/Grimoire.Hub/wwwroot/chat.js:187-216`). Entweder die Spec nachziehen oder
   den Code ändern, als Owner-Entscheidung.
3. **Offene Fragen ohne Antwort:** T083 Q2 (`src/Grimoire.Hub/wwwroot/run.js:631` pollt), T083 Q3
   (`src/Grimoire.Hub/InstructionLoader.cs:137-139`), III.4 E2E-Anzahl (T090 Punkt 7),
   `RunAddress.RunId` (`specs/004-ask-the-wiki/mutation.md:233-241`), OUT-24 (T093).
4. **II.7:** „OWNER DECISION“ in `specs/004-ask-the-wiki/spec.md:71,75,80,83,107`,
   `research.md:102,259,286,375`, `plan.md:30,37,42,44,268-276`,
   `contracts/hub-http-api.md:17` und `tests/Grimoire.Fast.Tests/RunNarrativeTests.cs:49`. Für
   `spec.md:107` gibt es überhaupt keinen DEC-Eintrag.
5. **Req-Traits ohne Beweis:**
   - `tests/Grimoire.Fast.Tests/QuestionCostTests.cs:117`, `:138` (ACCESS-008)
   - `tests/Grimoire.Fast.Tests/RunOutcomeTests.cs:67` (RUNS-008)
6. **test-audit:** Empfehlungen 6 und 8 ohne Status; DEC-001-Stub-Test verloren; §7-Lücken DEC-001,
   DEC-021, ACCESS-006 (Reconnect), ACCESS-009 (Wiki unverändert) und QUERY-006 weder geschlossen
   noch in Later.
7. **mutation.md:** sagt nicht, dass T091 = Stand nach Pruning ist, und hat keinen Gesamtscore.

### Kann nach dem Merge

1. DEC-031: Test für den Fehlerpfad (Meldung nennt die Kopie), Kopie bei jedem Rebuild erneuern
   (`SqliteSubmissionStore.cs:133`), Spalteninspektion gegen „not by inspecting columns“ klären
   (:241-262).
2. DEC-021: die Vierer-Grenze erzwingen oder den Trait auf Methoden setzen
   (`tests/Grimoire.Contract.Tests/HarnessProcessTests.cs:28`).
3. `docs/decisions.md:186`: veralteten Satz zur DEC-022-Begründung markieren.
4. Nur am Rand berührte IDs: `ChatTests.cs:283`, `:325`; `SubmissionAcceptanceTests.cs:32`;
   `ChatChangeTests.cs:135`; `QuestionAcknowledgementTests.cs:54`; `AnswerReferencesTests.cs:158`;
   `RunBoardTests.cs:153`; dazu `RunRecordTests.cs:23-24` (strittig).
5. Veraltete Kommentare:
   - `QuestionGrantTests.cs:15-16`, `:126`
   - `AskingTheWikiTests.cs:17`, `AnswerReferencesTests.cs:19`
   - `AgentTranscriptTests.cs:353`, `:364`
6. Dokument-Drift:
   - `specs/004-ask-the-wiki/spec.md:318`, `:489` und `docs/capabilities/runs.md:24` („four“ statt
     fünf)
   - `docs/capabilities/access.md:21` (zerbrochene Tabelle)
7. CLAUDE.md: `:143` (003-Plan), `:95` (`StartUp.cs` statt `Program.cs`), `:114` (Wortlaut zu
   SQLite).
8. Mutation-Badge 82 % → ca. 68 %: korrigiert sich mit dem laufenden CI-Job. Danach `README.md:57`
   und `docs/decisions.md:188` mit der gemessenen CI-Dauer abgleichen.
9. `scripts/mutation.sh:2,11,19-25,75`: Header und Zahlen veraltet.
10. mutation.md: Rechenfehler (335/337, 35/45), ungeklärte Mutanten, Gründe für den Rückgang der
    Hub-CompileErrors.
11. test-audit §7: Rückverweise auf T096–T111 eintragen; :570 (DEC-014) aktualisieren.
12. `scripts/run-hub.sh:106-109`: weitere CLI-Aliase (`opusplan`, `sonnet[1m]`).
