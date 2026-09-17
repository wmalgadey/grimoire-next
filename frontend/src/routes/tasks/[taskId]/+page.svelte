<script lang="ts">
  import { page } from "$app/state";
  import TaskStateBadge from "$lib/TaskState.svelte";
  import { getTask, revertTask, type TaskDetail } from "$lib/api/client.js";

  // The task view. This page alone has to answer: which instruction version ran, what the agent
  // looked at, what it wrote, and what the wiki looks like now versus before — without opening a
  // terminal or the repository (FR-021, FR-022, SC-008, SC-010, SC-011).
  let task = $state<TaskDetail | null>(null);
  let problem = $state<string | null>(null);
  let reverting = $state(false);

  async function load() {
    problem = null;
    try {
      task = await getTask(page.params.taskId!);
    } catch (error) {
      problem = String(error);
    }
  }

  // One action, no confirmation step and no second page (SC-005). The hub answers with the updated
  // task view, so the result is rendered from the same response that performed the revert — a
  // reload here could show a tip someone else has already moved.
  async function revert() {
    reverting = true;
    problem = null;
    try {
      task = await revertTask(page.params.taskId!);
    } catch (error) {
      problem = String(error);
    } finally {
      reverting = false;
    }
  }

  $effect(() => {
    void load();
  });

  /** The instruction version as the view shows it: `sha256:` plus the first 12 hex (research R10). */
  const shortVersion = (sha256: string) => `sha256:${sha256.slice(0, 12)}`;

  /** Why revert is not offered, said in words rather than shown as a disabled control (FR-027). */
  const eligibilityExplanation = (reason: string | null | undefined): string => {
    switch (reason) {
      case "superseded":
        return "This ingest was superseded by a later wiki commit, so it can no longer be undone. Undo reaches one ingest back, not further.";
      case "already-reverted":
        return "This ingest has already been reverted.";
      case "no-commit":
        return "This run produced no commit, so there is nothing to undo.";
      default:
        return "Revert is not available for this task.";
    }
  };
</script>

