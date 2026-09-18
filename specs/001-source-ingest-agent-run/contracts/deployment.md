# Contract: Deployment, Environment and Egress

**Internal.** The runtime contract the hub and runner satisfy so they can be
container-deployed ([ADR-0011](../../../docs/adr/0011-cloud-native-runtime-contract.md)), the
boundary they run behind ([ADR-0007](../../../docs/adr/0007-host-trust-boundary.md)), and
the egress seam ([ADR-0010](../../../docs/adr/0010-model-egress-and-credential-custody.md)).

## Topology

```text
      browser
         │  HTTP (one origin: frontend assets + /api + /healthz + /readyz)
         ▼
┌──────────────────────────────────────────┐        ┌──────────────────────┐
│  grimoire-hub            (1 replica) │        │  grimoire-egress     │
│  non-root · read-only rootfs             │        │  YARP · custodian    │
│                                          │        │                      │
│   hub (.NET 10)  ──── spawns ───┐        │ model  │  route /v1/*  ──────────►  Anthropic API
│    │                            ▼        │ ──────►│    injects upstream auth    (allowlisted)
│    │                    runner (Node 22) │        │                      │
│    │                     one per run     │ fetch  │  route /fetch ──────────►  arbitrary http(s)
│    └──── URL retrieval ─────────────────►│ ──────►│    no credential            (SSRF policy)
│                                          │        └──────────────────────┘
│  volumes: wiki repo · state db                    ▲
└──────────────────────────────────────────┘        │
                    │                               │
                    └── no other egress permitted ──┘
```

The hub container has **exactly one permitted destination**. Everything else is denied at the network
layer, which is what TS-18 asserts against the built image.

A container attached only to an `internal` network cannot publish a port, so in
`deploy/compose.yaml` the browser reaches the hub through an **ingress relay** — a reverse proxy on
both networks that forwards only *to* the hub. It is inbound plumbing: it holds no credential and
forwards nothing outward, so the hub's one route *out* is still the egress proxy.

## Environment contract

Configuration is environment variables only. No configuration file is baked into an image layer, and
no setting is read from a path an operator cannot set.

### Hub

| Variable | Required | Meaning |
|----------|----------|---------|
| `GRIMOIRE_WIKI_REPO` | yes | Path to the wiki git repository, checked out on `main` (volume) |
| `GRIMOIRE_STATE_DB` | yes | Path to the SQLite state file (volume, **local block storage only**) |
| `GRIMOIRE_INSTRUCTION` | no | Instruction file path; default `src/instructions/ingest.md` |
| `GRIMOIRE_RUN_MAX_TOOL_CALLS` | no | Tool-call half of the run limit (FR-009) |
| `GRIMOIRE_RUN_MAX_ELAPSED_MS` | no | Elapsed half of the run limit (FR-009) |
| `GRIMOIRE_MODEL_BASE_URL` | yes | Proxy model route; passed to the runner as `ANTHROPIC_BASE_URL` |
| `GRIMOIRE_MODEL_TOKEN` | yes | **Opaque internal token** for the proxy; never an Anthropic credential |
| `GRIMOIRE_FETCH_PROXY` | in containers | URL of the proxy's fetch route used by URL retrieval (FR-003), e.g. `http://egress:8080/fetch`; the hub requests `?url=<destination>` from it |
| `GRIMOIRE_FETCH_DEADLINE_MS` | no | How long URL retrieval may take in all — redirects and the body read included — before the task fails with a reason (FR-003). Default 120000 |
| `GRIMOIRE_LOG_FORMAT` | no | `json` (default) or `text`; `text` is for reading the log by eye outside a container |

### Egress proxy

| Variable | Required | Meaning |
|----------|----------|---------|
| `GRIMOIRE_EGRESS_MODEL_UPSTREAM` | yes | The single upstream the model route reaches, e.g. `https://api.anthropic.com` — the whole allowlist |
| `GRIMOIRE_EGRESS_MODEL_API_KEY` | exactly one of these two | The upstream API key, attached as `x-api-key`. Held by this process and no other |
| `GRIMOIRE_EGRESS_MODEL_AUTH_TOKEN` | exactly one of these two | The upstream OAuth-style token, attached as `Authorization: Bearer`. Held by this process and no other |
| `GRIMOIRE_EGRESS_INTERNAL_TOKEN` | yes | The opaque token callers must present as `Authorization: Bearer`; the hub's `GRIMOIRE_MODEL_TOKEN` |
| `ASPNETCORE_URLS` | no | Where the proxy listens; the image default is `http://+:8080` |

The two routes:

| Route | Accepts | Forwards |
|-------|---------|----------|
| model | `/v1/*` with the internal token | to the configured upstream; the caller's `Authorization`, `x-api-key` and `X-Grimoire-Run` removed, the upstream credential attached, the response streamed unbuffered |
| fetch | `GET /fetch?url=<http(s) URL>` | to that URL only if every address it resolves to is public — checked in the connect step itself; none of the caller's headers and no credential; redirects returned, not followed. A refusal is `403` with the reason in `X-Grimoire-Egress-Reason`, which the hub records on the task |

Anything else — another path, an absolute-form or `CONNECT` request naming its own destination — is
refused. The proxy is not a forward proxy.

