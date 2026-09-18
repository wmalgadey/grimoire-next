/**
 * The single sanctioned test double (ADR-0004, constitution III.2): an Anthropic-compatible HTTP
 * server the runner reaches through `ANTHROPIC_BASE_URL` — the same variable the egress proxy
 * occupies in production. The LLM is replaced at the wire rather than by a second adapter class,
 * so the SDK's own agent loop executes in every test.
 *
 * It is a server, not a client: this package has no Anthropic dependency.
 */

import { createServer, type IncomingMessage, type Server, type ServerResponse } from "node:http";
import type { AddressInfo } from "node:net";
import { scriptByName, type Script, type ScriptedTurn } from "./scripts.js";

/** What one request to the double looked like, recorded so tests can assert ordering. */
export interface RecordedRequest {
  /** Arrival timestamp. TS-07 compares this to the persisted grant and instruction version. */
  readonly at: Date;
  readonly method: string;
  readonly url: string;
  readonly headers: Readonly<Record<string, string | string[] | undefined>>;
  /** The raw bytes, so TS-04 can assert the source arrived whole (FR-029). */
  readonly bodyBytes: Buffer;
  readonly body: unknown;
}

/** A running double. */
export interface ScriptedModel {
  /** The value to hand the runner as `ANTHROPIC_BASE_URL`. */
  readonly baseUrl: string;
  /** Every request, in arrival order. */
  readonly requests: readonly RecordedRequest[];
  /** Replaces the script mid-run; used by suites that drive more than one run per server. */
  useScript(name: string): void;
  close(): Promise<void>;
}

interface AnthropicContentBlock {
  type: string;
  text?: string;
  id?: string;
  name?: string;
  input?: Record<string, unknown>;
}

interface RequestBody {
  stream?: boolean;
  messages?: { role?: string }[];
}

/**
 * Starts the double on an ephemeral port.
 *
 * @param scriptName Which named script to replay. See `scripts.ts`.
 */
