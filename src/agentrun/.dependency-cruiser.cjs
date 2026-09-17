/**
 * Quality gate 2, TypeScript layer (constitution V.3, plan "Architecture").
 *
 * Adapter confinement: the LLM is an external system and is reached through the model port.
 * `query()` — the call that actually talks to the model — lives only in `src/model/`, which is
 * what makes "the model port is the single port" true and what the scripted double replaces.
 *
 * The SDK's in-process MCP registration helpers (`createSdkMcpServer`, `tool`) are a different
 * thing: they declare what the agent may do, which is `src/wiki-tools/`'s subject and the thing
 * ADR-0009 is about. Routing them through `src/model/` would mean wrapping a framework to satisfy
 * a rule (constitution VII.1), so the rule names the boundary it actually protects: nothing
 * outside `src/model/` may call the model, and nothing outside `src/model/` and `src/wiki-tools/`
 * may reach for the SDK at all.
 *
 * @type {import('dependency-cruiser').IConfiguration}
 */
module.exports = {
  forbidden: [
    {
      name: "sdk-confined-to-model-and-tools",
      comment:
        "@anthropic-ai/claude-agent-sdk may be imported only under src/model/ (the model port) "
        + "and src/wiki-tools/ (the granted tool definitions) — T039.",
      severity: "error",
      from: { pathNot: "^src/(model|wiki-tools)/" },
      to: { path: "^(@anthropic-ai/claude-agent-sdk|@anthropic-ai/sdk)" },
    },
    {
      name: "instruction-module-is-a-leaf",
      comment:
        "The sole constructor of SystemPrompt must not depend on the model port or the tools, so "
        + "composing the system prompt cannot come to depend on anything but instruction files "
        + "(constitution I.2).",
      severity: "error",
      from: { path: "^src/instruction/" },
      to: { path: "^src/(model|wiki-tools|run)/" },
    },
    {
      name: "tools-do-not-call-the-model",
      comment:
        "src/wiki-tools/ declares what the agent may do; it never drives the loop. Only the model "
        + "port does that.",
      severity: "error",
      from: { path: "^src/wiki-tools/" },
      to: { path: "^src/model/" },
    },
    {
      name: "no-circular",
      severity: "error",
      from: {},
      to: { circular: true },
    },
    {
      name: "no-orphans",
      severity: "warn",
      from: { orphan: true, pathNot: ["^src/main\\.ts$"] },
      to: {},
    },
  ],
  options: {
    doNotFollow: { path: "node_modules" },
    tsConfig: { fileName: "tsconfig.json" },
    tsPreCompilationDeps: true,
  },
};
