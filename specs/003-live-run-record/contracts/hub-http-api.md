# Contract: Hub HTTP API (browser ↔ hub)

**Supersedes** `specs/002-ingest-queue/contracts/hub-http-api.md`. That document is left as written;
it is the change record of a closed feature.

JSON over HTTP on the trusted network. No authentication — `docs/product.md` §2 puts Grimoire inside a
network the user trusts, and ACCESS beyond the browser door is OUT-11/OUT-12.

The page is static content the hub serves from `wwwroot/` — no build step (DEC-019). It is now **two**
pages: the list, and a page that shows one run's record. Both call the endpoints below with `fetch` and
poll.

## What changed in this feature

| Change | Why |
| --- | --- |
| **The scope note of the 001 and 002 documents is withdrawn.** This API now exposes detail about the run | ACCESS-002 is **retired**. Its clause "and no further detail about the run" is what OUT-02 exists to undo, and the sentence in the `001-first-ingest` document that repeated it goes with it |
| `GET /api/submissions` gains `model`, `tokensUsed`, `toolCalls` and, conditionally, `entriesLost` | ACCESS-005 — the row carries the model and the two figures, and they follow a run while it is under way |
| `GET /api/submissions/{id}/record` is new | ACCESS-006 — the record read in the browser, live and afterwards alike |

**What did not change**, and is not narrowed:

- **No run identifier reaches the browser.** ACCESS-002's retirement lifts the rule that required
  that, but nothing needs it: the record endpoint addresses the *submission*, which has exactly one
  run (INGEST-002), exactly as the acknowledgement has since `002-ingest-queue`. It is now a design
  property rather than a requirement.
- **Cost is tokens.** No figure in this API is in currency, and the token figure is the same quantity
  the cost ceiling counts — not a second definition of cost (DEC-015, GUARD-004, `docs/ux.md`: never a
  number on a screen that exists nowhere else).
- The four states, the refusals, and the acknowledgement stand exactly as `002-ingest-queue` wrote
  them.

---

## `POST /api/submissions`

Unchanged from `002-ingest-queue`, except that the accepted submission's body now carries the same
fields `GET /api/submissions` returns for it — which for a submission that has no run yet means the
run fields are absent.

**`202 Accepted`**

```json
{ "id": "0c9f…", "state": "submitted", "submittedAt": "2026-09-26T09:22:41Z",
  "excerpt": "Ada Lovelace wrote the first program." }
```

The refusals are unchanged: `422` with `instruction-missing`, `purpose-description-missing` or
`text-empty` (INGEST-003, INGEST-004). There is still no refusal for a run being in progress.

---

## `GET /api/submissions`

Every submission the user made, newest first, with its state and — where it has a run — that run's
model and figures (ACCESS-004, ACCESS-005).

**`200 OK`**

```json
{ "submissions": [
    { "id": "0c9f…", "state": "running", "submittedAt": "2026-09-26T09:22:41Z",
      "excerpt": "Grace Hopper found the first bug in a relay…",
      "model": "claude-opus-4-5-20251101", "tokensUsed": 148233, "toolCalls": 9 },
    { "id": "7ab2…", "state": "failed", "submittedAt": "2026-09-26T09:12:40Z",
      "excerpt": "Alan Turing described a universal machine.",
      "model": "claude-opus-4-5-20251101", "tokensUsed": 2004118, "toolCalls": 31,
      "awaitingAcknowledgement": true },
    { "id": "3e10…", "state": "submitted", "submittedAt": "2026-09-26T09:11:02Z",
      "excerpt": "Ada Lovelace wrote the first program." }
] }
```

| Field | Always? | Meaning |
| --- | --- | --- |
| `id` | yes | The submission |
| `state` | yes | Exactly one of `submitted` · `running` · `done` · `failed` (RUNS-001) |
| `submittedAt` | yes | When it was made (ACCESS-004) |
| `excerpt` | yes | The opening of the submitted text, cut to the same length for every submission (ACCESS-004). Unchanged |
| `model` | only where the submission has a run | The pinned model id that run runs on (DEC-010). Recorded with the run, so an older run keeps the model it actually used even after `--model` changes (ACCESS-005, RUNS-008) |
| `tokensUsed` | only where the submission has a run | Every token the run has caused so far — **the same quantity the cost ceiling counts**, over every model the run touched, the CLI's own background calls included (ACCESS-005, RUNS-010, GUARD-004, DEC-015). Never goes backwards. Final once the run has ended |
| `toolCalls` | only where the submission has a run | How many tool calls the run has made (ACCESS-005, RUNS-010). Never goes backwards |
| `entriesLost` | **only** where the run's record could not hold some of what happened, and then a number above zero | That lines are missing from that run's record. The run went on; the gap is shown rather than hidden (RUNS-007) |
| `awaitingAcknowledgement` | **only** where `state` is `failed` and the failure has not been acknowledged, and then always `true` | Unchanged (ACCESS-003) |

