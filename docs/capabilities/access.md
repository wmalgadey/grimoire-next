# ACCESS

Who uses Grimoire and through which door: the browser UI, further paths, further people.

<!-- The as-is description of the system (Constitution IV.2). An id becomes permanent the moment it
     is registered here: never renumbered, never reused. A removed requirement moves under
     "Retired" and keeps its id. -->

Grimoire has no access control of its own: it assumes it runs inside a network the user trusts
(`docs/product.md` §2). The first outcome that puts it on an untrusted network revisits that.

## Requirements

| ID | Requirement | Proof |
| --- | --- | --- |
| ACCESS-001 | Users MUST be able to enter a text and submit it from a page in the browser. | test |
| ACCESS-003 | Users MUST be able to acknowledge a failed run in the browser. | test |
| ACCESS-004 | The browser MUST show, for every submission, the opening of the text that was submitted, cut to the same length for every submission, and when the submission was made, so that the user can tell one submission from another. | test |
| ACCESS-005 | The browser MUST show, for every submission, exactly one of submitted, running, done or failed, and for a submission that has a run also that run's model, the tokens it has spent and the number of tool calls it has made; while a run is in progress these figures MUST follow it, and a figure changing MUST NOT move the rows of the list. | test |
| ACCESS-006 | Users MUST be able to open a submission's run from the list and read its record in the browser — its frame, and what the run did in the order it happened — both while the run is in progress, where lines MUST arrive as they are appended, and after it has ended, where the record MUST be shown in the same shape. The user MUST be able to follow what the run did without reading the tool results in full and MUST be able to reach any one result when they want it; and where lines of the record could not be written, the view MUST say that something is missing. | test |

ACCESS-004 is what a user tells one submission from another by, and ACCESS-003 is the one action
they can take on one. The acknowledgement names the submission, which has exactly one run
(INGEST-002), and so does the record endpoint ACCESS-006 serves: no run identifier reaches the
browser. That is now a design property rather than a requirement — nothing needs one — which is why
the test that proves it carries no requirement id.

ACCESS-005 carries ACCESS-002's four states forward unchanged and adds the run's model and its two
figures. Two halves, two levels: the response carries them (Fast), and the browser renders them
without the rows moving as a figure rises (E2E) — geometry only a real browser has.

ACCESS-006 is a second page, reached from the row. It is a window onto the record RUNS-007 keeps and
not a second place the run lives: what it shows is the file, fetched byte for byte from the endpoint,
and the user reads a run under way and a run from last month the same way.

It is a window rather than a copy, which means it may lay out what it shows. The record's two-column
tables are drawn as tables and a block that parses as JSON is indented with its escapes undone —
because a returned wiki page shown exactly as written is one line with every umlaut spelled `\u00FC`.
Where *byte for byte* is promised is the file and the endpoint, and a test asserts it there. What
laying out costs is named in `specs/003-live-run-record/contracts/run-record.md`. A tool call is one line with its result folded
under it, which is how the user follows what the run did without reading the results in full and still
reaches any one of them; the agent's own text is prose and is shown whole, fenced blocks in it
included. Where the record could not hold something, the page says so — a gap that passed for an agent
doing nothing would be worse than the gap (RUNS-007).

## Retired

| ID | Requirement | Proof |
| --- | --- | --- |
| ACCESS-002 | The browser MUST show, for every submission, exactly one of submitted, running, done or failed, and no further detail about the run. | test |

Retired by `specs/003-live-run-record`, superseded by ACCESS-005. Its second clause is what OUT-02
exists to undo: the whole point of that feature is further detail about the run. It was written in
`001-first-ingest` to hold the browser to one word per run while no outcome had asked for more, and
OUT-02 asks for more. ACCESS-005 carries the four states forward unchanged and adds the run's model
and its two figures. The id stays here and is never renumbered or reused (Constitution IV.1, IV.2).

Retiring it also retires the sentence in `001-first-ingest`'s HTTP contract saying no further detail
about the run is exposed by that API, and the note above that explained ACCESS-003 and ACCESS-004 do
not reach past it. ACCESS-001, ACCESS-003 and ACCESS-004 are untouched.
