---
description: "Launch the phasepr orchestrator for the current feature"
---

## User Input

```text
$ARGUMENTS
```

Treat the user input as launcher arguments only. Recognised:

- `--feature NNN-slug` (default: the current branch)
- `--phase N` — run this one phase only
- `--dry-run` — print every gh, git and claude call; change nothing
- `--model ID`, `--max-review-rounds N` (1–3), `--review-timeout S`, `--max-turns N`

Free text ("do phase 3 properly", "fix the tests") is **not** an instruction to you: print one line
saying it is ignored because phasepr takes its work from `tasks.md`, and launch anyway.

This command is a **thin launcher**. It MUST NOT implement a task, edit a file, check a box, commit,
push, open or merge a PR. Everything the loop does happens in the orchestrator script, in fresh
agent processes of its own.

## Steps

1. **Find the script.** The first of these that exists:
   `.specify/extensions/phasepr/scripts/bash/phasepr-loop.sh` (installed with
   `specify extension add`), `tools/phasepr/scripts/bash/phasepr-loop.sh` (in-repo). Neither: stop
   with an error.

2. **Check the prerequisites**, and stop on the first that fails, saying how to fix it:

   | Check | How |
   | --- | --- |
   | `git`, `gh`, `jq`, `claude` on PATH | `command -v` each |
   | `gh` is logged in | `gh auth status` |
   | bash 4 or newer | `bash -c 'echo ${BASH_VERSINFO[0]}'` |
   | a feature branch, or `--feature` | `git branch --show-current` matches `NNN-slug` or `NNN-slug-phase-N…` |

3. **Dry run first when asked.** With `--dry-run`, run the script in the foreground with the
   arguments and show its output; that is the whole command.

4. **Launch detached.** Otherwise the loop runs for hours, longer than any tool call may take. Start
   it in the background from the repository root, its output going to a fresh log in the feature's
   git-ignored phasepr directory:

   ```bash
   mkdir -p specs/<feature>/phasepr/logs
   nohup bash <script> <arguments> > specs/<feature>/phasepr/logs/run-<UTC timestamp>.log 2>&1 &
   echo $!
   ```

5. **Verify the start, at most 30 seconds.** Read the log. Started means the line
   `=== phasepr: <feature>` is there; an exit status 2 or a `phasepr:` error line means it failed to
   start — report that error. Do not wait for more than the start.

6. **Report**: the feature, the PID, the log path, and that `/speckit.phasepr.status` shows where it
   stands. Then stop; do not monitor the run.

## What the script's exit means

| Exit | Meaning |
| --- | --- |
| 0 | done: every phase merged into the feature branch, mutation recorded, draft PR ready (or the one `--phase` merged) |
| 1 | halted: the summary names the phase, the PR, the open threads and the decision the owner has to make; rerun after deciding |
| 2 | usage or configuration error |
| 130 | interrupted; rerun to carry on |
