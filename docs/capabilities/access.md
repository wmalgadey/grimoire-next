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
| ACCESS-002 | The browser MUST show, for every submission, exactly one of submitted, running, done or failed, and no further detail about the run. | test |
| ACCESS-003 | Users MUST be able to acknowledge a failed run in the browser. | test |
| ACCESS-004 | The browser MUST show, for every submission, the opening of the text that was submitted, cut to the same length for every submission, and when the submission was made, so that the user can tell one submission from another. | test |
| ACCESS-005 | The browser MUST show, for every submission, exactly one of submitted, running, done or failed, and for a submission that has a run also that run's model, the tokens it has spent and the number of tool calls it has made; while a run is in progress these figures MUST follow it, and a figure changing MUST NOT move the rows of the list. | test |
| ACCESS-006 | Users MUST be able to open a submission's run from the list and read its record in the browser — its frame, and what the run did in the order it happened — both while the run is in progress, where lines MUST arrive as they are appended, and after it has ended, where the record MUST be shown in the same shape. The user MUST be able to follow what the run did without reading the tool results in full and MUST be able to reach any one result when they want it; and where lines of the record could not be written, the view MUST say that something is missing. | test |

ACCESS-002 has two halves and is proven at two levels: the response carries the state and nothing
else (Fast), and the browser renders it (E2E).

ACCESS-004 is what a user tells one submission from another by, and ACCESS-003 is the one action
they can take on one. Neither reaches past ACCESS-002: the opening of the user's own text and the
moment they submitted it are facts about the submission, and that an acknowledgement is available
says an action can be taken, not what the run did. The acknowledgement names the submission, which
has exactly one run (INGEST-002); no run identifier reaches the browser.

ACCESS-005 carries ACCESS-002's four states forward unchanged and adds the run's model and its two
figures. Two halves, two levels: the response carries them (Fast), and the browser renders them
without the rows moving as a figure rises (E2E) — geometry only a real browser has.

ACCESS-006 is a second page, reached from the row. It is a window onto the record RUNS-007 keeps and
not a second place the run lives: the bytes it shows are the bytes of the file, and the user reads a
run under way and a run from last month the same way.
