# ADR-0003: Agent harness runtime and the model port

## Status

Accepted

## Context

The system's defining behaviour is that an agent, not the harness, decides what to do. The
constitution puts all judgment about content in versioned instruction files loaded into the agent
context at dispatch (I.1), and makes the harness own lifecycle, credentials, guardrails and
artifacts but never the content decision (I.3). For that split to mean anything, the execution model
has to be a genuine tool-use loop: the agent issues a call, receives its result, and continues
deciding on that basis, for as many iterations as it judges necessary. A single model call whose
output the harness then applies is a different architecture wearing the same words — the harness
would be deciding when to stop and what to apply.

The system will run **more than one kind of agent** over time — agents that only read, agents that
propose rather than write, agents driven conversationally with output streamed back while they work.
The harness is therefore not a single agent's scaffolding: it is where any agent configuration is
expressed, and it has to absorb new kinds without each one bringing its own execution model.

Two further constitutional facts constrain where the loop lives:

- The **model port** is the system's one port (V.2), because the LLM is the one external system
  replaced by a test double (III.2). Wherever the SDK is used, that is where the port is.
- Exactly one component — the **instruction loader** — composes the system prompt, and the model port
  accepts it only as a dedicated type whose constructor is internal to that component's namespace,
  with an architecture test asserting no other namespace constructs it (I.2).

Building the loop by hand is possible but means owning, and testing, control flow that mature
harness libraries already provide. Not building it by hand means adopting a library, and agent
harness libraries are published for a limited set of runtimes — which is a constraint on the runner's
runtime, though not on the hub's, since the two are separate processes for containment reasons
regardless.

## Decision

The agent loop runs in a **child process on Node.js, written in TypeScript**, driving the
**Claude Agent SDK** (`@anthropic-ai/claude-agent-sdk`) via `query()`. One process per run, never
reused.

The **model port** lives in `src/agentrun/model/`, with one adapter over the SDK. The SDK library is
referenced from that directory and nowhere else, asserted by an architecture test. The
**instruction loader** lives beside it in `src/agentrun/instruction/`, is the only module that reads
instruction files, and is the only module that can construct the system-prompt type the port accepts.

The system prompt is a **custom string** — the instruction file's content and nothing else. The SDK's
`claude_code` preset is not used, and `settingSources` is set to `[]`.

The hub spawns the runner, gates it with a handshake so dispatch-time records are persisted before
the first model call, and consumes its event stream.

## Consequences

- The loop under test in CI is the shipped loop. Iteration count and call sequence are the model's,
  which is what the judgment/control split requires, and nothing in the harness decides when a run
  is finished except the run limit.
- Not using the preset means **no instruction text reaches the model from outside the instruction
  file**: no coding-agent guidance, no environment description, no ambient project context. The
  constitution's "judgment only in versioned instruction files" becomes literally true rather than
  approximately true, and the architecture test on the system-prompt type has something real to
  protect.
- `settingSources: []` means no filesystem-loaded settings, memory files, skills or output styles
  reach the run, so a developer's local configuration cannot change what an agent is told.
- The process split is not incidental cost: it is also the per-run trust boundary and the real child
  process the testing rules want.
- The runner is a second language in the system. It is kept narrow — it holds no git, no database,
  and no HTTP server — so the surface where the two ecosystems meet is one process boundary with a
  documented event protocol.
- The harness generalises across agent kinds: a read-only agent, a proposing agent, or a
  conversational one is a different prompt, tool set and event mapping over the same execution
  model, not a second harness.
- Incremental output is available rather than having to be invented: the SDK surfaces partial
  messages and per-call events, which is what a surface that streams a run's progress or a
  conversation needs from below.
- The system inherits the SDK's release cadence and its default tool surface, which must be actively
  removed rather than passively absent. That removal is a decision of its own.

## Alternatives Considered

**A hand-written tool-use loop on the plain API SDK** — **why not:** *makes the system's most
load-bearing control flow ours to test, and every test would exercise our reimplementation rather
than a mechanism many systems exercise.*

The most controllable option: the loop is ordinary code, every branch is visible, no dependency's
defaults have to be audited, and it could live in the orchestrator's language, removing the second
ecosystem entirely. Against that, the loop is not the interesting part of this system and it is not
trivial — tool-result threading, parallel calls, turn accounting, error and refusal handling, context
growth and cancellation are all things a harness library has already met. The trade is real: control
and one language, against maturity and a smaller surface we are responsible for. It is the
alternative to revisit if a library's defaults ever become more work to constrain than the loop would
be to own.

**The tool runner helper in the plain API SDK** — **why not:** *no per-call observation point, so the
guardrail that records refusals moves into our own call sites.*

A middle position: the SDK drives the request → execute → loop cycle over tools we define, with
per-turn hooks and no built-in tools to remove — that last point a genuine advantage, since nothing
has to be stripped because nothing is granted. What it does not provide is an in-process tool server
with a permission evaluation pipeline, or a hook that observes every call before any rule applies. The
containment mechanism would then live in code we write rather than in a documented evaluation order
we can point at.

**Managed agent platforms (hosted loop and sandbox)** — **why not:** *a hosted session becomes a
second system of record, with lifecycle and containment attested elsewhere rather than proven here.*

Removes both the loop and the execution sandbox from our responsibility, which is a large share of
what this system otherwise has to build and prove. But the constitution requires this system to own
agent lifecycle, to produce artifacts recording the instruction version and tool grant, and to prove
its own trust boundary in its own CI. That is a reasonable architecture for a different set of
constraints; it is not this system's.

**The Python agent SDK instead of the TypeScript one** — **why not:** *adds a third toolchain for
capability the TypeScript one already provides.*

Functionally equivalent for this use — same loop, same in-process tool mechanism, same permission
model — and the choice is close to arbitrary on capability. TypeScript wins only because the frontend
already puts a Node toolchain in the repository, so the harness adds no third package manager, test
runner or container base layer. Python would win if the surrounding work were data-processing rather
than web.

**Driving the agent CLI as a subprocess with a JSON output mode** — **why not:** *rebuilds in-process
tools and the pre-invocation hook out of flags and parsed text, at exactly the point where the
containment guarantee lives.*

The documented route for hosts whose language has no SDK, and it would let the orchestrator own the
agent process directly in its own language. The mechanisms this system depends on most — in-process
tools whose handlers record into the run, and a hook that sees every call — are library affordances,
and re-deriving them from a text protocol puts the guardrail in the least reliable place available.
