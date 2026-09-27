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
`--max-review-rounds N`, `--review-timeout S`, `--max-turns N`, `--skip-mutation`.

A run takes hours. Start it in a terminal you can leave open (or with `nohup`), and rerun it after
a halt or a Ctrl+C: it carries on where it stopped.

As a Spec Kit extension the layout is ready for `specify extension add --dev tools/phasepr`, which
provides `/speckit.phasepr.run` (a thin launcher that starts the script detached and verifies it
started) and `/speckit.phasepr.status`. That installation has **not** been tried yet.

### Requirements

- bash 4 or newer (macOS: `brew install bash`), git, jq.
- `gh`, logged in with rights to push, open, review-request and merge. Review threads, resolving a
  thread, `gh pr merge`, `gh pr ready` and `gh pr checks` go through GitHub's GraphQL API; the rest
  is REST. A Claude Code cloud session blocks GraphQL at its proxy, so phasepr does not run there as
  it stands; run it on a machine with a normal `gh` login.
- `claude`, signed in through its own OAuth login. No `ANTHROPIC_API_KEY`: every iteration uses the
  CLI's existing sign-in; phasepr removes `ANTHROPIC_API_KEY` from each agent's environment.
- `dotnet` for the gates, and whatever `scripts/mutation.sh` needs.

Every agent iteration is

```
claude -p "<prompt>" --permission-mode auto --output-format json --max-turns N [--model ID] \
       --disallowedTools "Bash(git push:*)" "Bash(git reset:*)" "Bash(git rebase:*)" \
                         "Bash(git merge:*)" "Bash(git commit --amend:*)" "Bash(gh pr merge:*)"
```

with stdin closed; the JSON output and the prompt are kept in `specs/<feature>/phasepr/logs/`.

### Permissions

Headless, a permission prompt has nobody to answer it, so an iteration runs in one of the two modes
that never ask:

- **`auto`** (the default): the CLI's own safety check decides each action. What it refuses is not
  asked about — it is listed in the result's `permission_denials`, and phasepr halts with
  `permission-denied`, naming the refused commands. Verified on 2026-09-27 with CLI 2.1.283, as
  root too: with `claude-sonnet-5` an implement and a triage iteration ran without a single refusal,
  the triage's replies, resolves and round comment through `gh-review.sh` included. With
  `claude-haiku-4-5-20251001` auto mode refused every action that needs approval, even writing a
  file — pick a model that supports it.
- **`bypassPermissions`**: everything is allowed. Only inside a sandbox; as root the CLI refuses it
  unless `IS_SANDBOX=1` is set.

In both modes the CLI refuses outright what phasepr alone does or forbids: `git push`, `git reset`,
`git rebase`, `git merge`, `git commit --amend`, `gh pr merge`. An agent that tries one anyway is logged, not
halted — nothing happened. The history check below stays either way: auto mode let a
`git commit --amend` through when it was not on that list.

## Configuration

`phasepr-config.yml`, overridden by an untracked `phasepr-config.local.yml` beside it, overridden by
the flags.

| Key | Default | |
| --- | --- | --- |
| `model` | `claude-opus-5-5` | a pinned id that supports auto mode |
| `agent_cli` | `claude` | the only one implemented; `invoke_agent` is where another would go |
| `permission_mode` | `auto` | `auto` or `bypassPermissions`; see "Permissions" |
| `max_turns` | 200 | per iteration |
| `max_implement_iterations` | 8 | per phase, a hard stop whatever else happens |
| `max_review_rounds` | 3 | 1–3; Constitution I.11 caps it at three |
| `review_timeout` | 600 | seconds to wait for the review of the current head |
| `review_poll_interval` | 20 | seconds between two polls |
| `reviewer_login` | `copilot-pull-request-reviewer[bot]` | see below |
| `run_mutation` | `true` | `false` (or `--skip-mutation` for one run) leaves the measurement to CI's `mutation` job on the PR to main |

### Repository settings phasepr relies on

Read on 2026-09-27. `GET …/branches/{branch}/protection` answers 403 to the token that could read
everything else, so what follows comes from `GET …/branches/{branch}` and `GET …/rulesets`:

- No classic branch protection: `main`, the feature branches and the phase branches all report
  `protected: false`.
- One ruleset, "main", targets only the default branch and is **disabled** (`enforcement:
  disabled`). Its rules, if it were enabled: no deletion, no force-push, changes through a pull
  request with 0 required approvals; merge, squash and rebase allowed.
