# Phase 0 Research: Source Ingest via Agent Run

**Feature**: `001-source-ingest-agent-run` | **Date**: 2026-09-16 | **Plan**: [plan.md](./plan.md)

Every `NEEDS CLARIFICATION` raised while filling Technical Context is resolved below.
Decisions of a kind the constitution requires an ADR for (VI.4) carry a link to that ADR;
the ADR is the record, this file is the reasoning trail that produced it.

---

## R1 — Hub language and runtime

**Decision**: **C# 14 on .NET 10 (LTS)**, using ASP.NET Core **Minimal APIs**. See [ADR-0001](../../docs/adr/0001-hub-runtime-and-http-surface.md).

**Rationale**: Directed by the user, and the right shape independently: .NET 10 is the current LTS,
so security updates continue to arrive; C# 14 is its language level. Minimal APIs keep four
endpoints plus two operations endpoints as a flat map with no controller layer, which also keeps a
technical-layer directory out of the first level (constitution V.1). `Microsoft.AspNetCore.OpenApi`
is in-box, so the contract drift test (R11) needs no extra toolchain, and the runtime containerises
cleanly — `mcr.microsoft.com/dotnet/aspnet:10.0` as the base layer, non-root by default.

**Alternatives considered**: .NET 6 / C# 10 — an earlier reading of "C# 10 minimal api", since
Minimal APIs shipped with that pair. Ruled out by the user: .NET 6 left support in November 2024.
.NET 8 LTS — supported and viable, rejected because nothing pulls us back from the current LTS.
MVC controllers — rejected: the surface is six endpoints and controllers invite the technical-layer
shape V.1 pushes against.

**Resolved**: this was the plan's one open assumption in the previous revision. It is now a
directed decision.

---

## R2 — Does the agent loop requirement (FR-007) survive using the Claude Agent SDK?

**Decision**: Yes. The run is `query()` from **`@anthropic-ai/claude-agent-sdk`**, which is the
Claude Code harness as a library: it owns the agent loop, feeding each tool result back to the
model and continuing until the model stops or a turn limit is reached. See [ADR-0003](../../docs/adr/0003-agent-runner-runtime-and-sdk.md).

**Rationale**: FR-007 explicitly disqualifies "a single model call whose output the hub then
applies to the wiki". The SDK is the opposite of that by construction — `query()` returns an
async generator that yields assistant messages, tool-use messages, and tool-result messages
across multiple iterations, with the number of iterations decided by the model (FR-008). Using
the SDK also means the loop under test in CI is the real one, not a reimplementation.

Key API facts confirmed against the current reference (`code.claude.com/docs/en/agent-sdk/typescript`,
`/permissions`, `/modifying-system-prompts`) rather than recalled:

- Package `@anthropic-ai/claude-agent-sdk`; entry point `query({ prompt, options })`.
- `systemPrompt` is a top-level `Options` field and accepts a **custom string**, which replaces
  the default entirely. The `claude_code` preset is *not* used — its coding-agent guidance and
  environment context would be instruction text living outside the instruction file, which
  constitution I.1 forbids.
- `settingSources: []` loads no user/project settings, no `CLAUDE.md`, no output styles, no
  skills. Required: anything loaded from the filesystem would be un-versioned judgment reaching
  the agent behind the instruction loader's back.
- `env` **replaces** `process.env` rather than merging — which is exactly the scrubbing primitive
  the trust boundary needs (see R6).
- `maxTurns` bounds agentic turns; the elapsed-time half of the run limit is the hub killing the
  child process.

**Alternatives considered**: the Messages API tool-use loop written by hand via `@anthropic-ai/sdk`
(rejected: we would own and have to test a loop the SDK already provides, and the user asked for
the Agent SDK); Managed Agents (rejected: Anthropic-hosted sandbox and session state is a second
system of record for runs, and the constitution wants this system owning lifecycle and artifacts);
driving the `claude` CLI as a subprocess with `-p --output-format json` (rejected: the SDK gives
typed in-process MCP tools and a `PreToolUse` hook, which the CLI path would force us to
reconstruct).

