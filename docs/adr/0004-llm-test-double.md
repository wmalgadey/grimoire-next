# ADR-0004: Where the LLM is replaced for tests

## Status

Accepted

## Context

**This is not a decision about testing the model.** The constitution makes agent judgment
permanently ungateable in CI: no test asserts that the model produced particular output, and no
build fails because model output changed (III.4). Where a capability's value depends on judgment,
the constitution replaces assertions with operator-observable criteria instead (III.5).

What must be tested is the **control mechanism** — the orchestrator and the agent harness — and the
standard there is high: those contracts are tested
exhaustively and hermetically, with no third-party network, no shared mutable state, deterministic
ordering, safe in parallel (III.3). The contracts in question are things like: each tool result is
fed back and the loop continues; every call is recorded in order including refusals; a run that
changes content produces exactly one commit; a run that fails produces none and leaves prior state
untouched.

Exercising any of those requires the model to emit a **known** sequence of tool calls. Against a real
endpoint there is no such thing, and a real endpoint is excluded by hermeticity in any case. This is
precisely why the constitution carves out exactly one exception to its "use real infrastructure"
rule: the LLM is the single sanctioned test double, "replaced behind the model port and driven by
scripted responses" (III.2). Everything else — filesystem, git, child processes, HTTP hosting —
stays real.

So the double is mandated. The open question is **where the seam sits**, and that question has teeth
because the agent loop itself is supplied by a harness library rather than written here. A double
placed at the port
replaces the loop along with the model; a double placed at the wire leaves the loop running. The
harness contracts listed above are largely contracts *about that loop*.

## Decision

The LLM is replaced **at the wire**. A scripted, Anthropic-compatible HTTP server serves canned
assistant responses, and the runner is pointed at it by base-URL configuration — the same
configuration point a proxy or gateway occupies in production. Non-essential background traffic is
disabled so the stub is not asked for endpoints it does not implement.

The server is one implementation, spawned as a child process by the test suites that need it and fed
a per-test script. It speaks the streaming form of the protocol as well as the buffered one, so
surfaces that consume incremental output are testable on the same seam. The model port keeps its
single adapter.

## Consequences

- Every test exercises the real loop, the real child process, the real in-process tool server and
  the real permission evaluation. The only thing supplied by us is what the model says.
- A contract like "N tool calls across N iterations within one run" becomes an assertion about the
  shipped mechanism rather than about a stand-in for it.
- The stub is a real HTTP server on an ephemeral port, so it satisfies the real-infrastructure rule
  rather than working around it, and it is hermetic: no third-party network, no shared state.
- One implementation serves every suite that needs scripted responses, in whatever language, because
  the seam is a protocol rather than a type.
- **The model port therefore has a single adapter**, which reads at first glance like the
  single-implementation interface the constitution bars (V.4). It is not: an external system sits
  behind it and that system genuinely is replaced. The rule exists to stop "port" becoming a habit of
  wrapping things, and the constitution itself names the model port as the one this system has. The
  tension is real and is recorded here rather than resolved by deleting the port.
- The stub must track the subset of the wire protocol the SDK actually uses, and an SDK upgrade can
  break it. That is the running cost of this choice, paid at the one seam where our code meets the
  thing we cannot pin.

## Alternatives Considered

**A second adapter implementing the model port, returning scripted responses in-process** — **why not:** *substitutes the loop along with the model, so contracts about the loop would be asserted
against a stand-in written to satisfy them.*

The most literal reading of the constitutional text, and the cheapest to build and maintain: no HTTP
server, no wire protocol to track, no extra process, and a library upgrade cannot break it. If the
loop were ours, this would be right. Because the loop is the library's, the suite would stay green
while the shipped mechanism changed underneath it — the exact failure the rule that every assertion
must be able to fail because of a change to our own code is aimed at.

**Recording real API interactions and replaying them** — **why not:** *hermetic only between
refreshes, and writing a new control test would require eliciting the behaviour from a real model
rather than stating it.*

Gives scripted determinism with genuinely representative payloads, including message shapes a
hand-written stub will get subtly wrong, and it tracks the provider's protocol automatically because
the fixtures came from it. But producing or refreshing a cassette needs the third-party network and a
credential, and a stale cassette silently tests a protocol the provider no longer speaks.

**Doubling on the orchestrator side, by never spawning the agent process** — **why not:** *removes
the child process, the tools, the permission evaluation and the containment surface from every test.*

Makes orchestrator-level tests fast and trivially deterministic, and would be appropriate if the
orchestrator's own logic were the only thing under test. What remains is assertions about a protocol
the orchestrator is talking to itself.

**Running a small local model instead of a stub** — **why not:** *does not produce a known tool-call
sequence, which is the entire requirement.*

Genuinely real inference with no third-party network and no protocol to reimplement. It fails
outright: a real model — however small — reintroduces the nondeterminism the double exists to
remove, while adding a model runtime to CI.

**No control-level loop testing; verify against the real API periodically instead** — **why not:**
*cannot be hermetic or run on every change, and checks the most expensive mechanisms least often and
least precisely.*

Sidesteps the stub entirely and tests the true system end to end. It cannot satisfy the requirement
that control contracts be tested exhaustively and hermetically on every change, and it inverts the
risk. A periodic real-endpoint smoke test is a reasonable *addition* outside CI; it is not a
substitute.
