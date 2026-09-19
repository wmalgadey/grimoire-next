# Grimoire

A self-hosted system in which LLM agents maintain a Markdown wiki, while the human decides what goes in and what gets asked.

> This README describes the idea. What gets built, and in which order, is stated exclusively in [docs/product.md](docs/product.md).

## The idea

Grimoire implements the [LLM wiki](https://gist.github.com/karpathy/442a6bf555914893e9891c11519de94f) pattern described by Andrej Karpathy. The central observation: the hardest part of a personal knowledge base is not reading or thinking, but administration. Cross-references have to be updated, outdated statements flagged and new material filed consistently. It is exactly this mechanical upkeep that LLMs do without tiring. The human keeps curatorial control over which sources go in and which questions get asked.

The result is a lasting artifact: a wiki of linked Markdown files that gains coherence with every source. The purpose of the wiki is freely chosen — a general knowledge collection, a recipe collection, repair know-how, or collected travel experience from which recommendations and checklists emerge.

## Why not RAG

Classic RAG systems transform raw data into a form the user can neither read nor use directly, and derive every answer anew from the raw material. With Grimoire the wiki itself is the result. It is readable, editable with any tool, and usable even without Grimoire. Answers arise from pages that have already been synthesized.

The wiki follows the [Open Knowledge Format](https://cloud.google.com/blog/products/data-analytics/how-the-open-knowledge-format-can-improve-data-sharing), an open format of Markdown files with metadata about provenance and confidence, readable by humans and agents without special tools.

## How it is meant to work

Three operations work on the wiki:

- **Ingest** works a source in: extract information, update affected pages, maintain cross-references, record the provenance.
- **Query** answers questions from the wiki pages and backs the answer with references. Valuable results can themselves become wiki pages.
- **Lint** checks the wiki for contradictions, outdated statements, orphaned pages and missing cross-references.

The roles are separated. Grimoire limits what an agent can reach and execute, and records what it did and why. Grimoire never decides about content. The agent and the user do that. The user sees the agent's decisions and approves what is consequential.

## Where things live

| Question                              | Place                           |
|---------------------------------------|---------------------------------|
| What gets built, what does not?        | docs/product.md                 |
| What exists and how does it behave?    | docs/capabilities/              |
| What is proven?                        | docs/trace.md                   |
| Which rules apply?                     | .specify/memory/constitution.md |
| What is a PR checked against?          | docs/review-checklist.md        |
| How did it come about?                 | specs/NNN-*                     |

## Development

Grimoire is built with Spec-Driven Development (Spec Kit). Every feature advances exactly one outcome from `docs/product.md` and goes through specify → plan → tasks → implement → converge.
