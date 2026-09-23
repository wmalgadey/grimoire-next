# RUNS

Traceability: what a run did, why, how it ended, what it cost; approving lint proposals.

<!-- The as-is description of the system (Constitution IV.2). An id becomes permanent the moment it
     is registered here: never renumbered, never reused. A removed requirement moves under
     "Retired" and keeps its id. -->

## Requirements

| ID | Requirement | Proof |
| --- | --- | --- |
| RUNS-001 | Every submission MUST carry exactly one state at a time, drawn from submitted, running, done, failed. | test |
| RUNS-002 | At most one run MUST be in progress at any time, and waiting submissions MUST start in the order they were made. | test |
| RUNS-003 | After a run ends failed, no further run MUST start until the user has acknowledged that failure; waiting submissions MUST stay waiting, and the acknowledged run MUST stay failed. | test |
| RUNS-004 | Submissions and their states MUST survive Grimoire stopping and starting again; a run that was in progress when Grimoire stopped MUST read failed afterwards. | test |
| RUNS-005 | A run MUST end done when the agent stopped on its own, neither ceiling was reached, and the wiki's log holds an entry for that run; an entry belongs to a run when `log.md` contains that run's identifier. When the agent stops inside both ceilings and the log holds no entry for the run, Grimoire MUST tell the agent once that the entry is missing and let it continue within the ceilings; if the agent then stops and the entry is there, the run MUST end done. In every other case the run MUST end failed. Grimoire MUST read nothing else in the wiki to decide this. | test |
| RUNS-006 | No agent MUST go on working on a run once Grimoire has ended that run. When Grimoire stops and is given the chance to act, a run that is in progress MUST be stopped with it. The process identifier of a run's agent MUST be recorded with the run; at start-up, for every run Grimoire reads as having been in progress, it MUST terminate that process where it is still alive — before that run reads failed and before any further run starts — and MUST NOT terminate a process that is no longer that run's agent. | test |

RUNS-002 is what makes `submitted` cover two situations — a submission waiting its turn, and one
whose agent has not yet reported in. RUNS-001's four states stay four: what tells the two apart is
the run a submission has been given, which is not a state and does not reach the browser.
