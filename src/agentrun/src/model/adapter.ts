/**
 * The model port and its Claude Agent SDK adapter (ADR-0003, plan V.2).
 *
 * The single port in the system, because the LLM is the single external system that is genuinely
 * doubled in tests — and it is doubled **at the wire**, through `ANTHROPIC_BASE_URL`, so the SDK's
 * own agent loop executes under test rather than a hand-rolled stand-in (ADR-0004). That is what
 * makes TS-06's "N tool calls across N iterations of one run" an assertion about the real loop.
 */

import {
  query,
  type SDKAssistantMessage,
  type SDKMessage,
  type SDKResultMessage,
} from "@anthropic-ai/claude-agent-sdk";
import type { SystemPrompt } from "../instruction/systemPrompt.js";
import { GRANTED_TOOLS, ToolGuard } from "../wiki-tools/guard.js";
import { createWikiToolServer } from "../wiki-tools/server.js";

/**
 * Every built-in tool, named individually so its definition never reaches the model.
 *
 * Listed one by one and **never** as the wildcard `"*"`: the wildcard would strip the granted pair
 * along with everything else, leaving an agent with no way to do its job and a run that looks
 * broken rather than contained (research R3).
 *
 * A list is a thing that rots — a built-in added by a later SDK release would arrive in the
 * request unnamed and therefore unremoved. That is why the containment gate asserts the *request*
 * carries exactly the granted pair rather than trusting this list to be complete: when the SDK
 * grows a tool, a test fails and this list is updated, instead of the agent's reach quietly
 * widening (FR-010, SC-004).
 */
export const DISALLOWED_BUILT_INS = [
  "Agent",
  "AskUserQuestion",
  "Bash",
  "BashOutput",
  "CronCreate",
  "CronDelete",
  "CronList",
  "Edit",
  "EnterWorktree",
  "ExitPlanMode",
  "ExitWorktree",
  "Glob",
  "Grep",
  "KillShell",
  "ListAgents",
  "ListMcpResources",
  "MultiEdit",
  "NotebookEdit",
  "NotebookRead",
  "Read",
  "ReadMcpResource",
  "ReportFindings",
  "ScheduleWakeup",
  "SendMessage",
  "Skill",
  "SlashCommand",
  "Task",
  "TodoWrite",
  "ToolSearch",
  "WebFetch",
  "WebSearch",
  "Workflow",
  "Write",
] as const;

/** How one run of the model ended. */
export interface ModelRunResult {
  /** The agent's final message text, verbatim — the run's commit message (research R9). */
  readonly finalMessage: string;
  /** Set when the run could not complete. */
  readonly failureReason: string | null;
  /**
   * Set when the model endpoint ended the run by answering with an error: the HTTP status, or
   * `no-response`. Null when the run ended on its own account (plan IV).
   */
  readonly modelEndpointStatus: string | null;
}

/** What the model port needs to run one ingest. */
export interface ModelRunRequest {
  /** The instruction file's content, and the only thing given as instruction (constitution I.2). */
  readonly systemPrompt: SystemPrompt;
  /** The source, whole — no size limit, no truncation, no summarisation (FR-029). */
  readonly sourceText: string;
  /** The wiki working tree; the agent's entire filesystem world. */
  readonly repositoryRoot: string;
  /** Records and decides every call before it acts (ADR-0009). */
  readonly guard: ToolGuard;
  /** The tool-call half of the run limit (FR-009). */
  readonly maxToolCalls: number;
}

/**
 * Runs one ingest as a real tool-use loop and returns how it ended.
 *
 * The system prompt is a `SystemPrompt`, not a string: the SDK's `claude_code` preset is
 * deliberately not used, so nothing outside the instruction file reaches the model as instruction
 * (plan I, ADR-0003).
 */
