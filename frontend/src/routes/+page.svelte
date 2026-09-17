<script lang="ts">
  import { goto } from "$app/navigation";
  import { HubError, submitSource } from "$lib/api/client.js";

  // The submit surface (FR-001). Pasted text or a URL; an empty or whitespace-only submission is
  // refused here and again by the hub, which is what creates no task at all.
  let kind = $state<"text" | "url">("text");
  let value = $state("");
  let problem = $state<string | null>(null);
  let submitting = $state(false);

  const isEmpty = $derived(value.trim().length === 0);

  async function submit(event: SubmitEvent) {
    event.preventDefault();
    problem = null;

    if (isEmpty) {
      problem =
        kind === "url"
          ? "A URL submission needs a URL."
          : "A text submission needs text.";
      return;
    }

    submitting = true;
    try {
      const task = await submitSource(kind, value);
      await goto(`/tasks/${task.id}`);
    } catch (error) {
      problem = error instanceof HubError ? error.message : String(error);
    } finally {
      submitting = false;
    }
  }
</script>

<h1>Submit a source</h1>

<form onsubmit={submit}>
  <fieldset>
    <legend>What are you submitting?</legend>
    <label>
      <input type="radio" name="kind" value="text" bind:group={kind} />
      Pasted text
    </label>
    <label>
      <input type="radio" name="kind" value="url" bind:group={kind} />
      A URL
    </label>
  </fieldset>

  {#if kind === "url"}
    <label class="field">
      <span>URL</span>
      <input
        type="text"
        name="value"
        data-testid="source-value"
        bind:value
        placeholder="https://example.com/article"
      />
    </label>
  {:else}
    <label class="field">
      <span>Text</span>
      <textarea name="value" data-testid="source-value" rows="12" bind:value></textarea>
    </label>
  {/if}

  {#if problem}
    <p class="problem" role="alert" data-testid="problem">{problem}</p>
  {/if}

  <button type="submit" data-testid="submit" disabled={submitting || isEmpty}>
    {submitting ? "Submitting…" : "Submit"}
  </button>
</form>

<style>
  fieldset {
    border: 1px solid #e3e3e8;
    border-radius: 6px;
    margin: 0 0 1.5rem;
    padding: 1rem;
  }

  legend {
    padding: 0 0.5rem;
    color: #55555f;
    font-size: 0.875rem;
  }

  fieldset label {
    margin-right: 1.5rem;
  }

  .field {
    display: block;
    margin-bottom: 1.5rem;
  }

  .field span {
    display: block;
    margin-bottom: 0.5rem;
    font-weight: 600;
  }

  input[type="text"],
  textarea {
    width: 100%;
    padding: 0.625rem;
    border: 1px solid #c9c9d1;
    border-radius: 6px;
    font: inherit;
  }

  .problem {
    padding: 0.75rem 1rem;
    border-left: 3px solid #b4242c;
    background: #fdf2f2;
    color: #8a1c22;
  }

  button {
    padding: 0.625rem 1.25rem;
    border: 0;
    border-radius: 6px;
    background: #16161a;
    color: #fff;
    font: inherit;
    cursor: pointer;
  }

  button:disabled {
    background: #c9c9d1;
    cursor: default;
  }
</style>
