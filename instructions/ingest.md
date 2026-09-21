# Working a submitted text into the wiki

You are maintaining a wiki. A person has handed you one text to work into it.

Read the wiki before you write to it. What is already there decides what this text adds, what it
changes, and what it contradicts.

## The shape the wiki is to have

What follows holds for the work of your run: the pages you add, the pages you change because this
text bears on them, and the links between them. Where what the text says belongs on a page that is
already there, change that page rather than adding a second one for the same thing. Bringing the
rest of the wiki into this shape is not your run's work.

### A source page for the submitted text

Every submitted text gets a page of its own that stands for it as a source, and every source page
lives in `sources/`. That one section is reserved for them, and it is the only one in the wiki
named for a kind of page rather than for a subject. Other pages name the source page where they
rest on it.

What the source page carries is your judgement — a faithful record of what the text says and
where it came from, in your words or the author's.

### What one run leaves behind

The source page, and then the pages the text is actually *about* — **five to ten of them where
the text carries that much** — each in the section of its subject, never in `sources/`.

A text rich enough for twenty gets the ten that matter most. A text that genuinely holds one gets
one, and the log entry says why so few. What a run does not do is stop at the source page because
going further would be work: the source page records that something was submitted, not what was
learned from it.

### A type on every page

Every page declares what kind of page it is, in its frontmatter, as `type`. No page is left
without one.

### Named sources, referenced where a statement relies on them

Every page names its sources in its frontmatter under `sources`, each entry carrying a `resource`.
A statement that rests on a source says so where the statement is made, not only in a list at the
foot of the page. A reader must be able to see which claim came from where.

### Links between pages

Pages link to the other pages they relate to, as standard Markdown links. A target is written
either from the wiki root — `[the customers table](/tables/customers.md)` — or relative to the page
the link sits on — `[a neighbouring page](./other.md)`. The `.md` is part of it.

A page that mentions something the wiki already covers links to it. Make the links in both
directions: a page you add links out to what is already there, and the pages it belongs with link
back to it. A new page is reachable from somewhere — nothing is added that nothing points at.

### One section per page, and sections one level deep

Every page lives in exactly one section — one directory — and sections are one level deep. There
are no sections inside sections, and no page belongs to two.

Apart from `sources/`, **a section is a subject.** Its name says what the pages in it are about —
the thing a reader has in mind when they go looking. `notes`, `inbox`, `misc`, `eingereicht`,
`sonstiges` and the like are not subjects: they say how a page came to exist, which is not how
anyone looks for it. `sources/` is the one exception, and it holds source pages only.

Which subjects exist is your decision, and a new subject gets its own section the moment the
first page belongs to it — a section holding one page is a section. Put a page where a reader
would look for it, not where the last page happened to go.

### A current index per section

Every section carries an `index.md` listing the pages in it. When your run adds a page, the
section's index lists it by the end of the run.

### A current root index

The wiki's root `index.md` is the entry point. It declares the version of the format standard the
wiki follows — `okf_version: "0.2"` — and lists the sections. It lists sections, not pages. When
your run makes a section, the root index lists it by the end of the run.

### A log entry for the run, identifying it

`log.md` gets one entry for this run. The entry **contains the run identifier you were given, as
plain text**, and says what you changed and why you changed it — what you added, what you updated,
what you decided not to do and the reason. Write it for the person who will read it weeks later
without remembering the text you were working from.

Write this entry last, after the pages and the indexes are as you want them.

## When the wiki is empty

The first run creates what it needs: the source page, its section and that section's `index.md`,
the root `index.md` with `okf_version` and the section listed, and the first entry in `log.md`.
Nothing is assumed to exist already.

## What you do not do

- You do not delete or move anything. Nothing in the wiki is yours to remove.
- You do not write the `generated` frontmatter key; whatever you put there is replaced.
- You do not reach outside the wiki. Every path you give a tool is relative to the wiki root.

## If you cannot do what was asked

Say so in the log entry, with the reason, and leave the wiki consistent: no page half-written, no
index naming a page that is not there. An honest entry is worth more than a forced change.
