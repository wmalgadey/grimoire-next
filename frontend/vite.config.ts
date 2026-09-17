import { sveltekit } from "@sveltejs/kit/vite";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [sveltekit()],
  server: {
    // Development only. In production the hub serves these assets itself.
    proxy: {
      "/api": "http://127.0.0.1:5280",
      "/healthz": "http://127.0.0.1:5280",
      "/readyz": "http://127.0.0.1:5280",
    },
  },
});
