# Architecture Decision Records

A record here exists so that a decision which is expensive to reverse, and invisible in a diff, can
be reviewed later by someone who was not present when it was taken. Everything on this page follows
from that one purpose.

**Status legend**: `Proposed` · `Accepted` · `Superseded by <id>`

| ID | Decision | Kind | Status | Introduced by |
|----|----------|------|--------|---------------|
| [0001](./0001-hub-runtime-and-http-surface.md) | Hub runtime and HTTP surface — C# on .NET 10 LTS, ASP.NET Core Minimal APIs | Technology choice | Accepted | [001](../../specs/001-source-ingest-agent-run/plan.md) |
| [0002](./0002-frontend-framework.md) | Frontend framework — SvelteKit on Svelte 5, static build served by the hub | Technology choice | Accepted | [001](../../specs/001-source-ingest-agent-run/plan.md) |
| [0003](./0003-agent-runner-runtime-and-sdk.md) | Agent harness runtime and the model port — Node child process on `@anthropic-ai/claude-agent-sdk` | External port | Accepted | [001](../../specs/001-source-ingest-agent-run/plan.md) |
| [0004](./0004-llm-test-double.md) | Where the LLM is replaced for tests — at the wire, via a scripted Anthropic-compatible HTTP server | External port | Accepted | [001](../../specs/001-source-ingest-agent-run/plan.md) |
| [0005](./0005-wiki-repository-layout.md) | Wiki repository layout and mutation path — one ordinary git repository, commit on success, reset on failure | Technology choice | Accepted | [001](../../specs/001-source-ingest-agent-run/plan.md) |
| [0006](./0006-operational-state-store.md) | Operational state store — SQLite via `Microsoft.Data.Sqlite`, no ORM | Technology choice | Accepted | [001](../../specs/001-source-ingest-agent-run/plan.md) |
| [0007](./0007-host-trust-boundary.md) | Host trust boundary — container, with a per-run process boundary inside it | Security boundary | Accepted | [001](../../specs/001-source-ingest-agent-run/plan.md) |
| [0008](./0008-api-contract-mechanism.md) | Frontend-to-hub contract mechanism — committed OpenAPI 3.1, generated client, CI drift test | Technology choice | Accepted | [001](../../specs/001-source-ingest-agent-run/plan.md) |
| [0009](./0009-tool-grant-enforcement.md) | How agent tool grants are expressed and enforced — in-process MCP tools, deny-by-default, four enforcement layers | Security boundary | Accepted | [001](../../specs/001-source-ingest-agent-run/plan.md) |
| [0010](./0010-model-egress-and-credential-custody.md) | Model egress and credential custody — no upstream credential in any executing process; all egress through one proxy | Security boundary | Accepted | [001](../../specs/001-source-ingest-agent-run/plan.md) |
| [0011](./0011-cloud-native-runtime-contract.md) | Cloud-native runtime contract — env config, stdout JSON, health, `SIGTERM`, volumes, one replica | Technology choice | Accepted | [001](../../specs/001-source-ingest-agent-run/plan.md) |
| [0012](./0012-egress-proxy-implementation.md) | Egress proxy implementation — YARP on ASP.NET Core, `src/egress/` | Technology choice | Accepted | [001](../../specs/001-source-ingest-agent-run/plan.md) |

No record is currently superseded.

---

## What we expect from a record

### What gets one, and what does not

- **Only the four kinds**: technology choice, external port, security boundary, agent autonomy
  change. Everything else is an elaboration of an already-accepted decision and gets none.
- **Capability and scope are specification, not architecture.** What an agent may do, how many
  surfaces exist, which endpoints are served — these are stated by the specification that requires
  them. A record decides the *mechanism* they instantiate. If adding a feature would mean editing a
  record, that record is describing the wrong thing.
- **One decision aspect per record.** "All egress leaves through one credential-holding proxy" and
  "that proxy is built with YARP" are two aspects, and they get two records: the boundary should
  survive swapping the implementation.

### Context

- **State the forces, not the conversation.** No "as discussed", no "X asked for this", no account of
  how the decision came about. A reader needs what constrained the choice, not its provenance.
