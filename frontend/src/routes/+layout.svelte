<script lang="ts">
  import { page } from "$app/state";

  let { children } = $props();
</script>

<nav>
  <a href="/" class:current={page.url.pathname === "/"}>Submit</a>
  <a href="/tasks" class:current={page.url.pathname.startsWith("/tasks")}>Tasks</a>
</nav>

<main>
  {@render children()}
</main>

<style>
  :global(body) {
    margin: 0;
    font: 16px/1.5 ui-sans-serif, system-ui, sans-serif;
    color: #16161a;
    background: #fbfbfd;
  }

  :global(*, *::before, *::after) {
    box-sizing: border-box;
  }

  nav {
    display: flex;
    gap: 1.5rem;
    padding: 1rem 2rem;
    border-bottom: 1px solid #e3e3e8;
    background: #fff;
  }

  nav a {
    color: #55555f;
    text-decoration: none;
  }

  nav a.current {
    color: #16161a;
    font-weight: 600;
  }

  main {
    max-width: 60rem;
    margin: 0 auto;
    padding: 2rem;
  }
</style>
