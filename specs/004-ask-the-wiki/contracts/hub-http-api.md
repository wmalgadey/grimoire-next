# Contract: Hub HTTP API (browser ↔ hub)

**Supersedes** `specs/003-live-run-record/contracts/hub-http-api.md`. That document is left as
written; it is the change record of a closed feature.

JSON over HTTP on the trusted network. No authentication — `docs/product.md` §2 puts Grimoire inside
a network the user trusts, and ACCESS beyond the browser door is OUT-11/OUT-12.

The pages are static content the hub serves from `wwwroot/` — no build step (DEC-019). There are now
**three**: the list, one run's record, and the chat. Each of them carries a line of links to the
other two (ACCESS-010).

## What changed in this feature

| Change | Why |
| --- | --- |
| **Nothing polls any more.** Three `text/event-stream` endpoints replace the two one-second polls | The owner's decision, and **DEC-032 is superseded**. SignalR's browser client means npm or a vendored script, which DEC-019 rules out; `EventSource` is native and is exactly one-directional (`../research.md` R-01) |
| `GET /api/submissions/events` is new | ACCESS-005 — the same list, sent rather than asked for. **Its wording does not change**, and its tests are proven again across the stream |
| `GET /api/submissions/{id}/record/events` is new | ACCESS-006 — the same record, sent as it is appended. Wording unchanged, proven again |
| `GET /api/chat/events`, `POST /api/chat/questions`, `POST /api/chat` and `POST /api/chat/questions/{id}/acknowledgement` are new | QUERY-001, QUERY-005, QUERY-006, ACCESS-007, ACCESS-008, ACCESS-009 |

**What did not change**, and is not narrowed:

- **No run identifier reaches the browser.** The record stream addresses the *submission*, the chat
  addresses the *question*. Still a design property rather than a requirement.
- **Cost is never currency**, anywhere. `costSpent` and the chat's total are the same quantity the
  cost ceiling counts — input-token equivalents (DEC-015, GUARD-004).
- `GET /api/submissions`, `GET /api/submissions/{id}/record` and
  `POST /api/submissions/{id}/acknowledgement` stand exactly as `003-live-run-record` wrote them.
  The streams are beside them, not instead of them: the snapshot each stream opens with is the same
  body those endpoints answer.
- **A question appears in none of the submission endpoints.** The list is a list of submissions and
  a question is not one (spec, Clarifications).

---

## The three streams

All three are `Content-Type: text/event-stream`, served with `TypedResults.ServerSentEvents` and
read in the browser with `EventSource`. Three rules hold for every one of them:

1. **Every stream opens with one snapshot event** carrying the whole of what that view shows. A
   browser that reconnects — `EventSource` does so by itself — gets a fresh snapshot and so shows
   what it is looking at as it then stands, including what arrived while it was away (ACCESS-007).
   Nothing is replayed from a buffer and no `Last-Event-ID` is used.
2. **Every event's `data` is a single line of JSON.** A record's and an answer's newlines travel as
   `\n` inside a JSON string, so nothing needs per-line SSE framing on the way out or re-joining on
   the way in.
3. **A stream carries nothing the page does not show.** One more field would be a mechanism with no
   consumer (Constitution II.1).

### `GET /api/submissions/events`

Every submission, its state and its figures — the same body `GET /api/submissions` answers
(ACCESS-004, ACCESS-005).

```
event: submissions
data: {"submissions":[…],"costCeiling":2000000}
```

Sent on connect, and again whenever anything about a submission changed: a state, a figure, an
acknowledgement. **The whole list each time, never a delta** — it is read as one instant under the
board's one lock, and a delta would break exactly that (ACCESS-005).

### `GET /api/submissions/{id}/record/events`

One run's record, as the file holds it (ACCESS-006, RUNS-007).

