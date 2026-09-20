# Contract: Hub ↔ the `claude` CLI

The hub spawns the `claude` CLI as a child process per run and speaks newline-delimited JSON to it:
the hub writes messages on stdin, the CLI writes events on stdout. There is no harness of ours in
between — `Grimoire.Agent/Adapters/HarnessProcess.cs` is the only place this process and its protocol
appear (Constitution V.2).

**The CLI decides nothing about the run.** Every judgment — both ceilings, the run's state, the
log-entry nudge — is the hub's. Everything the CLI may touch is the grant the hub serves it
(research.md R-02, R-11).

---

## How the process is started

One process per run, with a working directory Grimoire owns — **not** the wiki, which the agent
reaches only through the granted tools.

```
claude -p
  --input-format stream-json --output-format stream-json --verbose
  --include-partial-messages
  --model claude-<model>-<yyyymmdd>        # a pinned id, never an alias, never the default
  --tools ""
  --mcp-config '{"mcpServers":{"wiki":{"type":"http","url":"http://127.0.0.1:5199/mcp/runs/8f3c…"}}}'
  --strict-mcp-config
  --allowed-tools "mcp__wiki__*"
  --permission-mode dontAsk
  --setting-sources ""
  --no-session-persistence
```

`ANTHROPIC_API_KEY` is **removed from the child's environment**. If it were set on the machine the
CLI would bill per token through an API key instead of the subscription, which is what DEC-001 rules
out; unsetting it means the run is on the subscription or does not start (research.md R-11).

| Flag | Why (evidence in research.md R-11) |
| --- | --- |
| `--tools ""` | No built-in tool exists in the run. The grant is the whole tool surface — deny-by-default by construction (GUARD-001) |
| `--mcp-config` + `--strict-mcp-config` | The only tools that exist are the ones the hub serves for this run. No MCP configuration from the machine |
| `--allowed-tools "mcp__wiki__*"` | Granted calls run instead of being denied by `dontAsk` |
| `--permission-mode dontAsk` | Anything that would prompt is denied, not hung — nobody is watching a run |
| `--setting-sources ""` | No settings, hooks or `CLAUDE.md` from the machine reach the prompt (Constitution V.1) |
| `--model <pinned id>` | A run is reproducible and its cost attributable only if the model is fixed. An alias follows whatever it is pointed at, and the default follows the owner's own setting — a machine setting of the kind `--setting-sources ""` exists to keep out (R-11) |
| `--include-partial-messages` | Token counts during the run, for the cost ceiling (GUARD-004) |
| `--input-format stream-json` | Lets the hub send a second message to the running agent — the nudge (RUNS-005) |
| `--no-session-persistence` | A run is not resumable; nothing of it is written outside the wiki |

No `--max-budget-usd` (currency, and a client-side estimate) and no `--max-turns` (the wrong
quantity). GUARD-004 counts model tokens and elapsed time, and the hub counts both.

---

## Hub → CLI (stdin)

### `dispatch` — the first user message, exactly once

What a run is given (INGEST-002). Instruction, purpose description, submitted text and the run's
identifier all arrive here; nothing else puts text into the prompt (Constitution V.1). The pinned
id after `--model` above is the one the hub was started with — the model is a start-up input, not a
per-submission choice.

```json
{ "type": "user",
  "message": { "role": "user",
               "content": "…instruction…\n\n…purpose description…\n\nRun id: 8f3c…\n\n…submitted text…" } }
```

The run identifier is also in the MCP URL, which is how a tool call is attributed to its run.

### `nudge` — a further user message

RUNS-005's single nudge, sent after the agent has stopped with no log entry for the run. The agent
continues in the same session, inside the same ceilings.

```json
{ "type": "user",
  "message": { "role": "user",
               "content": "No log entry for this run was found in log.md." } }
```

### `stop` — a control request

```json
{ "type": "control_request", "request_id": "stop-1", "request": { "subtype": "interrupt" } }
```

