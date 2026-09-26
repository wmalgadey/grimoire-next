# Brief: live-run-record

Owner-written input for `/speckit-specify` (sections 1–5) and `/speckit-plan` (section 6). One page.
Wishes are wishes; the implementing agent decides layout and implementation (see `docs/ux.md`).

## 1. Outcome

- Outcome: **OUT-02** — see for every run what it did, why it ended and what it cost —
  **together with OUT-16** — watch what the agent is doing while a run is in progress.
  The owner decided to close both in one feature: the recording is the same work either way, and
  the list already polls every second, so serving the record during the run is what makes it live.
  `docs/product.md` moves OUT-16 into this feature's row when the outcome is marked Done.
- Afterwards I can say: "I can watch a run as it happens, see what it is costing me and which model
  is spending it, and afterwards read back everything it did — in Grimoire or in my own editor."

## 2. Walkthrough

I paste a text into Grimoire and submit it. The row appears in the list at once and turns
`running`. From then on the row tells me which model is driving this run, what it has cost so far,
and how many tool calls it has made; the numbers grow while I watch. When I want to know what the
agent is actually doing, I open the row and see the run in full: each tool call with its arguments,
what came back from it, and what the agent wrote between them. I leave it open and watch the next
call arrive. Then I walk away. Later I come back: the run reads `done`, the row shows the final cost
and the total number of calls, and the detail still holds everything — the model, the granted tools,
both ceilings with where they stood, when the run started and ended, and why it ended that way.
When it reads `failed` I see there what stopped it — a ceiling, a missing log entry, a process that
died — and acknowledge it the way I do today. And because the run is a Markdown file of its own, I
can open it in my editor months later without Grimoire running at all.

## 3. Wishes

- Like Claude Code in the terminal: the tool call is one line, its result folded underneath, the
  agent's own text as prose in between.
- The list row carries two numbers — cost so far and number of tool calls — plus the model. It is
  still a row: growing numbers must not make it jump or reflow while I am reading the list.
- One Markdown file per run, in a `runs/` directory of Grimoire's own. The detail view is a window
  onto that file, not a second place where the information lives.
- A run that is still going and a run from last month look the same; the live one just has fewer
  lines yet.

## 4. Not in this feature

- Being told a run ended without Grimoire open — later (OUT-17, has its own promotion trigger).
- Comparing runs, trends, statistics over time — later (OUT-13).
- A stop button for a run in progress — later, no ID yet. Today only a ceiling ends a run
  (GUARD-004); a button the user presses is a new requirement in no outcome.
- Cost shown in currency — never. DEC-015 already rejected it: the CLI's own budget figure is a
  client-side estimate the documentation says can differ from the bill. Cost is tokens.
- Anything of Grimoire's bookkeeping inside the wiki — never (Invariants 1 and 3, DEC-023).

## 5. Already in force

- Decisions: DEC-009 (the CLI's NDJSON stream is where a run's events come from), DEC-010 (the model
  is a pinned id), DEC-015 (cost is the four token fields of every model the run touched),
  DEC-019 (the browser surface is static files, no bundler), DEC-023 (state lives in SQLite under
  `--state`, never in the wiki).
- Capabilities touched: RUNS (the record), ACCESS (the browser showing it).
- Existing requirements this builds on: RUNS-001 (the four states), RUNS-004 (state survives a
  restart), RUNS-005 (how a run ends), GUARD-003 (the grant is recorded), GUARD-004 (the two
  ceilings), ACCESS-004 (what tells one submission from another).
- **ACCESS-002 is the requirement this feature contradicts**: it says the browser shows one of the
  four states "and no further detail about the run". It is retired and replaced here, its ID kept
  (Constitution IV.1, IV.2).

## 6. Plan notes (input for /speckit-plan only)

- Constraints:
  - No bundler, no npm (DEC-019). If the detail view needs Markdown rendered, weigh serving it as
    text in monospace against any dependency — the file is meant to be readable as text anyway.
  - Nothing Grimoire records goes into the wiki (DEC-023's reasoning, Invariants 1 and 3). `runs/`
    is Grimoire's, beside the SQLite file under `--state`.
  - `AgentTranscript` stays the only reader of the CLI protocol. Tool calls and agent text are new
    message kinds for it; `system/init`, streamed usage and `result` are already read.
  - RUNS-005's "Grimoire MUST read nothing else in the wiki" is untouched — the run file is not in
    the wiki.
- Ideas:
  - `runs/<runId>.md`, appended as the run proceeds. Appending is also the cheapest thing to make
    crash-safe, which RUNS-004's reasoning in DEC-023 cares about.
  - The two aggregates the list row needs (cost so far, tool-call count) are state and belong in
    SQLite, where the list poll already reads. Parsing them back out of the Markdown every second
    would mean reading prose Grimoire just wrote. Expect a seam: numbers in SQLite, narrative in
    Markdown, both written from the same event.
  - The detail view may well be a second static page, or the same page expanded. Owner has no
    preference; `docs/ux.md` applies.
- Open questions for the plan:
  - Tool *results* can be kilobytes — a run that reads twenty wiki pages writes a large file. How
    the file stays readable (truncation, folding, a size ceiling) is a real design question the
    owner has not decided.
  - Whether the detail view polls the file or an endpoint, and at what rate. The list's one second
    (`app.js`) is the existing precedent.
- Done spikes: none.