{#if problem}
  <p class="problem" role="alert" data-testid="problem">{problem}</p>
{/if}

{#if task}
  <header>
    <div>
      <h1>Task</h1>
      <code class="id" data-testid="task-id">{task.id}</code>
    </div>
    <div class="actions">
      <TaskStateBadge state={task.state} />
      <!-- The view reflects state as of load; there is no polling loop (FR-018). -->
      <button onclick={load} data-testid="refresh">Refresh</button>
    </div>
  </header>

  {#if task.failureReason}
    <p class="problem" data-testid="failure-reason">{task.failureReason}</p>
  {/if}

  <section>
    <h2>Source</h2>
    <dl>
      <dt>Kind</dt>
      <dd data-testid="source-kind">{task.source.kind}</dd>
      <dt>Size</dt>
      <dd>{task.source.byteLength} bytes</dd>
    </dl>
    <pre data-testid="source-value">{task.source.retrievedText ?? task.source.submittedValue}</pre>
  </section>

  {#if task.run}
    <section>
      <h2>The run</h2>
      <dl>
        <dt>Instruction version</dt>
        <dd data-testid="instruction-version">
          <code>{shortVersion(task.run.instructionVersion.sha256)}</code>
          <span class="path">{task.run.instructionVersion.path}</span>
        </dd>
        <dt>Granted tools</dt>
        <dd data-testid="tool-grant">
          {#each task.run.toolGrant.tools as tool (tool)}
            <code>{tool}</code>
          {/each}
        </dd>
        {#if task.run.durationMs !== null && task.run.durationMs !== undefined}
          <dt>Duration</dt>
          <dd>{task.run.durationMs} ms</dd>
        {/if}
      </dl>
    </section>

    <section>
      <h2>What the agent did</h2>
      {#if task.run.toolCalls.length === 0}
        <p class="quiet" data-testid="no-tool-calls">
          {task.state === "queued"
            ? "This run has not started."
            : task.state === "running"
              ? "The agent has not called a tool yet."
              : "The agent made no tool calls."}
        </p>
      {:else}
        <table data-testid="tool-calls">
          <thead>
            <tr><th>#</th><th>Tool</th><th>Target</th><th>Outcome</th><th>Detail</th></tr>
          </thead>
          <tbody>
            {#each task.run.toolCalls as call (call.seq)}
              <tr class={call.outcome} data-testid="tool-call">
                <td>{call.seq}</td>
                <td><code>{call.tool}</code></td>
                <td>{call.target ?? "—"}</td>
                <td>{call.outcome}</td>
                <td class="detail">{call.detail ?? ""}</td>
              </tr>
            {/each}
          </tbody>
        </table>
      {/if}
    </section>

    <section>
      <h2>What changed in the wiki</h2>
      {#if task.run.commit}
        <p data-testid="commit">
          <code>{task.run.commit.sha.slice(0, 12)}</code>
          — {task.run.commit.message}
        </p>
        {#each task.run.commit.fileDiffs as diff (diff.path)}
          <article data-testid="file-diff">
            <h3>{diff.path} <span class="change {diff.changeKind}">{diff.changeKind}</span></h3>
            <pre class="patch">{diff.patch}</pre>
          </article>
        {/each}
      {:else if task.run.changedNothing}
        <!-- Stated explicitly, so a deliberate no-op does not read as breakage (FR-016, SC-011).
             Worded from the recorded tool calls, not assumed: a run can complete having made no
             tool calls at all, and "the agent read the wiki" would claim something that did not
             happen. -->
        <p class="quiet" data-testid="changed-nothing">
          This run changed nothing.
          {#if task.run.toolCalls.some((call) => call.tool === "mcp__wiki__read_page" && call.outcome === "ok")}
            The agent read the wiki and judged that it already said what this source had to say.
          {:else}
            The wiki is exactly as it was before the run started.
          {/if}
        </p>
      {:else if task.state === "running"}
        <!-- Nothing is decided yet: the commit happens at run end, so "no commit" here would be a
             conclusion about a run that is still working (FR-020). -->
        <p class="quiet" data-testid="run-in-flight">
          This run is still working. Anything it has written is uncommitted, and stays that way
          until the run ends.
        </p>
      {:else}
        <p class="quiet" data-testid="no-commit">
          This run produced no commit. Anything it wrote was discarded, and the wiki is exactly as
          it was before the run started.
        </p>
      {/if}
    </section>
  {:else}
    <section>
      <h2>The run</h2>
      <p class="quiet" data-testid="no-run">
        {task.state === "queued"
          ? "This task is queued. The agent has not started."
          : "No run was dispatched for this task."}
      </p>
    </section>
  {/if}

  <section>
    <h2>Revert</h2>
    {#if task.revert}
      <p data-testid="revert-commit">
        Reverted by <code>{task.revert.revertCommitSha.slice(0, 12)}</code>
        on {new Date(task.revert.revertedAt).toLocaleString()}.
      </p>
    {:else if task.revertEligibility.eligible}
      <button onclick={revert} disabled={reverting} data-testid="revert">
        {reverting ? "Reverting…" : "Revert this ingest"}
      </button>
    {:else}
      <p class="quiet" data-testid="revert-reason">
        {eligibilityExplanation(task.revertEligibility.reason)}
      </p>
    {/if}
  </section>
{:else if !problem}
  <p class="quiet">Loading…</p>
{/if}

<style>
  header {
    display: flex;
    align-items: flex-start;
    justify-content: space-between;
  }

  header h1 {
    margin-bottom: 0.25rem;
  }

  .actions {
    display: flex;
    gap: 0.75rem;
    align-items: center;
  }

  .id {
    color: #6b6b75;
    font-size: 0.875rem;
  }

  section {
    margin: 2rem 0;
    padding-top: 1.5rem;
    border-top: 1px solid #e3e3e8;
  }

  h2 {
    font-size: 1.0625rem;
  }

  dl {
    display: grid;
    grid-template-columns: 11rem 1fr;
    gap: 0.5rem 1rem;
    margin: 0 0 1rem;
  }

  dt {
    color: #6b6b75;
  }

  dd {
    margin: 0;
  }

  dd code {
    margin-right: 0.5rem;
  }

  .path {
    color: #6b6b75;
    font-size: 0.875rem;
  }

  pre {
    max-height: 20rem;
    overflow: auto;
    padding: 0.875rem;
    border: 1px solid #e3e3e8;
    border-radius: 6px;
    background: #fff;
    white-space: pre-wrap;
    word-break: break-word;
  }

  .patch {
    font-size: 0.8125rem;
    white-space: pre;
    word-break: normal;
  }

  table {
    width: 100%;
    border-collapse: collapse;
  }

  th,
  td {
    padding: 0.5rem 0.75rem;
    border-bottom: 1px solid #e3e3e8;
    text-align: left;
    vertical-align: top;
  }

  th {
    color: #6b6b75;
    font-size: 0.8125rem;
    font-weight: 600;
  }

  tr.refused td,
  tr.failed td {
    color: #8a1c22;
  }

  .detail {
    font-size: 0.875rem;
  }

  .change {
    padding: 0.0625rem 0.4rem;
    border-radius: 999px;
    font-size: 0.75rem;
    font-weight: 600;
  }

  .change.added {
    background: #e4f3e7;
    color: #1f6b33;
  }

  .change.modified {
    background: #e3eefc;
    color: #1c4f8a;
  }

  .change.removed {
    background: #fdf2f2;
    color: #8a1c22;
  }

  .quiet {
    color: #6b6b75;
  }

  .problem {
    padding: 0.75rem 1rem;
    border-left: 3px solid #b4242c;
    background: #fdf2f2;
    color: #8a1c22;
  }

  button {
    padding: 0.5rem 1rem;
    border: 1px solid #c9c9d1;
    border-radius: 6px;
    background: #fff;
    font: inherit;
    cursor: pointer;
  }
</style>
