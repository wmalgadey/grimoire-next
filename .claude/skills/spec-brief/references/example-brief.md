# Example brief (written in the owner's language, German)

Illustrates shape and tone. The feature is the deferred follow-up of 001-first-ingest: submissions while a run is active. Details are plausible, not authoritative — verify IDs against the repo.

---

# Brief: ingest-queue

Owner-Input für `/speckit-specify` (Abschnitte 1–5) und `/speckit-plan` (Abschnitt 6). Eine Seite. Wünsche sind Wünsche; Layout und Umsetzung entscheidet der Agent (siehe docs/ux.md).

## 1. Outcome

- Outcome: OUT-01 — Text im Browser einreichen, verlinkte Seiten samt Quellseite entstehen
- Danach kann ich sagen: „Ich reiche ein, wann ich will, und muss nicht warten, bis der letzte Lauf fertig ist."

## 2. Walkthrough

Ich öffne Grimoire, während ein Lauf noch läuft. Ich füge einen zweiten Text ein und schicke ihn ab. Statt einer Ablehnung sehe ich meine Einreichung als wartend unter dem laufenden Lauf. Ich schließe den Browser. Zwei Stunden später öffne ich Grimoire wieder: der erste Lauf ist fertig, der zweite lief danach von selbst und ist ebenfalls fertig, beide Log-Einträge stehen da. Beim ersten sehe ich den Hinweis, dass er noch nicht bestätigt ist, und bestätige ihn, damit die Ergebnisse als gelesen gelten. Wenn ich Grimoire zwischendurch neu starte, sind die wartenden Einreichungen noch da.

## 3. Wünsche

- Eine Liste wie eine Druckerwarteschlange: oben läuft einer, darunter warten die anderen, in Reihenfolge
- Ablehnen einer Einreichung soll nicht mehr vorkommen, solange Platz ist; wenn die Schlange voll ist, sehe ich das vor dem Absenden
- Der Bestätigen-Schritt darf nicht nerven — ein Klick, keine Frage

## 4. Nicht in diesem Feature

- Reihenfolge ändern oder wartende Einreichung löschen — später
- Mehrere Läufe parallel — nie (Wiki-Konsistenz, siehe DEC-00x)
- Modellwahl pro Einreichung — später, eigenes Outcome

## 5. Gilt schon

- Entscheidungen: DEC-001 (Abo-Anmeldung), DEC-00x (kein Commit, kein Rollback)
- Capabilities berührt: RUNS, ACCESS, INGEST
- Bestehende Requirements: INGEST-005 (Ablehnung bei laufendem Lauf — wird ersetzt), RUNS-002, RUNS-003, RUNS-004, ACCESS-003 (IDs aus 001 reserviert)

## 6. Plan-Hinweise (nur für /speckit-plan)

- Constraints: Persistenz kommt hinzu — kleinste Lösung, die Neustart überlebt; keine DB-Migrationstools; Harness bleibt CLI-Subprozess
- Ideen: Schlange als Datei/SQLite neben dem Wiki, nicht im Wiki; Länge fest, nicht konfigurierbar (wie die Ceilings)
- Spikes: keine

---

## Why it looks like this

- Section 2 carries the UX: waiting state, restart survival, acknowledge — all as story, none as requirement IDs.
- Section 3 uses one reference (print queue) instead of a wireframe.
- Section 4 names the split brake before `/speckit-analyze` can ask for one.
- Section 6 holds the only technology, marked so `/speckit-specify` skips it.
