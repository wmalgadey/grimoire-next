# Contract: Hub HTTP API (browser ↔ hub)

**Supersedes** `specs/001-first-ingest/contracts/hub-http-api.md`. That document is left as written;
it is the change record of a closed feature.

JSON over HTTP on the trusted network. No authentication — `docs/product.md` §2 puts Grimoire inside
a network the user trusts, and ACCESS beyond the browser door is OUT-11/OUT-12.

The page is static content the hub serves from `wwwroot/` — one HTML file and one script, no build
step (DEC-019). It calls the endpoints below with `fetch` and polls the second one for state.

## What changed in this feature

| Change | Why |
| --- | --- |
| `409 Conflict` / `run-in-progress` **is gone** | INGEST-005 is retired. A submission made while a run is in progress is accepted and waits its turn (RUNS-002) |
| `GET /api/submissions` gains `excerpt` | ACCESS-004 — the user has to be able to tell which text a failed run was working on |
| `GET /api/submissions` gains `awaitingAcknowledgement`, conditionally | ACCESS-003 — the page has to know which row offers the control |
| `POST /api/submissions/{id}/acknowledgement` is new | ACCESS-003 |

**The scope note of the 001 document is not narrowed; it stands exactly as written.** No further
detail about the run is exposed by this API — no identifier, no step, no reasoning, no duration, no
cost, no history, no queue position, no time waited. The acknowledgement addresses a *submission*,
whose identifier this API has returned since `001-first-ingest`, and a submission has exactly one
run in this feature, so naming the submission names its failed run. The one field added for
ACCESS-003 says whether an action is available, not what the run did.

---

## `POST /api/submissions`

Submit a text (INGEST-001, ACCESS-001).

**Request**

```json
{ "text": "…the pasted text…" }
```

**`202 Accepted`** — the submission is accepted and is either under way or waiting its turn. The
response returns immediately; the user is not made to wait for any run to end (INGEST-001), and is
not told which of the two it is: a waiting submission reads `submitted`, like one whose agent has
not yet reported in (ACCESS-002, RUNS-001).

```json
{ "id": "0c9f…", "state": "submitted", "submittedAt": "2026-09-23T10:04:11Z",
  "excerpt": "Ada Lovelace wrote the first program." }
```

The submission is on disk before this response is written (RUNS-004): a user who is told `202` and
then loses power finds the submission after the restart.

### Refusals

Every refusal carries the same body — a machine-readable `reason` and a message for the user — and
stores nothing: a refused submission is not a Submission and carries no state.

```json
{ "reason": "text-empty", "message": "There is no text to submit." }
```

| Status | `reason` | When | Requirement |
| --- | --- | --- | --- |
| `422 Unprocessable Content` | `instruction-missing` | Grimoire's instruction file is absent. No run starts | INGEST-003 |
| `422 Unprocessable Content` | `purpose-description-missing` | Grimoire's purpose description file is absent. No run starts | INGEST-003 |
| `422 Unprocessable Content` | `text-empty` | The text is empty or only whitespace. No run starts | INGEST-004 |

Both start-up inputs are checked before the text and the instruction before the purpose description,
so each refusal names exactly one missing file.

**There is no refusal for a run being in progress.** It was INGEST-005 and is retired.

---

## `GET /api/submissions`

Every submission the user made, with its current state, newest first (ACCESS-002, ACCESS-004).

**`200 OK`**

```json
{ "submissions": [
    { "id": "0c9f…", "state": "submitted", "submittedAt": "2026-09-23T10:41:02Z",
      "excerpt": "Grace Hopper found the first bug in a relay…" },
    { "id": "7ab2…", "state": "failed", "submittedAt": "2026-09-23T10:12:40Z",
      "excerpt": "Alan Turing described a universal machine.",
      "awaitingAcknowledgement": true },
    { "id": "3e10…", "state": "done", "submittedAt": "2026-09-22T18:55:02Z",
      "excerpt": "Ada Lovelace wrote the first program." }
] }
```

| Field | Always? | Meaning |
| --- | --- | --- |
| `id` | yes | The submission |
| `state` | yes | Exactly one of `submitted` · `running` · `done` · `failed` (RUNS-001) |
| `submittedAt` | yes | When it was made (ACCESS-004) |
| `excerpt` | yes | The opening of the submitted text: whitespace collapsed, trimmed, cut to 120 characters with `…` where it was cut. A shorter text is returned whole, with nothing appended (ACCESS-004) |
| `awaitingAcknowledgement` | **only** where `state` is `failed` and the failure has not been acknowledged, and then always `true` | That this submission's failure is the one holding the queue, and that the control is offered on this row. It says an action is available; it says nothing about the run (ACCESS-002, ACCESS-003). Absent once acknowledged, so the control disappears on the next poll |

**A waiting submission is not marked as waiting.** It reads `submitted`, carries no position in the
queue and says nothing about how long it has waited (ACCESS-002).

The browser polls this endpoint; there is no push channel.

The list survives Grimoire stopping and starting again, however it stopped, and a submission whose
run was in progress at the moment of the stop comes back reading `failed` (RUNS-004).

---

## `POST /api/submissions/{id}/acknowledgement`

Acknowledge a submission's failed run, so that the queue moves on (ACCESS-003, RUNS-003).

No request body — the submission is named in the path, and it has exactly one run. No response body.

| Status | When |
| --- | --- |
| `204 No Content` | The submission's run was a failure waiting to be acknowledged, and now is not. The next waiting submission starts, if there is one |
| `204 No Content` | It is not an unacknowledged failure — already acknowledged, not failed, or not a submission this Grimoire knows. **Nothing starts and nothing changes** |

**One status for both, deliberately.** A page loaded before the last run failed can send an
acknowledgement for a run that has already been cleared; the spec's edge case says nothing starts
and no state changes. Answering that with an error would put a failure on the user's screen for a
request that did exactly what it should — nothing. The acknowledged run stays `failed`, which the
browser shows on its next poll, and that is the user's confirmation.

The acknowledgement is on disk before the response is written, so a restart does not re-block a
queue the user has already cleared (RUNS-003, RUNS-004).

---

## What the page does with all this

- One form, posted to `POST /api/submissions` (ACCESS-001).
- One list, polled from `GET /api/submissions` once a second. Each row: when it was submitted, the
  excerpt, and the state.
- A row carrying `awaitingAcknowledgement` also offers one control, which posts the acknowledgement
  to that row's own submission and then refreshes. A row without it offers nothing. **No identifier
  is rendered** — neither the submission's nor, since it never arrives, the run's.
