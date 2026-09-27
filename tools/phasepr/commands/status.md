---
description: "Show where phasepr stands for the current feature (read-only)"
---

## User Input

```text
$ARGUMENTS
```

Optional: `--feature NNN-slug` (default: the current branch).

This command is **read-only**: it changes no file, runs no agent and calls no GitHub API.

## Steps

1. Find the script: `.specify/extensions/phasepr/scripts/bash/phasepr-loop.sh`, else
   `tools/phasepr/scripts/bash/phasepr-loop.sh`.
2. Run it with `--status` and the `--feature` argument if one was given, and show its output
   as it is: the state phasepr recorded (phase, PR, step, review round, counters, the last halt)
   and how many tasks of each phase are checked on the feature branch.
3. If a run is in progress, the newest `specs/<feature>/phasepr/logs/run-*.log` holds its output;
   show its last 20 lines.
4. If the state shows a halt, say in one sentence what the owner has to decide — it is the
   "Owner decision" line — and that rerunning `/speckit.phasepr.run` carries on from there.
