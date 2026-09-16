# ADR-0007: Host trust boundary

## Status

Accepted

## Context

This system executes agents whose actions are decided by a language model, under instructions that
are edited as ordinary text, over content that arrives from outside — submitted by a user and, once
written, read back by later runs. Every one of those three inputs is a channel an instruction can
arrive through that nobody reviewed as code.

The constitution requires a host trust boundary between agent execution and the host, and requires a
CI test proving it holds **independent of instruction-file content, task input, and user-supplied
content** (II.5). Which kind of boundary — process, container, or network — is left open (II.6).

The forces that decide it are about trajectory, not about any one agent's current reach:

- **Capability grows.** An agent's tool set is deny-by-default and narrow, but the system is built to
  run several kinds of agent and the point of the tool mechanism is that a grant can be widened when
  a capability needs it. A boundary chosen on the assumption that agents will never hold a
  host-reaching tool would have to be rebuilt the first time one is granted — at exactly the moment
  the boundary matters most and the change is least welcome.
- **Containment is per-run, but isolation must be per-host.** A run's own effects are constrained by
  what it was granted; what constrains a *defect* in that grant, or in a tool handler, is the layer
  underneath it.
- **The deployment target is containers**, so container isolation is not a new operational
  dependency. The alternative would be declining an available boundary.
- **The proof must be cheap enough to run on every change.** A boundary that can only be exercised in
  a specialised environment gets proven rarely, which is the same as not being proven.

## Decision

A **container boundary**, with a **per-run process boundary inside it**.

The container runs as a **non-root user** with a **read-only root filesystem**, writable only on its
mounted data volumes and a small `tmpfs`, with no added capabilities and **no network egress except
one mediating endpoint**.

Inside it, each run executes in a dedicated child process with:

- its working directory pinned to the one directory tree the run is permitted to touch;
- its environment **fully replaced** by a narrow allowlist, carrying no credential that is valuable
  outside the deployment;
- no configuration loaded from the host or the repository that could add capability;
- a hard kill on the run's elapsed deadline;
- deterministic cleanup of the run's effects on every exit path, including the paths taken by a
  process that dies.

Proof is layered to match: the process-and-capability half is exercised by tests that drive
adversarial instruction text, adversarial task input and adversarial stored content at the boundary
with canaries outside it, and run anywhere; the container half is exercised against the built image
on a deny-all network and runs in CI.

## Consequences

- Separation between agent execution and the host is enforced by the kernel rather than by the
  absence of a dangerous tool, so widening a grant later is a bounded change rather than a
  re-architecture.
- A read-only root filesystem means an escape has nowhere to persist: the only writable paths hold
  data that is either revertible or is itself the record of what happened.
- Replacing rather than inheriting the environment makes credential and host-context scrubbing a
  property of how a process is started, not something a code path has to remember.
- A per-run temporary home directory means agent-adjacent tooling cannot read or write an operator's
  own configuration even if it acquires filesystem reach.
- The cheap half of the proof runs on a developer machine; the container half needs a container
  runtime, so it runs in CI on every change and is skipped locally with a named reason. The gate
  still runs on the change.
- **Stated rather than smoothed over**: the orchestrator and the agent process share a container and
  therefore a network namespace. Restricting the agent to one endpoint is a property of its
  configuration and of it having no network-capable tool, not of a per-container network policy.
- **Successor**: giving the agent process its own container and its own network namespace is the next
  step, and the trigger is a deployment target that enforces per-container network policy. The
  process protocol is deliberately shaped to survive that move — its transport changes, its content
  does not.
- Granting a tool with host reach supersedes this record. A container raises the bar; it does not
  remove the requirement to decide again.

## Alternatives Considered

**A process boundary alone — no container** — **why not:** *isolates one path check and one
environment allowlist deep, which is adequate only while agents hold narrowly scoped tools — and the
system is built expecting that to change.*

Materially cheaper, and with a real benefit: the entire boundary becomes testable on any machine with
no container runtime, so the proof runs everywhere rather than only in CI — not a small thing for a
rule whose point is continuous demonstration. But a child process with a scrubbed environment runs as
the same user, in the same filesystem namespace, with the same network reach as everything else on
the host. Choosing it would buy a cheaper proof of a weaker property.

**Giving the agent process its own container, separate from the orchestrator** — **why not:**
*launching a sibling container requires handing the orchestrator a container-runtime socket — a
host-escape primitive given to the component whose job is to contain one.*

The architecturally correct end state, and for a concrete reason: a separate network namespace turns
"the agent may only reach one endpoint" from a configuration property into a policy the agent cannot
influence, removing the shared-namespace caveat entirely. The alternative shape — a long-lived
agent-side service pulling work over an internal API with a shared volume — avoids the socket but
introduces an internal protocol, a second deployable and a shared-state question with no caller
needing them until the network policy exists to justify them.

**A fresh container per run** — **why not:** *creating them from inside the deployment needs the same
runtime socket, and it adds container startup latency to every run.*

The strongest per-run isolation available: a run cannot leave residue because its entire filesystem
and process namespace is discarded, and the startup-cleanup path disappears. It would be attractive
if per-run containers could be created without privilege.

**A microVM or sandboxed runtime (Firecracker, gVisor, Kata)** — **why not:** *defends against
arbitrary code execution, which is not the threat model while agents hold a small set of narrowly
scoped tools — and it constrains where the system can run and how easily the boundary is exercised.*

Genuinely stronger isolation: a separate kernel or an intercepted syscall surface defeats classes of
escape that namespaces do not, and for untrusted code execution this is the honest answer. It is the
right escalation if agents are ever granted general code execution.

**A separate OS user for run processes** — **why not:** *it is hardening within a boundary, not a
boundary: it does not constrain the network, does not make the root filesystem immutable, and is
configured where CI cannot assert it.*

Cheap, effective against a whole class of accidental cross-reach, and entirely compatible with the
decision above — worth adding alongside it rather than instead of it.

**No boundary, relying on the tool grant alone** — **why not:** *the grant is enforced by code that
can have defects, and without a boundary the credentials and host environment of the whole system sit
inside the process running model-directed logic.*

The tool grant is the primary control and it is strong: an agent cannot use a capability it was never
given. This is an argument that a second layer is redundant, and the constitution requires one
regardless.
