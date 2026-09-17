/**
 * The only module that reads instruction files (constitution I.2).
 *
 * Everything the model is told to care about comes from here and nowhere else: no preamble the
 * harness adds, no wiki-content branching, no prompt fragment assembled at the call site. What the
 * agent judges is exactly what the versioned instruction file says, which is what makes a change
 * in behaviour attributable to a specific instruction revision (FR-014, SC-009).
 */

import { createHash } from "node:crypto";
import { readFile } from "node:fs/promises";
import { createSystemPrompt, type SystemPrompt } from "./systemPrompt.js";

/** The instruction file that was loaded, and the identity the task records for it. */
export interface LoadedInstruction {
  /** The path as configured, recorded on the task (FR-014). */
  readonly path: string;
  /** Full content hash of the file's bytes; the task view shows the first 12 hex (research R10). */
  readonly sha256: string;
  /** Byte length of the file as loaded. */
  readonly byteLength: number;
  /** The file's content, as the only thing the model is given as instruction. */
  readonly systemPrompt: SystemPrompt;
}

/** An instruction file that could not be loaded. A run without instruction is not a run. */
export class InstructionLoadError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "InstructionLoadError";
  }
}

/**
 * Reads an instruction file and reports its version.
 *
 * The hash is of the bytes, not of the decoded text: two files that differ only in encoding are
 * two versions, because they are two things the model could be given.
 */
export async function loadInstruction(path: string): Promise<LoadedInstruction> {
  let bytes: Buffer;
  try {
    bytes = await readFile(path);
  } catch (cause) {
    throw new InstructionLoadError(
      `The instruction file '${path}' could not be read: ${(cause as Error).message}`,
    );
  }

  const text = bytes.toString("utf8");
  if (text.trim().length === 0) {
    throw new InstructionLoadError(`The instruction file '${path}' is empty.`);
  }

  return {
    path,
    sha256: createHash("sha256").update(bytes).digest("hex"),
    byteLength: bytes.byteLength,
    systemPrompt: createSystemPrompt(text),
  };
}
