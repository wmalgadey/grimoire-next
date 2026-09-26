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
| GUARD-004 | A run MUST have a fixed ceiling on elapsed time and a fixed ceiling on cost counted in input-token equivalents — the four token classes a model call reports, each weighted by what it is billed at relative to an input token. When either ceiling is reached, Grimoire MUST stop the run at once, a model call in flight included, and the run MUST end failed. | test |

GUARD-001 and GUARD-002 are proven at two levels: Fast against the hub's own half of the grant, and
Contract against the real `claude` CLI. The deny-by-default configuration is a decision we made
rather than framework behaviour, so III.8 does not exclude it.

GUARD-004's cost is **not** a token count. An input token, an output token, a cache read and a cache
write are billed at ratios of 1 : 5 : 0.1 : 2, so summing the four raw counts measures turns times
context size and not what a run costs. Measured: ten million cache reads — the cheapest thing the
CLI does — are 1 000 000 equivalents and reached the old raw ceiling of 2 000 000 five times over,
while four hundred thousand output tokens are 2 000 000 equivalents, twice the money, and sat at a
fifth of that same ceiling. The raw sum had the two the wrong way round by a factor of fifty, which
is the ratio between the dearest class and the cheapest. The quantity has no unit of its own and is never
currency — currency is not available while a run is under way, only in the `result` that ends it
(DEC-015). The ratios are Anthropic's price structure, which every first-party model shares, so no
model's price is in the tree; a sign-in contract test is what holds them to the CLI's own `costUSD`.

The four raw counts a run causes are kept and written down beside the weighted figure, because the
weighting cannot be undone and the ceiling's value is calibrated from them after the acceptance run
(RUNS-008, RUNS-010).
