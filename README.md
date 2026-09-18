# Grimoire

A hub with a web frontend that dispatches LLM agents to maintain a markdown wiki in git. Every
operation is a task you can inspect: which instruction ran, which tools the agent was granted, what
it read, what it wrote, the commit it produced — and every commit can be reverted.

## Where things are decided

- **[Constitution](.specify/memory/constitution.md)** — the rules everything else answers to:
  judgment in instruction files and control in code, every wiki change a revertible commit, tools
  deny-by-default, real infrastructure in tests with the LLM as the only double.
- **[Architecture decision records](docs/adr/index.md)** — the expensive-to-reverse choices and
  why, one aspect per record.
- **[HTTP contract](contracts/hub-api.openapi.yaml)** — the only way the frontend and the hub talk.
  The frontend's types are generated from it and a drift test holds the hub to it.
- **[Feature 001: source ingest via an agent run](specs/001-source-ingest-agent-run/)** — the
  specification, plan, tasks, and the [quickstart](specs/001-source-ingest-agent-run/quickstart.md)
  scenarios that validate it end to end.

## Build, test, run

.NET 10 SDK, Node 22, git. `make help` lists everything; the essentials:

```bash
make build        # hub, runner, scripted model, frontend
make test         # every suite, in CI order — no Anthropic key needed
make dev-setup    # a scratch wiki and a starter .env
make run          # the hub on http://127.0.0.1:5099
```

As the container deployment — the hub on a network whose only way out is the egress proxy, which
alone holds the upstream credential ([contracts/deployment.md](specs/001-source-ingest-agent-run/contracts/deployment.md)):

```bash
docker compose -f deploy/compose.yaml -f deploy/compose.override.yaml up -d --build
curl -fsS localhost:8080/readyz

lnav <(podman logs -f --tail 200 grimoire-hub) <(podman logs -f --tail 200 grimoire-egress)

docker compose -f deploy/compose.yaml -f deploy/compose.override.yaml down
```

`CLAUDE.md` is the working guide for changing the code: the mechanisms that span files, the
environment facts that bite, and the change routes.