- Hence the assumption phasepr is built on: **feature and phase branches have no required reviews
  and no required checks.** `gh pr merge --merge` on a phase PR is gated only by phasepr's own
  checks (CI green via `gh pr checks`, no open thread, the owner's approval where it stops for one).
  Should a feature branch ever require an approval, the merge fails and phasepr halts with
  `github-error`.
- `delete_branch_on_merge` is on, which makes `--delete-branch` redundant but harmless.

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

Not verified: `gh pr edit --add-reviewer copilot`. phasepr does not use it.

### Verified against GitHub

On 2026-09-27, with `gh` 2.101.0 and a normal login, every `gh-review.sh` subcommand ran against
this repository on a throwaway PR (#59, into a throwaway base branch; both deleted afterwards):
`phase-open`; `wait` (Copilot's review on opening, 102 s); `threads` (GraphQL — the one open thread
with its node id, comment id, path and line; GraphQL reports the author as
`copilot-pull-request-reviewer`, without `[bot]`); `reply`; `resolve` (GraphQL — no open thread
left); `rerequest` and `wait` again (review of the new head, 119 s); `ready` (draft → ready, and a
no-op on a ready PR); `checks` (`gh pr checks --watch`, exit 0 with every check green); `merge`
(`gh pr merge --merge --delete-branch`: a merge commit with two parents, the head branch deleted,
no local checkout touched).

Not seen for real: `checks` while checks are still running or not yet registered, `merge` on a
conflict, `find … merged`, `tick` and `draft-open` — the fake GitHub of the tests covers them.

### Branch protection

Read on 2026-09-27: `main` and the feature branch `004-ask-the-wiki` are not protected, and the one
ruleset (`main`) is disabled. phasepr assumes feature branches carry no required reviews or checks:
it merges a phase PR itself once its checks are green. A protection that requires an approval makes
`gh pr merge` fail, and phasepr halts with `github-error`.

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
- no other local branch moved — an agent that committed elsewhere and came back would slip that
  commit into a later phase unreviewed — and no merge commit was made;
- no checkbox outside the phase moved;
- progress is a new commit or a newly checked task; three iterations in a row without either trip
  the circuit breaker;
- a triage's `{"changed": …}` must agree with whether it committed;
- every new commit's subject reads `type(scope): subject`, the scope the feature's number or a
  requirement ID in lower case (`feat(003): …`, `test(runs-008): …`), as this repository's history
  does. A commit that does not halts the run with `commit-subject`; phasepr never rewords it. A rerun
  judges only the commits made after it starts, so rerunning accepts the flagged ones as they are.

A phase is done when its tasks are checked, the tree is clean and `gates.sh` — run by phasepr —
is green on that exact commit: `dotnet build`, the Fast suite under its 15 s session timeout
(time-budget), and `trace-check` — all in Release, as CI builds them. A red gate goes back to a fresh implement iteration with the end of
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
| `protocol-violation` | history rewritten, branch switched, another branch moved, a merge commit, another phase's checkbox moved | repairs by hand; nothing was reset |
| `owner-review` | the phase changes `instructions/`, `docs/decisions.md`, the constitution (I.11) or `docs/product.md` (I.1) | approves the PR's current head as the owner (the repository owner, or `PHASEPR_OWNER_LOGIN`) — another person's or an older head's approval does not count — or merges it |
| `untrusted-review` | an open thread has a comment — its first or a reply — by someone other than Copilot or the owner: its text would reach an agent that can commit and reply | answers and resolves it |
| `commit-subject` | an agent commit is not `type(scope): subject` with the feature's number or a requirement ID as scope | rewords it on the branch, or reruns to accept it |
| `ci-red` | the PR's checks are red although `gates.sh` was green | looks at the check |
| `permission-denied` | the permission mode refused an agent something it needed | allows it in `.claude/settings.json`, changes model or mode, or takes it out of the task |
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

bats-core, git and jq; about a minute on Linux, several on macOS. CI runs them, with shellcheck,
in `.github/workflows/tooling.yml` whenever `tools/phasepr/` changes — not a required check. `tests/shims/` puts fakes first on `PATH`: `gh` is a small
file-backed GitHub (PRs, reviews, GraphQL threads, merges into a local bare `origin`, a Copilot that
reviews on opening and on request), `claude` runs a per-test scenario script per iteration, and
`dotnet` fails `dotnet test` on cue. Every test builds its own repository from
`tests/fixtures/repo`. None of this touches GitHub or a model; see the PR that added phasepr for
what was checked against the real ones.
