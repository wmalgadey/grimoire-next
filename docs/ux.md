# UX principles

Applies to every screen Grimoire has. Read before specifying or implementing anything with a UI.

## How to read wishes in briefs and specs

Screen descriptions, sketches, lists and examples in briefs and specs are the owner's concerns and
wishes. They say what matters, not how it must look. The implementing agent decides layout,
components and wording, as long as the concern is visibly addressed. Words like "exactly", "must",
"always" are not used for visual details; where they appear, treat them as emphasis, not as a
contract.

## Feel

- A tool, not a product — dense, quiet, text-first. Nothing on a screen that is not being read.
- One page per job; no navigation chrome until a second job exists.
- Where a screen shows a log, it reads as a log: monospace, in the order things happened.
- The readable artifact is the product. A screen is a window onto a file the user could open
  themselves, never the only place the information exists.

## References

- Claude Code in the terminal: a tool call is one line, its result folded underneath, the agent's
  own text as prose in between. The same data as a Grimoire run, in a form the owner reads daily.

## Constants

- Language of the UI: en
- Typography: system font; monospace wherever the content is a log or a file
- Colors: follow system
- Feedback: every action that takes longer than a second shows that it is running and how it ended
- Live content grows in place — arriving lines must not move what the user is reading

## Never

- Modal dialogs for confirmation
- Toast notifications that disappear on their own
- A number on a screen that exists nowhere else, and that the user cannot get at without Grimoire
