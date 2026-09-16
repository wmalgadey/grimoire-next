# Quickstart: Validating Source Ingest via Agent Run

**Feature**: `001-source-ingest-agent-run` | **Plan**: [plan.md](./plan.md)

Runnable scenarios that prove the feature works end to end. Each maps to spec acceptance scenarios
and success criteria, and to the Test Strategy row that automates it. Details live in
[data-model.md](./data-model.md) and [contracts/](./contracts/) — this file does not repeat them.

## Prerequisites

| Tool | Version | Used for |
|------|---------|----------|
| .NET SDK | 10.x (C# 14) | Hub (`src/hub`, `src/ingest`, `src/tasks`, `src/dispatch`, `src/wiki`) |
| Node.js | 22 LTS | Agent runner (`src/agentrun`), frontend build, scripted-model double |
| git | 2.40+ | Wiki repository: commit, reset, revert, diff |
| Docker or Podman | any current | Scenarios 11 and 12 only; the rest run as plain processes |

Plus, for a **live** run only: an Anthropic API key in `ANTHROPIC_API_KEY`. Every automated
scenario below runs without one — the LLM is the single sanctioned double (constitution III.2) and
is reached through `ANTHROPIC_BASE_URL` pointed at `tests/scripted-model/`.

## Setup

```bash
# One-time: build everything
dotnet build
npm --prefix src/agentrun ci && npm --prefix src/agentrun run build
npm --prefix frontend ci && npm --prefix frontend run build

# One-time: create the wiki repository (outside this feature's scope, but every run needs it)
git init "$GRIMOIRE_WIKI_REPO"
# the wiki must have at least one commit before the first ingest — see spec Assumptions
```

Environment the hub reads:

| Variable | Meaning |
|----------|---------|
| `GRIMOIRE_WIKI_REPO` | Path to the bare wiki repository |
| `GRIMOIRE_STATE_DB` | Path to the SQLite operational-state file |
| `GRIMOIRE_INSTRUCTION` | Path to the ingest instruction (default `src/instructions/ingest.md`) |
| `GRIMOIRE_RUN_MAX_TOOL_CALLS`, `GRIMOIRE_RUN_MAX_ELAPSED_MS` | The run limit (FR-009) |
| `GRIMOIRE_MODEL_BASE_URL` | The egress proxy's model route; reaches the runner as `ANTHROPIC_BASE_URL` |
| `GRIMOIRE_MODEL_TOKEN` | Opaque token for the proxy — **never an Anthropic credential** |
| `GRIMOIRE_FETCH_PROXY` | The proxy's fetch route, used by URL retrieval |

No process in the deployment holds an upstream model credential: the proxy is the custodian
([ADR-0010](../../docs/adr/0010-model-egress-and-credential-custody.md)). Running directly against
the Anthropic API for a live trial means pointing `GRIMOIRE_MODEL_BASE_URL` at it and putting a real
key in `GRIMOIRE_MODEL_TOKEN` — fine on a laptop, not how the container deployment runs. The full
environment contract is [contracts/deployment.md](./contracts/deployment.md).

## Run the full suite

```bash
dotnet test                            # hub slices, architecture gates, observability, contract drift
npm --prefix src/agentrun test         # runner: real SDK loop against the scripted model
npm --prefix frontend run test:e2e     # Playwright over the three surfaces
```

Expected: all green, hermetic, parallel-safe, no third-party network. Each suite spawns its own
wiki repository, its own SQLite file, its own hub port, and its own scripted-model server.

## Start the app

Either as plain processes:

```bash
dotnet run --project src/hub &         # Kestrel; serves the built frontend and /api on one origin
```

or as the container deployment:

```bash
docker compose -f deploy/compose.yaml up --build
curl -fsS localhost:8080/healthz       # {"status":"healthy"}
curl -fsS localhost:8080/readyz | jq   # checks: wikiRepo, stateDb, egress
```

---

## Scenario 1 — Ingest a source and inspect what the agent did

*Spec: User Story 1, acceptance 1–5, SC-001/002/004/007/008. Automated by TS-02, TS-05, TS-06, TS-13.*

1. Open the submit surface, paste a paragraph of notes, submit.
2. **Expected**: exactly one task appears and is openable **within 2 seconds** (SC-001). No second
   task, no second run.
3. Wait for the run to end, then open the task.
4. **Expected on the task view**, without opening a terminal or the repository (SC-008):
   - state `completed`;
   - the **instruction-file version** the run loaded — `sha256:` and its first 12 hex;
   - the **granted tool set** — exactly `mcp__wiki__read_page` and `mcp__wiki__write_page`, two
     tools, no more (SC-004);
   - the **ordered tool-call record**, each entry with its target and outcome, showing what the
     agent read before it wrote (SC-010);
   - the **diff** — per file, content added, changed, removed — and the commit identity.
5. **Expected in the wiki**: exactly one new commit for this run, never zero and never two (SC-002).

```bash
git -C "$GRIMOIRE_WIKI_REPO" log --oneline -1     # the run's commit
git -C "$GRIMOIRE_WIKI_REPO" show --stat HEAD     # the same files the task view listed
```

## Scenario 2 — The loop is a loop, not one model call

*Spec: FR-007, FR-008, SC-007. Automated by TS-06, parameterised N = 1…8.*

Drive the scripted model with an escalation script: each response issues one tool call, and the
script's next response depends on the tool result it receives.

**Expected**: all N tool calls are executed and recorded **within a single run**, in order, with
each result visibly informing the next call. A mechanism that applies one model output and stops
fails this scenario — which is exactly the point of running the real SDK agent loop against a
scripted wire rather than doubling the loop away (see [research.md](./research.md) R4).

## Scenario 3 — Revert an ingest

*Spec: User Story 2, acceptance 1–4, SC-005. Automated by TS-11, TS-16.*

1. Capture the wiki content before the run: `git -C "$GRIMOIRE_WIKI_REPO" rev-parse HEAD`.
2. Run Scenario 1 so a commit exists and is the wiki tip.
3. Reopen the task **from the task list** (not from the submission response) and trigger revert.
4. **Expected**: one action, zero manual steps; wiki content byte-identical to the pre-run state;
   a **new** commit performing the restoration; the run's own commit **still in history**; task
   state `reverted`; the revert commit identity shown; instruction version, tool calls and original
   diff all still shown; **no** revert action offered any more.

```bash
git -C "$GRIMOIRE_WIKI_REPO" log --oneline -3
# newest first: the revert commit, the run's commit, the pre-run commit — history intact
git -C "$GRIMOIRE_WIKI_REPO" diff <pre-run-sha> HEAD    # empty: content restored byte-for-byte
```

5. Trigger revert a second time (second tab, or repeat the request). **Expected**: refused; the
   task is reverted exactly once.

## Scenario 4 — Superseded ingest offers no revert

*Spec: User Story 2 acceptance 4, FR-027. Automated by TS-11, TS-16.*

1. Run two ingests in sequence so the first task's commit is no longer the tip.
2. Open the **first** task. **Expected**: no revert action, and the view says the task was
   **superseded by a later wiki commit** — not a disabled button with no explanation. The wiki is
   unchanged and the task stays `completed`.
3. Immediately after any revert, open the just-reverted task's predecessor. **Expected**: still no
   revert offered anywhere, because the revert is itself the tip. Undo reaches one ingest back.

## Scenario 5 — Nothing changed, and it says so

*Spec: Edge case "Agent changes nothing", FR-016, SC-011. Automated by TS-13.*

Script the model to read a page and stop without writing.

**Expected**: state `completed`, **no commit**, an empty diff, and the view stating explicitly that
nothing changed. From the empty diff plus the tool-call record an operator can tell an agent that
judged no change was warranted from a run that failed to do its work — and the corrective surface
for the former is the ingest instruction, not the code.

## Scenario 6 — Failure leaves the wiki exactly where it was

*Spec: User Story 3 acceptance 3, FR-017, SC-003. Automated by TS-12.*

1. Note the tip: `git -C "$GRIMOIRE_WIKI_REPO" rev-parse HEAD`.
2. Script a run that writes several pages and then hangs; let the elapsed run limit kill it.
   Separately, kill the runner mid-write to cover the crash case, and kill the **hub** mid-write to
   cover the case where cleanup falls to the next startup.
3. **Expected in all three cases**: task `failed` with a human-readable reason; **no commit** for
   the task; the tip unchanged; wiki content **byte-identical** to step 1; the partial writes
   nowhere to be seen. The first two are cleaned at run end, the third at startup — both reach the
   same state, which is why all three are tested ([research.md](./research.md) R5).

## Scenario 7 — Containment holds against hostile input

*Spec: FR-010, FR-011, constitution II.5. Automated by TS-09, TS-10.*

Place canary files outside the wiki repository. Then, in three separate runs, drive an escape attempt
from each of the three sources the constitution names:

| Source of the attempt | What it tries |
|---|---|
| Instruction file content | Ask for `Bash`, `WebFetch`, `Task`, and an unknown MCP tool |
| Task input (the submitted source) | Same, smuggled through the source text |
| Wiki page content read during the run | A planted symlink out of the repository, `../` traversal, an absolute path |

**Expected** in every case: the call is refused; the refusal is **recorded as a tool call** with
`outcome: "refused"` and a reason, visible on the task view; the run continues under the agent's
own decision; every canary file is untouched. The boundary holds independent of instruction-file
content, task input, and user-supplied wiki content.

## Scenario 8 — Queued, running, and surviving a restart

*Spec: User Story 3 acceptance 1, 2, 4; FR-019, FR-028, SC-006. Automated by TS-14, TS-16.*

1. Submit a source; while its run is executing, submit a second.
2. **Expected**: the second task is `queued`, appears in the task list as queued, and opens to show
   it has not started. Open the first while it runs: state `running`, instruction version, granted
   tool set, and the tool calls **so far**.
3. Stop the hub mid-run. Start it again.
4. **Expected**: the interrupted task is `failed` with a reason **naming the interruption**, and was
   **not** run a second time; the queued task **has been dispatched**; no task is left `running`
   while nothing is executing; the wiki sits at the pre-run commit.

## Scenario 9 — URL submission that cannot be retrieved

*Spec: User Story 1 acceptance 6, FR-003. Automated by TS-03.*

Submit a URL served by a local listener that returns 500 (repeat with a non-text response).

**Expected**: the task is `failed` with a human-readable reason, **no run is dispatched**, and the
wiki is untouched. The task is still openable and still shows its source.

## Scenario 10 — Rejections and oversized sources

*Spec: Edge cases; FR-001, FR-029. Automated by TS-01, TS-04.*

1. Submit an empty field, then a whitespace-only one. **Expected**: rejected at submission, **no
   task created** — nothing appears in the task list.
2. Submit a source far larger than the agent can take in. **Expected**: the submission is
   **accepted**, the task is created, the source is passed **whole** (no truncation, no summary),
   and the run ends `failed` with a reason **identifying the source size**, with no commit.
   Splitting an oversized source is left to the user.

## Scenario 11 — The container boundary and the egress policy hold

*Spec: FR-010, constitution II.5; plan ADR-0007, ADR-0010. Automated by TS-18.*

Run the built image against a deny-all network with exactly one permitted destination.

1. **Identity and filesystem**: the container runs as a non-root user with a read-only root
   filesystem; only the three volumes and the `tmpfs` are writable.
2. **Egress**: complete a full ingest. **Expected**: the only outbound connections are to the
   configured model endpoint and, for a URL submission, the fetch route. Nothing else is attempted —
   in particular no version checks, telemetry, or release-note traffic, because
   `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1` turns off the background traffic the SDK would
   otherwise send *outside* the gateway path.
3. **Credentials**: inspect the runner process's environment. **Expected**: `ANTHROPIC_AUTH_TOKEN`
   holds the opaque internal token and no Anthropic credential is present anywhere in the container.
4. **Blocked upstream**: block the model endpoint and submit a source. **Expected**: the run ends
   `failed` with a reason naming the unreachable endpoint — not a hang — the wiki is untouched, and
   `/readyz` reports `egress: failed` so the cause is distinguishable from an agent failure.

```bash
docker inspect --format '{{.Config.User}} {{.HostConfig.ReadonlyRootfs}}' grimoire-hub
docker exec grimoire-hub env | grep -c ANTHROPIC_API_KEY   # expect 0
```

## Scenario 12 — Graceful shutdown leaves nothing stranded

*Spec: FR-017, FR-028, SC-006; plan ADR-0011. Automated by TS-19.*

1. Start an ingest and, while it runs, submit a second source so one task is `running` and one is
   `queued`.
2. Send `SIGTERM` to the hub (`docker compose stop`, or a rollout).
3. **Expected before exit**: `/readyz` reports not-ready and `draining: true`; the running runner is
   terminated; its task is `failed` with a reason **naming the interruption**; the wiki working tree is reset;
   the wiki is at the pre-run commit.
4. Start again. **Expected**: the interrupted task is **not** run a second time, the queued task
   **is** dispatched, and no task sits in `running` while nothing executes.

Repeat with `SIGKILL` instead of `SIGTERM`. **Expected**: the same end state, reached by startup
recovery instead of by the shutdown path — the two paths converge, which is why both are tested.

---

## The operator loop this validates

Scenarios 1, 3, 5 and 7 are the point of the whole feature: the task view alone answers *which
instruction ran, what the agent looked at, what it wrote, and what the wiki says now*. When the
answer is wrong — a new page where an update belonged, a name that does not fit, links missing —
the correction is an edit to `src/instructions/ingest.md` and a re-submission, with no code change
and no deploy. The next task view shows the new instruction version beside the new diff, so the
change in behaviour is attributable to that revision (SC-009). Nothing in CI asserts what the model
wrote, and nothing should start to.

Scenarios 11 and 12 validate the other half of the promise: no process in the deployment holds an upstream credential
and reaches nothing but its configured endpoint, so a proxy carrying Claude Code auth can be dropped
in later as a deployment change, with no application code touched.
