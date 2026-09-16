# ADR-0002: Frontend framework

## Status

Accepted

## Context

The browser is where this system's operator loop closes. The constitution makes task artifacts an
observability surface (IV.5) and makes observe → edit instructions → re-dispatch the only correction
mechanism the system has (IV, rationale). Whatever renders in the browser is therefore not a
convenience layer over an API; it is the control surface.

Two kinds of surface follow from that, and the choice has to hold for both:

- **Inspection.** Structured records rendered densely and unambiguously: ordered call records with
  per-item outcomes, diffs, state, identifiers. Read-mostly, navigation-driven, correctness of
  rendering matters more than interaction.
- **Live and interactive.** A system whose work is performed by long-running agents will want to
  watch a run as it happens rather than reload after it ends, and directing agents conversationally
  is a natural surface for it. Both are high-frequency incremental updates — a token arriving, a
  call appended to a growing list — pushed by the server into a view that is already open, with
  local interaction state (drafts, expansion, scroll position) that must survive them. Aggregate
  dashboards are a third, with many independent small values updating on their own cadences.

The second kind is the harder constraint and the one that discriminates between candidates. A
framework that renders inspection surfaces well is easy to find; one that also absorbs sustained
high-frequency partial updates without the update cost becoming the thing you design around is not.
Committing to a technology that makes the first easy and the second a rewrite would be choosing for
the state the system is in rather than the state it is heading for.

Further constraints:

- The frontend and hub communicate **only** through a contract versioned in the repository, with
  runtime behaviour deriving from the committed contract (V.5) — so types are generated, never
  hand-written.
- Frameworks must not be wrapped (VII.1); no indirection without a caller that needs it (VII.3).
- Each additional runtime in the deployment is another thing to build, patch and keep in step.

## Decision

The frontend is **Svelte 5 with SvelteKit**, built with `@sveltejs/adapter-static` and served by the
hub, so there is one origin and no CORS. Request and response types are generated from the committed
API contract. No client-side state library until something needs one.

**How many surfaces there are and what each shows is specification, not part of this decision.**

Serving a static build does not constrain interactivity: how assets are delivered is independent of
whether the page holds an open connection. A page served this way subscribes to server-sent events
or a websocket against the hub exactly as a server-rendered one would. What the static adapter gives
up is server-side rendering of first paint, not live updating.

## Consequences

- Svelte compiles components to direct DOM operations and, with runes, tracks dependencies at the
  granularity of individual bindings. An update touches the nodes that actually changed, with no
  per-update reconciliation pass over a component tree. That property is mildly useful for
  inspection surfaces and is the decisive one for streaming text and for lists that grow while
  visible — the cost of an update scales with what changed rather than with what is on screen.
- No virtual-DOM runtime is shipped, so the browser's work stays close to the page's actual
  requirement.
- SvelteKit supplies routing and load functions, used through its own API rather than assembled.
- Generated types make a contract change the frontend has not absorbed a build error rather than a
  runtime surprise.
- **The ecosystem is smaller than React's**, and this is the real cost, concentrated exactly where
  the system is heading: for chat and dashboard surfaces there are fewer mature off-the-shelf
  components — streaming markdown renderers, virtualised message lists, charting — so more of that
  is written here. Accepted because these are contained components against data shapes the contract
  already defines, not architecture; but if a conversational surface became the system's primary
  product rather than one of its surfaces, the balance would shift and this record should be
  revisited.
- Server-side rendering is given up by the static adapter. For an internal tool with no SEO surface
  and no first-paint budget worth defending, that costs nothing; recovering it is an adapter change,
  not a rewrite, and it is orthogonal to live updating.

## Alternatives Considered

**React, with a router or Next.js** — **why not:** *pays for every update by re-running components
and reconciling a tree, which becomes standing design attention exactly where this system is
heading.*

The largest ecosystem of the candidates, and the advantage is concentrated precisely in the surfaces
this system is growing toward: streaming-chat components, virtualised lists, diff viewers and
charting are mature and numerous, so less of the interactive surface would be built by hand. The
cost is in how updates are paid for. Keeping reconciliation acceptable under sustained
high-frequency updates is the work of memoisation, dependency arrays, key discipline and effect
hygiene — not incidental details but the idiom the framework is used through. For token streams and
continuously growing lists that is ongoing attention spent on the update model rather than on the
surface. React's compiler narrows the gap, and this is the alternative to reconsider if the
ecosystem advantage ever outweighs the update model, which a chat-first product could make true.

**Solid** — **why not:** *a near-tie on the deciding axis, with less surrounding infrastructure.*

Technically the closest to Svelte: compiled, signal-based, fine-grained updates, and it would satisfy
the streaming requirement equally well. Svelte carries a first-party application framework whose
static output drops behind the hub with no second runtime, a larger component and documentation
base, and a broader contributor pool. Between near-equivalents the one with more surrounding
infrastructure is the lower-risk commitment. There is no argument here that Solid is worse, only that
it is not better on anything that decides this.

**Vue** — **why not:** *still reconciles a virtual DOM, so it sits between React and Svelte on the
one axis that decides this, with nothing offsetting.*

Mature, a larger ecosystem than Svelte's, single-file components with comparable ergonomics, a
proxy-based fine-grained reactivity system that handles incremental updates well, and Nuxt as a
credible SvelteKit counterpart. It simply brings no advantage that offsets its position on the
update-cost axis for these surfaces.

**Server-rendered HTML from the hub with htmx and server-sent events** — **why not:** *collapses the
frontend-to-hub contract boundary the constitution requires, and degrades as client-side state
grows.*

The strongest simplicity argument of any option, and — unlike a plain server-rendered UI — genuinely
capable of live updates: htmx's SSE extension swaps server-pushed fragments into an open page, which
covers watching work progress with no client framework at all. No second toolchain, no build step, no
generated client. But server-rendered fragments leave the committed API either unused or duplicated
rather than honouring it as the agreement between the two sides. And the model degrades exactly where
the system is heading: a conversational surface has substantial client-side state — an in-progress
draft, optimistic echo, scroll anchoring, per-message interaction — and fragment swapping either
destroys that state or accumulates the code to preserve it, which is a component framework arrived at
by increments and without a framework's guarantees.

**Svelte without SvelteKit, plus a standalone router** — **why not:** *hand-assembles the framework's
core responsibilities.*

Fewer conventions to learn and fewer dependencies. What has to be assembled by hand — routing,
per-route data loading, the static build pipeline — is precisely the framework's own job, and
hand-assembling it is the shape both the no-wrapping and the no-speculative-layers rules push
against.