### Runner (set by the hub at spawn; `env` is **replaced**, not merged)

| Variable | Value |
|----------|-------|
| `PATH` | minimal: the directory holding `node`, then `/usr/bin` and `/bin` |
| `HOME` | per-run directory on `tmpfs`, discarded with the run |
| `ANTHROPIC_BASE_URL` | from `GRIMOIRE_MODEL_BASE_URL` |
| `ANTHROPIC_AUTH_TOKEN` | from `GRIMOIRE_MODEL_TOKEN`; sent as `Authorization: Bearer` |
| `ANTHROPIC_CUSTOM_HEADERS` | `X-Grimoire-Run: <runId>` — joins the proxy access log to a task |
| `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC` | `1` — **required**, see below |
| `GRIMOIRE_INSTRUCTION` | absolute path of the instruction file the runner loads, resolved by the hub from its own `GRIMOIRE_INSTRUCTION` |
| `CLAUDE_CODE_DISABLE_EXPERIMENTAL_BETAS` | `1` — tests only, against the scripted model |

Nothing else. No inherited process environment, no Anthropic credential, no git configuration, and
no hub path but the instruction file — the runner reads it to announce its version (FR-014), and no
other hub path reaches it.

> **Why `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1` is required, not advisory**: Claude Code
> otherwise sends version checks, telemetry, release notes, and third-party requests **outside** the
> gateway path. Under a deny-all egress policy those become failed connections and blocked-connection
> noise in egress monitoring. Turning them off is what makes "the only thing that leaves this
> container is a model request" true rather than approximately true.

## Container contract

| Property | Value |
|----------|-------|
| Image | `grimoire-hub` — .NET 10 hub, Node 22 runner build, instruction files, built frontend assets |
| Platforms | `linux/amd64`, `linux/arm64` |
| User | non-root; no added capabilities |
| Root filesystem | read-only |
| Writable paths | the three volumes, plus a `tmpfs` for per-run `HOME` and scratch |
| Network | egress denied except the proxy |
| Replicas | **exactly one** |

**One replica is a property, not a limitation awaiting a fix.** FR-019 permits one run at a time,
the state store is SQLite, and runs hold the wiki working tree. A second replica violates the first
and corrupts the other two.

## Volumes

| Mount | Contents | Lifecycle |
|-------|----------|-----------|
| wiki | the git repository — the only durable home of wiki content and history | outlives the container; backed up |
| state | the SQLite file — tasks, runs, tool grants, tool calls, revert records | outlives the container; backed up |

## Operations endpoints

Both live in `hub-api.openapi.yaml` under the `Operations` tag, so the contract drift test (TS-15)
covers them.

| Endpoint | Meaning | Fails when |
|----------|---------|------------|
| `GET /healthz` | Liveness: the process is up and serving | the process is wedged; the orchestrator restarts it |
| `GET /readyz` | Readiness: wiki repository reachable, state database writable, egress endpoint reachable | any of the three is not; the replica should not take traffic |

`/readyz` is the surface for `grimoire.hub.readiness`, and the reason "the egress path is broken" is
distinguishable from "the agent failed".

## Lifecycle

**Startup**

1. Read configuration; fail fast and loudly on a missing required variable.
2. Open the state database; open the wiki repository.
3. **Recovery** (FR-028): every task recorded `running` becomes `failed` with a reason naming the
   interruption, and is never dispatched again. The wiki working tree is reset with
   `git reset --hard && git clean -fd`, removing anything a crashed run left behind.
4. Dispatch queued tasks in submission order.
5. `/readyz` starts reporting ready.

**`SIGTERM`**

1. Stop accepting dispatches; `/readyz` reports not-ready so traffic drains.
2. Terminate the running runner process.
3. Mark that task `failed` with a reason naming the interruption; emit
   `grimoire.dispatch.interrupted_on_shutdown`.
4. Reset the wiki working tree. Nothing was committed, so the wiki is at the pre-run commit
   (FR-017).
5. Exit.

Queued tasks stay queued and are dispatched by the next start. Startup recovery remains the backstop
for an ungraceful kill, so both paths end in the same state — tested by TS-19 and TS-14.

## Logging

Structured JSON on stdout. One event per line. No files, no rotation, no sink configuration. Every
signal declared in the plan's observability table is emitted here **and** reaches the operator on a
user-facing surface; the proxy's access log is undeclared diagnostic logging, joinable to a task
through the `X-Grimoire-Run` header.

## What this feature does and does not build

**Builds**: the runtime contract above, both images (`deploy/hub.Dockerfile`,
`deploy/egress.Dockerfile`), the compose deployment (`deploy/compose.yaml`) with the hub container on
an `internal: true` network and an inbound relay, the deny-all egress posture, and the minimal YARP
proxy (two routes, static upstream credential, single-upstream allowlist, SSRF policy) so that
posture is real and testable.

**Does not build**: credential refresh, usage accounting, per-run quotas, or upstream failover.
Those are swaps behind the proxy's `CredentialProvider` and additions to one pipeline
([ADR-0012](../../../docs/adr/0012-egress-proxy-implementation.md)), never reopenings of the
application — which holds no upstream credential and connects to nothing but the configured
endpoint.
