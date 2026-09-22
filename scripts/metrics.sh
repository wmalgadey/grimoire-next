#!/usr/bin/env bash
# The three project metrics, computed from a built tree.
#
#   ./scripts/metrics.sh                     # Debug, the configuration a plain local build leaves
#   CONFIGURATION=Release ./scripts/metrics.sh
#
# Writes a Markdown table to stdout — and nothing else to stdout, so `./scripts/metrics.sh > table`
# is the table — plus three shields.io endpoint documents to artifacts/metrics/badges/. Nothing
# into a file that is committed: no number reaches README.md except through a badge URL.
#
# None of the three is a gate and none is a target. There is no threshold anywhere in this script:
# it fails when a measurement breaks — a tool missing, a report unreadable, a value absent — and
# never because a value is low. The tools are the existing ones (Constitution II.3): the repository's
# own trace reader for the requirements, Microsoft.Testing.Platform's coverage extension with
# ReportGenerator for the coverage, and cloc for the line count. Nothing here measures anything
# itself.
#
# Needs: a built tree (`dotnet build Grimoire.slnx`), `dotnet tool restore`, `jq`, `cloc`
# (`brew install cloc`, `apt-get install cloc`), and a git checkout — cloc is asked for the files
# git lists.
set -euo pipefail

cd "$(dirname "$0")/.."

CONFIGURATION="${CONFIGURATION:-Debug}"
OUT="$PWD/artifacts/metrics"
BADGES="$OUT/badges"

# One flat colour for all three. A colour scale would say "this value is good" or "this value is
# bad", which is what turns a measurement into a target.
COLOUR="blue"

rm -rf "$OUT"
mkdir -p "$BADGES"

# ---------------------------------------------------------------------------
# 1. Requirements proven
#
# The `summary` verb of the repository's own trace tool: the same two readers trace-check uses, so
# the badge and the gate cannot disagree about what is registered. Retired ids are not counted.
# ---------------------------------------------------------------------------
dotnet run --project tools/Grimoire.Trace --no-build --configuration "$CONFIGURATION" \
  -- summary --configuration "$CONFIGURATION" > "$OUT/requirements.json"

requirements=$(jq -e '.requirements' "$OUT/requirements.json")
by_test=$(jq -e '.byProof.test' "$OUT/requirements.json")
by_eval=$(jq -e '.byProof.eval' "$OUT/requirements.json")
by_review=$(jq -e '.byProof.review' "$OUT/requirements.json")
with_test=$(jq -e '.testRequirementsWithATest' "$OUT/requirements.json")

# ---------------------------------------------------------------------------
# 2. Coverage
#
# Line coverage of the Fast suite over the code that holds decisions of ours. The collector is the
# test platform's own extension (`--coverage`, `--coverage-output-format`, `--coverage-output`);
# ReportGenerator does the filtering and the summing.
#
# No `--timeout` here. The 15 s budget of Constitution III.7 is a gate and belongs to the run
# without instrumentation; instrumenting a run and then holding it to that budget would let a
# measurement fail the build.
#
# Scope: Grimoire.Runs, Grimoire.Agent, Grimoire.Wiki and Grimoire.Trace, without any Adapters
# namespace and without generated code under obj/. Grimoire.Hub is the composition root and the
# adapters are the ports to the outside; both are low by design (III.8 tests neither dependency
# wiring nor framework behaviour), so counting them would make the number mean less, not more.
# `IncludeTestAssembly` is false by default in this extension, so the suites themselves are out.
# ---------------------------------------------------------------------------
dotnet test tests/Grimoire.Fast.Tests --no-build --configuration "$CONFIGURATION" \
  -- --coverage --coverage-output-format cobertura --coverage-output "$OUT/coverage.cobertura.xml" >&2

dotnet reportgenerator \
  "-reports:$OUT/coverage.cobertura.xml" \
  "-targetdir:$OUT/report" \
  "-reporttypes:JsonSummary;TextSummary" \
  "-assemblyfilters:+Grimoire.Runs;+Grimoire.Agent;+Grimoire.Wiki;+Grimoire.Trace" \
  "-classfilters:-*.Adapters.*" \
  "-filefilters:-*/obj/*" \
  "-verbosity:Warning" >&2

coverage=$(jq -e '.summary.linecoverage' "$OUT/report/Summary.json")
covered=$(jq -e '.summary.coveredlines' "$OUT/report/Summary.json")
coverable=$(jq -e '.summary.coverablelines' "$OUT/report/Summary.json")

# ---------------------------------------------------------------------------
# 3. Time to read the code
#
# Every source file under src/, counting the lines that are neither blank nor comment-only — which
# is what cloc reports as `code` — at 20 lines per minute, rounded to 5 minutes.
#
# The rate is a convention for comparing this project with itself over time. It is not a claim
# about any reader.
# ---------------------------------------------------------------------------
readonly LINES_PER_MINUTE=20

# --vcs=git counts the files git lists under src/, so build output cannot be counted and a stray
# file in a working tree cannot change the number.
cloc --vcs=git src --quiet --json > "$OUT/cloc.json"
lines=$(jq -e '.SUM.code' "$OUT/cloc.json")

# lines / 20, rounded to the nearest 5 minutes — which is lines / 100, rounded, times 5.
minutes=$(( ((lines + LINES_PER_MINUTE * 5 / 2) / (LINES_PER_MINUTE * 5)) * 5 ))
hours=$(( minutes / 60 ))
rest=$(( minutes % 60 ))

if [ "$hours" -eq 0 ]; then
  readtime="~${rest} min"
elif [ "$rest" -eq 0 ]; then
  readtime="~${hours} h"
else
  readtime="~${hours} h ${rest} min"
fi

# ---------------------------------------------------------------------------
# The three endpoint documents, and the table
# ---------------------------------------------------------------------------
badge() {
  jq -n --arg label "$1" --arg message "$2" --arg color "$COLOUR" \
    '{schemaVersion: 1, label: $label, message: $message, color: $color}' > "$BADGES/$3"
}

badge "requirements" "$requirements · $with_test/$by_test proven by test" requirements.json
badge "coverage (fast)" "${coverage}%" coverage.json
badge "time to read" "$readtime" readtime.json

cat <<TABLE
| Metric | Value | Scope |
| --- | --- | --- |
| Requirements proven | $with_test of $by_test | \`test\` requirements carrying a test, of $requirements registered — test $by_test, eval $by_eval, review $by_review |
| Coverage | ${coverage}% of lines | Fast suite over Grimoire.Runs, Grimoire.Agent, Grimoire.Wiki, Grimoire.Trace without Adapters — $covered of $coverable lines |
| Time to read the code | $readtime | $lines lines under src/ that are neither blank nor comment-only, at $LINES_PER_MINUTE lines per minute |

None of the three is a gate or a target.
TABLE
