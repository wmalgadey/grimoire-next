# ADR-0009: How agent tool grants are expressed and enforced

## Status

Accepted

## Context

An agent's **capability** is the set of actions it can take. The constitution makes capability
deny-by-default — an agent receives only what is explicitly granted for that dispatch — requires the
granted set to be recorded in the artifact the dispatch produces (II.3), and forbids offering any
tool whose external side effects are irreversible (II.4).

**Which** tools a given agent receives is specification: it is a property of what that agent is for,
it is stated in the requirements that describe it, and it changes when the agent's purpose changes.
Recording it as an architecture decision would duplicate the specification and would mean that
changing what an agent does edits an architecture record. What is architecture, and what no
specification states, is the **mechanism**: how a grant is expressed to the model, how everything
not granted is made unreachable, where an attempted violation is recorded, and where the boundary of
a permitted action is actually checked.

The forces are durable, and they get stronger as the system grows:

- **Several kinds of agent.** Read-only agents, proposing agents, agents driven conversationally. Each
  has a different grant. The mechanism must express any of them without each one arriving with its
  own enforcement path — otherwise capability control fragments exactly as the number of agents
  grows.
- **Agent frameworks are permissive by default.** A harness library ships a broad built-in tool
  surface — shell, filesystem, network fetch, sub-agents — because most consumers want it. A system
  that wants deny-by-default must actively remove that surface; it cannot rely on not enabling it.
- **A refusal is evidence, not just a block.** The constitution makes artifacts the observability
  surface and the correction loop's input. An attempted action outside the grant is one of the more
  informative things an operator can learn about a run, so the mechanism has to *record* refusals,
  not merely prevent them.
- **The grant must be known before the agent acts**, because an artifact that records capability after
  the fact cannot be used to judge what the agent was permitted to do.

## Decision

A grant is a **set of in-process tools exposed through the harness's tool protocol, deny-by-default,
enforced in four layers**:

1. **Granted tools are defined in-process** and registered with the agent's tool server, so the model
   sees exactly the named set and each handler runs inside the harness with direct access to the
   run's record.
2. **Everything else is removed from the request**, by naming each built-in tool in the framework's
   deny list so its definition never reaches the model. A wildcard deny is not used, because it
   removes the granted set with everything else.
3. **A pre-invocation hook observes, records and decides every call**, before any allow rule,
   permission mode or other evaluation step. This is the only position that sees every attempt,
   which is what makes refusals recordable rather than merely blocked.
4. **Each handler contains its own target**, resolving any path it is given against its permitted
   root and refusing anything that resolves outside it — covering traversal, absolute paths, and
   symbolic links planted in content an earlier run wrote.

The permission mode denies anything that would otherwise prompt, since an unattended run has no one
to ask. No configuration is loaded from the filesystem that could add capability. The granted set is
recorded on the artifact before the first model call.

## Consequences

- An agent's effective capability is exactly what it was granted and nothing the framework ships by
  default. Deny-by-default is a property of the mechanism rather than of anyone's vigilance.
- **A new kind of agent changes no architecture record**: it states its grant in its own
  specification and instantiates this mechanism. This record changes only if the mechanism does.
- Attempted actions outside the grant appear on the artifact as recorded, refused calls with a
  reason, so "what did this agent try to reach for?" is answerable from the surface an operator
  already reads.
- Because unavailable tools are removed rather than merely denied, the model spends no context on
  capabilities it cannot use and cannot form a plan that depends on them.
- Handlers are domain-shaped, so the recorded target of a call is meaningful to a reader rather than
  being a filesystem path.
- **Enumerating the framework's built-ins by name is standing maintenance**: a framework upgrade can
  add a tool the list does not know. Layer 3 is the backstop — anything not in the granted set is
  refused and recorded — and the containment tests drive an unknown tool deliberately to keep that
  backstop honest.
- Relaxing any layer, or granting a tool that reaches the host, supersedes this record.

## Alternatives Considered

**Express capability through instructions or skills rather than through the grant** — **why not:** *an
instruction is a request and a grant is a guarantee; capability that can be argued out of is not
capability control.*

The most tempting alternative because it is the cheapest: no mechanism at all, editable without a
deploy, and it fits a system that already puts judgment in instruction files. But "do not use the
shell" is advice a model under pressure — or under content that arrived from outside — may not
follow, while removing the shell is a fact it cannot reason around. This is also why
filesystem-loaded configuration is disabled: skills and settings discovered from disk are this
alternative arriving through the back door, adding capability without passing through the mechanism
or appearing in the record.

**Grant the framework's built-in file tools, scoped by path rules** — **why not:** *their permission
semantics are broader than the domain operation intended, and what gets recorded is a filesystem
operation rather than a domain action.*

Substantially less code: the tools exist, they are well tested, and the framework's path-scoping can
confine them to a directory. For a system whose agents genuinely operate on files as files this
would be right. Here the granted capability becomes "write files under this path" rather than the
narrower thing actually intended — and that difference is where a bug lives — while the artifact
becomes a worse record for the operator who has to read it.

**Use the framework's permission callback as the guardrail** — **why not:** *a tool auto-approved by
an allow rule never reaches the callback, so the guardrail is silently bypassed for precisely the
tools that are granted.*

The obvious place to put a decision function, and it would centralise the logic. The framework
documents this shadowing and warns about it explicitly. A control that is skipped for the common case
is worse than no control, because it looks like one.

**Deny everything with a wildcard and allow the granted set back** — **why not:** *the wildcard
matches the granted tools too, so it removes the grant along with everything else.*

Reads as the most obviously correct expression of deny-by-default and would remove the maintenance of
enumerating built-ins. The enumeration is the cost of the framework's rule semantics, not a choice.

**Rely on the permission mode alone, with no hook** — **why not:** *a denial that reaches no record is
invisible, so the artifact cannot distinguish an agent that behaved from one that repeatedly tried
not to.*

Correct in outcome — anything not pre-approved is denied — and the simplest configuration that
achieves containment. It gives up the half of the requirement that is about evidence, and containment
without observability is half the control.

**Run tools as external tool servers over a transport** — **why not:** *handlers must record into the
run's ordered record and share its event stream; across a transport that is a second protocol and a
second lifecycle, for tools with no consumer outside this harness.*

The conventional deployment shape, with real benefits: tools become independently versioned and
reusable across agents, and a tool server can be isolated further than the agent process. It is the
right move when a tool needs sharing across agents or isolation stronger than the agent process
itself.
