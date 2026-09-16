# ADR-0011: Cloud-native runtime contract

## Status

Accepted

## Context

The system is a long-lived stateful service with durable local state, child processes it supervises,
and outbound dependencies. It will be run in two quite different ways for a long time: directly on a
developer's machine, and as containers under something that starts, stops and replaces it on its own
schedule.

"Cloud native" is a label that means nothing unless it is written down as properties that can be
checked. Left implicit, it degrades into an aspiration that is discovered to be false during the
first rollout.

Several of these properties are not conveniences but correctness requirements for *this* system
specifically:

- **Termination is routine, not exceptional.** An orchestrator replaces a running instance on
  deployment, on node drain, on scaling and on health failure. A system that supervises long-running
  agent processes and holds a working tree must therefore treat "you are being stopped mid-work" as
  an ordinary path with a defined end state — otherwise correctness depends on never being
  restarted.
- **Readiness is not liveness.** A process can be serving while unable to reach its store or its
  egress path. Conflating them either sends traffic to an instance that cannot do its job, or
  restarts one whose dependency is merely slow.
- **State outlives the container.** Anything that must survive replacement has to be on a volume, and
  anything not on a volume must be genuinely reconstructible.
- **Configuration arrives from outside.** Baking configuration into an image means rebuilding it to
  change an endpoint, and it prevents the same artifact being promoted between environments.
- **Streaming connections change what shutdown means.** As surfaces begin holding open connections to
  watch work in progress, draining is no longer "finish in-flight requests" but "stop accepting, let
  open streams end or close them deliberately".

## Decision

The system satisfies a written runtime contract:

| Aspect | Contract |
|--------|----------|
| Configuration | Environment variables only; no configuration file inside an image; missing required values fail at startup, loudly |
| Logging | Structured JSON on stdout; no files, no rotation, no sink configuration in the application |
| Health | A liveness endpoint (the process is serving) and a readiness endpoint (dependencies reachable, not draining), reported separately |
| Shutdown | On `SIGTERM`: stop accepting new work, terminate supervised child processes, bring interrupted work to a defined recorded end state, release held resources, exit |
| State | All durable state on mounted volumes; the container filesystem read-only apart from those and a `tmpfs` |
| Identity | Non-root, no added capabilities |
| Scaling | **One replica**, stated in the contract rather than discovered |
| Images | Multi-architecture, built reproducibly from a committed definition |

**Running directly on a host is a first-class mode, not a degraded one.** Nothing in the contract
requires a container: the volumes are directory paths, the endpoints are ordinary HTTP, and signal
handling is how a process behaves anyway.

## Consequences

- An orchestrator's expectations are met without adaptation: it can stop the process and get a drain,
  read the two health endpoints for different decisions, and collect stdout without the application
  knowing where logs go.
- Interrupted work has a defined end state at the moment of interruption, rather than only being
  reconciled at the next startup. Startup reconciliation remains as the backstop for a process that
  is killed outright, so both paths converge — and both are tested.
- Readiness makes a broken dependency diagnosable as a broken dependency.
- The same image is promoted between environments; only its environment differs.
- **One replica is a property, not a defect to fix later.** The write path is serialised by the
  domain, the operational store is single-writer, and runs hold a working tree; a second replica
  would violate the first and corrupt the others. Writing it into the contract means nobody
  discovers it by scaling.
- Local block storage is required for the operational store; a network filesystem breaks its locking.
  The contract has to say so, because the failure is silent corruption rather than a refusal.
- Developers run the system without a container runtime, so the inner loop stays fast and most tests
  need no container. What genuinely differs on a host is the absence of a network-level egress
  policy, which is why protections that matter are implemented in the application as well.

## Alternatives Considered

**Treat containerisation as a later concern and build for the host first** — **why not:** *graceful
shutdown and readiness are not additive — they change how dispatch, supervision and startup
reconciliation are structured, so retrofitting means reopening the hardest code once the system
already holds state worth preserving.*

Defensible: less work now, no designing for a deployment nobody is running yet, and a simpler inner
loop. Most of the contract genuinely could be retrofitted — which is why the two items that cannot
are the whole reason to decide now.

**Adopt a specific orchestrator's conventions directly** — **why not:** *makes the system harder to
run anywhere else, including on a developer's machine, where most of the work actually happens.*

Its probe semantics, configuration objects and lifecycle hooks would fit that platform better than a
generic contract does, using richer features — startup probes distinct from liveness, pre-stop hooks,
termination grace periods — instead of a lowest common denominator. Those features are expressible as
configuration *around* a process that behaves as described here, so nothing is lost by staying
generic.

**Adopt a full platform stack now: tracing exporters, metrics endpoints, a service mesh** — **why not:** *a declared signal without a surface where an operator reads it is a control that is not
connected — it looks like observability while providing none.*

The conventional meaning of "cloud native", and it would produce richer operational data than logs
and two health endpoints. Every signal this system declares reaches an operator through a surface it
already has. When a dashboard exists, instrumentation follows it; not the other way round.

**Write no contract; adopt the practices informally** — **why not:** *the properties most likely to be
violated by someone reasonably assuming otherwise are exactly the ones a written contract lets a test
assert and a reviewer notice.*

Cheapest, and in a small team often adequate. "One replica", "local block storage only" and "SIGTERM
leaves work in a defined state" are not discoverable from the code.