- **Ground it in the system and the constitution, not in one feature.** No feature names, no
  requirement identifiers. A record outlives the work that occasioned it, and a record that quotes a
  feature's requirements has duplicated the specification instead of deciding anything.
- **Reason from where the system is going, not from what today's scope excludes.** A current
  limitation — "no live updates yet", "few endpoints today", "one run at a time" — is a snapshot, not
  a force, and a decision argued from one expires the moment the snapshot changes. Name the durable
  forces, including those the system does not exercise yet, and be explicit when a constraint really
  is durable (a property of the domain) rather than merely current.

### Decision

Say what is done, in enough detail that someone could implement it. Where the decision deliberately
stops short — a mechanism adopted now, its richer uses deferred — say so here rather than leaving the
scope to be inferred.

### Consequences

Both directions. The benefits are the easy half; the half that matters later is the cost paid, the
thing given up, and the standing maintenance accepted. Where a consequence is a limitation, state it
plainly rather than phrasing it as a feature — a record that reads as advocacy is one nobody trusts
when the decision is being reconsidered.

### Alternatives Considered

This is the part that makes a record worth re-reading, and the part most likely to be hollow.

- **They must be real choices.** A version bump on the same runtime is not an alternative; a
  different runtime is. If an option was never actually available, leave it out.
- **Verdict first, then the treatment.** Each alternative opens with a scannable line so the section
  can be skimmed for reasons and read for reasoning:

  ```markdown
  **Envoy** — **why not:** *two routes do not justify a control-plane configuration model, and the
  policy would still be written as a separate service — so the code exists regardless.*

  Genuinely the most capable option and the right answer at a different scale: mature filter
  chains, first-class streaming, retries, circuit breaking...
  ```

- **Give each option its strongest case, in its own terms, before the reason it loses.** An
  alternative written as a strawman teaches a later reader nothing and quietly removes the option
  from consideration for good.
- **Say where the chosen option is worse.** If the rejected proxy forwards traffic better, if the
  rejected database is the better database, if the rejected boundary gives a cheaper proof — write it
  down. That honesty is what makes the verdict trustworthy.
- **Name the condition that would flip it.** Most rejections are conditional. "The right answer at a
  different scale" is only useful if the scale is named.

### Cross-references

**Records do not reference one another.** A decision that depends on another states the fact it
depends on, not the record that decided it. Two reasons: a superseded record leaves no dangling
citations to chase, and no record ever appears to have known about a decision taken after it. The
single exception is a `Status` line naming what superseded it.

Plans and specifications may cite records freely; the prohibition is on citations *between* records.

### Format

The fixed template, these headings in this order: **Title; Status; Context; Decision; Consequences;
Alternatives Considered.** A record is merged in the same pull request as the plan that makes the
decision, with its row added to the table above in that same pull request.

## Adding a record

Follow the expectations above. The row here goes in the same pull request, so this page never lags
the directory.

## Revising versus superseding

An **accepted** record is superseded whole, never edited in place. A changed decision is a **new**
record naming the one it supersedes; the superseded file stays in the directory with its reasoning
intact and its status changed to `Superseded by <id>`, and its row here is updated to match.

A record still being drafted in an unmerged plan is a different case: it is **revised in place**.
Writing a supersession chain for a decision that was never accepted manufactures history that did not
happen and makes the genuinely superseded records harder to find.

Anticipated supersessions, each with its trigger written into the record itself: **0007** when the
agent process moves into its own container with its own network namespace; **0012** if the upstream
credential becomes permanently static and the content route moves elsewhere. Granting an agent a tool
with host reach would supersede **0007** and **0009** together, and those two must not be bundled
into one record.

What a particular agent may do is **not** in that list: a grant is stated in its specification and
instantiates the mechanism 0009 records. A new agent adds a specification, not a record here.

## Vocabulary

Records use these terms precisely, because conflating the first two has already caused one error:

- **Hub** — the orchestrator. Owns the HTTP surface, dispatch, task artifacts, the wiki repository,
  the operational store and observability. It supervises agent processes; it does not run agents.
- **Agent harness** — the runtime that actually runs an agent: drives the loop, composes the system
  prompt from instruction files, enforces the tool grant.
- **Grant** — the set of actions an agent can take. Deny-by-default, recorded on the artifact before
  the agent acts.
