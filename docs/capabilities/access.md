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
| ACCESS-005 | The browser MUST show, for every submission, exactly one of submitted, running, done or failed, and for a submission that has a run also that run's model, what it has spent against the cost ceiling it is held to, and the number of tool calls it has made; while a run is in progress these figures MUST follow it, and a figure changing MUST NOT move the rows of the list. | test |
| ACCESS-006 | Users MUST be able to open a submission's run from the list and read its record in the browser — its frame, and what the run did in the order it happened — both while the run is in progress, where lines MUST arrive as they are appended, and after it has ended, where the record MUST be shown in the same shape. The user MUST be able to follow what the run did without reading the tool results in full and MUST be able to reach any one result when they want it; and where lines of the record could not be written, the view MUST say that something is missing. | test |
| ACCESS-007 | The browser MUST show, for every question in the chat, exactly one of waiting its turn, being answered, answered, or got no answer and why; an answer MUST appear as the agent produces it, and what the agent did to reach it MUST be readable under it in the shape a run's record is read in (ACCESS-006) — shut by default and openable a step at a time — arriving as it happens; and content arriving MUST NOT move what the user is already reading. Where the browser's connection to Grimoire is lost and made again, the browser MUST show the chat as it then stands, including what arrived while it was away. | test |
| ACCESS-008 | The browser MUST show, for every question that has a run, what that run has spent against the cost ceiling it is held to, and for the chat what its questions have spent altogether, the total without a ceiling beside it; while a question's run is in progress its figure MUST follow it, and a figure changing MUST NOT move what the user is reading. | test |
| ACCESS-009 | Users MUST be able to open a wiki page an answer references, from the answer, in the editor the wiki is open in, without that opening changing anything in the wiki — and a reference that leaves the wiki is shown as text and opens nothing. Where Grimoire has not been told what that opening needs, the answer MUST still arrive, the page's name MUST still be readable in it, and the browser MUST say that opening a page is not set up. | test |
| ACCESS-010 | Users MUST be able to reach each of submitting a source, reading a submission's run, and asking the wiki from the others, and to start a new chat from the chat. | test |

ACCESS-004 is what a user tells one submission from another by, and ACCESS-003 is the one action
they can take on one. The acknowledgement names the submission, which has exactly one run
(INGEST-002), and so does the record endpoint ACCESS-006 serves: no run identifier reaches the
browser. That is now a design property rather than a requirement — nothing needs one — which is why
the test that proves it carries no requirement id.

ACCESS-005 carries ACCESS-002's four states forward unchanged and adds the run's model and its two
figures. Two halves, two levels: the response carries them (Fast), and the browser renders them
without the rows moving as a figure rises (E2E) — geometry only a real browser has.

The cost is shown **against its ceiling** and with no unit beside it, as `12 000 / 2 000 000`. It is
counted in input-token equivalents (GUARD-004), a quantity with no unit of its own and no scale a
reader carries in their head, so the bare number would be one docs/ux.md rules out — a number on a
screen the user cannot make sense of. The ceiling is what gives it a sense, and it comes with the
list rather than being written into the page: the owner revises it in one place. Calling the figure
tokens would be worse than saying nothing, because it is not a count of tokens.

What a run's four raw token counts were is not on the row. It is in the record, which is where a
reader goes when the one figure is not enough (ACCESS-006, RUNS-008).

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

ACCESS-007 is the chat, and it binds three things at once. The four values are the same four RUNS-001
names, read in the chat's words and read from the run rather than stored, so the chat and the queue rule
cannot disagree about a question. The answer appears **as the agent produces it**, which is what decides
where the answer ends and the steps begin: every piece of the agent's own text is the answer and the tool
calls are the steps, because that is the only split decidable at the moment a block arrives and never
revised — and revising one would move text the user has already read, which the third clause forbids in
as many words.

The steps are read **in the shape a run's record is read in** (ACCESS-006), so there is one format to
learn rather than two: shut by default, openable a step at a time, the call and what it returned whole.

Its last clause is answered with no replay buffer behind it. Every stream opens with a snapshot carrying
the whole of what the view shows, so a browser whose connection was lost and made again reads the chat as
it then stands — `EventSource` reconnects by itself, and nothing is kept to be replayed to it.

**ACCESS-008 stands apart from ACCESS-005 rather than extending it.** ACCESS-005 is a row in a list of
submissions and the figures there are one run's; here there are two different quantities — a question's
run against its ceiling, and the chat's total, which has **no ceiling** because every question carries
its own. A total dressed as `x / y` would invent a ceiling that does not exist. A question waiting its
turn carries no figure at all, not a zero: it has no run, so there is nothing true to say about one.

The total includes a question whose run failed, because a run that failed still spent. Nothing is counted
twice: each figure is the run's own (DEC-030, RUNS-010) and the total is a sum over the turns.

ACCESS-009 is the one place OUT-03's promise "with references to wiki pages" becomes something the
user can act on. **What the answer contains is the agent's** (QUERY-004); that the name in it can be
followed is Grimoire's.

**Changed**: ACCESS-009's clause on a reference that leaves the wiki — clause added when closing 004,
closing-review §1 (`specs/004-ask-the-wiki/closing-review.md`). The id is kept (Constitution IV.1).

The agent writes ordinary relative Markdown links, in the wiki's own link form (WIKI-001, OKF §6.1),
and **the browser** makes them followable — so nothing new goes into a wiki page and the link form
lives in one place. The prose stays as the agent wrote it, and each reference to a page of the wiki is
drawn as a link in a line beside the answer, appended as the answer grows: rewriting the text the user
is reading would replace it, which ACCESS-007 forbids. A reference's target is the page's path **relative to the wiki's root**, which is the one
anchor an answer has: §6.1 writes a link relative to the page it sits on, and an answer sits on no
page.

The two values a link needs are start-up inputs, `--vault` and `--vault-root`, and they reach the
browser on the chat stream's opening snapshot — they are the hub's and not the page's, the same
argument the cost ceiling already makes. `--vault-root` exists because the owner defines the directory
the in-vault paths hang off; an absolute-path form would have taken that decision away from them.

**Their absence refuses nothing.** The answer still arrives and the page's name is still readable in
it as plain text, with one line saying that opening a page is not set up. Refusing the way QUERY-003
refuses a missing purpose description was declined: the purpose description is what the agent's
judgment rests on, and this is a convenience. Saying nothing was declined too — a reader could not
then tell a setting that is absent from a page that is simply not linkable.

**A link to a page that does not exist is Grimoire's business in neither direction.** OKF requires
readers to tolerate a broken link and nothing in Grimoire checks one (`docs/capabilities/wiki.md`);
the link is built and the editor does what it does with a missing file.

ACCESS-010 is where `docs/ux.md`'s "no navigation chrome until a second job exists" gets its consumer: a
third job exists, so the three pages carry a line of links to each other. It stays a line of links — no
bar and no menu — because that is what the pages already are.

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
