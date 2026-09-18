import js from "@eslint/js";
import svelte from "eslint-plugin-svelte";
import tseslint from "typescript-eslint";

export default tseslint.config(
  { ignores: ["dist/**", "build/**", "node_modules/**", ".svelte-kit/**", "src/lib/api/schema.d.ts"] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  ...svelte.configs["flat/recommended"],
  // Components are written with <script lang="ts">; without the TypeScript parser ESLint cannot
  // read them at all, and a file it cannot read is a file no rule — complexity included — measured.
  {
    files: ["**/*.svelte"],
    languageOptions: { parserOptions: { parser: tseslint.parser } },
  },
);
