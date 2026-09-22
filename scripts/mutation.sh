#!/usr/bin/env bash
# The mutation measurement of feature 001-first-ingest.
#
#   ./scripts/mutation.sh
#
# One Stryker run per project in scope, because Stryker mutates one project under test at a
# time. The Fast suite is the only suite used; Grimoire.Mutation.slnx is what keeps it that way.
# Reports land in StrykerOutput/<project>/reports/mutation-report.json, which is git-ignored.
#
# This is a measurement. It has no threshold, it is not a gate, and nothing here changes a test,
# a test runner or a line of production code.
set -euo pipefail

cd "$(dirname "$0")/.."

dotnet tool restore

# The parser that turns the CLI's stream lines into port events lives inside an adapter that also
# starts and stops the process, and Stryker's mutate filter takes character spans rather than
# member names. These two spans are that parser — SurfaceIsTheGrant, and ReadAsync with every
# helper below it — and they are computed from the code so that editing the file above them
# cannot silently move the measurement onto the process plumbing that is out of scope.
parser_spans() {
  python3 - "$1" <<'PY'
import sys
source = open(sys.argv[1], newline='').read()
first = source.index("    public static bool SurfaceIsTheGrant")
after_first = source.index("    public Task DispatchAsync")
rest = source.index("    private async Task ReadAsync")
print(f"{{{first + 1}..{after_first}}}{{{rest + 1}..{len(source)}}}")
PY
}

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
# so is every Adapters/ folder, except the parser named above.
run Grimoire.Runs
run Grimoire.Agent \
  --mutate '**/Ceilings.cs' \
  --mutate '**/IAgentHarness.cs' \
  --mutate '**/ToolGrant.cs' \
  --mutate "**/Adapters/HarnessProcess.cs$(parser_spans src/Grimoire.Agent/Adapters/HarnessProcess.cs)"
run Grimoire.Wiki --mutate '**/*' --mutate '!**/Adapters/**'
run Grimoire.Trace