---

## R3 — How is the two-tool grant enforced so that FR-010 and FR-011 actually hold?

**Decision**: A grant is a set of in-process MCP tools, deny-by-default, enforced in four layers,
all inside `src/agentrun/`. See [ADR-0009](../../docs/adr/0009-tool-grant-enforcement.md) — which
decides the *mechanism*; *which* tools this feature grants is its spec's FR-010.

1. **The granted pair exists as in-process MCP tools.** `createSdkMcpServer({ name: "wiki", tools: [readPage, writePage] })`
   with `tool(name, description, zodShape, handler)`; the model sees them as
   `mcp__wiki__read_page` and `mcp__wiki__write_page`.
2. **Built-ins are removed from the request.** Each built-in tool name goes in `disallowedTools`.
   A bare-name deny rule removes the tool definition from the request, so the model never sees
   `Bash`, `Read`, `Write`, `Edit`, `Glob`, `Grep`, `WebSearch`, `WebFetch`, `Task`/`Agent`,
   `TodoWrite`, `AskUserQuestion`, or `NotebookEdit` at all. The wildcard `"*"` is *not* used:
   documented behaviour is that it matches every tool, which would strip the wiki pair too.
3. **A `PreToolUse` hook records and decides every call.** The documented evaluation order runs
   hooks *first*, before deny rules, ask rules, permission mode, and allow rules, and a hook deny
   applies even in `bypassPermissions`. This is the only place in the SDK where every call is
   observable, which matters because the spec requires refusals to be *recorded as tool calls*
   (FR-011, FR-021). It is also why `canUseTool` is not used: the docs are explicit that a bare
   `allowedTools` entry auto-approves before the callback is consulted (the SDK even emits
   `CLAUDE_SDK_CAN_USE_TOOL_SHADOWED`), so a guardrail placed there would be silently skipped for
   exactly the two tools we grant.
4. **The write handler contains its own target.** `realpath`-resolve the requested page path
   against the wiki repository root and refuse anything that resolves outside it — covering `../`
   traversal, absolute paths, and symlinks planted in wiki content by an earlier run.

`permissionMode: "dontAsk"` is set so that anything reaching the permission step is denied rather
than left waiting on a prompt that a headless run can never answer.

**Alternatives considered**: `canUseTool` as the guardrail (rejected: documented to be shadowed by
`allowedTools`); `disallowedTools: ["*"]` (rejected: removes the granted pair); relying on
`permissionMode: "dontAsk"` alone (rejected: denies, but records nothing, and the spec wants the
refusal visible on the task view).

---

## R4 — Where does the model port live, and how is the LLM doubled (III.2)?

**Decision**: The model port lives in `src/agentrun/model/`, with one adapter over the Agent SDK.
The LLM behind it is replaced in tests by a scripted **Anthropic-compatible HTTP server**, reached
by pointing `ANTHROPIC_BASE_URL` at it. See [ADR-0004](../../docs/adr/0004-llm-test-double.md).

**Rationale**: The constitution wants the LLM replaced "behind the model port and driven by
scripted responses" (III.2) while also wanting real infrastructure everywhere else (III.2) and
control contracts tested exhaustively (III.3). Those pull in opposite directions here: swapping a
second *adapter class* into the port would double the SDK's agent loop away, and SC-007 —
"an agent driven by scripted responses that issues N tool calls across N iterations has all N
executed and recorded within a single run" — is precisely an assertion *about* that loop. Doubling
at the wire keeps the real loop, the real child process, the real MCP tools, and the real
permission evaluation in every test, and replaces only the thing we genuinely cannot pin.

The trade-off, recorded honestly in ADR-0004: the port then has a single adapter, which brushes
against V.4's rule against interfaces with one implementation. It is not a wrapper — an external
system is behind it and that system *is* doubled — and constitution V.2 names the model port as
the one port this system has, so the port stays.

