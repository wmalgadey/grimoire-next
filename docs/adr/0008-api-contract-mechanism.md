# ADR-0008: Frontend-to-hub contract mechanism

## Status

Accepted

## Context

The constitution requires the frontend and the hub to communicate only through an explicit contract
versioned in this repository, and requires runtime behaviour to derive from the committed contract so
that **every contract change is visible in the PR diff** (V.5). It leaves the mechanism — schema
language, generation, validation — to be decided (V.6).

The rule's purpose is reviewability. A contract that exists only as the emergent behaviour of running
code is one nobody reviews: it changes silently, and the first sign of a breaking change is a broken
client. So whatever is chosen must make a contract change *a diff a reviewer sees*, and must make a
client that has not absorbed that change *fail loudly and early*.

What the contract has to describe, stated to hold as the system grows rather than for any one set of
endpoints:

- **Request/response resources** with precise types, including the discriminated shapes that
  inspection surfaces need (a record that is present in one state and absent in another).
- **Streaming responses.** Surfaces that watch long-running work want server push; a contract that can
  only describe request/response would leave the most operationally interesting traffic undescribed.
- **Operational endpoints** alongside product ones, since both are part of the served surface and a
  drift check is only meaningful if it covers everything served.

Tooling reality also constrains the choice: the contract has to be consumable by the frontend's
build to generate types, and comparable against what the server actually serves, without adopting a
toolchain that exists only for this purpose.

## Decision

The contract is an **OpenAPI 3.1 document committed in the repository**.

- The frontend's request and response types are **generated from that file** at build time. No API
  type is hand-written in the frontend.
- The document the server actually serves is **diffed against the committed file in CI**, and any
  difference fails the build.
- Operational endpoints live in the same document under their own tag, so the drift check covers the
  whole served surface.

Protocols that OpenAPI describes poorly — a websocket conversation, for instance — are not smuggled
into it; when one is introduced it gets its own committed schema alongside, under the same rule that
the committed artifact is the reviewed one.

## Consequences

- The frontend genuinely derives from the committed contract: changing an endpoint's shape without
  changing the document breaks the frontend build.
- The server side is enforced rather than generated: a server change that alters the served surface
  cannot merge unless the document changes in the same diff. The rule's purpose — no contract change
  without a visible diff — holds from both directions, by different means.
- A reviewer reads one file to see the entire served surface.
- No code generation sits between the contract and the server's endpoints, so the HTTP framework is
  used in its own shape rather than through generated scaffolding.
- OpenAPI 3.1 is JSON Schema compatible, so the discriminated and nullable shapes inspection
  surfaces need are expressible without extensions.
- Server-sent events are describable as a media type, but OpenAPI describes the *response*, not the
  event grammar, so a streaming endpoint's event shapes need care and convention. That is a known
  weak spot rather than a surprise.
- The generation step is a build dependency the frontend cannot skip, and a contract edit is a
  two-step loop (edit, regenerate) rather than one.

## Alternatives Considered

**Generate the server's endpoint stubs from the document** — **why not:** *imposes a generated layer
the HTTP framework is then used through rather than directly, for a guarantee the drift check already
provides.*

The strictest reading of "runtime behaviour derives from the committed contract": the server could not
diverge, because its handlers and the client's types would come from the same source, making drift
structurally impossible rather than detected. The cost is structural — handler signatures constrained
to what the generator emits, regeneration in every change, and merge conflicts in generated code.

**Derive the document from the server's code at build time and commit the output** — **why not:**
*inverts the direction of authority: a breaking change appears in the diff as an accomplished fact
rather than a proposal to review.*

Practically attractive — annotations live next to handlers, the document is never stale, nothing to
keep in sync by hand. But it makes the contract a consequence of the code, which is precisely the
"contract that only exists at runtime" the rule was written against, and it leaves the frontend no
independent source: both sides would derive from the server, so the contract could not be the
agreement between them.

**gRPC with Protocol Buffers** — **why not:** *browsers cannot speak it natively, so it needs a
translating proxy introduced solely to serve a client that speaks HTTP and JSON perfectly well — and
it generates server code, inheriting the objection above.*

The strongest contract technology on offer: a schema language designed for the job, generated clients
and servers, first-class bidirectional streaming — which directly addresses the weakest part of the
chosen option — and evolution rules that make compatibility a property of the format rather than of
discipline. If bidirectional streaming between frontend and hub were the dominant traffic, this would
likely win. It also replaces an ecosystem both sides already speak with one neither does.

**TypeSpec, or hand-written JSON Schema, as the authored source** — **why not:** *adds a compile step
whose output is what gets consumed, so either two artifacts are under review or the drift check
compares against something the server has never seen.*

TypeSpec is a better authoring experience than raw OpenAPI — concise, composable, strongly typed, and
it emits OpenAPI — so for a large or rapidly changing surface it would reduce real friction. For a
surface of this size, authoring OpenAPI directly keeps exactly one artifact under review.

**GraphQL** — **why not:** *its strength is client-shaped projections, which these surfaces do not
want, and its schema is introspected from the running server — the contract-as-consequence problem
again, over a heavier runtime.*

Solves over- and under-fetching, ships an introspectable schema that is by construction the contract,
and has mature client tooling. The surfaces here want whole records, and the cost is a resolver
layer, a query-complexity concern, and caching semantics HTTP provides for free.
