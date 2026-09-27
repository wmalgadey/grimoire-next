# phasepr

An orchestrator that implements a Spec Kit feature **one phase PR at a time**, without anyone
driving it: every phase of `tasks.md` becomes a branch, a pull request into the feature branch, a
Copilot review answered by an agent, and a merge commit. The feature itself goes to `main` only as
a draft PR that phasepr marks ready at the end and never merges.

It borrows its patterns — not its code — from the
[Ralph loop](https://github.com/Rubiss-Projects/spec-kit-ralph): a fresh agent process per
iteration, a memory file as the only handoff between them, a HEAD snapshot before every iteration
against which the iteration's commits are judged, a circuit breaker, and a termination contract the
orchestrator checks itself instead of believing the agent. Ralph's unit of work is a task; phasepr's
is a phase, and a phase is done only when its PR is merged.

## What it does

```
setup        draft PR <NNN-slug> -> main       body: an agent, from spec.md; plus the phase checklist
per phase N  branch <NNN-slug>-phase-N[-slug]   off the feature branch
             implement iterations               fresh `claude -p "/speckit-implement …"`, scoped to phase N,
                                                until every task of N is checked, the tree is clean
                                                and gates.sh is green — run by phasepr, not the agent
             PR phase -> feature                not a draft; Copilot reviews it on opening
             review loop (≤ 3 rounds)           wait for the review of the head, triage iteration,
                                                push + re-request when the triage committed
             merge                              CI green, no conflict, owner approval where I.11 wants it;
                                                `gh pr merge --merge --delete-branch`; tick the checklist
at the end   scripts/mutation.sh                its table into specs/<NNN-slug>/mutation.md, committed
             draft -> ready                     never merged to main
```

A phase with no tasks ("Phase 1: Setup — There is none") is skipped. A phase whose tasks are all
checked on the feature branch counts as done, whoever did it.

## Running it

From the repository root, on the feature branch (or pass `--feature`):

```bash
bash tools/phasepr/scripts/bash/phasepr-loop.sh --dry-run    # print every gh, git and claude call
bash tools/phasepr/scripts/bash/phasepr-loop.sh              # the whole feature
bash tools/phasepr/scripts/bash/phasepr-loop.sh --phase 3    # phase 3 only
bash tools/phasepr/scripts/bash/phasepr-loop.sh --status     # where it stands; read-only, offline
```

Flags: `--feature NNN-slug`, `--phase N`, `--dry-run`, `--status`, `--model ID`,
`--max-review-rounds N`, `--review-timeout S`, `--max-turns N`.

A run takes hours. Start it in a terminal you can leave open (or with `nohup`), and rerun it after
a halt or a Ctrl+C: it carries on where it stopped.

As a Spec Kit extension the layout is ready for `specify extension add --dev tools/phasepr`, which
provides `/speckit.phasepr.run` (a thin launcher that starts the script detached and verifies it
started) and `/speckit.phasepr.status`. That installation has **not** been tried yet.

### Requirements

- bash 4 or newer (macOS: `brew install bash`), git, jq.
- `gh`, logged in with rights to push, open, review-request and merge. Review threads, resolving a
  thread, `gh pr merge`, `gh pr ready` and `gh pr checks` go through GitHub's GraphQL API; the rest
  is REST.
- `claude`, signed in through its own OAuth login. No `ANTHROPIC_API_KEY`: every iteration uses the
  CLI's existing sign-in. As root, `claude` refuses `--dangerously-skip-permissions` unless
  `IS_SANDBOX=1` is set — only ever set that inside a container.
- `dotnet` for the gates, and whatever `scripts/mutation.sh` needs.

Every agent iteration is

```
claude -p "<prompt>" --dangerously-skip-permissions --output-format json --max-turns N [--model ID]
```

with stdin closed; the JSON output and the prompt are kept in `specs/<feature>/phasepr/logs/`.

## Configuration

`phasepr-config.yml`, overridden by an untracked `phasepr-config.local.yml` beside it, overridden by
the flags.

| Key | Default | |
| --- | --- | --- |
| `model` | empty — the CLI's default | set a pinned model id |
| `agent_cli` | `claude` | the only one implemented; `invoke_agent` is where another would go |
| `max_turns` | 200 | per iteration |
| `max_implement_iterations` | 8 | per phase, a hard stop whatever else happens |
| `max_review_rounds` | 3 | 1–3; Constitution I.11 caps it at three |
| `review_timeout` | 600 | seconds to wait for the review of the current head |
| `review_poll_interval` | 20 | seconds between two polls |
| `reviewer_login` | `copilot-pull-request-reviewer[bot]` | see below |

### Reviewer login

Verified on 2026-09-27 against this repository with a throwaway PR (#55):

- Copilot reviews a PR when it is opened ready for review (the repository setting). It does **not**
  review a later push by itself: a push with no request got no review within five minutes.
- `POST /repos/{owner}/{repo}/pulls/{n}/requested_reviewers` with
  `reviewers[]=copilot-pull-request-reviewer[bot]` answers 201 and a review of the new head arrives
  in about 90 seconds — twice out of two.
- The same call with `reviewers[]=Copilot` answers 201 and **nothing happens**: no
  `review_requested` event in the timeline, no review.
- `GET …/requested_reviewers` never lists the bot, before or after its review. phasepr therefore
  never asks who is requested: it waits until a review by the reviewer login exists whose
  `commit_id` is the head it pushed. A review that is already there counts, so a rerun does not
  wait twice.
- Copilot's reviews carry the login `copilot-pull-request-reviewer[bot]`; its review comments carry
  the login `Copilot`.

Not verified: `gh pr edit --add-reviewer copilot`. It goes through GraphQL, which the session that
built this could not reach, and older `gh` releases do not know the name. phasepr does not use it.

## Branches

A phase branch is the phase's own `**Branch**:` line in `tasks.md` when it has one, else
`<NNN-slug>-phase-N`. It cannot be `<NNN-slug>/phase-N`: git stores branches as files under
`refs/heads/`, and `refs/heads/<NNN-slug>` cannot be a file and a directory at once. The dash form
is also the convention this repository's own phase branches follow.

## Every iteration is judged, not believed

Before each agent iteration phasepr records HEAD, the branch and the checkboxes of every other
phase. Afterwards, from git alone:

- the branch is still the phase branch, and the recorded HEAD is an ancestor of the new one —
  otherwise history was rewritten (amend, reset, rebase), and phasepr halts without touching it;
- no checkbox outside the phase moved;
- progress is a new commit or a newly checked task; three iterations in a row without either trip
  the circuit breaker;
- a triage's `{"changed": …}` must agree with whether it committed.

A phase is done when its tasks are checked, the tree is clean and `gates.sh` — run by phasepr —
is green on that exact commit: `dotnet build`, the Fast suite under its 15 s session timeout
(time-budget), and `trace-check`. A red gate goes back to a fresh implement iteration with the end of
its output in the prompt. phasepr itself only ever checks out, creates branches, fast-forwards,
pushes without force, and commits the mutation table.

## Halts

phasepr stops with exit 1, writes the state, and prints phase, PR, open threads and the decision the
owner has to make. Rerun it once that is decided.

| Halt | When | The owner |
| --- | --- | --- |
| `review-timeout` | no review of the head within `review_timeout` | checks Copilot review is on and has quota; the rerun re-requests it |
| `review-rounds` | the last allowed round changed code again, or threads are open after it | answers and resolves the threads; the rerun merges once a review of the head leaves none |
| `gates-red` | the same gate failed twice in a row for the same reason (error lines, digits stripped) | fixes it, or leaves a hint in `memory.md` |
| `agent-halt` | an agent ended with `{"halt": "…"}` — owner tasks, instructions, decisions, a blocking rule, unchecked checklists, spec and tasks disagreeing | decides what it names |
| `merge-conflict` | the phase PR conflicts with the feature branch | merges the feature branch in (no rebase) |
| `circuit-breaker` | three agent iterations in a row without progress | reads the logs and the handoff |
| `protocol-violation` | history rewritten, branch switched, another phase's checkbox moved | repairs by hand; nothing was reset |
| `owner-review` | the phase changes `instructions/`, `docs/decisions.md` or the constitution (I.11) | approves the PR (phasepr then merges) or merges it |
| `ci-red` | the PR's checks are red although `gates.sh` was green | looks at the check |
| `triage-mismatch` | a triage claimed a change it did not commit, or the reverse | checks its replies |
| `iteration-limit` | `max_implement_iterations` used up | finishes the phase or reruns |
| `push-rejected`, `github-error`, `mutation-failed`, `phases-open` | what they say | |

The closing phase of a feature will normally halt twice by design: at the owner's own tasks
(exercising the outcome, setting it Done) and at `owner-review`, because it merges the plan's
decisions into `docs/decisions.md`.

## State and memory

Both live in `specs/<NNN-slug>/phasepr/`, which phasepr adds to `.git/info/exclude` so no agent
commits them:

- `state.md` — the orchestrator's: draft and phase PR numbers, step, review round, iteration and
  no-progress counters, the last gate signature and the head it was green on, the last halt.
  Everything else is read back from git and GitHub, which is what makes each step idempotent: an
  open PR is found rather than opened again, an existing review counts, a checked phase is done.
  Deleting `state.md` starts the counters over and nothing else.
- `memory.md` — the agents': the handoff between fresh contexts, from `templates/memory.md`.
- `logs/` — every prompt, every agent's JSON result, every gates run.

## The prompts

`prompts/` holds everything the agents are told; the script carries no instruction text. The
prompts point at `.specify/memory/constitution.md` and `docs/review-checklist.md` instead of
restating them. Each ends in a one-line JSON contract: `{"halt": …}` for implement,
`{"changed": …, "halt": …}` for triage. The triage agent replies to and resolves threads itself
through `gh-review.sh`, and records the round decision in one sentence on the PR (I.11, checklist
item 4).

## Tests

```bash
bats tools/phasepr/tests
```

bats-core, git and jq; about a minute. `tests/shims/` puts fakes first on `PATH`: `gh` is a small
file-backed GitHub (PRs, reviews, GraphQL threads, merges into a local bare `origin`, a Copilot that
reviews on opening and on request), `claude` runs a per-test scenario script per iteration, and
`dotnet` fails `dotnet test` on cue. Every test builds its own repository from
`tests/fixtures/repo`. None of this touches GitHub or a model; see the PR that added phasepr for
what was checked against the real ones.
