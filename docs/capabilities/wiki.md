# WIKI

What the wiki is about and how it is shaped: purpose description, format, linking, provenance,
sections and their indexes, run log.

<!-- The as-is description of the system (Constitution IV.2). An id becomes permanent the moment it
     is registered here: never renumbered, never reused. A removed requirement moves under
     "Retired" and keeps its id. -->

The wiki follows [OKF 0.2](https://github.com/GoogleCloudPlatform/open-knowledge-format/blob/ad30107c31c06aec8a7d5636e0d1058118604e6f/SPEC.md),
pinned in `docs/product.md`. Seven parts of it apply and nothing beyond them is built (Constitution
I.8): `type`, `sources` (each entry with `resource`), `generated`, `okf_version`, a section
`index.md`, `log.md`, and the link form of §6.1 — standard Markdown links, written relative to the
page the link sits on. Of those, `generated` is the only one Grimoire writes; the
agent writes the rest.

`generated.by` follows §7, the standard's actor convention: one actor, written
`<producer>/<version>` for a tool, `human:<id>` for a person or `process:<id>` for an automated
process, and no free text. Grimoire writes `grimoire/<model>` — the tool, and the model the run
was served by. The run identifier is not in it: an actor is one actor, the page was produced by
the agent rather than by a run, and what a run did is in its log entry, which names every file it
touched. That is what WIKI-002 asks for when it says the record carries the updating run's
values — the record is rewritten by whichever run last wrote the page, never left standing from
an earlier one.

The link form is named by WIKI-001, which demands links between pages; the instruction pins their
syntax to the standard rather than inventing one. §6.1 offers two forms and the instruction takes
only the relative one: the absolute form resolves against the bundle root, which is a root only
for a reader who opens the wiki's own directory, and the wiki is read in editors opened a
directory above it. OKF leaves linking optional and requires readers
to tolerate a broken link, so nothing in Grimoire checks them — whether the links lead anywhere is
the owner's reading, and later lint's (OUT-07).

## Requirements

| ID | Requirement | Proof |
| --- | --- | --- |
| WIKI-001 | The instruction MUST state the shape the wiki is to have: a source page for the submitted text; a type on every page; named sources, referenced where a statement relies on them; links between pages; every page in exactly one section, sections one level deep; a current index per section; a current root index declaring the version of the format standard pinned in `docs/product.md` and listing the sections; a log entry per run that identifies the run and says what changed and why. | review |
| WIKI-002 | Every page a run writes MUST record who generated it and when. Grimoire MUST write both, and MUST replace any value the agent supplies for either. When a run updates a page, the record MUST carry that run's values. When a page arrives without a place for the record, Grimoire MUST add it; when that place cannot be read, the write MUST fail and the agent MUST be told why. Grimoire MUST judge nothing else about the page. | test |
| WIKI-003 | A run that ends failed MUST leave everything it had already written in the wiki in place; Grimoire MUST NOT remove, revert or commit any of it. | test |

### Why WIKI-001 is proven by review

The requirement is about what a text says. A test could only match its wording, which is static
content the constitution does not test (III.8). Whether the agent follows it is agent judgment, out
of scope here and the business of OUT-07. It is proven by item 3 of `docs/review-checklist.md`,
which asks whether the instruction every run receives states the shape this requirement lists, in
full.
