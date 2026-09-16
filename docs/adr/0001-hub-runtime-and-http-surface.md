# ADR-0001: Hub runtime and HTTP surface

## Status

Accepted

## Context

The hub is the **orchestrator**. It owns the HTTP surface and the operator's web surfaces, the queue
and dispatch of agent runs, the task artifacts those runs produce, the wiki repository, the
operational state store, and observability. It supervises agent processes and terminates them on a
deadline, but it does not run agents: the **agent harness** — the runtime that drives the agent loop,
composes the system prompt from instruction files and enforces the tool grant — is a separate
process, decided separately. The constitution's requirement that all of this
live in code rather than in instruction files (I.3) is what makes the split meaningful: the
orchestrator decides *when and whether*, the harness decides *how the agent is run*, and neither
decides content.

What the orchestrator runtime has to be good at, stated so it holds beyond any one capability:

- **Hosting an HTTP surface that will grow in kind, not just in size.** Today's surfaces are
  request/response. A system whose work is performed by long-running agents will want to stream
  progress while a run is in flight, and conversational direction of agents is a natural surface for
  it. The runtime needs first-class server-push (SSE, websockets) and asynchronous streaming
  responses, not as an add-on.
- **Supervising child processes** with deadlines, signal handling, output streaming and reliable
  termination, as a normal and well-supported thing rather than a corner of the ecosystem.
- **Owning local durable state** — a git repository and a database — with predictable behaviour under
  a process that starts, drains and stops repeatedly.
- **Being deployable and operable as a long-lived service**: configuration from the environment,
  structured events, liveness and readiness, graceful shutdown.

Three constitutional constraints bear on the runtime rather than on the code written in it:

- **Architecture tests are CI gates.** External-system libraries must be provably confined to their
  adapters, and first-level directories must be domain slices. The runtime determines whether that
  confinement is a compile-time property or a lint rule someone can suppress.
- **Tests run against real infrastructure** — real HTTP hosting, real child processes, real
  filesystem, real git — so the runtime needs a test host that serves over a real socket rather than
  an in-memory substitute.
- **Technical-layer directories** — controllers, services, utils, helpers, models — must not appear at
  the first level (V.1), and frameworks must not be wrapped (VII.1).

## Decision

The hub is **C# on .NET (current LTS: .NET 10, language level C# 14)**, using **ASP.NET Core Minimal
APIs** for its HTTP surface.

Each domain slice is its own project, so a slice can only reach an external library it holds a
package reference to. `src/hub/` is the composition root: it maps endpoints, wires dependencies,
registers health checks and reads configuration. It holds no domain logic, and the slices it
composes hold no HTTP types.

## Consequences

- Adapter confinement is a **compile-time property**, not a convention: a slice without the package
  reference cannot name the library. The architecture test asserting it becomes a second line of
  defence rather than the only one.
- Process supervision, graceful shutdown, dependency injection, configuration binding and health
  checks are in-box (`IHostedService`, `IHostApplicationLifetime`, `HealthChecks`) rather than
  assembled from third-party packages — less to wrap, less to keep in step.
- Streaming responses and server-sent events are native (`IAsyncEnumerable<T>` results, long-lived
  responses), and websockets are first-class, so the surfaces the system is heading toward do not
  require a different hosting model.
- Minimal APIs keep routing, binding and the handler in one visible expression per endpoint, and
  `MapGroup` lets each slice contribute its own endpoints from inside the slice — so the HTTP surface
  organises the same way the codebase does.
- Real HTTP hosting in tests is Kestrel on an ephemeral port: the same server as production.
- The system runs **two languages**, C# for the orchestrator and TypeScript for the agent harness.
  Two toolchains, two test runners, two dependency ecosystems, two CI paths. The cost is real and is
  accepted because the process split exists for containment reasons regardless, so unifying the
  language would not unify the deployment.
- Targeting the current LTS keeps security updates flowing; moving with the LTS train is maintenance,
  not a reopening of this record.

## Alternatives Considered

**ASP.NET Core MVC controllers** — **why not:** *organises the HTTP surface by technical layer,
cutting across the domain slices the constitution mandates, and moves behaviour away from the call
site.*

The conventional choice, and it brings real ergonomics Minimal APIs do not: `[ApiController]` gives
automatic model-state validation and `ProblemDetails` responses, action filters express a
cross-cutting concern once for many endpoints, and conventions keep a broad surface consistent
without relying on discipline. The objection is structural rather than about size. A `Controllers/`
tree, one class per resource, means each slice either surrenders its HTTP entry point to a shared
tree — breaking slice locality — or keeps it locally and leaves the convention half-applied. Filters
compound it: understanding one endpoint means reconstructing which filters ran. Minimal APIs invert
both, and the ergonomics given up are recoverable explicitly, which the constitution prefers anyway.
The trade would flip for an API with many resources sharing deep cross-cutting behaviour, where
conventions earn their implicitness.

**A REPR-style endpoint library (FastEndpoints, Carter)** — **why not:** *a framework layered over
the framework, and the structure it imposes is orthogonal to domain slices.*

These target the real failure mode of Minimal APIs — a composition root that accumulates routes until
nothing can be found — by giving each endpoint a class with validation and mapping attached, and they
do it well. But at the point of use the thing being used stops being ASP.NET Core, so its
documentation, diagnostics and upgrade path stop applying directly; that is what the prohibition on
wrapping is aimed at. And one class per endpoint is a different axis from slices; the same
anti-sprawl discipline is available by having each slice map its own group, with no dependency added.

**A single TypeScript/Node orchestrator, unifying with the agent harness** — **why not:** *adapter
confinement degrades from a compile error into a suppressible lint rule.*

The most serious alternative: one language, one package manager, one test runner, one CI path; types
shared across the orchestrator/harness boundary with no protocol translation; and the harness's SDK
ecosystem becomes the orchestrator's. It loses on the constitution's architecture gates. In a single
Node project everything in `node_modules` is importable from everywhere, so confining git, the
database driver or the model SDK to their adapters becomes an import-graph lint — advisory,
suppressible, and only as current as someone keeps its configuration. The .NET equivalent is the
absence of a package reference, which the compiler enforces and no local override defeats.
Secondarily, supervision, signal handling, readiness and configuration binding are assembled from
libraries rather than in-box. And the unification is partial regardless: the agent must run in its
own process for containment, so the two-process topology survives the change while the compile-time
guarantee does not.

**Go or Rust** — **why not:** *adds a third ecosystem to obtain a guarantee the second already
provides.*

Both suit a supervising service well — a single static binary, a small image, excellent signal and
subprocess handling, low idle footprint — and Go especially would make the orchestrator a single
artifact. Neither improves on the property that decided this: Go's `internal` packages restrict
visibility by directory but not library reachability within a module, and Rust's crate boundaries
would, at the price of a third language in a system that already requires TypeScript for the agent
SDK.
