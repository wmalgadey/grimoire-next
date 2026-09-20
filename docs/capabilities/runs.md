# RUNS

Traceability: what a run did, why, how it ended, what it cost; approving lint proposals.

<!-- The as-is description of the system (Constitution IV.2). An id becomes permanent the moment it
     is registered here: never renumbered, never reused. A removed requirement moves under
     "Retired" and keeps its id. -->

## Requirements

| ID | Requirement | Proof |
| --- | --- | --- |
| RUNS-001 | Every submission MUST carry exactly one state at a time, drawn from submitted, running, done, failed. | test |
| RUNS-005 | A run MUST end done when the agent stopped on its own, neither ceiling was reached, and the wiki's log holds an entry for that run; an entry belongs to a run when `log.md` contains that run's identifier. When the agent stops inside both ceilings and the log holds no entry for the run, Grimoire MUST tell the agent once that the entry is missing and let it continue within the ceilings; if the agent then stops and the entry is there, the run MUST end done. In every other case the run MUST end failed. Grimoire MUST read nothing else in the wiki to decide this. | test |

RUNS-002, RUNS-003 and RUNS-004 — the queue, its acknowledgement gate and surviving a restart — are
wanted and still advance OUT-01, but they are not built by `001-first-ingest` and are therefore not
registered here. They become permanent when the follow-up feature registers them.
