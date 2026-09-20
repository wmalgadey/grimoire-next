# Phase 0 Research: The First Ingest

**Feature**: `001-first-ingest` · **Date**: 2026-09-20

This file resolves every NEEDS CLARIFICATION in the plan's Technical Context. The spec carried no
blocking open question; everything below is a technology question the constitution puts in this
plan (II.6), plus the findings that changed the design.

`docs/decisions.md` was read first. **DEC-001** — models are reached through the Claude Agent SDK
with the owner's subscription sign-in, API-token billing is not acceptable, and the fallback is an
API-key adapter behind the same port — is in force and is not re-decided here. R-11 works inside it.

---

## Owner decisions taken at plan time

| Question | Decision | Where it binds |
| --- | --- | --- |
| Where the wiki tools live | The hub owns them and serves them over MCP; the agent process is wiring | R-02, R-03 |
| Split | Applied. RUNS-002, RUNS-003, RUNS-004 and ACCESS-003 move to a follow-up feature | spec, Budget and split decision |
| The Contract suite and the real model | No scripted Anthropic endpoint. The agent adapter's Contract tests run against the real thing with the owner's real sign-in, locally, and are excluded from CI | R-09 |
| The per-run tool endpoint | Unauthenticated, bound to loopback, resting on the trusted network of `docs/product.md` §2 | R-02, and the plan's Technology decisions |

---

## R-11 — Can `HarnessProcess` drive the Claude Code CLI directly? *(the research item that decided the stack)*

**Question**: the earlier draft put a TypeScript harness on `@anthropic-ai/claude-agent-sdk` between
the hub and the model. Can the hub's own adapter drive the `claude` CLI in headless mode instead?
It must support all six of: deny-by-default for every tool outside the grant; no settings picked up
from the machine; streamed token counts for the cost ceiling; a second message to the running agent
for the nudge; a stop; and an explicit, pinned model ID served under the subscription sign-in.

**Answer: yes, all six.** The TypeScript harness and the `npm` toolchain are dropped.
`Grimoire.Agent/Adapters/HarnessProcess.cs` spawns `claude` directly and speaks newline-delimited
JSON to it.

**Evidence**. Each need was checked against the installed CLI, `claude` **2.1.240**, driven from a
Python parent process exactly as `HarnessProcess` will drive it — one `subprocess` with
`--input-format stream-json --output-format stream-json`. The documentation each finding rests on is
linked with it.

| Need | Mechanism | What was observed |
| --- | --- | --- |
| Deny-by-default | `--tools ""` + `--mcp-config` + `--strict-mcp-config` + `--allowed-tools "mcp__wiki__*"` + `--permission-mode dontAsk` | `system/init` reported `"tools":["mcp__wiki__append_log"]` — the whole tool surface is the grant, nothing else |
| No machine settings | `--setting-sources ""` | A `CLAUDE.md` in the working directory was invisible to the run; without the flag the same run quoted it |
| Streamed token counts | `--include-partial-messages` | Three `stream_event` / `message_delta` events carried a growing `usage`; the final `result` carried the authoritative totals and a `modelUsage` breakdown |
| A second message to the running agent | another user message written on stdin | Same `session_id`, and the agent answered about the turn before it |
| A stop | `control_request` / `interrupt` on stdin | A response in flight ended in ~0.9 s with `terminal_reason: "aborted_streaming"`; the process stayed alive and served a further message |
| A pinned model | `--model <full id>` | `modelUsage` came back keyed by the exact ID asked for, with `apiKeySource: "none"` — a pinned model *and* the subscription sign-in, in the same run |

### The five findings in detail

