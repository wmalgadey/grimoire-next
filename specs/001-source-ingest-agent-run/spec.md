# Feature Specification: Source Ingest via Agent Run

**Feature Branch**: `main` (spec directory: `specs/001-source-ingest-agent-run`)

**Created**: 2026-09-16

**Status**: Draft

**Input**: User description: "A user submits a source (pasted text or a URL) in the web frontend and gets back one task. The hub dispatches one agent run for it. The agent operates in a loop with tools, reads the existing wiki, and decides by itself, under its instruction file, what to create or update; a single structured model call is not an agent and does not satisfy this spec. The harness grants the agent a read tool and a write tool for the wiki only, records the grant, and commits everything the run changed as exactly one git commit when the run ends; an aborted or crashed run leaves the wiki at the commit before it. The task shows the instruction-file version used, the tool calls made, the resulting diff, and a revert action that restores the previous commit and marks the task as reverted. The user can open the task while the run is queued, running, completed, failed, or reverted."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ingest a source and inspect what the agent did (Priority: P1)

A user has something worth keeping in the wiki — a page of pasted notes, an article URL. They paste it into the web frontend and submit. They immediately get one task. The hub dispatches one agent run for that task. The agent reads what the wiki already contains and, guided only by its instruction file, writes the wiki changes it judges appropriate. When the run ends, the task shows the user which instruction-file version the run used, the ordered list of tool calls the agent made, and the diff of what changed in the wiki — all in one place, without opening a terminal or the repository.

**Why this priority**: This is the entire product loop in one slice. Without it there is no ingest and nothing to inspect; with it alone the system already delivers value — a wiki that grows from submitted sources, and a record the operator can read to judge whether the agent's judgment was good.

**Independent Test**: Submit pasted text through the frontend, wait for the run to end, and open the task. The task view alone must be enough to answer: which instruction version ran, what the agent looked at, what it wrote, and what the wiki looks like now versus before. Delivers value with no other story implemented.

**Acceptance Scenarios**:

1. **Given** an empty wiki and a user on the submission surface, **When** the user submits pasted text, **Then** exactly one task is created and openable, and exactly one agent run is dispatched for it.
2. **Given** a dispatched run, **When** the agent issues a read call and then one or more write calls and stops, **Then** the task ends in state completed and the task view lists every tool call in the order it was made, with its target and whether it succeeded.
3. **Given** a run that changed wiki content, **When** the run ends, **Then** all of that run's changes appear as exactly one commit, and the task view shows the diff of that commit.
4. **Given** a completed task, **When** the user opens it, **Then** the instruction-file version that was loaded for the run is shown.
5. **Given** a wiki that already contains pages, **When** a source is submitted, **Then** the tool-call record shows what the agent read before writing, so the operator can see whether the run consulted existing content.
6. **Given** a URL submission whose content cannot be retrieved, **When** the task is opened, **Then** the task is in state failed with a human-readable reason and the wiki is unchanged.

---

### User Story 2 - Revert an ingest (Priority: P2)

The user opens a task, reads the diff, and decides the wiki was better before. One revert action on the task view restores the wiki to the state it was in before that run, and the task is marked reverted. The run record stays intact and readable, because the point of a bad run is to learn what the instruction told the agent to do.

**Why this priority**: Reversibility is what makes it safe to let the agent decide. It is second only because the first story must exist to have something to revert.

**Independent Test**: Run an ingest that changes the wiki, revert it from the task view, and confirm the wiki content is identical to its pre-run state, the task reads reverted, and the original diff and tool-call record are still shown.

**Acceptance Scenarios**:

1. **Given** a completed task with a commit, **When** the user triggers revert, **Then** the wiki content is restored to the state at the commit preceding that task's commit with no manual step, and the task state becomes reverted.
2. **Given** a reverted task, **When** the user opens it, **Then** the instruction-file version, the tool calls, and the original diff are still shown, and no revert action is offered.
3. **Given** a task with no commit (failed, or completed with no change), **When** the user opens it, **Then** no revert action is offered.

---

### User Story 3 - Follow a run while it is queued, running, or broken (Priority: P3)

The user opens the task the moment they get it — before the agent has done anything, while it is working, and after it has failed. The task is a real object in every state, not a page that only exists once a run succeeds.

**Why this priority**: Accountability for runs that never produced a diff. Valuable, but the system is usable without it as long as completed tasks are inspectable.

**Independent Test**: Open a task while its run is queued, again while it is running, and again after a crash-induced failure; each time the view loads and shows what is known so far.

**Acceptance Scenarios**:

