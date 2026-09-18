/**
 * The model port and its Claude Agent SDK adapter (ADR-0003, plan V.2).
 *
 * The single port in the system, because the LLM is the single external system that is genuinely
 * doubled in tests — and it is doubled **at the wire**, through `ANTHROPIC_BASE_URL`, so the SDK's
 * own agent loop executes under test rather than a hand-rolled stand-in (ADR-0004). That is what
 * makes TS-06's "N tool calls across N iterations of one run" an assertion about the real loop.
 */

import { query } from "@anthropic-ai/claude-agent-sdk";
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
  let finalMessage = "";
  let failureReason: string | null = null;

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
      if (message.type === "assistant") {
        const text = message.message.content
          .map((block) => (block.type === "text" ? block.text : ""))
          .join("");

        // Verbatim: the message reaches the hub exactly as the model wrote it (contracts/
        // runner-protocol.md). Only the emptiness test trims — leading/trailing whitespace in an
        // otherwise-real message is not this adapter's call to remove.
        if (text.trim().length > 0) {
          finalMessage = text;
        }

        // Every tool_use block is an attempt, whether or not the SDK will dispatch it. A tool
        // whose definition was removed from the request is rejected as unknown before any
        // permission step, so this is the only place such an attempt can be seen — and an
        // operator needs to see what the agent reached for (FR-011, FR-021).
        for (const block of message.message.content) {
          if (block.type === "tool_use" && !ToolGuard.isGranted(block.name)) {
            request.guard.recordRefusedAttempt(block.name, block.input, block.id);
          }
        }
      }

      if (message.type === "result" && message.subtype !== "success") {
        failureReason = `The run ended without completing: ${message.subtype}.`;
      }
    }
  } catch (cause) {
    failureReason = (cause as Error).message;
  }

  return { finalMessage, failureReason };
}
