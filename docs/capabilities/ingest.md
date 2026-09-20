# INGEST

Accept sources and work them into the wiki.

<!-- The as-is description of the system (Constitution IV.2). An id becomes permanent the moment it
     is registered here: never renumbered, never reused. A removed requirement moves under
     "Retired" and keeps its id. `trace-check` reads the ids and the proof kinds out of the table
     below; `specs/NNN-*` are the change records that put them there. -->

## Requirements

| ID | Requirement | Proof |
| --- | --- | --- |
| INGEST-001 | When no run is in progress, users MUST be able to submit a text, and the submission MUST be accepted without the user waiting for the run to end. | test |
| INGEST-002 | An accepted submission MUST cause a run that is given the submitted text, the purpose description, the instruction and the run's identifier, and that runs on the model Grimoire was started with. | test |
| INGEST-003 | When the instruction or the purpose description is missing, a submission MUST be refused, no run MUST start, and the user MUST be told which of the two is missing. A refused submission MUST NOT be stored and MUST carry no state. | test |
| INGEST-004 | A submission whose text is empty or only whitespace MUST be refused and MUST NOT cause a run. | test |
| INGEST-005 | A submission made while a run is in progress MUST be refused, MUST NOT cause a run, and the user MUST be told that a run is in progress. | test |