1. **Given** a run in progress, **When** a second source is submitted, **Then** its task is created in state queued and is openable, showing that it has not started.
2. **Given** a running task, **When** the user opens it, **Then** the view shows state running, the instruction-file version, the granted tool set, and the tool calls recorded so far.
3. **Given** a run that crashes after the agent has written wiki content, **When** the user opens the task, **Then** the task is failed, no commit exists for it, and the wiki content is identical to the commit that was current before the run started.

---

### Edge Cases

- **URL cannot be retrieved** (unreachable, error response, non-text content): the task fails with a recorded reason; no run is dispatched or the run is not started; the wiki is untouched.
- **Agent changes nothing**: the run ends completed with no commit and an empty diff; the task says so explicitly rather than appearing broken.
- **Agent writes, then the run crashes or is aborted**: no commit; the wiki stays at the pre-run commit; the partial writes are not visible in the wiki.
- **Run never stops on its own**: the run ends as failed when its run limit is reached; no commit.
- **Write tool aimed outside the wiki**: the call is refused, the refusal is recorded as a tool call in the task, and the run continues under the agent's own decision.
- **A tool outside the granted set is attempted**: the attempt is refused and recorded; no such tool is available to the run.
- **Revert triggered twice** (double click, two open tabs): the second attempt is refused; the task is reverted exactly once.
- **Revert of a task whose commit is no longer the latest wiki commit**: [NEEDS CLARIFICATION: is revert offered only for the most recent commit-producing task, or for any task — and if any, does reverting an older task discard the later runs on top of it or leave them in place?]
- **Empty or whitespace-only submission**: rejected at submission; no task is created.
- **Second submission arrives while a run is executing**: queued; exactly one run executes at a time.

## Requirements *(mandatory)*

### Behaviour Classification (Judgment vs. Control)

Per Constitution I.1, every behaviour in this feature is classified below. Only **control** behaviour appears as a functional requirement. **Judgment** behaviour is named in "Judgment Boundary" and lives exclusively in the versioned ingest instruction file.

| Behaviour | Class | Where it lives |
|-----------|-------|----------------|
| Accepting a pasted-text or URL submission; rejecting an empty one | Control | Harness |
| Retrieving URL content and attaching it to the task as source text | Control | Harness |
| Creating exactly one task per accepted submission | Control | Harness |
| Dispatching exactly one agent run per task | Control | Harness |
| Running the agent as an iterative tool-use loop until it stops or hits its limit | Control | Harness |
| Which tools exist and which are granted for the run | Control | Harness |
| Refusing calls outside the granted tool set or outside the wiki | Control | Harness |
| Recording the instruction-file version and the granted tool set | Control | Harness |
| Recording every tool call, in order, with target and outcome | Control | Harness |
| Committing a run's changes as exactly one commit at run end | Control | Harness |
| Leaving the wiki at the pre-run commit when a run fails, aborts, or crashes | Control | Harness |
| Task states and their transitions; openability in every state | Control | Harness |
| Showing state, source, instruction version, tool calls, and diff | Control | Harness |
| Offering revert, restoring the previous commit, marking the task reverted | Control | Harness |
| Serialising runs so at most one executes at a time | Control | Harness |
| Whether the source warrants one page or several | Judgment | Ingest instruction |
| Whether to create a new page or update an existing one | Judgment | Ingest instruction |
| What a page is named and where it sits | Judgment | Ingest instruction |
| What a good page contains, how it is worded and structured | Judgment | Ingest instruction |
| Which links to add, and to what | Judgment | Ingest instruction |
| Which existing pages to read before deciding | Judgment | Ingest instruction |
| Whether to leave the wiki unchanged | Judgment | Ingest instruction |
| How much of the source to reflect, and what to discard | Judgment | Ingest instruction |
| Whether and how to reorganise neighbouring content | Judgment | Ingest instruction |
| The commit message wording for the run's changes | Judgment | Ingest instruction |

### Functional Requirements

**Submission and task creation**

- **FR-001**: The web frontend MUST accept a source submission as either pasted text or a URL, and MUST reject a submission that is empty or whitespace-only without creating a task.
- **FR-002**: Each accepted submission MUST produce exactly one task with a stable identifier, and that task MUST be openable from the moment it is created.
- **FR-003**: For a URL submission, the harness MUST retrieve the source content and store the retrieved text with the task; if retrieval fails, the task MUST end in state failed with a recorded human-readable reason and the wiki MUST be unchanged.
- **FR-004**: The task MUST retain the source text it was created from for as long as the task is retained.

**Dispatch and the agent loop**

