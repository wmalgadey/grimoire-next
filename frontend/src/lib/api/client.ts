/**
 * The frontend's entire view of the hub.
 *
 * Every shape here comes from `schema.d.ts`, which `openapi-typescript` generates from
 * `contracts/hub-api.openapi.yaml` at build time — never hand-written (constitution V.5,
 * ADR-0008). A contract change therefore cannot happen without appearing in the PR diff, and a
 * frontend that reads a field the hub does not serve fails to compile.
 */

import type { components } from "./schema.js";

export type TaskState = components["schemas"]["TaskState"];
export type Task = components["schemas"]["Task"];
export type TaskList = components["schemas"]["TaskList"];
export type TaskDetail = components["schemas"]["TaskDetail"];
export type ToolCall = components["schemas"]["ToolCall"];
export type FileDiff = components["schemas"]["FileDiff"];
export type Problem = components["schemas"]["Problem"];

/** A hub response that was not a success, carrying the problem document's own words. */
export class HubError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
    this.name = "HubError";
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: { "content-type": "application/json", ...(init?.headers ?? {}) },
  });

  if (!response.ok) {
    let detail = `The hub answered ${response.status}.`;
    try {
      const problem = (await response.json()) as Problem;
      detail = problem.detail ?? problem.title ?? detail;
    } catch {
      // A response that is not a problem document still has a status worth reporting.
    }

    throw new HubError(detail, response.status);
  }

  return (await response.json()) as T;
}

/** Submits pasted text or a URL. Exactly one task comes back (FR-002). */
export const submitSource = (kind: "text" | "url", value: string): Promise<Task> =>
  request<Task>("/api/tasks", { method: "POST", body: JSON.stringify({ kind, value }) });

/** Every retained task, newest first (FR-030). */
export const listTasks = (cursor?: string | null): Promise<TaskList> =>
  request<TaskList>(`/api/tasks${cursor ? `?cursor=${encodeURIComponent(cursor)}` : ""}`);

/** One task, in whatever state it is in (FR-020). */
export const getTask = (taskId: string): Promise<TaskDetail> =>
  request<TaskDetail>(`/api/tasks/${encodeURIComponent(taskId)}`);

/** Restores the wiki to its state before this task's run (FR-025). */
export const revertTask = (taskId: string): Promise<TaskDetail> =>
  request<TaskDetail>(`/api/tasks/${encodeURIComponent(taskId)}/revert`, { method: "POST" });