**A submission with no run carries no run fields at all** — not `model`, not the figures, not zeros. A
submission waiting its turn has no run (RUNS-002), so there is nothing true to say about one; zeros
would claim a run that spent nothing rather than no run.

**The four run fields are read as one instant with the state**, under the board's one lock. Asked
separately, a run ending between two answers would put `running` beside a final figure — a pair that
never existed.

The browser polls this endpoint once a second; there is no push channel. The list survives Grimoire
stopping and starting again, and so do the figures: a run cut off by a stop comes back reading `failed`
with the figures it had reached (RUNS-004, RUNS-010).

---

## `GET /api/submissions/{id}/record`

One run's record, as the file holds it (ACCESS-006, RUNS-007).

**`200 OK`**, `Content-Type: text/markdown; charset=utf-8` — the record's bytes, unaltered.

```markdown
# Run 4f1c…

| | |
| --- | --- |
| Submission | 0c9f… |
| Model | claude-opus-4-5-20251101 |
…
```

| Status | When |
| --- | --- |
| `200 OK` | The submission has a run and its record can be read. The body is the file's bytes, whatever the run's state — a run under way answers with the record as far as it goes |
| `404 Not Found` | There is no such submission, or it has no run yet, or its record was never written at all |

**No caching.** The browser fetches it with `cache: "no-store"`, as it does the list: a polled record
answered from the cache is a run that has stopped growing on the screen and not in fact.

**The body is the file and not a rendering of it.** `docs/product.md`'s invariant that the artifact is
readable without Grimoire applies to the record too (spec, US3): the browser gets exactly what the
owner's editor would get. Nothing is rendered server-side, nothing is summarised, and no second,
machine-shaped view of a run exists anywhere in this API.

**A record that could not be fully written still answers.** What is there is served, and the number of
missing entries is in the list's `entriesLost` — the browser says lines are missing rather than
letting the gap pass for an agent that did nothing (RUNS-007).

---

## `POST /api/submissions/{id}/acknowledgement`

Unchanged from `002-ingest-queue`: no body either way, `204 No Content` whether it cleared a failure or
nothing, and the acknowledged run stays `failed` (ACCESS-003, RUNS-003).

---

## What the two pages do with all this

**The list** (`index.html`, `app.js`):

- One form, posted to `POST /api/submissions` (ACCESS-001).
- One list, polled from `GET /api/submissions` once a second. Each row: when it was submitted, the
  excerpt, the state, and — where there is a run — the model, the tokens and the tool calls.
- **Rows are updated in place**, keyed by the submission's id, and each figure sits in its own element
  with tabular figures and a reserved width, so that a rising figure moves nothing in the list
  (ACCESS-005, `docs/ux.md`: live content grows in place). The whole list is no longer rebuilt every
  second, which also stops the Acknowledge button being replaced under the user's finger.
- A row with a run links to that run's page. A row carrying `awaitingAcknowledgement` also offers the
  one control, as before. **No identifier is rendered.**

**The run's page** (`run.html`, `run.js`):

- Polls `GET /api/submissions/{id}/record` once a second and shows it in monospace, as a log reads
  (`docs/ux.md`).
- Renders one element per moment, **appending** only what is not already on the page and never
  replacing an element that is. That is what keeps the scroll where the user put it and a result they
  have opened open (ACCESS-006).
- A tool call is one line; its result is folded under it, and the user opens the one they want. The
  segmentation rule is the record's own and is written down in [run-record.md](run-record.md).
- Says that lines are missing where `entriesLost` is above zero, and reads the same for a run under way
  as for one from last month — the live one just has fewer lines yet.