- **FR-005**: The hub MUST dispatch exactly one agent run per task. A task MUST NOT have more than one run.
- **FR-006**: At dispatch the run MUST be given the task's source text and the ingest instruction file's content as loaded for that run.
- **FR-007**: The run MUST execute as an iterative loop in which the agent issues tool calls, receives each call's result, and continues deciding on that basis, until the agent stops of its own accord or the run limit is reached. A single model call whose output the harness then applies to the wiki MUST NOT satisfy this requirement.
- **FR-008**: The number of iterations and the sequence of tool calls MUST be determined by the agent during the run, not fixed by the harness.
- **FR-009**: The harness MUST enforce a run limit (elapsed time and tool-call count); reaching it MUST end the run as failed.

**Tool grant and containment**

- **FR-010**: The harness MUST grant the run exactly two tools: one that reads wiki content and one that writes wiki content. Every other tool MUST be denied, including any means of network access, command execution, or filesystem access outside the wiki.
- **FR-011**: The write tool MUST refuse any target outside the wiki, and the refusal MUST be recorded as a tool call with a failed outcome.
- **FR-012**: A write performed by the run MUST NOT be visible in the wiki before that run's commit.
- **FR-013**: The harness MUST record the granted tool set on the task at dispatch, before the first model call of the run.
- **FR-014**: The harness MUST record on the task the version of the instruction file that was loaded for the run, at dispatch, before the first model call.

**Commit and failure containment**

- **FR-015**: When a run ends having changed wiki content, the harness MUST commit all of that run's changes as exactly one commit, and MUST record that commit's identity on the task.
- **FR-016**: When a run ends having changed no wiki content, the harness MUST create no commit, and the task MUST end completed and state explicitly that nothing changed.
- **FR-017**: When a run fails, aborts, crashes, or reaches its run limit, the harness MUST create no commit and MUST leave the wiki content identical to the commit that was current before the run started.
- **FR-018**: A failed task MUST record a human-readable failure reason.
- **FR-019**: At most one run MUST execute at a time; a submission accepted while a run is executing MUST produce a task in state queued that is dispatched after the running one ends.

**Task view**

- **FR-020**: A task MUST expose exactly these states: queued, running, completed, failed, reverted; and MUST be openable in every one of them, showing what is known so far.
- **FR-021**: The task view MUST show, for every task: its state, its source, the instruction-file version recorded for its run, the granted tool set, and the tool calls recorded so far in the order they were made, each with its target and its outcome.
- **FR-022**: The task view MUST show the resulting wiki diff — per file, the content added, changed, and removed — for a task whose run produced a commit, and MUST show the commit identity.
- **FR-023**: The recorded instruction-file version, granted tool set, tool calls, and diff of a finished run MUST NOT be altered afterwards; the only permitted post-run change to a task is its transition to reverted.

**Revert**

- **FR-024**: The task view MUST offer a revert action exactly when the task's run produced a commit and the task is not already reverted, and MUST NOT offer it otherwise.
- **FR-025**: Revert MUST restore wiki content to its state at the commit preceding the task's commit, MUST complete without any manual intervention outside the task view, and MUST set the task's state to reverted.
- **FR-026**: A reverted task MUST remain openable with its instruction-file version, tool calls, and original diff intact, and MUST NOT offer revert again. A second revert attempt on the same task MUST be refused.

### Judgment Boundary *(non-requirements)*

The following are **not** functional requirements of this feature and MUST NOT be implemented as harness rules, branching, or hard-coded content logic. They are decided by the agent during the run, under the versioned ingest instruction file, and are changed by editing that file (Constitution I.1, Instruction Change Workflow):

- What makes a wiki page good — its contents, wording, length, structure, or headings.
- How a page is named, and where in the wiki it is placed.
- When to update an existing page instead of creating a new one, and when to do both.
- Whether one source becomes one page or several.
- Which links to create, between which pages, and in which direction.
- Which existing pages the agent reads before deciding, and how many.
- Whether a source justifies changing the wiki at all.
- Which parts of the source to keep, summarise, or discard.
- Whether to reorganise or tidy neighbouring pages while ingesting.
- The wording of the commit message for the run's changes.

No test in this feature asserts that a run produced particular wiki content, particular page names, or a particular number of tool calls (Constitution III.4).

### Key Entities