export async function runModel(request: ModelRunRequest): Promise<ModelRunResult> {
  const wikiTools = createWikiToolServer(request.repositoryRoot, request.guard);
  const run = new RunObservation(request.guard);

  // The guard refuses every call past the ceiling; stopping the conversation there is what keeps
  // the model from being asked for another turn the run is not allowed to act on (FR-009).
  const abortController = new AbortController();
  request.guard.onCeiling = () => abortController.abort();

  try {
    const conversation = query({
      prompt: request.sourceText,
      options: {
        systemPrompt: request.systemPrompt,
        cwd: request.repositoryRoot,
        // No settings file, no project configuration, no user configuration: the run's
        // instruction is the instruction file and nothing else (constitution I.2).
        settingSources: [],
        // The runner's environment is the one the hub composed; tool search is switched off on top
        // of it. It is on by default against the real API, where it defers the granted tools
        // behind a search tool outside the grant, so the agent could call neither (FR-010).
        env: { ...process.env, ENABLE_TOOL_SEARCH: "false" },
        mcpServers: { wiki: wikiTools },
        allowedTools: [...GRANTED_TOOLS],
        disallowedTools: [...DISALLOWED_BUILT_INS],
        // Deny-by-default with no human to ask: anything outside the grant is refused by the
        // PreToolUse hook, and recorded (FR-011).
        permissionMode: "dontAsk",
        maxTurns: request.maxToolCalls + 1,
        abortController,
        hooks: {
          PreToolUse: [
            {
              hooks: [
                async (input) => {
                  if (input.hook_event_name !== "PreToolUse") {
                    return {};
                  }

                  const decision = request.guard.decide(
                    input.tool_name,
                    input.tool_input,
                    input.tool_use_id,
                  );
                  return decision.allow
                    ? {}
                    : {
                        hookSpecificOutput: {
                          hookEventName: "PreToolUse" as const,
                          permissionDecision: "deny" as const,
                          permissionDecisionReason: decision.reason ?? "tool not granted",
                        },
                      };
                },
              ],
            },
          ],
        },
      },
    });

    for await (const message of conversation) {
      run.observe(message);
    }
  } catch (cause) {
    run.failWith((cause as Error).message);
  }

  return run.result();
}

/**
 * What the conversation has said about the run so far, one message at a time. Each kind of message
 * the run's outcome depends on has its own method, so no one of them has to know about the others.
 */
class RunObservation {
  private finalMessage = "";
  private finalTurnId: string | null = null;
  private failureReason: string | null = null;
  private apiError: { status: string; detail: string } | null = null;
  private lastRetryStatus: string | null = null;

  constructor(private readonly guard: ToolGuard) {}

  observe(message: SDKMessage): void {
    if (message.type === "system" && message.subtype === "api_retry") {
      this.lastRetryStatus = message.error_status === null ? "no-response" : String(message.error_status);
    } else if (message.type === "user") {
      // A tool result closes the turn that asked for it. Whatever that turn said was narration,
      // and a final turn with no text — which the SDK may not yield as a message at all — must
      // not inherit it.
      this.finalTurnId = null;
      this.finalMessage = "";
    } else if (message.type === "assistant") {
      this.observeAssistant(message);
    } else if (message.type === "result") {
      this.observeResult(message);
    }
  }

  failWith(reason: string): void {
    this.failureReason = reason;
  }

  result(): ModelRunResult {
    const modelEndpointStatus = this.apiError !== null ? this.apiError.status : null;
    const failureReason =
      modelEndpointStatus === null
        ? this.failureReason
        : `The model endpoint answered ${modelEndpointStatus === "no-response" ? "with no response" : `HTTP ${modelEndpointStatus}`}`
          + ` (${this.apiError!.detail}). This is the egress path or the upstream behind it, not the agent.`;

    return {
      finalMessage: this.finalMessage,
      failureReason,
      modelEndpointStatus,
    };
  }

  private observeAssistant(message: SDKAssistantMessage): void {
    // An API error arrives as a synthetic assistant message carrying the error's text. That text
    // is the SDK's, not the agent's, so it is never the run's final message.
    if (message.error !== undefined) {
      return;
    }

    // The final message is the last turn's text and nothing earlier: narration that came with a
    // tool call is not a commit message, so a final turn with no text leaves this empty and the
    // hub falls back to its fixed one (research R9). One turn can arrive as several messages
    // sharing an id, so a turn's text accumulates until the id changes. Verbatim otherwise: the
    // message reaches the hub exactly as the model wrote it (contracts/runner-protocol.md).
    if (message.message.id !== this.finalTurnId) {
      this.finalTurnId = message.message.id;
      this.finalMessage = "";
    }

    this.finalMessage += message.message.content
      .map((block) => (block.type === "text" ? block.text : ""))
      .join("");

    // Every tool_use block is an attempt, whether or not the SDK will dispatch it. A tool whose
    // definition was removed from the request is rejected as unknown before any permission step,
    // so this is the only place such an attempt can be seen — and an operator needs to see what
    // the agent reached for (FR-011, FR-021).
    for (const block of message.message.content) {
      if (block.type === "tool_use" && !ToolGuard.isGranted(block.name)) {
        this.guard.recordRefusedAttempt(block.name, block.input, block.id);
      }
    }
  }

  private observeResult(message: SDKResultMessage): void {
    if (message.subtype !== "success") {
      this.failureReason = `The run ended without completing: ${message.subtype}.`;
    } else if (message.is_error) {
      // A turn that ended on an API error reports `success` with `is_error` set; left there, it
      // would read as an agent that judged nothing needed changing.
      const status =
        message.api_error_status === null || message.api_error_status === undefined
          ? this.lastRetryStatus ?? "no-response"
          : String(message.api_error_status);
      this.apiError = { status, detail: message.result };
      this.failureReason = message.result;
    }
  }
}
