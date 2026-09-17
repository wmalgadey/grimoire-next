/**
 * Quality gate 2, frontend layer (constitution V.3, plan V.5).
 *
 * The frontend's view of the hub is the generated contract client and nothing else:
 * types come from contracts/hub-api.openapi.yaml via openapi-typescript (T036), never
 * hand-written, so a contract change cannot happen without appearing in the PR diff.
 *
 * @type {import('dependency-cruiser').IConfiguration}
 */
module.exports = {
  forbidden: [
    {
      name: "no-hand-written-api-types",
      comment:
        "src/lib/api/ holds the generated schema and the thin client over it. Nothing else may declare the hub's wire shapes (T051 territory in spirit; T036 generates them).",
      severity: "error",
      from: { pathNot: "^src/lib/api/" },
      to: { path: "^src/lib/api/(?!schema\\.d\\.ts$|client\\.ts$)" },
    },
    {
      name: "no-circular",
      severity: "error",
      from: {},
      to: { circular: true },
    },
  ],
  options: {
    doNotFollow: { path: "node_modules" },
    tsConfig: { fileName: "tsconfig.json" },
    tsPreCompilationDeps: true,
  },
};