**Alternatives considered**: a second `ScriptedModelAdapter` implementing the port in TypeScript
(rejected: reimplements the loop we are trying to test, and SC-007 would then assert against our
own reimplementation); recording/replaying real API responses (rejected: not hermetic, and III.3
forbids third-party network in control tests); mocking at the C# side by never spawning the runner
(rejected: loses the child process, the MCP tools, and the containment surface all at once).

**Note for implementation**: hermetic runs set `CLAUDE_CODE_DISABLE_EXPERIMENTAL_BETAS=1` so the
SDK does not attempt capability negotiation the stub does not implement.

---

## R5 — How does a run's write stay invisible until the commit (FR-012, FR-017)?

**Decision**: The wiki is **one ordinary git repository**, checked out on `main`. The run's `cwd` is
that repository; the agent writes there. At run end, if content changed: `git add -A` + `git commit`,
once. On failure, crash, abort, or limit: `git reset --hard HEAD && git clean -fd`, no commit — on
the run-end path, and again at startup as the backstop for a hub that died mid-run. Revert is
`git revert --no-edit <sha>` in the same working tree, and revert eligibility is a plain
`git rev-parse HEAD` comparison. See [ADR-0005](../../docs/adr/0005-wiki-repository-layout.md).

**Rationale**: Simplicity, deliberately chosen over structural containment. This is four git
commands. The alternative — a bare repository, a per-run `git worktree`, and an `update-ref`
compare-and-swap — buys one real property, that a partial write is never reachable from any ref, at
the cost of a worktree lifecycle, a third volume, an extra environment variable, bare-repo handling,
and a temporary worktree created just to perform a revert.

The property that actually matters is identical either way: nothing enters wiki history except one
commit per run, and a failed run leaves history untouched. What differs is whether a **transient
dirty working tree** can exist on disk. It can here — and it is unobservable through the product.
"The wiki" as every user-facing surface defines it is the *committed* content: the task view diffs a
commit, revert restores a commit, and no surface reads the working tree. If the runner dies the hub
cleans immediately; only a hub crash leaves the tree dirty, and in that window nothing is serving.
FR-012's "not visible in the wiki before that run's commit" therefore holds at every moment a user
could look, and SC-003's byte-identical pre-run content is restored on both cleanup paths — which is
why TS-12 kills the runner *and* the hub.

Because the hub is the only writer and runs are serialised (FR-019), a plain `rev-parse HEAD` check
covers revert eligibility (FR-024) with no compare-and-swap and no lock file. And revert needs a
working tree, which this design has — the bare-repository alternative would have had to create one.

**Alternatives considered**: **bare repository + per-run worktree + CAS `update-ref`** (the previous
revision; structural containment, rejected as disproportionate — and the one to return to if wiki
content ever has to be readable while a run is in flight); a full `git clone` per run (rejected:
copies object storage per run and adds a step to get the commit back); write to a temp directory and
copy in on success (rejected: worktree overhead without git's help, and a second write path into the
wiki, against II.2); commit on every tool write and squash at the end (rejected: violates FR-015's
exactly-one commit, and a crash would leave those commits in history); LibGit2Sharp instead of the
git CLI (rejected: a native binding whose semantics we would have to keep in step with git's, where
the CLI *is* the semantics, and III.2 already wants real child processes).

---

## R6 — What is the host trust boundary, and how is it proven (II.5, II.6)?

**Decision**: A **container boundary**, with a per-run process boundary kept inside it. See
[ADR-0007](../../docs/adr/0007-host-trust-boundary.md).

The hub container runs as a **non-root user** in a container with a **read-only root filesystem**,
writable only on its mounted volumes (wiki repository, state database) and a small
`tmpfs`. It has **no network egress except the egress proxy** (R14). Inside that container the
per-run process boundary is unchanged: a dedicated Node child process, `cwd` pinned to that run's
wiki working tree, `env` fully replaced by an allowlist, no granted tool that reaches network,
shell, or filesystem outside that tree, and a hard kill on the elapsed run limit.

