# Decisions in force

<!-- DEC-NNN, in order of assignment, never renumbered, never reused. DEC is not a capability name.
     Every entry: decision, reason (a constraint or evidence, never just "owner decision"), made by. -->

## DEC-001 — Models are paid for through the owner's Claude subscription sign-in, never per token

**Decision**: Every model call goes through Claude Code's subscription sign-in. Locally the
stored sign-in of the machine is used; Grimoire handles no credentials itself. An API key must
never reach the agent process: `ANTHROPIC_API_KEY` is removed from its environment.
**Reason**: This project is meant to run on Claude Pro/Max subscription usage; per-token API
billing is not acceptable for it. Subscription authentication is available only through
Claude Code (the CLI, or the Agent SDK that wraps it), not through the API directly — a client
calling the API with the subscription token is served Haiku only.
**Risk**: Anthropic changed its terms for this path several times in 2026 and may enforce them
without notice. **Fallback**: an API-key adapter behind the same agent port; nothing outside
that adapter may know which one is in use.
**Made by**: owner, before feature 001.

## Superseded