- **Source**: what the user submitted — either pasted text or a URL plus the text retrieved from it. Belongs to exactly one task.
- **Task**: the user-facing, inspectable record of one ingest. Has an identifier, a state (queued, running, completed, failed, reverted), timestamps, its source, and exactly one agent run. Openable in every state.
- **Agent run**: one execution of the agent for one task. Carries the instruction-file version loaded for it, the tool grant record, the ordered tool-call record, an outcome (completed, failed) with a failure reason when failed, and at most one resulting commit.
- **Tool grant record**: the set of tools the run was given, recorded at dispatch; for this feature, the wiki read tool and the wiki write tool.
- **Tool call record**: one entry per call the agent made — order, tool, target, outcome — including refused calls.
- **Wiki commit**: the single commit produced by a run that changed content, and the diff derived from it.
- **Revert record**: the fact that a task's commit was undone, and when; makes the task's state reverted.
- **Ingest instruction**: the versioned instruction file loaded at dispatch. Its version is recorded on every task; its content is the sole home of the judgment listed in Judgment Boundary.

## Success Criteria *(mandatory)*

### Control Outcomes *(deterministic and measurable)*

- **SC-001**: 100% of accepted submissions produce exactly one task that the user can open, and the task is visible to the user within 2 seconds of submitting.
- **SC-002**: Across all runs that changed wiki content, the ratio of commits to runs is exactly 1:1 — never zero, never more than one.
- **SC-003**: After any run that failed, aborted, crashed, or hit its limit, the wiki content is byte-identical to the commit that was current before the run started, in 100% of cases, and no commit exists for that task.
- **SC-004**: Every run's task records the instruction-file version and the granted tool set, and the granted set is exactly the two wiki tools in 100% of runs. No run performs an effective action outside that set.
- **SC-005**: One revert action from the task view restores the wiki to its pre-run content byte-for-byte and marks the task reverted, with zero manual steps, in 100% of attempts on eligible tasks.
- **SC-006**: A task can be opened in each of the five states without error, and shows its state, instruction-file version, granted tool set, and tool calls so far in every state where those exist.
- **SC-007**: The run mechanism feeds every tool call's result back to the agent and continues from it: an agent driven by scripted responses that issues N tool calls across N iterations has all N executed and recorded within a single run, for any N up to the run limit. A mechanism that applies one model output and stops fails this criterion.

### Judgment Outcomes *(operator-observable, per Constitution III.5)*

- **SC-008**: From the **task view** alone, reading the **resulting diff and the ordered tool-call record**, an operator can decide whether the run's wiki changes were appropriate — which pages it created or updated, what it read first, and what the wiki now says — without opening the repository, a log file, or a terminal. When the answer is repeatedly hard to reach, the operator edits the **ingest instruction**.
- **SC-009**: When the diff shows the wrong decision — a new page where an update belonged, a name that does not fit the wiki, missing links, content that should not have been written — the operator's correction is an edit to the **ingest instruction** followed by a re-dispatch, with no harness change and no code deploy. The **task view** of the next run shows the new instruction-file version alongside the new diff, so the change in behaviour is attributable to that instruction revision.
- **SC-010**: From the **tool-call record on the task view**, an operator can tell a run that consulted existing wiki pages before writing from one that wrote without looking; when runs are not consulting the wiki, the operator edits the **ingest instruction**.
- **SC-011**: For a run that changed nothing, the **task view** — its empty diff and its tool-call record — lets the operator distinguish an agent that judged no change was warranted from a run that failed to do its work; the corrective surface for the former is the **ingest instruction**.

## Out of Scope

Explicitly excluded from this feature: querying or searching the wiki; linting or validating wiki content; streaming or live progress of a run; interrupting or cancelling a run in flight; user annotations, comments, or notes on tasks; authentication, accounts, and authorisation; more than one concurrent run; scheduled or recurring ingest; and any submission channel other than the web frontend.

## Assumptions

- The wiki is a markdown wiki held in a git repository, and commits and reverts are the mutation and undo mechanism. This is a constitutional given (Constitution II.1), not a technology choice made by this spec.
- Because the granted tool set is the wiki read and write tools only, the agent has no means of fetching a URL. The harness therefore retrieves URL content before or at dispatch and passes the retrieved text to the run as the task's source.
- The system is single-user in this feature; authentication is out of scope, so "the user" and "the operator" are the same person.
- A run limit exists and its values (elapsed time, maximum tool calls) are configuration rather than part of this specification; only the existence of the limit and its consequence (failed, no commit) are specified.
- Exactly one versioned instruction file — the ingest instruction — is loaded for an ingest run in this feature.
- Tasks and their run records are retained indefinitely; no retention or cleanup policy is part of this feature.
- The task view reflects state as of when it is loaded or refreshed; live updating is out of scope.
- Commit identity is shown to the user as an opaque reference; the user is not expected to use git directly.
