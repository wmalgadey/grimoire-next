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

ACCESS-002 has two halves and is proven at two levels: the response carries the state and nothing
else (Fast), and the browser renders it (E2E).