```
event: record
data: {"append":"## 09:22:41 · called read_page\n\n```json\n…"}
```

| Event | When | `data` |
| --- | --- | --- |
| `record` | on connect | `{"append": …}` — the record so far, byte for byte |
| `record` | whenever it grew | `{"append": …}` — the bytes appended since this subscriber's last event |
| `missing` | on connect, and when the count rises | `{"entriesLost": 3}` — lines of this record could not be written (RUNS-007) |

**Only what is past this subscriber's offset** is sent, which keeps the browser's rule what it
already is: it appends what it has not seen and never replaces an element it has drawn, so the
scroll holds and a result the user has opened stays open (ACCESS-006). The record's own segmentation
stays the one description of a run's shape — `specs/003-live-run-record/contracts/run-record.md` —
and `MarkdownRunRecord` stays the only thing that renders one.

**`404 Not Found`** where there is no such submission, it has no run yet, or its record was never
written — the same three cases, and the same one answer, as the endpoint beside it.

### `GET /api/chat/events`

The one chat, which every browser reading it reads the same (QUERY-005).

```
event: chat
data: {"turns":[…],"total":148233,"costCeiling":2000000,"vault":{"name":"Notes","wikiPath":"wiki"}}
```

| Event | When | `data` |
| --- | --- | --- |
| `chat` | on connect, and after a new chat is started | The whole chat: every turn, the total, the ceiling, the vault settings |
| `asked` | a question joined the chat | `{"id":…,"text":…,"askedAt":…,"state":"waiting"}` |
| `answer` | the agent wrote more of an answer | `{"id":…,"append":"…"}` |
| `step` | the agent made a call, or one returned | `{"id":…,"step":{"kind":"called"\|"returned","tool":"read_page","content":"…"}}` |
| `question` | a question's state or figures changed | `{"id":…,"state":…,"because":…,"costSpent":…,"total":…}` |

**A turn**, as the snapshot carries it:

| Field | Always? | Meaning |
| --- | --- | --- |
| `id` | yes | The question. What the acknowledgement addresses |
| `text` | yes | What the user asked, whole, as they typed it |
| `askedAt` | yes | When. Shown in the chat; not what the queue is ordered by |
| `state` | yes | Exactly one of `waiting` · `answering` · `answered` · `no-answer` (ACCESS-007) |
| `because` | **only** where `state` is `no-answer` | Why it got no answer — one of `RunEndedBecause`'s reasons, in the words the chat shows (QUERY-006) |
| `answer` | yes, possibly empty | The agent's own text so far, in the order it arrived, as one piece of prose |
| `steps` | yes, possibly empty | One per tool call and one per result: `kind`, `tool`, `content` |
| `costSpent` | **only** where the question has a run | What that run has spent, in the quantity the ceiling counts (ACCESS-008, RUNS-010) |
| `awaitingAcknowledgement` | **only** where `state` is `no-answer` and the failure is not yet acknowledged, and then always `true` | The chat offers the one control (ACCESS-003, RUNS-003) |

Beside `turns`, the snapshot carries three fields of its own:

| Field | Always? | Meaning |
| --- | --- | --- |
| `total` | yes | What every question in this chat has spent, a failed one included. **No ceiling stands beside it**: each question carries its own, and `x / y` would invent one that does not exist (ACCESS-008) |
| `costCeiling` | yes | The ceiling each question's own figure is written against (GUARD-004) |
| `vault` | **only** where Grimoire was told both | `{"name":…,"wikiPath":…}` — the Obsidian vault, and the wiki's path inside it. **Absent** where either is missing, and the browser then shows page names as plain text and says opening is not set up (ACCESS-009) |

**A question waiting its turn carries no `costSpent`** — not a zero. It has no run (RUNS-002), so
there is nothing true to say about one, exactly as a waiting submission carries no run fields.

---

## `POST /api/chat/questions`

Ask the wiki (QUERY-001).

**Request**: `{ "text": "What does the wiki say about Ada Lovelace?" }`

**`202 Accepted`** — the question is accepted and a run will answer it; the user waits for none of
it (QUERY-001). The body is the turn as the stream's snapshot carries one:

```json
{ "id": "5b8e…", "text": "What does the wiki say about Ada Lovelace?",
  "askedAt": "2026-09-27T14:02:11Z", "state": "waiting", "answer": "", "steps": [] }
```

**`422 Unprocessable Entity`** — refused, and the user is told which of the three it was
(QUERY-003). No run starts, the question is stored nowhere and carries no state.

| `reason` | When |
| --- | --- |
| `question-instruction-missing` | Grimoire's own question instruction is not at the path the hub was started with |
| `purpose-description-missing` | The purpose description is not at its path |
| `question-empty` | The text is empty or only whitespace |

Checked in that order, so each refusal names exactly one thing — the order the submission refusals
already establish. **There is no refusal for a run being in progress**: a question asked while
something else runs is accepted and waits its turn (RUNS-002, ACCESS-007).

---

## `POST /api/chat`

Start a new chat (QUERY-005). No body either way; **`204 No Content`**.

Afterwards the chat is empty, nothing of the previous one is reachable and nothing of it is kept
anywhere. Every browser reading the chat is sent the new, empty snapshot — a new chat empties both
tabs, because there is one chat and they all read it.

**A question still being answered is not stopped.** Its run is not a chat and goes on being a run;
what it produces belongs to the chat that is gone, so nothing of it appears in the new one. Its
figures stay with the run (RUNS-010).

---

## `POST /api/chat/questions/{id}/acknowledgement`

The user has seen that this question got no answer (ACCESS-003, RUNS-003, QUERY-006). No body either
way; **`204 No Content`** whether it cleared a failure or nothing.

It exists because a failed question blocks the queue exactly as a failed ingest does, and there is
no row in the submissions list to clear it from — a question is not a submission. ACCESS-003's
wording is unchanged: it asks for a failed run to be acknowledgeable in the browser, and it is. The
acknowledged question still reads *got no answer*, as an acknowledged submission still reads
`failed`, and the user can then ask again.

One status for both cases, as the submission's acknowledgement already does: a page that
acknowledges a failure already cleared did exactly what it should — nothing.

---

## What the three pages do with all this

**The list** (`index.html`, `app.js`): one form posted to `POST /api/submissions` (ACCESS-001); the
list drawn from `GET /api/submissions/events` instead of a poll. Rows are still updated in place,
keyed by the submission's id, each figure in its own element with tabular figures and a reserved
width, so a rising figure moves nothing (ACCESS-005). The Acknowledge control and the link to a run
are unchanged. A line of links to the other two jobs (ACCESS-010).

**The run's page** (`run.html`, `run.js`): the record drawn from
`GET /api/submissions/{id}/record/events` instead of a poll, appending exactly what it appended
before — the segmentation rule is the record's own and has not changed. A line of links
(ACCESS-010).

**The chat** (`chat.html`, `chat.js`): the conversation drawn from `GET /api/chat/events`. Each
question, the answer growing in place under it as `answer` events arrive, and the steps folded shut
beneath, each openable on its own and read in the shape a run's record is read in (ACCESS-007). What
the question has spent stands beside it against the ceiling; the chat's total stands with no ceiling
beside it (ACCESS-008). Page names in the answer are rewritten to
`obsidian://open?vault=<name>&file=<wikiPath>/<the link's target>` where `vault` is in the snapshot,
and shown as plain text with one line saying opening is not set up where it is absent (ACCESS-009).
Nothing arriving moves what the user is already reading: an element already drawn is never replaced,
and the answer grows by appending to the text node that is there. A line of links, and the one
control that starts a new chat (ACCESS-010, QUERY-005).
