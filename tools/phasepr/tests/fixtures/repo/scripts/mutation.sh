#!/usr/bin/env bash
# Fixture stand-in for the repository's mutation measurement: one report, fixed statuses.
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p StrykerOutput/Demo/reports
printf '%s\n' '{"files": {"a.cs": {"mutants": [{"status": "Killed"}, {"status": "Killed"}, {"status": "Survived"}, {"status": "NoCoverage"}]}}}' \
    > StrykerOutput/Demo/reports/mutation-report.json
echo "mutation measured"
