# Quickstart: The First Ingest

**Feature**: `001-first-ingest` · **Date**: 2026-09-20

Two things live here. The **acceptance run** is what closes the feature (Constitution I.9) — the
owner exercising OUT-01 once, with a real model and a real wiki, no stand-ins. The **validation
scenarios** are how the automated suites and the two gates are run.

For what each interface looks like, see [`contracts/`](contracts/); for the entities,
[`data-model.md`](data-model.md). Neither is repeated here.

---

## Prerequisites

| | |
| --- | --- |
| .NET SDK | 10.x — builds the hub, `trace-check` and all three test suites. It is the only build toolchain: there is no `npm`, no bundler and no TypeScript (research.md R-01, R-10, R-11) |
| `claude` CLI | **2.1.240 or newer**, on `PATH`, **signed in with the owner's subscription** (`claude auth`). This is the external system the agent adapter wraps. 2.1.240 is the build every finding in research.md R-11 was observed on; an older one may lack `--tools`, `--setting-sources` or the `interrupt` control request, and `-p` is documented to ignore settings it cannot validate *silently*, so such a build would fail open rather than loudly. `HarnessProcess` therefore refuses to start a run whose `system/init` does not report the grant as its tool list and `interrupt_receipt_v1` among its capabilities. `--permission-prompts` (2.1.259 or newer) is not used and not required |
| Browsers | `pwsh tests/Grimoire.E2E.Tests/bin/…/playwright.ps1 install` once, for the E2E suite |
| A wiki | A git repository the owner controls. May be empty — the instruction states the shape the first run is to create |
| A purpose description | Hand-written, at the path the hub is configured with. Without it every submission is refused (INGEST-003) |
| A model id | A pinned id, given to the hub at start-up. Every run is dispatched on it; an alias or the CLI's default is not used (INGEST-002, plan Technology decisions) |

No `ANTHROPIC_API_KEY`. DEC-001 puts this project on subscription sign-in, and the CLI is that path;
an API-key adapter behind the same port is DEC-001's fallback, not this feature's wiring. If the
variable happens to be set on your machine it does no harm: `HarnessProcess` removes it from the
child's environment, so a run is on the subscription or does not start (research.md R-11).

---

## The owner's acceptance run

**Outcome exercised**: OUT-01 — submit a text in the browser and afterwards find new, linked pages
including a source page in the wiki.

**Real external systems in place**: the `claude` CLI signed in and reaching a real model, and a real
wiki repository on disk. No stand-ins, no temporary directory.

### Steps

1. Start the hub against the real wiki and the real purpose description:

   ```
   dotnet run --project src/Grimoire.Hub -- \
     --wiki /path/to/your/wiki \
     --purpose /path/to/purpose.md \
     --model claude-<model>-<yyyymmdd>
   ```

2. Open the submission page in a browser.
3. Paste one text you actually want in the wiki, and submit it. Then leave the page — the run is
   under way and you are not waiting for it (INGEST-001).
4. Come back later. The submission reads `done` or `failed`.
5. Read the wiki.

If you submit a second text while the first run is still going, it is refused and you are told a run
is in progress (INGEST-005). Submit it again once the first run has ended.

### What the owner must see

The submission reached `done`, and in the wiki:

- a source page for the text just submitted;
- one or more pages that name that source, with links that lead somewhere;
- every page carrying a `type`, filed in exactly one section, that section's `index.md` listing it;
- the root `index.md` declaring `okf_version: "0.2"` and listing the sections;
- an entry in `log.md` identifying that run and saying what changed and why;
- `generated: { by, at }` on every page the run wrote, holding Grimoire's values.

**What is not being judged here**: whether the agent wrote *good* pages, or whether it followed the
instruction faithfully. The spec puts both out of scope — the owner's own reading is the gate, and
systematic hunting is OUT-07. Only the last item in that list is Grimoire's doing; the rest is the
agent's, demanded of it by the instruction (WIKI-001).

**If the run reads `failed`**: whatever it had already written is still in the wiki, unchanged and
uncommitted (WIKI-003). Removing it is the owner's business, through the wiki's own history
(`docs/product.md` §4). Note that the next submission will start a run on top of it — the
acknowledgement gate is RUNS-003, in the follow-up feature.