export async function startScriptedModel(
  scriptName: string,
  listen: { host: string; port: number } = { host: "127.0.0.1", port: 0 },
): Promise<ScriptedModel> {
  let script: Script = scriptByName(scriptName);
  const requests: RecordedRequest[] = [];
  const variables = new Map<string, string>();

  const server: Server = createServer((request, response) => {
    void handle(request, response);
  });

  async function handle(request: IncomingMessage, response: ServerResponse): Promise<void> {
    const bodyBytes = await readBody(request);
    let body: unknown;
    try {
      body = JSON.parse(bodyBytes.toString("utf8")) as unknown;
    } catch {
      body = null;
    }

    requests.push({
      at: new Date(),
      method: request.method ?? "",
      url: request.url ?? "",
      headers: request.headers,
      bodyBytes,
      body,
    });

    // Introspection, so the C# suites can assert what the model received and when — TS-04's
    // "the bytes the runner received equal the bytes submitted" and TS-07's ordering comparison.
    // Not part of the Anthropic surface; a path the SDK never calls.
    if ((request.url ?? "").startsWith("/__requests")) {
      response.writeHead(200, { "content-type": "application/json" });
      response.end(
        JSON.stringify(
          requests
            .filter((recorded) => !recorded.url.startsWith("/__requests"))
            .map((recorded) => ({
              at: recorded.at.toISOString(),
              method: recorded.method,
              url: recorded.url,
              headers: recorded.headers,
              byteLength: recorded.bodyBytes.byteLength,
              body: recorded.bodyBytes.toString("utf8"),
            })),
        ),
      );
      return;
    }

    // Lets a suite drive more than one run against one hub, which has exactly one model
    // endpoint. Not part of the Anthropic surface; a path the SDK never calls.
    if ((request.url ?? "").startsWith("/__script/")) {
      const name = decodeURIComponent((request.url ?? "").slice("/__script/".length));
      try {
        script = scriptByName(name);
        response.writeHead(200, { "content-type": "application/json" });
        response.end(JSON.stringify({ script: name }));
      } catch (cause) {
        response.writeHead(400, { "content-type": "application/json" });
        response.end(JSON.stringify({ error: (cause as Error).message }));
      }

      return;
    }

    // Lets a suite aim a script at something only it knows — a canary path created for one test.
    // `{{name}}` in a scripted tool input is replaced with the value. Not part of the Anthropic
    // surface; a path the SDK never calls.
    if ((request.url ?? "").startsWith("/__var/")) {
      variables.set(decodeURIComponent((request.url ?? "").slice("/__var/".length)), bodyBytes.toString("utf8"));
      response.writeHead(200, { "content-type": "application/json" });
      response.end("{}");
      return;
    }

    if (!(request.url ?? "").includes("/messages")) {
      // Anything but the Messages API is a request this double does not model. Answering it with
      // a success would hide an egress the deny-all posture is supposed to make visible.
      response.writeHead(404, { "content-type": "application/json" });
      response.end(JSON.stringify({ type: "error", error: { type: "not_found_error" } }));
      return;
    }

    const parsed = (body ?? {}) as RequestBody;
    const turn = turnFor(script, parsed);

    if (!turn) {
      response.writeHead(400, { "content-type": "application/json" });
      response.end(
        JSON.stringify({
          type: "error",
          error: { type: "invalid_request_error", message: `Script '${script.name}' ran out of turns.` },
        }),
      );
      return;
    }

    if (turn.delayMs) {
      await new Promise((resolve) => setTimeout(resolve, turn.delayMs));
      if (response.writableEnded || response.destroyed) {
        return;
      }
    }

    if (turn.error) {
      response.writeHead(turn.error.status, { "content-type": "application/json" });
      response.end(
        JSON.stringify({ type: "error", error: { type: turn.error.type, message: turn.error.message } }),
      );
      return;
    }

    const message = messageFrom(turn, bodyBytes, variables);
    if (parsed.stream) {
      writeEventStream(response, message);
      return;
    }

    response.writeHead(200, { "content-type": "application/json" });
    response.end(JSON.stringify(message));
  }

  await new Promise<void>((resolve) => server.listen(listen.port, listen.host, resolve));
  const { port } = server.address() as AddressInfo;

  return {
    baseUrl: `http://${listen.host}:${port}`,
    requests,
    useScript(name: string) {
      script = scriptByName(name);
    },
    close: () =>
      new Promise<void>((resolve, reject) =>
        server.close((error) => (error ? reject(error) : resolve())),
      ),
  };
}

/**
 * Which turn answers this request.
 *
 * Derived from how many assistant turns the conversation already carries, never from a counter of
 * requests served. The SDK legitimately sends the same request more than once — a streaming
 * attempt and a non-streaming fallback, or a retry — and a counter would hand each of those a
 * different turn, silently skipping steps and making the loop under test not the loop that ran.
 */
function turnFor(script: Script, body: RequestBody): ScriptedTurn | undefined {
  const assistantTurns = (body.messages ?? []).filter((message) => message.role === "assistant").length;
  const turn = script.turns[assistantTurns];
  if (turn) {
    return turn;
  }

  return script.repeatLastTurn ? script.turns[script.turns.length - 1] : undefined;
}

/** Replaces `{{name}}` in every string of a scripted tool input. */
function substitute(value: unknown, variables: ReadonlyMap<string, string>): unknown {
  if (typeof value === "string") {
    return value.replace(/\{\{(\w+)\}\}/g, (whole, name: string) => variables.get(name) ?? whole);
  }

  if (Array.isArray(value)) {
    return value.map((item) => substitute(item, variables));
  }

  if (value !== null && typeof value === "object") {
    return Object.fromEntries(
      Object.entries(value).map(([key, item]) => [key, substitute(item, variables)]),
    );
  }

  return value;
}

