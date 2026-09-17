<script lang="ts">
  import TaskStateBadge from "$lib/TaskState.svelte";
  import { listTasks, type Task } from "$lib/api/client.js";

  // The task list (FR-030, SC-006). Every retained task, newest first, each openable — so closing
  // the browser loses access to no task.
  let tasks = $state<Task[]>([]);
  let nextCursor = $state<string | null | undefined>(undefined);
  let problem = $state<string | null>(null);
  let loading = $state(true);

  async function load(cursor?: string | null) {
    loading = true;
    problem = null;
    try {
      const page = await listTasks(cursor);
      tasks = cursor ? [...tasks, ...page.tasks] : page.tasks;
      nextCursor = page.nextCursor;
    } catch (error) {
      problem = String(error);
    } finally {
      loading = false;
    }
  }

  $effect(() => {
    void load();
  });
</script>

<header>
  <h1>Tasks</h1>
  <!-- The view reflects state as of load. There is no polling loop: live updating is out of
       scope, so the refresh is the user's (FR-018). -->
  <button onclick={() => load()} data-testid="refresh">Refresh</button>
</header>

{#if problem}
  <p class="problem" role="alert">{problem}</p>
{/if}

{#if tasks.length === 0 && !loading}
  <p class="empty" data-testid="empty">No tasks yet. Submit a source to make one.</p>
{:else}
  <ul data-testid="task-list">
    {#each tasks as task (task.id)}
      <li>
        <a href="/tasks/{task.id}" data-testid="task-row">
          <TaskStateBadge state={task.state} />
          <span class="preview">{task.source.preview}</span>
          <span class="kind">{task.source.kind}</span>
          <time datetime={task.submittedAt}>{new Date(task.submittedAt).toLocaleString()}</time>
        </a>
        {#if task.failureReason}
          <p class="reason" data-testid="failure-reason">{task.failureReason}</p>
        {/if}
      </li>
    {/each}
  </ul>
{/if}

{#if nextCursor}
  <button onclick={() => load(nextCursor)} data-testid="load-more">Load more</button>
{/if}

<style>
  header {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
  }

  ul {
    list-style: none;
    margin: 0;
    padding: 0;
  }

  li {
    border-bottom: 1px solid #e3e3e8;
  }

  li a {
    display: grid;
    grid-template-columns: 6rem 1fr 3rem auto;
    gap: 1rem;
    align-items: center;
    padding: 0.875rem 0;
    color: inherit;
    text-decoration: none;
  }

  li a:hover {
    background: #f4f4f7;
  }

  .preview {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .kind,
  time {
    color: #6b6b75;
    font-size: 0.875rem;
  }

  .reason {
    margin: 0 0 0.75rem;
    color: #8a1c22;
    font-size: 0.875rem;
  }

  .problem {
    padding: 0.75rem 1rem;
    border-left: 3px solid #b4242c;
    background: #fdf2f2;
    color: #8a1c22;
  }

  .empty {
    color: #6b6b75;
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