**1. Deny-by-default (GUARD-001, GUARD-002).** `--tools` is an allow-list over the built-in set, not
an auto-approval list: `claude --help` (2.1.240) reads "Specify the list of available tools from the
built-in set. Use `""` to disable all tools, `default` to use all tools, or specify tool names". The
CLI reference says the same from the other side — of `--allowedTools`: "To restrict which tools are
available, use `--tools` instead"
([cli-reference](https://code.claude.com/docs/en/cli-reference)). MCP tools are a separate namespace
that `--tools` does not cover, which `--disallowedTools`'s documented `"mcp__*"` form confirms, so
`--mcp-config` + `--strict-mcp-config` ("Only use MCP servers from `--mcp-config`, ignoring all
other MCP configurations") decides the MCP half by itself.

Observed, with a stub MCP server offering exactly one tool:

```
claude -p --input-format stream-json --output-format stream-json --verbose \
  --tools "" --setting-sources "" --strict-mcp-config --mcp-config '{…wiki…}' \
  --allowed-tools "mcp__wiki__*" --permission-mode dontAsk --include-partial-messages
→ {"type":"system","subtype":"init", "tools":["mcp__wiki__append_log"],
   "mcp_servers":[{"name":"wiki","status":"connected"}], …}
```

This is deny-by-default by construction rather than deny-by-enumeration: the grant is the tool list,
and there is no second list of built-ins to keep up to date as the CLI ships new ones.

`--allowed-tools` is still needed, and the first probe showed why: without it, the same run denied
the granted tool and the final `result` carried
`permission_denials:[{"tool_name":"mcp__wiki__append_log", …}]`. `dontAsk` "denies every call that
would otherwise prompt" ([headless](https://code.claude.com/docs/en/headless)), and a granted MCP
tool would otherwise prompt. So: `--tools ""` removes everything, `--mcp-config` adds back exactly
the grant, `--allowed-tools` keeps the granted calls from prompting, `dontAsk` denies anything that
still would. `permission_denials` on the `result` message is a free record of every refusal.

*Not used*: `--permission-prompts none` would be the belt to that braces, but it "requires Claude
Code v2.1.259 or later" and 2.1.240 rejects it with an unknown-option error. Nothing needs it: with
`--tools ""` there is nothing left to prompt for.

**2. No settings picked up from the machine (Constitution V.1).** `--setting-sources` takes a
"Comma-separated list of setting sources to load (user, project, local)"; empty loads none. The
danger this closes is real — the headless documentation is explicit that "Without `--bare`,
`claude -p` loads the same context an interactive session would, including anything configured in
the working directory or `~/.claude`", and that a `-p` session "runs the hooks in a project's
`.claude/settings.json` and connects the servers in its `.mcp.json`, even in a folder you've never
trusted" ([headless](https://code.claude.com/docs/en/headless)). A `CLAUDE.md` lying in the wiki
would otherwise put text into the agent's prompt, which V.1 forbids.

Observed, twice in the same directory, whose `CLAUDE.md` carried a codeword:

```
… --tools "" --setting-sources "" --strict-mcp-config   → "No codeword or project instructions in the provided context."
… --tools "" (no --setting-sources)                     → "Yes, I see project instructions with the codeword ZEPHYR-9."
```

*Not used*: `--bare`, which would also cover this, because "bare mode doesn't use your subscription
login" and "never reads OAuth credentials or the system keychain"
([headless](https://code.claude.com/docs/en/headless)). That would break DEC-001. Belt and braces
instead: `--setting-sources ""`, `--strict-mcp-config`, and a working directory Grimoire owns rather
than the wiki — the agent reaches the wiki only through the granted MCP tools, so it needs no file
access to it at all.

**3. Streamed token counts (GUARD-004).** `--include-partial-messages` "Include partial streaming
events in output. Requires `--print` and `--output-format stream-json`"
([cli-reference](https://code.claude.com/docs/en/cli-reference)). The probe saw three
`stream_event` events whose `event.type` was `message_delta`, each carrying a `usage` that grew
during the response, and a final `result` carrying `usage.output_tokens`, `cache_*` counts and a
per-model `modelUsage` breakdown. The hub totals the deltas during the run and reconciles against
the `result` at the end.

*Not used*: `--max-budget-usd`, "Maximum dollar amount to spend on API calls before stopping". It is
currency, and the documented figures are "client-side estimates" that "can differ from your actual
bill" ([headless](https://code.claude.com/docs/en/headless)). GUARD-004 counts model tokens. Also
not used: `--max-turns`, which is a turn ceiling and neither of the two the spec names.

**4. A second message to the running agent (RUNS-005's nudge).** With
`--input-format stream-json`, the parent writes further user messages on stdin and the same session
continues. Observed: after the first `result`, a second user message was written; the run produced a
second `result` under the same `session_id`, and its text referred correctly to the tool it had
called in the first turn. That is exactly the nudge: the hub sees the agent stop, finds no log entry,
writes one more user message, and the agent continues inside the ceilings.

**5. A stop (GUARD-004).** Two mechanisms, both verified:

- *Graceful, mid-call*: `{"type":"control_request","request_id":…,"request":{"subtype":"interrupt"}}`
  on stdin. The probe interrupted a long response 3.8 s in; 0.9 s later the run emitted
  `{"subtype":"error_during_execution","terminal_reason":"aborted_streaming","output_tokens":0}`,
  the process stayed alive, answered one more message, and exited 0 when stdin closed. This is what
  the Agent SDK's `interrupt()` sends, and `system/init` advertises it in `capabilities`
  (`interrupt_receipt_v1`), which the documentation recommends feature-detecting on rather than
  comparing versions ([headless](https://code.claude.com/docs/en/headless)).
- *Backstop*: kill the process. Documented: "If you stop a `claude -p` run with SIGTERM … Claude
  Code exits with code 143 … leaves the turn that was in progress unfinished". The hub uses the
  interrupt first and the signal only if the process does not end.

**6. A pinned model ID under the subscription sign-in (DEC-001, GUARD-004).** A run must be
reproducible and its cost attributable, and neither survives an alias: `opus` and `sonnet` follow
whatever Anthropic points them at, and the default follows the owner's own `model` setting, which is
a machine setting of exactly the kind `--setting-sources ""` exists to keep out. So **the model is
passed by pinned ID and never by alias or default** — `--model claude-haiku-4-5-20251001`, not
`--model haiku`.

Observed, with `ANTHROPIC_API_KEY` removed from the child's environment:

```
env -u ANTHROPIC_API_KEY claude -p --output-format json --tools "" --setting-sources "" \
  --strict-mcp-config --no-session-persistence --model claude-haiku-4-5-20251001 "Reply with exactly: PINNED"

→ result       "PINNED"
  apiKeySource  null                       ← the subscription sign-in, not an API key
  modelUsage    { "claude-haiku-4-5-20251001": {
                    "inputTokens": 10, "outputTokens": 42, "thinkingTokens": 33,
                    "cacheReadInputTokens": 0, "cacheCreationInputTokens": 6612,
                    "canonicalModel": "claude-haiku-4-5", "provider": "firstParty" } }
```

The key of the `modelUsage` entry is the ID that was asked for, so the served model is verifiable
after the fact and at the `result` of every run — which is the assertion the Contract test makes.

**`HarnessProcess` removes `ANTHROPIC_API_KEY` from the child's environment.** If the variable is set
on the machine — and it may be, for reasons that have nothing to do with Grimoire — the CLI would
bill the owner per token through an API key instead of the subscription, which is the one thing
DEC-001 rules out. Unsetting it for the child is a one-line guarantee that the run is on the
subscription or does not start at all. The probe above was run that way.

### What this changes

- **The TypeScript harness is dropped**, and with it `@anthropic-ai/claude-agent-sdk` and the `npm`
  toolchain for it. The adapter is `Grimoire.Agent/Adapters/HarnessProcess.cs`, and the `claude`
  binary is the external system it wraps (Constitution V.2).
- **Nothing is lost against the earlier design.** The TypeScript SDK is itself a wrapper that spawns
  this same CLI; the harness it required "decided nothing" (R-02) and therefore had nothing to test
  (III.8). Removing it removes a process, a language and a package manager without removing a
  decision.
- **DEC-001 holds**, in its letter as the owner has since reworded it — "through the Claude Code
  Cli or Claude Agent SDK with the owner's subscription sign-in" — so this plan departs from nothing.
  The CLI is the subscription path: the probe ran with `apiKeySource: "none"` in
  `system/init` and no `ANTHROPIC_API_KEY` in the environment, the adapter having removed it. DEC-001's reason names "Claude Code
  and the Agent SDK" as the two places subscription authentication is available, and this is the
  first of them. The fallback DEC-001 names — an API-key adapter behind the same port — stays
  available: `IAgentHarness` is that port, and an API-key adapter would sit beside `HarnessProcess`.

**Limits of the evidence, stated plainly**: the probes used a stdio MCP server, not the streamable
HTTP one the hub will serve, and they used the stub tool rather than the real wiki tools. The
transport is not what any of the five findings turn on, but it is not *proven* here — that is what
the Contract test in R-09 is for, and it is the first test to write.

---

## R-01 — Stack

**Decision**: .NET 10 (ASP.NET Core) for the hub, the tools and every test suite. One static page
with one script for the browser, served by the hub. No TypeScript, no `npm`, no bundler. The model
is reached by spawning the `claude` CLI (R-11).

**Rationale**: every requirement proven by `test` — INGEST, WIKI, GUARD, ACCESS, RUNS — lands in the
hub under R-02, so the hub's language is where the tests are. Once R-11 removed the SDK package, the
only remaining reason for a second toolchain was the front end, and R-10 removes that too. A second
toolchain with no requirement behind it is a mechanism with no consumer (II.1).

**Alternatives considered**:

- *Whole stack in TypeScript* — one language, first-class SDK. Rejected by the owner; the hub is
  where the owner's tooling knowledge sits.
- *Keep the TypeScript harness anyway* — rejected by R-11's evidence: it has no decisions of its
  own, so it buys a language and a package manager for nothing.

**Consequence**: one toolchain. `dotnet` builds the hub, the tools and all three test suites. The
`claude` CLI is a runtime prerequisite, not a build dependency.

---

## R-02 — Tool ownership: the hub serves MCP, the agent process is wiring

**Decision**: The hub exposes the wiki tools as an MCP server over streamable HTTP at a per-run
endpoint. `HarnessProcess` points the CLI at that endpoint with `--mcp-config` and
`--strict-mcp-config`. Provenance stamping (WIKI-002), the grant (GUARD-001/002), the grant record
(GUARD-003) and the ceilings (GUARD-004) are all hub-side.

**Rationale**: it puts every decision in one language, where Fast tests prove it. The agent process
is left with no judgment of its own. Constitution III.8 excludes dependency wiring from testing, so
what remains outside the hub has nothing to test, and `trace-check` needs to read only one test
format (R-05).

**Transport**: streamable HTTP (`"type": "http"`), not stdio. A stdio server would make the CLI spawn
a *second* .NET process per run, which would then need its own wiki path, run identifier and
configuration. Over HTTP the hub is already running, already holds the run context, and the run
identifier sits in the URL. The C# side is `ModelContextProtocol.AspNetCore`:
`AddMcpServer().WithHttpTransport()` plus `app.MapMcp(pattern)`.

**Per-run endpoint, unauthenticated**: the endpoint is `/mcp/runs/{runId}`, bound to loopback, with
no bearer token. `docs/product.md` §2 puts Grimoire inside a network the user trusts and gives it no
access control of its own; a token here would be a mechanism with no consumer (II.1) and a second
half-built access story. The run identifier in the path is addressing, not authorisation. Recorded
as a decision in the plan, and it binds later features: the first feature that puts Grimoire on a
network the user does *not* trust (OUT-10, OUT-12) has to revisit it.

**Alternatives considered**:

- *The agent process owns the tools* — fewer moving parts at runtime, but WIKI-002 and the GUARD
  requirements would move outside the hub, into a component with no test suite.
- *Hub stamps, agent process serves the tool surface* — collapses into this option with a chattier
  contract.

---

## R-03 — Deny-by-default tools (GUARD-001, GUARD-002)

Superseded in mechanism by R-11, which decides it against the CLI. The finding that shaped both is
worth keeping: **an allow-list of tool names is not a grant**. `--allowedTools` (and the SDK's
`allowedTools`) auto-approves what it names and leaves everything else available — the CLI reference
says to use `--tools` instead when the goal is restricting what exists. The grant is therefore the
*tool surface*, not a permission list:

| Layer | Flag | What it buys |
| --- | --- | --- |
| The surface | `--tools ""` | No built-in tool exists in the session. Deny-by-default, not deny-by-enumeration |
| The grant | `--mcp-config` + `--strict-mcp-config` | The only tools that exist are the ones the hub serves for this run |
| No prompting on the grant | `--allowed-tools "mcp__wiki__*"` | Granted calls run instead of being denied by `dontAsk` |
| Everything else | `--permission-mode dontAsk` | Anything that would prompt is denied, not hung |
| No leaks | `--setting-sources ""` | The machine's settings, hooks and `.mcp.json` stay out |

**Where it is proven** (plan item: twice): at Fast level in the hub — the endpoint serves exactly the
granted tools and the grant is recorded (GUARD-002, GUARD-003) — and at Contract level against the
real CLI, by asserting `system/init`'s `tools` array equals the grant and that a run told to use a
tool outside the grant produces no such tool call and no effect. The deny configuration is a decision
we made, not framework behaviour, so III.8 does not exclude it.

---

## R-04 — The two ceilings (GUARD-004)

**Decision**: the hub holds both ceilings and both decisions; `HarnessProcess` reports and obeys.

- **Cost**: token counts read from the CLI's own stream. `--include-partial-messages` gives
  `message_delta` events with a growing `usage` during a response (R-11); the `result` message
  carries the authoritative totals.
- **Elapsed time**: measured by the hub against `TimeProvider` (R-12).
- **At either ceiling the hub sends the `interrupt` control request**, which stops the run at once,
  a model call in flight included (R-11), and falls back to killing the process. The run ends failed.

**Why one mechanism for both.** Inside a single turn the agent loops model call → tool call → model
call by itself. A caller watches the usage grow but has no way to veto the next call short of ending
the turn, so "prevent the next call without touching the one in flight" is not something the CLI
offers. The alternative — wait for the turn's `result` and send nothing further — would let a ceiling
overrun by a whole turn's worth of calls. GUARD-004 was rewritten to say what is actually done: at
either ceiling the run stops at once, a call in flight included, and ends failed. What the run had
already written through the tools stays in the wiki (WIKI-003); what it loses is the partial response
it was producing.

### What the cost ceiling counts

**OWNER DECISION — every token the run causes.** Not only the main model's, and not only the
assistant's visible response: the CLI spends tokens on background calls of its own, and those are the
run's doing too. The authority is the `result` message's **`modelUsage`** map, which is keyed by
model ID and covers every model the run touched.

Per entry in `modelUsage`, the four fields that are counted:

| Field | What it is |
| --- | --- |
| `inputTokens` | prompt tokens neither read from nor written to the cache |
| `outputTokens` | everything generated, **thinking included** — `thinkingTokens` is a breakdown of this number, not an addition to it |
| `cacheReadInputTokens` | context re-read on each call; on a many-turn run this is the largest of the four by far |
| `cacheCreationInputTokens` | context written into the cache |

**The run's cost is the sum of those four across every entry in `modelUsage`.** The same quantities
appear on the top-level `usage` object as `input_tokens`, `output_tokens` (with
`output_tokens_details.thinking_tokens` as its breakdown), `cache_read_input_tokens` and
`cache_creation_input_tokens` (with `cache_creation.ephemeral_5m_input_tokens` and
`cache_creation.ephemeral_1h_input_tokens` as its split, not as extra tokens) — but that object
covers the main model's stream alone. So `usage` is what the *live* counter adds up during the run,
and `modelUsage` is what the hub reconciles against when the `result` arrives. The remaining fields
on `usage` — `server_tool_use`, `service_tier`, `inference_geo`, `speed`, `iterations` — carry no
tokens and are not counted.

**Background calls are real, and observed.** A probe whose main model was Opus came back with two
entries in `modelUsage`: `claude-opus-5[1m]` at 2 input / 4 output / 3 514 cache-creation tokens, and
`claude-haiku-4-5-20251001` at 897 input / 12 output — a call the run never asked for, and one a hub
counting only the main model would have missed entirely.

**The live counter under-counts, by design.** `message_delta` events cover the streamed response and
nothing else, so a background call appears only in the `result`. The hub therefore treats the live
total as a floor: it interrupts as soon as that floor crosses the ceiling, and records the reconciled
`modelUsage` total at the end, which may exceed the ceiling by whatever the last turn's background
calls cost. A ceiling whose job is to stop a runaway run does not have to be exact to the token.

### The initial ceiling values

Fixed values, not settings (`docs/product.md` §4). **The owner revises both after the acceptance run**,
against what one real ingest actually costs — that run is the first honest measurement, and until it
happens these are reasoned estimates.

| Ceiling | Initial value | Reasoning |
| --- | --- | --- |
| Cost | **2 000 000 tokens** | The floor is measured: a trivial one-turn run cost 6 664 tokens, nearly all of it the one-off cache creation of the system prompt. A real ingest reads a few pages and writes a few more — call it 20 to 40 model calls over a context of a few tens of thousands of tokens — and `cacheReadInputTokens` then dominates at roughly 30 k × 30 ≈ 900 k, with output a rounding error beside it. Two million is about double the expected run, and still stops a loop that has stopped making progress |
| Elapsed time | **15 minutes** | Those same 20 to 40 calls take single-digit minutes once tool round trips are counted. Nobody waits on a run (INGEST-001), so this ceiling exists to bound a stuck run, not to hurry a working one |

Both are constants in `Grimoire.Agent/Ceilings.cs`. Changing them is an owner decision and a code
change, which is what "fixed, not configurable" means here.

**Alternatives considered**: `--max-budget-usd` and `--max-turns`, both rejected in R-11 — currency
and client-side estimates in the first case, the wrong quantity in the second.

---

## R-05 — How a test carries its level and requirement ID, and how `trace-check` reads them

**Decision**: every test is an xunit v3 test in .NET, carrying
`[Trait("level", "fast|contract|e2e|deploy")]` and, where it proves a requirement,
`[Trait("req", "<CAPABILITY>-NNN")]`. `trace-check` reads those attributes out of the *built test
assemblies* with `System.Reflection.MetadataLoadContext` — metadata only, no test execution, no
runner involved.

**Rationale**: Constitution IV.3 demands one deterministic check that writes nothing and never asks
an agent. Reading assembly metadata is deterministic, needs no runner cooperation, and survives the
.NET 10 / Microsoft.Testing.Platform churn described below.

**The runner complication this avoids**: xunit v3's `-list full/json` — the obvious way to get
traits as JSON — is exposed by xunit's *native* entry point and is not available through the
Microsoft.Testing.Platform entry point (xunit issue #3529). Since .NET 10 dropped VSTest for MTP,
`dotnet test` runs through the MTP entry point. Binding `trace-check` to a runner flag would tie the
gate to whichever entry point happens to be configured. Reading metadata sidesteps the question.

**The browser tests are .NET too**: E2E runs through `Microsoft.Playwright.Xunit.v3`, which drives
a real browser against the running hub. This is what keeps `trace-check` to a single reader.

**Suites** are separate projects, so each can carry its own time budget:
`tests/Grimoire.Fast.Tests`, `tests/Grimoire.Contract.Tests`, `tests/Grimoire.E2E.Tests`.

**The four rules** are exactly the ones IV.3 names, and no more: a `test` requirement with no test;
a test carrying an unknown, retired or reserved id (`OUT-*` is reserved by I.2); a test with no
level; an E2E or Deploy test with no requirement id.

**Shown failing** (II.2): the gate counts only after a real violation. The PR adds a run of each —
`trace-check` against a test whose `req` trait names a nonexistent id, `time-budget` against a Fast
suite pushed over 15 s.

---

## R-06 — `time-budget` enforcement

**Decision**: each suite runs under the platform's own session timeout. Fast: `--timeout 15s`.
Contract: `--timeout 90s`. E2E and Deploy carry no budget (III.7 names only Fast and Contract). The
default test run is the Fast suite alone.

**Rationale**: III.7 says "the simplest means the stack offers, not purpose-built tooling".
`--timeout` is a first-class Microsoft.Testing.Platform option — "A global test execution timeout.
Takes one argument as string in the format `<value>[h|m|s]` where `<value>` is float"
([MTP CLI options](https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-cli-options#platform-options))
— it measures the test session rather than the build, and it fails the run. Nothing is written.

**Consequence**: with the TypeScript harness and the front-end build both gone (R-11, R-10), there is
no second test runner to give a second budget mechanism to, and no second trace format for
`trace-check` to read.

---

## R-07 — Which parts of OKF 0.2 apply

`docs/product.md` pins OKF 0.2. Constitution I.8 says nothing of the standard beyond the parts the
capability requirements name is built. The spec's assumption names six parts; these are the exact
field names in the pinned revision:

| Part named in the spec | OKF 0.2 field | Written by |
| --- | --- | --- |
| Every page carries its type | `type:` (string) | agent |
| Every page names its sources | `sources:` (list; each entry requires `resource`) | agent |
| Who generated a page and when | `generated: { by, at }` | **Grimoire** (WIKI-002) |
| Root index declares the standard's version | `okf_version: "0.2"` in the bundle-root `index.md` | agent |
| Every section has an index listing its pages | `index.md` per section directory | agent |
| The log records changes | `log.md` | agent |

Nothing else of OKF is built. `verified:`, `usage_count`, `usage_window` and the rest of the
optional surface are untouched — no requirement names them.

**What WIKI-002's two edge cases mean concretely**: "a page arrives without a place for the record"
is a page whose frontmatter has no `generated:` key — Grimoire adds it. "A place that cannot be
read" is frontmatter that does not parse as YAML, or a `generated:` that is not a mapping — the
write fails and the agent is told why. Grimoire parses the frontmatter and nothing else; the body
is opaque to it.

---

## R-08 — Where submissions and their states live

**Decision**: in a plain object owned by the RUNS context, in memory. **No storage port, no storage
adapter, no database.**

**Rationale**: RUNS-004 — the requirement that made a store necessary — moved to the follow-up
feature with the split. What is left is one process holding at most one run at a time
(`docs/product.md` §2, INGEST-005). An interface over an in-memory dictionary would have neither a
second implementation nor an outside system behind it, which is exactly what Constitution II.4 says
an interface is for; and the object itself would be a mechanism kept for a requirement this feature
does not have (II.1). Fast tests use the real object, not a double.

**What this costs, stated in the spec**: a stop loses the submissions. The spec's lifecycle answer
says so, and the owner accepted it.

**When the port arrives**: with RUNS-004, in the follow-up feature. That plan chooses the storage
technology; this one deliberately does not, so it decides nothing later work has to live with.

---

## R-09 — The Contract suites

**Decision**: two Contract suites, with different homes.

| Adapter | External thing | Where it runs |
| --- | --- | --- |
| `FileSystemWikiStore` | a real wiki directory on disk | CI, every run |
| `HarnessProcess` | the real `claude` CLI, with the owner's real sign-in | **locally, before the PR** — excluded from CI |

**Why no scripted Anthropic endpoint** (owner decision): the earlier draft pointed the SDK at a local
server replaying canned responses. That server is a stand-in we would write, run and maintain, and
every finding in R-11 that matters — the tool surface, the settings isolation, the usage stream, the
second message, the interrupt — is the CLI's behaviour, not the model's. Scripting the model does not
make those findings more true, and it adds a component with no requirement behind it (II.1).

**How they are excluded**: the three tests carry `[Trait("requires", "signin")]` alongside their
level and requirement traits, and CI runs the Contract suite with `--filter-not-trait "requires=signin"`
— a documented xunit v3 option under Microsoft.Testing.Platform
([xunit under MTP](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)). One marker,
named once in the CI workflow.

**The three tests** (at most three, by the owner's decision; each drives one real run of a
two-sentence text against a temporary wiki):

1. **Grant surface (GUARD-001, GUARD-002)** — dispatch a run; assert `system/init` reports
   `tools` equal to the grant and `mcp_servers` connected; assert the run can write a page through a
   granted tool.
2. **Outside the grant (GUARD-001)** — dispatch a run whose text asks the agent for something only a
   shell or a file tool could do; assert no tool call outside the grant occurs, the run ends, and
   nothing outside the wiki was touched.
3. **Nudge and stop (RUNS-005, GUARD-004)** — dispatch a run, withhold nothing, then after the
   agent's first stop send the nudge and observe it continue; then send the stop and observe the run
   end failed inside the ceiling.

**What CI loses**: CI has no subscription sign-in, so it cannot run these three, and their execution
time is not measured there (III.7). The CI Contract run still carries and fails on the 90 s budget
for the suite it does run; the three are run locally under the same `--timeout 90s`. Recorded in
Complexity Tracking.

**Note on III.9**: nothing here is a double at an owned port. The real external thing runs.

---

## R-10 — Front end

**Decision**: one static HTML page with one script, served by the hub from `wwwroot/`. No Vite, no
`npm`, no build step. The page uses `fetch` against the JSON surface in
`contracts/hub-http-api.md` and polls for state.

**Rationale**: the feature's browser surface is one form (ACCESS-001) and one list of states
(ACCESS-002). A bundler for two files is a mechanism with no consumer (II.1), and it was the last
thing keeping `npm` in the tree after R-11 removed the harness. If a later feature earns a build
step, its plan records the departure (II.6).

**Consequence**: the page is static content the hub serves, so nothing about it is tested directly
(III.8); its two requirements are browser-observable and are proven by the E2E suite.

---

## R-12 — Elapsed time in tests

**Decision**: `TimeProvider` (built into .NET) wherever the elapsed-time ceiling is measured, with
`FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` in the Fast suite.

**Rationale**: GUARD-004's elapsed-time ceiling is the one requirement that would otherwise make a
test wait for real seconds, and the Fast budget is 15 s for the whole suite (III.7). `TimeProvider`
is the framework's own abstraction, so no interface of ours is created for it — II.4 would not allow
one, and III.8 means the provider itself is not what we test. No Fast test waits for real time.

---

## Open items carried into the plan

None. The two items the earlier draft carried are closed: the refusal during a run is now
INGEST-005, and the task estimate is inside the budget.
