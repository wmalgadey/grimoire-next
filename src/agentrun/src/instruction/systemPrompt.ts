/**
 * The type the model port accepts as instruction — and the reason it is a *type* rather than a
 * string.
 *
 * Judgment lives in instruction files; control lives in code (constitution I.1). The mechanism
 * that keeps those apart is that the model port will not take a `string`: it takes a
 * `SystemPrompt`, and the only way to get one is {@link createSystemPrompt}, which is only
 * reachable from this module. A developer who wants to slip a sentence of prompt text into harness
 * code has to write `as SystemPrompt` to do it, and quality gate 1
 * (`tests/architecture/system-prompt-construction.test.ts`) fails the build when they do.
 */

declare const systemPromptBrand: unique symbol;

/**
 * Instruction text loaded from an instruction file. Structurally a string, nominally not: a plain
 * string is not assignable to it.
 */
export type SystemPrompt = string & { readonly [systemPromptBrand]: "SystemPrompt" };

/**
 * Mints a {@link SystemPrompt}. Internal to `src/agentrun/src/instruction/` on purpose — this is
 * the single constructor quality gate 1 exists to protect.
 *
 * @internal
 */
export function createSystemPrompt(text: string): SystemPrompt {
  return text as SystemPrompt;
}
