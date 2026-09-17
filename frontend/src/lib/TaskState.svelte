<script lang="ts">
  import type { TaskState } from "./api/client.js";

  // The five states, worded so a reader can tell a deliberate no-change from a breakage and an
  // interrupted run from one that is still going (FR-020, SC-006).
  let { state }: { state: TaskState } = $props();
</script>

<span class="state {state}" data-testid="state">{state}</span>

<style>
  .state {
    display: inline-block;
    padding: 0.125rem 0.5rem;
    border-radius: 999px;
    font-size: 0.8125rem;
    font-weight: 600;
    text-transform: lowercase;
  }

  .queued {
    background: #ececf1;
    color: #55555f;
  }

  .running {
    background: #e3eefc;
    color: #1c4f8a;
  }

  .completed {
    background: #e4f3e7;
    color: #1f6b33;
  }

  .failed {
    background: #fdf2f2;
    color: #8a1c22;
  }

  .reverted {
    background: #f6eedf;
    color: #7a5312;
  }
</style>