**Rationale**: A process boundary alone would be defensible while the agent has no host-reaching
tool, and it keeps the whole boundary testable without a container runtime. It was the working
answer until the direction settled on containers — and once containers are the deployment target,
the stronger boundary costs nothing extra and buys genuine kernel-level separation between agent
execution and the host, which is what II.5 actually asks for. It also means the CI containment test
exercises the boundary production runs behind, rather than one production would later replace.

Proving it (II.5 demands independence from instruction content, task input, and wiki content):
TS-09 and TS-10 keep proving the process and tool boundary and run everywhere without a container
runtime; **TS-18** adds the container half against the built image — non-root, read-only root
filesystem, no egress except the configured endpoint, no upstream credential in the runner's
environment, and a blocked host surfacing as a recorded failure rather than a hang.

**The limitation, stated rather than smoothed over**: hub and runner share one container, so the
runner's confinement to the model endpoint rests on its configuration and on its having no network
tool — not on a per-container network policy. Splitting the runner into its own container with its
own network namespace is the named successor in ADR-0007, and the runner protocol is deliberately
shaped to survive that move (its transport changes from stdio to the same NDJSON over HTTP;
nothing else changes).

**Alternatives considered**: **process boundary alone** (rejected once containers became the
deployment target: it costs nothing extra now, and a process boundary leaves the runner as the
operator's own user with the host filesystem one path-check bug away); **runner in its own
container today** (rejected for this feature: it needs either a container-runtime socket in the hub,
which hands the hub a host-escape primitive and makes containment *worse*, or a worker-pull API and
shared volume that VII.3 calls speculative until the network policy exists to justify it);
**a separate OS user inside the container** (compatible, and worth adding, but it is hardening
within the boundary rather than the boundary itself); **gVisor/Kata or a microVM** (rejected:
disproportionate for a single-operator tool whose agent has two file-scoped tools).

---

## R7 — Where does operational state live?

**Decision**: **SQLite** via `Microsoft.Data.Sqlite`, one file, adapter-contained in
`src/tasks/adapters/`. See [ADR-0006](../../docs/adr/0006-operational-state-store.md).

**Rationale**: Tasks are retained indefinitely, must survive a hub restart (FR-028), must be listed
newest-first (FR-030), and must carry an append-only ordered tool-call record (FR-023). That is a
small relational shape with ordering and a durability requirement. SQLite is a real database in
tests as in production — a file per test, hermetic and parallel-safe, satisfying III.2/III.3
without a container or a shared server. Raw `Microsoft.Data.Sqlite` rather than EF Core: the schema
is six tables that never migrate in this feature, and an ORM would be a layer the work does not yet
need (VII.3).

**Alternatives considered**: JSON files per task (rejected: listing newest-first and asserting
"exactly one run per task" become directory scans and lock dances); EF Core (rejected for now:
speculative layering; adding it later is a change inside one adapter); Postgres (rejected: a server
process for a single-operator app, and III.3's "no shared mutable state" gets harder, not easier);
storing run state in git alongside the wiki (rejected: conflates the artifact record with the
content it describes, and a failed run must record state *while* leaving the wiki untouched).

---

## R8 — How do FR-013 and FR-014 get their "before the first model call" ordering?

**Decision**: A **proceed handshake** on the runner's stdio. The runner's first act is to load the
instruction file and emit `instruction_loaded` and `tool_grant` as NDJSON on stdout, then block
until the hub writes `{"type":"proceed"}` on stdin. The hub persists both records, then unblocks
the run. The model is not contacted until after that.

**Rationale**: FR-013 and FR-014 are ordering requirements, not merely content requirements —
"at dispatch, before the first model call". Without a handshake the ordering is a hope about
scheduling; with one it is a property TS-07 can assert by comparing the persisted record against
the scripted-model server's first-request timestamp. The alternative — the hub reading the
instruction file itself for versioning — would put a second reader on the instruction file, open a
time-of-check/time-of-use gap against the version actually composed into the prompt, and blur
constitution I.2's "exactly one component".

This is the one piece of mechanism in the plan that exists purely to make a requirement provable,
so it is called out rather than buried: it is not a speculative extension point (VII.3), it is the
ordering guarantee itself.

**Alternatives considered**: fire-and-forget event emission (rejected: ordering unprovable);
the hub hashing the instruction file before spawn (rejected: two readers, TOCTOU, and I.2);
having the runner persist to SQLite directly (rejected: would put the state-store adapter in two
languages, against VII.2).

---

## R9 — How does the commit message stay judgment (I.1) with only two tools granted?

**Decision**: The run's **final assistant message text** is taken verbatim as the commit message;
the ingest instruction is what tells the agent to end its run with one. If the final message is
empty, the hub commits under a fixed constant (`ingest <taskId>`).

**Rationale**: The spec classifies commit-message wording as judgment, but FR-010 grants exactly
two tools, so there is no third "commit" tool to carry a message argument. Taking the final message
keeps the wording in the instruction file where the classification says it belongs, and keeps the
tool grant at two. The empty-message fallback is a constant, not content logic — it makes no
decision about the wiki, so it stays on the control side of I.1.

**Alternatives considered**: a third tool taking a message (rejected: widens the grant, which is an
agent autonomy change, for a string); a hub-generated message from the diff (rejected:
the hub deciding wiki-facing content, straight against I.1/I.3); a message argument on the write
tool (rejected: N writes would mean N candidate messages and the hub would have to pick — that
choice is judgment).

---

## R10 — What is an "instruction-file version" (FR-014, SC-009)?

**Decision**: `sha256:<first 12 hex>` of the instruction file's bytes, recorded together with the
file's repo-relative path and byte length.

**Rationale**: It must be stable, comparable across runs, and computable without a git dependency
in the runner (which has none and should keep none). A content hash makes "did the behaviour change
because the instruction changed?" — the SC-009 loop — answerable by comparing two task views, and
maps to a revision an operator can find with `git log -p src/instructions/ingest.md`.

**Alternatives considered**: the git commit sha of the file's last change (rejected: puts git in the
runner, and a dirty working copy during instruction iteration would report a stale version — the
worst possible failure for the one signal that attributes behaviour to a revision); a
hand-maintained version header inside the file (rejected: an editor can forget to bump it, and
SC-009's attribution silently breaks); full 64-hex sha (rejected only for display length; the full
value is recorded, 12 hex is what the task view shows).

---

## R11 — How does the frontend↔hub contract stay reviewable (V.5, V.6)?

**Decision**: An **OpenAPI 3.1 document committed at `contracts/hub-api.openapi.yaml`**. The
SvelteKit client's types are generated from it at build time with `openapi-typescript`; CI diffs
the hub's served OpenAPI document against the committed file and fails on drift.
See [ADR-0008](../../docs/adr/0008-api-contract-mechanism.md).

**Rationale**: V.5 requires runtime behaviour to *derive* from the committed contract so every
contract change is visible in the PR diff. Generation-from-committed-file gives the frontend half
of that directly. The hub half cannot be generated from the contract without a code-generation
layer the work does not need, so it is enforced instead: the drift test (TS-15) means a hub change
that alters the contract cannot merge without the YAML changing in the same diff. The net effect —
no contract change without a visible diff — is what the rule is for.

**Alternatives considered**: generating hub endpoint stubs from the YAML (rejected: a codegen layer
over four endpoints, and it fights Minimal APIs' own shape); deriving the YAML from the hub at
build time and committing the output (rejected: inverts the direction — the contract would then be
a *consequence* of code rather than a reviewed artifact, which V.5 explicitly rules out);
JSON Schema or TypeSpec (rejected: OpenAPI is what ASP.NET Core emits natively and what
`openapi-typescript` consumes, so this choice adds no toolchain).

---

## R12 — Frontend framework specifics

**Decision**: **SvelteKit on Svelte 5**, three routes, no client-side state library.
See [ADR-0002](../../docs/adr/0002-frontend-framework.md).

**Rationale**: The user asked for Svelte. SvelteKit rather than bare Svelte because the feature has
routing (`/`, `/tasks`, `/tasks/[taskId]`) and server-side loading of a task view, both of which
would otherwise be hand-rolled. Live updating is explicitly out of scope, so the task view is a
load-on-navigate page with a manual refresh — no websocket, no polling loop, nothing speculative.

**Alternatives considered**: Svelte + a router (rejected: rebuilds a subset of SvelteKit);
server-rendered pages from the hub with no JS framework (rejected: the user asked for Svelte);
adding a store library (rejected: three pages, no shared client state).

---

## R13 — What does "cloud native" mean concretely for the hub and the agent harness?

**Decision**: A named runtime contract the hub and agent harness satisfy from the start, recorded in
[ADR-0011](../../docs/adr/0011-cloud-native-runtime-contract.md) and specified in
[contracts/deployment.md](./contracts/deployment.md):

| Aspect | Decision |
|--------|----------|
| Configuration | Environment variables only. No config file is baked into an image, and no setting is read from a path an operator cannot set. |
| Logging | Structured JSON to stdout. No log files, no rotation, no sink configuration. |
| Health | `/healthz` (liveness: the process is up) and `/readyz` (readiness: wiki repo reachable, state DB writable, egress endpoint reachable), both in the committed OpenAPI document under an `Operations` tag. |
| Shutdown | `SIGTERM` → stop dispatching, terminate the running runner, mark that task `failed` with an interruption reason, reset the wiki working tree, exit. |
| State | All durable state on mounted volumes; the container filesystem is read-only apart from those and a `tmpfs`. |
| Identity | Non-root user; no capability beyond the default set. |
| Scaling | **One replica.** Explicitly not horizontally scalable. |
| Images | One `grimoire-hub` image (hub + agent harness + built frontend assets), `linux/amd64` and `linux/arm64`. |

**Rationale**: These are the properties that make a container deployment work at all — an
orchestrator sends `SIGTERM` and expects the process to drain, reads liveness and readiness to
decide whether to route traffic, collects stdout, and treats the container filesystem as
disposable. Adopting them now costs almost nothing and removes the retrofit later. Three of them
also serve requirements the spec already has: graceful shutdown makes FR-028's "no task left
`running` while no run executes" true at shutdown rather than only at the next startup;
readiness makes "the egress path is broken" distinguishable from "the agent failed"; volumes are
where FR-017's byte-identical pre-run content actually lives.

**Single replica is a property, not a limitation to fix**: FR-019 requires at most one run at a
time, the state store is SQLite, and runs hold the wiki working tree. A second replica would
violate the first and corrupt the other two. It is written into the deployment contract so nobody discovers it
by scaling.

**Alternatives considered**: **retrofit cloud-native properties later** (rejected: graceful
shutdown and readiness change dispatch and startup logic, which is exactly the code that would have
to be reopened); **OpenTelemetry exporter now** (rejected: constitution IV.4 forbids a declared
signal without a surface, and there is no dashboard to be one — stdout JSON plus the task view
covers every declared signal today, and OTel is the natural successor once a dashboard exists);
**`dotnet publish /t:PublishContainer`** instead of a Dockerfile (rejected: SDK container publishing
produces a .NET-only image, and the hub image must also carry the Node runtime for the runner).

---

## R14 — How does the agent harness reach the model, and who holds the credential?

**Decision**: No process in the deployment holds **an upstream model credential and makes no direct outbound
connection**. All egress leaves through a single **egress proxy**, which is the credential
custodian. See [ADR-0010](../../docs/adr/0010-model-egress-and-credential-custody.md).

Mechanically, this is the documented LLM-gateway path, confirmed against
`code.claude.com/docs/en/llm-gateway-connect` rather than recalled:

- `ANTHROPIC_BASE_URL` points at the proxy.
- The credential variable determines the header: `ANTHROPIC_AUTH_TOKEN` is sent as
  `Authorization: Bearer`, `ANTHROPIC_API_KEY` as `x-api-key`. The caller is given an **opaque
  internal token** for the proxy, not an Anthropic credential; the proxy strips it and injects the
  real upstream auth.
- A gateway credential variable **takes precedence over a saved claude.ai login**, so the runner
  cannot silently fall back to an ambient identity.
- `ANTHROPIC_CUSTOM_HEADERS: X-Grimoire-Run: <runId>` correlates the proxy's access log to a task.
- **`CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1` is required.** The docs are explicit that Claude
  Code otherwise sends background traffic — version checks, telemetry, release notes, third-party
  requests — *outside* the gateway path. Under a deny-all egress policy those become failed
  connections and blocked-connection noise in egress monitoring. Turning them off is what makes
  "the only thing that leaves is a model request" true rather than approximately true.
- The hub's URL retrieval for FR-003 goes through the **same** proxy on a different route with a
  different policy: arbitrary `http(s)` destinations, no credential injected.

**Rationale**: Two policies are genuinely different — the agent path must reach exactly one
upstream, while URL retrieval must reach arbitrary user-supplied hosts. Routing both through one
proxy gives the container a single egress point that can hold both policies, which is what makes a
deny-all network policy possible at all. Putting the credential in the proxy means a compromised
runner process yields no reusable secret, and it is precisely the seam the user described: a proxy
holding Claude Code auth can be dropped in later with **no application code change**, because the
hub already expects an endpoint and an opaque token.

**What the proxy is**: YARP hosted in ASP.NET Core, in `src/egress/` — decided separately in
[ADR-0012](../../docs/adr/0012-egress-proxy-implementation.md), because the boundary here should
survive swapping the implementation. The deciding requirement was credential *refresh*: leveraging
Claude Code auth means an OAuth credential that rotates, which a configuration-only proxy (nginx,
Caddy) cannot do. Feature 001 builds its minimal form — two routes, static credential,
single-upstream allowlist, SSRF policy — enough for the deny-all posture to be real and TS-18 and
TS-21 to assert something. Refresh, usage accounting and quotas are deferred behind one service
interface.

**One thing to decide with open eyes**: the Agent SDK documentation states that, unless previously
approved, Anthropic does not allow third-party developers to offer claude.ai login or subscription
rate limits for products built on the SDK, and directs such products to API-key authentication.
A single operator proxying their **own** Claude Code credential for their **own** tool is not the
same as offering it to others, but this design is exactly the mechanism that restriction is about,
so it is worth reading the current terms before wiring a subscription credential in. The design is
credential-agnostic either way: a Console API key works identically, and nothing in the application
changes between the two.

**Alternatives considered**: **credential in the hub container, direct to `api.anthropic.com`**
(rejected: puts a reusable secret next to the agent process and gives the container broad egress);
**`HTTPS_PROXY` for everything** (rejected: one policy for both routes, so URL retrieval and the
model path could not be separated); **a sidecar per container rather than a shared proxy** (viable,
and the natural shape once the runner gets its own container — noted in ADR-0010, not built today);
**`apiKeyHelper`** to fetch a rotating credential (a real option the docs support, and the right
mechanism if the proxy ever issues short-lived tokens — recorded, not needed while the token is a
static internal one).

---

## R15 — What goes in the image, and how many images are there?

**Decision**: **One `grimoire-hub` image** carrying the .NET 10 hub, the Node 22 runner build,
the instruction files, and the built frontend static assets; plus whatever image the egress proxy
is. Multi-stage build, non-root, `linux/amd64` and `linux/arm64`. See ADR-0007 and ADR-0011.

**Rationale**: The hub spawns the runner as a child process and owns the wiki repository the runner
writes into, so the two share a filesystem and a lifecycle. Splitting them into separate containers
today would require either handing the hub a container-runtime socket — which is a host-escape
primitive and makes containment strictly worse than the boundary it is meant to strengthen — or
introducing a worker-pull API plus a shared volume, which is indirection with no caller that needs
it until a per-container network policy exists to justify it (VII.3).

The frontend is built with `@sveltejs/adapter-static` and served by the hub as static files: one
origin, no CORS configuration, no second container, and the committed OpenAPI contract stays the
only thing between the two. That is an elaboration of ADR-0002, not a new framework decision.

**Alternatives considered**: **hub image + runner image + shared volume** (the natural cloud-native
shape, and the named successor in ADR-0007 once per-container network policy is the reason to do
it); **runner container per run, launched by the hub** (the strongest per-run isolation — fresh
container per run — but it needs the container-runtime socket, which is the primitive we are trying
to keep the agent away from); **separate nginx image for the frontend** (rejected: a container and
a CORS configuration to serve three routes).

---

## R16 — URL retrieval fetches user-supplied addresses. What stops it reaching inward?

**Decision**: `src/ingest/adapters/UrlFetch.cs` refuses non-`http(s)` schemes and destinations that
resolve to loopback, link-local, or private ranges, and caps redirects; refusal is recorded as the
task's failure reason and no run is dispatched. The egress proxy enforces the same policy
independently (R14).

**Rationale**: FR-003 has the hub fetch a URL the user typed. In a container that is a
server-side request forgery surface pointing at the cloud metadata endpoint, at sibling services,
and at the hub itself. The spec does not mention it because it is not a user-facing behaviour, but
building this container-deployed without a guard would be shipping a known hole. Two independent
layers because the in-process guard is what protects a developer running without a proxy, and the
proxy is what protects against a guard that has a bug.

**It does change observable behaviour, so it is flagged rather than slipped in**: submitting
`http://localhost/…` or `http://169.254.169.254/…` now fails the task with a reason instead of
retrieving content. That is a control decision, recorded here, and it fits FR-003's existing
"retrieval fails → task failed with a recorded reason" path rather than adding a new one.

**Alternatives considered**: **proxy-only enforcement** (rejected: a developer running the hub
directly has no proxy, which is exactly when a mistake is made); **in-process only** (rejected:
DNS rebinding defeats a pre-connection check, and the proxy sees the actual connection);
**no guard, document the risk** (rejected: the spec's own edge-case list already treats retrieval
failure as ordinary, so there is a natural home for the refusal).


## Summary of decisions

| # | Decision | ADR |
|---|----------|-----|
| R1 | C# 14 / .NET 10 LTS, ASP.NET Core Minimal APIs | ADR-0001 |
| R2 | Agent loop = `@anthropic-ai/claude-agent-sdk` `query()` in a Node child process | ADR-0003 |
| R3 | Grants are in-process MCP tools, built-ins stripped, `PreToolUse` hook records and decides | ADR-0009 |
| R4 | Model port in `src/agentrun/model/`; LLM doubled at the wire via `ANTHROPIC_BASE_URL` | ADR-0004 |
| R5 | One ordinary git repo; commit once on success, reset on failure; revert as a new commit | ADR-0005 |
| R6 | Container trust boundary, per-run process boundary inside it | ADR-0007 |
| R7 | SQLite for operational state, raw `Microsoft.Data.Sqlite` | ADR-0006 |
| R8 | Proceed handshake to prove dispatch-time recording precedes the first model call | elaboration |
| R9 | Final assistant message is the commit message | elaboration |
| R10 | Instruction version = `sha256:` content hash | elaboration |
| R11 | Committed OpenAPI 3.1 + generated client + drift test | ADR-0008 |
| R12 | SvelteKit / Svelte 5, three routes, static adapter served by the hub | ADR-0002 |
| R13 | Cloud-native runtime contract: env config, stdout JSON, health, `SIGTERM`, volumes, one replica | ADR-0011 |
| R14 | No upstream credential in any process; all egress through one proxy; nonessential SDK traffic off | ADR-0010, ADR-0012 |
| R15 | One hub image (hub + agent harness + static frontend); split named as successor | ADR-0007, ADR-0011 |
| R16 | SSRF guard on URL retrieval, in-process and at the proxy | elaboration |

No `NEEDS CLARIFICATION` remains. R1's open assumption from the previous revision — which .NET
runtime "C# 10 minimal api" meant — has been resolved by direction: C# 14 on .NET 10 LTS.

Two findings change observable behaviour and are flagged rather than buried: R14's
`CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1` requirement together with the terms question about
proxying a Claude Code subscription credential, and R16's refusal of loopback and private-range
URLs.
