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
| RUNS-007 | Every run MUST have exactly one record of its own, a Markdown file in a directory Grimoire owns and never inside the wiki, created when the run begins and appended to as the run proceeds, so that it can be read while the run is in progress and after it has ended. Grimoire MUST NOT rewrite or remove a record; what has been appended stays, a run cut off by a stop included. Where a record cannot be written, the run MUST go on; that something is missing MUST be recorded with the run and MUST be appended to the record once it can be written again. | test |
| RUNS-008 | A run's record MUST hold the frame of that run: the pinned model id it ran on, the tools it was granted, both ceilings with the values the run reached against them, the tokens spent per model the run caused, when the run started, when it ended, and why it ended — one of: the agent stopped inside both ceilings with its log entry present; it stopped without that entry after being told once; the time ceiling; the cost ceiling; the reported tools were not the grant; the agent's process died; Grimoire was stopped while the run was in progress. | test |
| RUNS-009 | A run's record MUST hold what the run did, in the order it happened: every tool call with its arguments, what that call returned — whole, with nothing cut and nothing dropped however large it is — the agent's own text between the calls, and anything Grimoire said to the agent. | test |
| RUNS-010 | For every run, the tokens it has spent — the same quantity the cost ceiling counts — and the number of tool calls it has made MUST be kept current while the run is in progress, MUST stand as the run's final figures once it has ended, and MUST survive Grimoire stopping and starting again. | test |

RUNS-002 is what makes `submitted` cover two situations — a submission waiting its turn, and one
whose agent has not yet reported in. RUNS-001's four states stay four: what tells the two apart is
the run a submission has been given, which is not a state and does not reach the browser.

RUNS-007 is the record as a file: one per run, in `runs/` inside the directory `--state` names, never
in the wiki. It is appended to and never rewritten, which is what makes a run cut off by a stop
readable — the head and the moments that happened are already on disk. A write that fails does not
end the run: the adapter counts what it could not write, the count travels with the run's figures,
and the record says how many entries were lost once a write succeeds again.

RUNS-008 splits across the record's two ends. What is known when the run begins is written then; what
only the ending knows — when, done or failed, why, where the run stood against both ceilings, and the
tokens of every model it touched — is appended when it ends. A run still under way has no reason yet,
which is the only difference between its record and one from last month. The seven reasons are values
inside RUNS-008 and not requirements of their own (Constitution IV.7).

The tokens per model come from what the CLI reports when a turn ends. A run stopped before it ever
reported one — a cost ceiling reached inside the first turn, a process that died, a tool surface that
was not the grant — has no breakdown to record, and its tail holds the total against the ceiling and
no model rows. The head still names the model the run was dispatched on. Nothing is invented to fill
the gap: attributing the whole total to that model would claim the CLI's own background calls, which
a run causes but never asks for, were made on it.

RUNS-009 is what the run *did*, and nothing about what it was told: no instruction, no purpose
description, no submitted text. A tool result goes in whole — nothing cut, nothing escaped — inside a
fence longer than any run of backticks in it.

**There is no reasoning in a record, because the CLI does not give any.** The agent's own text is
recorded wherever it writes some, which is at the turn boundaries: it says what it is about to do,
makes its calls, and says what it found. Inside one turn it makes call after call without prose, and
that gap is the model's doing rather than something dropped on the way. Its *thinking* is a different
matter and is not available at all — measured twice, on `claude-haiku-4-5` (research.md R-05) and
again on `claude-sonnet-4-5` with CLI 2.1.283: the complete message carries `"thinking": ""` beside a
several-hundred-character signature, and the streamed `thinking_delta` events carry empty strings too.
There is nothing behind the signature to record. Should a later CLI deliver it, that is a change to
what RUNS-009 covers and an owner's decision, not a silent addition.

RUNS-010 exists apart from ACCESS-005 because keeping the figures and showing them are two
behaviours, and because a stop must not lose them: `002-ingest-queue` assumed the token counts of a
cut-off run need not survive, and that assumption is withdrawn here — the row of a failed run still
carries what that run spent.
