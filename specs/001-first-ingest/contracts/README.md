# Interface Contracts: The First Ingest

Three interfaces cross a process boundary in this feature. Each has its own file. There is no
acknowledgement endpoint and no run store: ACCESS-003 and RUNS-004 belong to the follow-up feature.

| Contract | Between | Serves |
| --- | --- | --- |
| [`hub-http-api.md`](hub-http-api.md) | browser ↔ hub | ACCESS-001, ACCESS-002, INGEST-001/003/004/005 |
| [`mcp-wiki-tools.md`](mcp-wiki-tools.md) | agent ↔ hub | GUARD-001/002/003, WIKI-002 |
| [`agent-cli-protocol.md`](agent-cli-protocol.md) | hub ↔ the `claude` CLI | INGEST-002, GUARD-001/004, RUNS-005 |

The wiki itself is not an interface contract — it is a filesystem artifact, described in
[`../data-model.md`](../data-model.md).
