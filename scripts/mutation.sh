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
#
# How long it takes. Every run pays a fixed part before its first mutant — the build, the initial
# test run and, under `perTestInIsolation`, one process per test to capture coverage. With the Fast
# suite at 429 tests (004, Phase 6) that fixed part measured 6:39 and 6:54 for a Grimoire.Hub run on
# a 4-core i7-6820HQ, 5 to 5½ minutes of it the coverage capture, and every one of the five runs
# below pays it. Grimoire.Hub then has about 430 mutants to test (654 created in scope, 228 of them
# compile errors — see below); two took about 9 seconds. Estimated from that, not measured: 25 to
# 35 minutes for the Grimoire.Hub run alone on that machine, about an hour for the whole script.
# CI's `mutation` job ran the other four projects in 8 to 11 minutes against a smaller suite; with
# Grimoire.Hub, expect roughly 20 to 30.
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

# The code that holds decisions of ours. Every Adapters/ folder is left out, except AgentTranscript: it sits in the agent's adapter folder
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

# Grimoire.Hub was left out from 001 on as "the composition root" (specs/001-first-ingest/
# mutation.md). That stopped being true of it: it is the only project that decides anything
# (CLAUDE.md), and most of 004's logic is here — Chat, ChatEndpoints, InstructionLoader,
# HubApplication.RestoreAfterAStop, WikiToolsServer (specs/004-ask-the-wiki/test-audit.md, T091).
# The Fast suite reaches it through HubApplication.Build with in-memory adapters (III.9), and it has
# no Adapters/ folder, source generator or file of records only, so everything is in except
# Program.cs.
#
# Program.cs is left out, and it is now the entry point and nothing else: the top-level statements
# that put the real adapters at their ports (dependency wiring, III.8). The start-up refusals —
# loopback only, no port 0, a wiki outside --vault-root, a --state inside the wiki — used to sit in
# the same file and went unmeasured with it; they are StartUp.cs now, read by StartUpTests, and
# mutated with everything else.
#
# Stryker cannot compile part of this project mutated. Where a mutation leaves a local variable
# unassigned (CS0165 — pattern variables such as `is not { } watched`), Stryker's safe mode
# drops every mutant of the enclosing method as a compile error: 228 of the 654 in scope, 97 of
# RunConductor's 145. Those methods go untested by this measurement; the report lists them.
run Grimoire.Hub --mutate '**/*' --mutate '!**/Program.cs'

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
