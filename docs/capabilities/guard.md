# GUARD

What an agent may do and reach: tool grants, the safety ceilings, reach limits.

<!-- The as-is description of the system (Constitution IV.2). An id becomes permanent the moment it
     is registered here: never renumbered, never reused. A removed requirement moves under
     "Retired" and keeps its id. -->

## Requirements

| ID | Requirement | Proof |
| --- | --- | --- |
| GUARD-001 | A run MUST receive only the tools granted for that run, and MUST NOT be able to use any other. A run whose agent reports any tool outside the grant MUST end failed before its first model call. | test |
| GUARD-002 | The grant for an ingest run MUST allow reading anything inside the wiki and creating and changing pages, indexes and the log, and nothing else. Deleting and moving MUST NOT be granted. | test |
| GUARD-003 | The tools granted MUST be recorded for every run. | test |
| GUARD-004 | A run MUST have a fixed ceiling on elapsed time and a fixed ceiling on cost counted in model tokens. When either ceiling is reached, Grimoire MUST stop the run at once, a model call in flight included, and the run MUST end failed. | test |

GUARD-001 and GUARD-002 are proven at two levels: Fast against the hub's own half of the grant, and
Contract against the real `claude` CLI. The deny-by-default configuration is a decision we made
rather than framework behaviour, so III.8 does not exclude it.