Sent at **either** ceiling. The CLI answers with a `control_response` and emits a `result` whose
`terminal_reason` is `aborted_streaming`; the run ends failed (GUARD-004).

One mechanism serves both because it is the only lever a caller has inside a turn: the agent loops
model call → tool call → model call on its own, so nothing can prevent the next call without ending
the one in flight. See research.md R-04.

If the process does not end after its stdin is closed, the hub kills it. That is the backstop, not
the mechanism: a signal leaves the turn unfinished, whereas the interrupt ends it.

---

## CLI → hub (stdout)

### `system` / `init` — first line

```json
{ "type": "system", "subtype": "init",
  "session_id": "…", "tools": ["mcp__wiki__list_pages", "…"],
  "mcp_servers": [{ "name": "wiki", "status": "connected" }],
  "capabilities": ["interrupt_receipt_v1", "…"] }
```

Read before the run is allowed to proceed. The hub asserts that `tools` equals the grant exactly; if
the agent reports any tool outside it, **the run ends failed here, before its first model call**
(GUARD-001). `mcp_servers` must show the `wiki` server connected, and `capabilities` must contain
`interrupt_receipt_v1` — feature-detection for the stop, in place of comparing version strings.

This is also the moment a submission stops reading `submitted` and starts reading `running`: the
agent has reported in (spec, Key Entities).

### `stream_event` — during a response

```json
{ "type": "stream_event",
  "event": { "type": "message_delta",
             "usage": { "input_tokens": 10, "output_tokens": 126, "cache_read_input_tokens": 0 } } }
```

The input for the hub's cost ceiling. Output counts are read here rather than off assistant
messages, which is what `--include-partial-messages` is for.

### `assistant` — tool calls and text

Read for `tool_use` blocks, which is how the hub sees what the agent reached for. Anything outside
the grant cannot appear, because it does not exist in the run.

### `result` — last line of a turn, once per turn

```json
{ "type": "result", "subtype": "success",
  "terminal_reason": "completed",
  "usage": { "input_tokens": 40112, "output_tokens": 3190,
             "cache_read_input_tokens": 22016, "cache_creation_input_tokens": 7228 },
  "modelUsage": { "claude-<model>-<yyyymmdd>": { "inputTokens": 40112, "outputTokens": 3190,
                                                 "cacheReadInputTokens": 22016,
                                                 "cacheCreationInputTokens": 7228 },
                  "claude-haiku-4-5-20251001": { "inputTokens": 897, "outputTokens": 12,
                                                 "cacheReadInputTokens": 0,
                                                 "cacheCreationInputTokens": 0 } },
  "permission_denials": [] }
```

A `result` means the agent has stopped. The hub then reads `log.md` and decides. **An entry is
present when `log.md` contains the run's identifier as plain text** — nothing else is parsed
(data-model.md §Log).

| What the hub sees | What it does |
| --- | --- |
| Entry present, both ceilings clear | Run ends `done` (RUNS-005) |
| No entry, both ceilings clear, not yet nudged | Sends the nudge; the run stays `running` (RUNS-005) |
| No entry, both ceilings clear, already nudged | Run ends `failed` (RUNS-005) |
| Either ceiling reached | Run ends `failed`, whatever the log says (GUARD-004) |
| `terminal_reason: "aborted_streaming"`, or a non-zero exit | Run ends `failed` |

**`modelUsage` is what the run's cost is reconciled against** — the sum of `inputTokens`,
`outputTokens`, `cacheReadInputTokens` and `cacheCreationInputTokens` across **every** entry, the
CLI's own background calls included. The second entry above is such a call: the run never asked for
it. `usage` covers the main model's stream alone and is what the live counter adds up while the turn
runs; see research.md R-04 for why the live total is a floor. The key of each `modelUsage` entry also
says which model actually served the run, which is what the Contract test compares against the
pinned id.

`permission_denials` lists anything refused during the turn — evidence for GUARD-001 that costs
nothing to keep.

Whatever the run had already written stays in the wiki in every failed case (WIKI-003).