**A stop loses the list.** Submissions and their states are held in memory in this feature
(research.md R-08). Restart behaviour is RUNS-004, in the follow-up feature; the wiki itself is
unaffected either way.

---

## Validation scenarios

### The default test run

```
dotnet test tests/Grimoire.Fast.Tests -- --timeout 15s
```

Fast only, and it fails if it exceeds 15 s — that is the `time-budget` gate on the default run
(Constitution III.7). No Fast test waits for real time: the elapsed-time ceiling is measured against
`FakeTimeProvider` (research.md R-12).

### Contract — in CI

```
dotnet test tests/Grimoire.Contract.Tests -- --timeout 90s --filter-not-trait "requires=signin"
```

`FileSystemWikiStore` against a real wiki directory on disk. The marker is the one named thing that
keeps the sign-in tests out; CI has no subscription sign-in.

### Contract — locally, before the PR

```
dotnet test tests/Grimoire.Contract.Tests -- --timeout 90s --filter-trait "requires=signin"
```

Three tests, no more (research.md R-09), each driving the real `claude` CLI with the owner's real
sign-in, the real MCP endpoint and a real wiki in a temporary directory:

1. **Grant surface** (GUARD-001, GUARD-002) — the run's tool surface is exactly the grant, and a
   granted tool writes.
2. **Outside the grant** (GUARD-001) — a run asked for something only a shell or file tool could do
   makes no such call and changes nothing outside the wiki.
3. **Nudge and stop** (RUNS-005, GUARD-004) — the agent continues after the single nudge, and the
   stop ends a run inside its ceiling.

### E2E

```
dotnet test tests/Grimoire.E2E.Tests
```

A real browser against a running hub, driven by `Microsoft.Playwright.Xunit.v3`. Two scenarios per
user story at most (Constitution III.4): submitting a text (ACCESS-001) and reading the states back
(ACCESS-002). No time budget — III.7 sets one for Fast and Contract only.

### The gates

```
dotnet run --project tools/Grimoire.Trace -- check
dotnet run --project tools/Grimoire.Trace -- check --complete
```

Reads requirement ids and proof kinds from `docs/capabilities/`, and level and requirement id off
the built test assemblies. Writes nothing, and fails on what it cannot read rather than skipping it.

`check` carries the three conditions that hold on every push: a test carrying an unknown, retired or
reserved id; a test with no level; an E2E or Deploy test with no requirement id. `check --complete`
adds the fourth — a `test` requirement with no test — which IV.3 applies where a feature lands on
main, because a requirement is registered before its test is written (IV.2) and the condition is
therefore red by construction while a feature is in flight. CI calls `check` on every push and
`check --complete` on a pull request whose base is `main`.

```
dotnet run --project tools/Grimoire.Trace -- write
```

Writes `docs/trace.md` — the single documented command of Constitution IV.4, committed when the
feature closes.

**Both gates must be shown failing once** before they count (Constitution II.2). The PR links a
`trace-check` run against a test whose `req` trait names an id that does not exist, and a
`time-budget` run against a Fast suite pushed past 15 s.

---

## Closing the feature

Not done until all of these hold (Constitution I.9, IV.2, IV.4):

- [ ] Merged to main at the end, with nothing of the feature on main before then; every phase PR
      merged into the feature branch before the next phase started (I.9, I.10).
- [ ] `docs/capabilities/{ingest,wiki,guard,access,runs}.md` — registered **before the first test was
      written**, and reconciled here as added, changed or removed. RUNS-002, RUNS-003, RUNS-004 and
      ACCESS-003 are *not* registered: they belong to the follow-up feature.
- [ ] `docs/decisions.md` carries this feature's binding decisions, each with its reason — every row
      marked "binds later features: yes" in the plan's Technology decisions.
- [ ] `docs/trace.md` written by the command above and committed.
- [ ] `docs/product.md`: OUT-01's status and spec reference updated — the only two edits an agent
      makes to that file.
- [ ] `docs/review-checklist.md` walked, all twelve items.
- [ ] **The acceptance run above has actually been done**, with a real model and a real wiki.
