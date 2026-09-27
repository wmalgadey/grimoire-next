#!/usr/bin/env bash
#
# gates.sh - what phasepr checks itself before a phase branch is pushed
#
# The same commands CI runs (.github/workflows/ci.yml), in the order a failure is cheapest:
#   build          warnings are errors (Directory.Build.props)
#   fast-suite     the Fast suite under its 15 s session timeout: the time-budget gate (III.7)
#   trace-check    `check`, not `check --complete`, which holds only where a feature lands on main
#
# Prints "=== gate failed: <name>" and exits 1 on the first red gate; "=== gates green" and 0
# otherwise. The orchestrator reads the gate's name from that line.

set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

gate() {
    local name=$1
    shift
    printf '=== gate: %s\n' "$name"
    if ! "$@"; then
        printf '=== gate failed: %s\n' "$name"
        exit 1
    fi
}

gate build dotnet build Grimoire.slnx
gate fast-suite dotnet test tests/Grimoire.Fast.Tests --no-build -- --timeout 15s
gate trace-check dotnet run --project tools/Grimoire.Trace --no-build -- check
printf '=== gates green\n'
