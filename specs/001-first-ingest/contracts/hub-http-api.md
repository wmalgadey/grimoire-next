# Contract: Hub HTTP API (browser ↔ hub)

JSON over HTTP on the trusted network. No authentication — `docs/product.md` §2 puts Grimoire
inside a network the user trusts, and ACCESS beyond the browser door is OUT-11/OUT-12.

Scope note: there is no acknowledgement endpoint. ACCESS-003 is held back by the split, together
with RUNS-002, RUNS-003 and RUNS-004.

The page itself is static content the hub serves from `wwwroot/` — one HTML file and one script,
no build step (research.md R-10). It calls the two endpoints below with `fetch` and polls the second
one for state.

---

## `POST /api/submissions`

Submit a text (INGEST-001, ACCESS-001).

**Request**

```json
{ "text": "…the pasted text…" }
```

**`202 Accepted`** — the submission is accepted and a run is under way. The response returns
immediately; the user is not made to wait for the run to end (INGEST-001).

```json
{ "id": "0c9f…", "state": "submitted", "submittedAt": "2026-09-20T10:04:11Z" }
```

### Refusals

Every refusal carries the same body — a machine-readable `reason` and a message for the user — and
stores nothing: a refused submission is not a Submission and carries no state.

```json
{ "reason": "run-in-progress",
  "message": "A run is in progress. Submit this text again once it has ended." }
```

| Status | `reason` | When | Requirement |
| --- | --- | --- | --- |
| `422 Unprocessable Content` | `purpose-description-missing` | Grimoire's purpose description file is absent. No run starts | INGEST-003 |
| `422 Unprocessable Content` | `text-empty` | The text is empty or only whitespace. No run starts | INGEST-004 |
| `409 Conflict` | `run-in-progress` | A run is in progress. The submission causes no run, and the user is told a run is in progress | INGEST-005 |

`409` rather than `422` for the last one: nothing is wrong with the request, only with the moment.
The same text submitted again after the run ends is accepted.

The purpose description is checked before the text, so a submission that is both empty and made
without a purpose description is refused with `purpose-description-missing`.

---

## `GET /api/submissions`

Every submission the user made, with its current state (ACCESS-002).

**`200 OK`**

```json
{ "submissions": [
    { "id": "0c9f…", "state": "running",  "submittedAt": "2026-09-20T10:04:11Z" },
    { "id": "7ab2…", "state": "failed",   "submittedAt": "2026-09-20T09:12:40Z" },
    { "id": "3e10…", "state": "done",     "submittedAt": "2026-09-19T18:55:02Z" }
] }
```

`state` is exactly one of `submitted` · `running` · `done` · `failed` (RUNS-001).

**No further detail about the run is exposed by this API** — no steps, no reasoning, no duration,
no cost, no history. ACCESS-002 says "and no further detail", and OUT-02 owns everything more. A
field added here would be a mechanism with no consumer (Constitution II.1).

The list is held in memory and does not survive Grimoire stopping (research.md R-08). Surviving a
restart is RUNS-004, in the follow-up feature.

The browser polls this endpoint; there is no push channel.
