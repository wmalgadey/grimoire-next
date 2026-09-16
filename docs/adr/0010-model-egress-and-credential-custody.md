# ADR-0010: Model egress and credential custody

## Status

Accepted

## Context

The system makes outbound network calls of two kinds, and they have opposite risk profiles.

- **Model traffic** goes to exactly one upstream and carries a credential. It originates from the
  process that runs model-directed logic — the process the trust boundary exists to contain.
- **Content retrieval** goes to destinations a user supplies, so it must be able to reach the open
  internet, and it must carry no credential at all. Because the destination is attacker-influenced,
  it is a server-side request forgery surface pointing at loopback, at link-local metadata services,
  and at anything else reachable from inside the deployment.

Collapsing these into one egress policy means choosing which to get wrong: permit the open internet
and the credential-bearing path is unconstrained, or restrict to one upstream and content retrieval
stops working.

The constitution makes capability deny-by-default (II.3) and requires a trust boundary around agent
execution whose holding is provable (II.5). A credential sitting inside that boundary is a credential
that any defect in the boundary yields — and unlike the effects the boundary otherwise contains, a
leaked credential is not revertible.

Forward-looking forces, which matter more than the current traffic shape:

- **Model traffic grows in volume and in kind.** More agent kinds, longer runs, and streamed
  responses. Anything that mediates it must pass streaming through without buffering it into
  uselessness.
- **Cost, rate and attribution become operational questions.** A single chokepoint for model traffic
  is where per-run accounting, quotas and failover can live later without touching the components
  that make the calls.
- **Credentials rotate.** A deployment may authenticate with a long-lived key today and a
  short-lived, refreshed credential tomorrow. Where the credential lives determines whether that is
  a configuration change or a code change in the wrong component.

## Decision

**No process that executes model-directed logic holds an upstream credential, and none connects
directly to the internet.** All outbound traffic leaves through a single mediating proxy, which is
the credential custodian, on two routes with separate policies:

- the **model route**, which strips whatever token the caller presented, injects the real upstream
  credential and forwards to one configured upstream;
- the **content route**, which forwards to a per-request destination after a policy check that
  rejects non-`http(s)` schemes and loopback, link-local and private targets, and attaches no
  credential.

Callers are configured entirely through environment: the model endpoint's base URL, an **opaque
internal token** that is meaningless outside the deployment, and a per-run correlation header so the
proxy's access log joins to an artifact. The agent framework's **non-essential background traffic is
disabled**, because it is documented to bypass the configured endpoint and would otherwise appear as
denied connections under a default-deny policy.

Content retrieval additionally applies the same destination policy **in-process**, so the protection
exists when the system runs without a proxy in front of it.

What the proxy is implemented with is a separate decision.

## Consequences

- A compromise of the process that runs agents yields an internal token valid against one proxy on
  one route from inside one network — not a credential that is useful anywhere else.
- The container can run under a default-deny egress policy with exactly one permitted destination,
  which makes "nothing else leaves" an assertion a test can make rather than a claim about
  configuration.
- The two policies stay genuinely separate, so content retrieval can reach arbitrary hosts without
  that ever being a path the credential-bearing traffic could take.
- Credential rotation, rate limiting, cost accounting and upstream failover all become changes at the
  chokepoint rather than in the components that make calls.
- The identity the deployment authenticates with is decided in one place. Which credential that is
  has licensing consequences the operator must check — an agent framework's terms may permit an
  organisational API credential while restricting a personal subscription identity for automated
  or third-party use — and this design deliberately confines that decision to the proxy rather than
  spreading it through the system.
- **Two hops instead of one**, and the proxy is now in the failure path. A distinct signal for
  "upstream unreachable" and a readiness check that exercises the path exist so that a broken egress
  is distinguishable from a failing agent.
- Disabling background traffic is not optional: without it, a default-deny policy produces a stream
  of denied connections that masks real ones.
- In-process destination policy duplicates the proxy's. That is deliberate defence in depth: the
  in-process check protects a developer running without a proxy, and the proxy protects against a bug
  in the in-process check — and DNS rebinding defeats a pre-connection check but not the proxy, which
  sees the actual connection.

## Alternatives Considered

**Keep the credential in the calling process and connect directly to the upstream** — **why not:**
*places a non-revertible secret inside the one process designed on the assumption that it may
misbehave, and forfeits any testable statement about what leaves.*

The simplest thing that works: no extra component, no extra hop, no additional failure mode, lowest
latency. Most systems do this and are fine. Here the credential would sit inside the process running
model-directed logic over content that arrived from outside — and while every other effect that
process can have is revertible by design, a leaked credential is not. It also requires broad egress
from the container.

**One egress policy for everything, via conventional proxy environment variables** — **why not:** *one
policy cannot be both "exactly one upstream, credential injected" and "arbitrary user-supplied
destinations, no credential".*

Substantially cheaper: `HTTPS_PROXY` is understood by nearly every HTTP client with no code change,
giving a single chokepoint immediately. It fails on the requirement that created the problem.
Splitting by route requires the caller to address the proxy explicitly.

**Network-layer allowlisting with no proxy** — **why not:** *permitting is not mediating — the
credential stays in the calling process, nothing can be injected or stripped, and per-request policy
for user-supplied destinations cannot be expressed at the network layer.*

Restricts egress using only the container runtime's own facilities, with no component to build,
operate or patch, and it cannot be bypassed by an application-level mistake; for the "nothing else
leaves" property it is arguably better than a proxy. It also leaves nowhere for attribution, rate
control or rotation to live later. It composes well *with* the decision above, which is how it is
used.

**A sidecar proxy per component rather than one shared mediator** — **why not:** *while components
share a network namespace it adds deployables without adding a boundary — the separation would be by
configuration, which the single proxy already provides.*

Better isolation: each component gets only the policy it needs, and a compromise of one cannot reach
the other's route. This is the natural shape once components have their own network namespaces, and
it should be adopted then.

**Have the calling process fetch a short-lived credential from a secret store or helper** — **why not:** *a short-lived credential is still a credential inside the boundary, it still requires broad
egress, and it leaves no mediation point for the content route, attribution or rate control.*

Keeps a long-lived secret out of the process while leaving the connection direct, and agent
frameworks support a credential-helper hook for exactly this — a genuine improvement over a static
key in the process, and the right pattern if direct connection is ever required. The helper remains
the mechanism to reach for if the proxy ever issues short-lived tokens to callers.