function messageFrom(
  turn: ScriptedTurn,
  requestBytes: Buffer,
  variables: ReadonlyMap<string, string>,
): Record<string, unknown> {
  const content: AnthropicContentBlock[] = [];

  if (turn.text !== undefined) {
    // The echo-length script reports the prompt's byte length, so TS-04 can assert the source
    // reached the run whole rather than truncated (FR-029).
    const text =
      turn.text === "__ECHO_PROMPT_BYTES__" ? `prompt-bytes=${requestBytes.byteLength}` : turn.text;
    content.push({ type: "text", text });
  }

  for (const [index, use] of (turn.toolUses ?? []).entries()) {
    content.push({
      type: "tool_use",
      id: `toolu_scripted_${Date.now()}_${index}`,
      name: use.name,
      input: substitute(use.input, variables) as Record<string, unknown>,
    });
  }

  return {
    id: `msg_scripted_${Date.now()}`,
    type: "message",
    role: "assistant",
    model: "scripted-model",
    content,
    stop_reason: (turn.toolUses ?? []).length > 0 ? "tool_use" : "end_turn",
    stop_sequence: null,
    usage: { input_tokens: 0, output_tokens: 0 },
  };
}

/** Replays a complete message as the server-sent event stream the Messages API emits. */
function writeEventStream(response: ServerResponse, message: Record<string, unknown>): void {
  response.writeHead(200, {
    "content-type": "text/event-stream",
    "cache-control": "no-cache",
    connection: "keep-alive",
  });

  const send = (event: string, data: unknown): void => {
    response.write(`event: ${event}\ndata: ${JSON.stringify(data)}\n\n`);
  };

  const content = message["content"] as AnthropicContentBlock[];

  send("message_start", {
    type: "message_start",
    message: { ...message, content: [], stop_reason: null },
  });

  for (const [index, block] of content.entries()) {
    if (block.type === "text") {
      send("content_block_start", {
        type: "content_block_start",
        index,
        content_block: { type: "text", text: "" },
      });
      send("content_block_delta", {
        type: "content_block_delta",
        index,
        delta: { type: "text_delta", text: block.text ?? "" },
      });
    } else {
      send("content_block_start", {
        type: "content_block_start",
        index,
        content_block: { type: "tool_use", id: block.id, name: block.name, input: {} },
      });
      send("content_block_delta", {
        type: "content_block_delta",
        index,
        delta: { type: "input_json_delta", partial_json: JSON.stringify(block.input ?? {}) },
      });
    }

    send("content_block_stop", { type: "content_block_stop", index });
  }

  send("message_delta", {
    type: "message_delta",
    delta: { stop_reason: message["stop_reason"], stop_sequence: null },
    usage: { output_tokens: 0 },
  });
  send("message_stop", { type: "message_stop" });
  response.end();
}

function readBody(request: IncomingMessage): Promise<Buffer> {
  return new Promise((resolve, reject) => {
    const chunks: Buffer[] = [];
    request.on("data", (chunk: Buffer) => chunks.push(chunk));
    request.on("end", () => resolve(Buffer.concat(chunks)));
    request.on("error", reject);
  });
}

// Runnable standalone so the deployment suite can point a container at it. Loopback on an
// ephemeral port unless told otherwise; in a container it has to listen where the egress proxy
// can reach it (GRIMOIRE_SCRIPTED_MODEL_HOST=0.0.0.0, GRIMOIRE_SCRIPTED_MODEL_PORT=8787).
if (process.argv[1] && process.argv[1].endsWith("server.js")) {
  const name = process.env["GRIMOIRE_SCRIPT"] ?? "read-then-write";
  const listen = {
    host: process.env["GRIMOIRE_SCRIPTED_MODEL_HOST"] ?? "127.0.0.1",
    port: Number(process.env["GRIMOIRE_SCRIPTED_MODEL_PORT"] ?? "0"),
  };
  void startScriptedModel(name, listen).then((model) => {
    process.stdout.write(`${JSON.stringify({ baseUrl: model.baseUrl, script: name })}\n`);
  });
}
