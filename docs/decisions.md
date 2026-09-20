# Decisions in force

<!-- DEC-NNN, in order of assignment, never renumbered, never reused. DEC is not a capability name.
     Every entry: decision, reason (a constraint or evidence, never just "owner decision"), made by. -->

## DEC-001 — Models are reached through the Claude Code Cli or Claude Agent SDK with the owner's subscription sign-in

**Reason**: This project is meant to run on Claude Pro/Max subscription usage, not per-token API
billing; API-token costs are not acceptable for this project. Subscription authentication is
available only through Claude Code and the Agent SDK.
**Risk**: Anthropic changed its terms for this path several times in 2026 and may enforce them
without notice. **Fallback**: an API-key adapter behind the same agent port; nothing outside
that adapter may know which one is in use.
**Made by**: owner, before feature 001.

## Superseded
