# ADR-0012: Egress proxy implementation

## Status

Accepted

## Context

The deployment mediates all outbound traffic through a single proxy that holds the upstream
credential, on two routes with different policies: one that injects a credential and forwards to a
single configured upstream, and one that forwards to a per-request, user-supplied destination with no
credential and after a destination policy check. That arrangement is settled; what it is built with
is not.

What the component actually has to do, stated as durable requirements rather than as today's traffic:

- **Rewrite credentials per route.** Strip what the caller presented, attach what the upstream
  expects. The two routes attach different things — one a secret, the other nothing.
- **Forward to a dynamic destination under policy.** The content route's target is known only per
  request and is attacker-influenced, so the destination must be validated before the connection and
  the decision must be expressed in real code, not in a match expression.
- **Pass streaming responses through untouched.** Model responses are streamed, and a surface that
  watches work in progress depends on incremental delivery. A mediator that buffers a response until
  it completes silently converts a streaming system into a batch one — a failure that does not
  announce itself.
- **Hold a credential that may refresh.** A deployment may authenticate with a long-lived key, or
  with a short-lived credential that has to be renewed on a schedule. The second case requires
  running code with state and a timer, not a value substituted at startup.
- **Become the place operational policy lands.** Per-run attribution, rate limiting, cost accounting
  and upstream failover all belong at this chokepoint eventually. Whatever is chosen should make
  adding them ordinary rather than exotic.

The refresh requirement is the discriminating one. It is the difference between "configure a proxy"
and "write a small service".

## Decision

The proxy is **YARP hosted in an ASP.NET Core application**, the same runtime as the orchestrator,
shipped as its own image.

- The **model route** is a YARP route and cluster with a request transform that removes the caller's
  token and attaches the upstream credential obtained from an injected credential provider.
- The **content route** uses YARP's direct forwarder with a per-request destination, after the
  destination policy rejects non-`http(s)` schemes and loopback, link-local and private targets.
- The **credential provider** is an ordinary service with one method the model route calls for the
  credential to attach. Today it returns a static value; a refreshing credential replaces its body,
  and the model route does not change. It is a class, not an interface: there is one implementation
  and nothing external behind it, and an interface would be introduced only when a second
  implementation has to coexist with the first.

Response buffering is disabled on both routes so streamed responses pass through as they arrive.

## Consequences

- Credential refresh is ordinary application code with the runtime's own scheduling and lifetime
  primitives, rather than a plugin in a foreign extension model.
- One runtime, one toolchain, one test framework across the system's server-side components; the
  proxy is tested and gated like any other component rather than through configuration fixtures.
- The destination policy for user-supplied targets is written, reviewed and unit-tested as code,
  which is what it deserves given what it is defending against.
- Streaming passes through by default once buffering is disabled, and that setting is explicit and
  visible rather than an environment-dependent default.
- Attribution, quotas, accounting and failover have an obvious home, each as ordinary middleware.
- **It is code we own, where a configuration-driven proxy would have been a file.** More surface to
  maintain, patch and review. Accepted because the refresh requirement forces code somewhere; this
  puts it where the rest of the system's code already is.
- A second image to build, ship and keep patched.
- A .NET process has a larger footprint than a purpose-built proxy binary. Irrelevant at this
  system's traffic, and the wrong thing to optimise for now.
- **Trigger to revisit**: if the upstream credential becomes permanently static *and* the content
  route moves elsewhere, the remaining job is header rewriting and forwarding, which a
  configuration-driven proxy does better with less to maintain.

## Alternatives Considered

**nginx or Caddy, configuration only** — **why not:** *neither can refresh a credential on a
schedule, and a per-request destination policy is not expressible in either configuration language —
so code has to exist anyway, in a runtime the project does not otherwise use.*

The strongest simplicity argument: no application code, no test suite, no dependency graph, and a
binary with an enormous operational track record. Caddy in particular would need very little
configuration for the model route, and both are better at raw forwarding than anything written here.
A rotating credential would need an external process rewriting the config and reloading — a service
again, in two pieces instead of one. The streaming requirement is a further trap rather than a
blocker: nginx buffers proxied responses by default, so this would work perfectly until someone
noticed responses arrive all at once.

**Envoy** — **why not:** *two routes do not justify a control-plane configuration model, and the
destination policy would still be written as a separate authorization service — so the code exists
regardless, now split across a second component and a configuration language.*

Genuinely the most capable option and the right answer at a different scale: mature filter chains,
first-class streaming, retries, circuit breaking, observability, and an external authorization
protocol that could carry the destination policy properly. With many services and cross-cutting
traffic policy it would be the choice. The credential-refresh problem also remains unsolved inside
Envoy itself.

**An off-the-shelf LLM gateway** — **why not:** *brings a separate runtime and dependency ecosystem
to solve one of the two routes, and does nothing for the other — so the second component still has to
exist.*

Purpose-built for exactly the model route: credential custody, multi-provider routing, usage and cost
tracking, rate limiting and failover, already written and maintained by people who do this full time.
If multi-provider routing or per-tenant cost accounting were needed today this would be the shortest
path to it, and it is the option to revisit when either becomes a requirement rather than a
possibility.

**A hand-written forwarder over the runtime's HTTP client** — **why not:** *hop-by-hop headers,
trailers, connection reuse, cancellation propagation, unbuffered body streaming, timeouts and error
mapping are all subtle, all security-relevant, and all already solved by a reverse-proxy library.*

Appealing because the job sounds small: read a request, rewrite a header, forward it, stream the
response back. Everything that is not the happy path is the reason the prohibition on reimplementing
framework capability exists.

**Two separate proxies, one per route** — **why not:** *doubles the deployables and gives the
default-deny egress policy two exceptions instead of one, weakening the property that makes the
posture testable.*

Better isolation between the credential-bearing path and the arbitrary-destination path: a defect in
the content route's policy could not touch the component holding the secret, which is a real
argument. It separates two routes that already have separate code paths, separate credentials and
separate tests within one component.
