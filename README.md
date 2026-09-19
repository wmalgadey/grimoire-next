# Grimoire

A self-hosted system in which an LLM agent maintains a Markdown wiki, while the human decides what goes in and what gets asked.

> This README describes the idea. What gets built, and in which order, is stated exclusively in [docs/product.md](docs/product.md) — where the two differ, that file is right.

## The idea

Grimoire implements the [LLM wiki](https://gist.github.com/karpathy/442a6bf555914893e9891c11519de94f) pattern described by Andrej Karpathy. The central observation: the hardest part of a personal knowledge base is not reading or thinking, but administration. Cross-references have to be updated, outdated statements flagged and new material filed consistently. It is exactly this mechanical upkeep that LLMs do without tiring. The human keeps curatorial control over which sources go in and which questions get asked.

The result is a lasting artifact: a wiki of linked Markdown files that gains coherence with every source. What the wiki is for is the user's to decide — a general knowledge collection, a recipe collection, repair know-how, or collected travel experience from which recommendations and checklists emerge.

## Why not RAG

Classic RAG systems transform raw data into a form the user can neither read nor use directly, and derive every answer anew from the raw material. With Grimoire the wiki itself is the result. It is readable, editable with any tool, and usable even without Grimoire. Answers arise from pages that have already been synthesized.

The wiki follows the [Open Knowledge Format](https://github.com/GoogleCloudPlatform/open-knowledge-format) ([background](https://cloud.google.com/blog/products/data-analytics/how-the-open-knowledge-format-can-improve-data-sharing)), an open format of Markdown files whose metadata carry provenance and trust, readable by humans and agents without special tools. A reader can see where a statement came from and how well it is attested.

## How it is meant to work

Three operations work on the wiki:

- **Ingest** works a source in: extract information, update affected pages, maintain cross-references, record where it came from.
- **Query** answers questions and requests from the wiki — an answer, a recommendation, a checklist — and backs the result with references.
- **Lint** checks the wiki for contradictions, outdated statements, orphaned pages and missing cross-references.

Operations are what happens to the wiki. `docs/product.md` groups the system into capabilities; the three operations are among them, next to the areas that carry them.

The roles are separated. Grimoire never decides about content — the agent and the user do. Grimoire's part is to run the agent, to make afterwards visible what a run did, and to keep an agent's abilities narrow: tools cut for the job, never general execution access. Runs are judged after the fact rather than approved up front.

The wiki lives in the user's own repository, in plain files. Nothing in it needs Grimoire in order to be read, and nothing Grimoire does is beyond the user's reach.

## Where things live

| Question                            | Place                             |
| ----------------------------------- | --------------------------------- |
| What gets built, what does not?     | `docs/product.md`                 |
| What exists and how does it behave? | `docs/capabilities/`              |
| What is proven?                     | `docs/trace.md`                   |
| Which rules apply?                  | `.specify/memory/constitution.md` |
| What is a PR checked against?       | `docs/review-checklist.md`        |
| How did it come about?              | `specs/NNN-*`                     |

## Development

Grimoire is built with Spec-Driven Development (Spec Kit). Every feature advances exactly one outcome from `docs/product.md` and goes through specify → plan → tasks → implement → converge.
