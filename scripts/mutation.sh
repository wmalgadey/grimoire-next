#!/usr/bin/env bash
# The mutation measurement of feature 001-first-ingest.
#
#   ./scripts/mutation.sh
#
# One Stryker run per project in scope, because Stryker mutates one project under test at a
# time. The Fast suite is the only suite used; Grimoire.Mutation.slnx is what keeps it that way.
# Reports land in StrykerOutput/<project>/reports/mutation-report.json, which is git-ignored.
#
# This script runs Stryker and collects nothing else. It classifies nothing and analyses nothing;
# reading the reports is a person's job, and specs/001-first-ingest/mutation.md is where that
# reading is written down.
#
# This is a measurement. It has no threshold, it is not a gate, and nothing here changes a test,
# a test runner or a line of production code.
set -euo pipefail

cd "$(dirname "$0")/.."

dotnet tool restore

run() {
  local project="$1"; shift
  echo "=== $project ==="
  dotnet stryker \
    --project "$project.csproj" \
    --output "StrykerOutput/$project" \
    --skip-version-check \
    "$@"
}

# The code that holds decisions of ours. Grimoire.Hub — the composition root — is left out, and
# so is every Adapters/ folder, except AgentTranscript: it sits in the agent's adapter folder
# because the CLI's protocol may not appear outside it (Constitution V.2), but it starts no
# process and touches no file, and the whole of the translation from CLI lines to port events is
# in it.
#
# Grimoire.Runs needed no filter until `002-ingest-queue` gave it its first adapter
# (SqliteSubmissionStore). It gets the same one Grimoire.Wiki has, for the same reason: this
# measurement runs the Fast suite alone, and an adapter is proven by a Contract suite against the
# real thing — so every mutant in one would come back uncovered, which says nothing about the
# tests and only buries the survivors that do. scripts/metrics.sh already scopes coverage this way
# for every project (`-classfilters:-*.Adapters.*`).
run Grimoire.Runs --mutate '**/*' --mutate '!**/Adapters/**'
run Grimoire.Agent \
  --mutate '**/Ceilings.cs' \
  --mutate '**/IAgentHarness.cs' \
  --mutate '**/ToolGrant.cs' \
  --mutate '**/Adapters/AgentTranscript.cs'
run Grimoire.Wiki --mutate '**/*' --mutate '!**/Adapters/**'

# Grimoire.Trace without its wiring and its output. `Program.cs` is argument parsing and the two
# verbs wired together, `RepositoryLayout.cs` is where the repository keeps things, and
# `TraceDocument.cs` renders docs/trace.md — none of the three holds a decision of ours, and
# III.8 does not test those. What is left is what the gate actually decides: the catalogue that
# reads the traits, the registry that reads the requirements, the check itself, and the records
# the three of them pass around.
run Grimoire.Trace \
  --mutate '**/*' \
  --mutate '!**/Program.cs' \
  --mutate '!**/RepositoryLayout.cs' \
  --mutate '!**/TraceDocument.cs'
