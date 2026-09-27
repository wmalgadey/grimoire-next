# Shared set-up for the phasepr bats suites.
#
# Every test gets its own world under $BATS_TEST_TMPDIR: a bare `origin`, a clone of the fixture
# feature 042-demo with phasepr installed at tools/phasepr, and the fakes of tests/shims first on
# PATH — gh (a file-backed GitHub), claude (scriptable per iteration) and dotnet (for gates.sh).

PHASEPR_SRC="$(cd "$BATS_TEST_DIRNAME/.." && pwd)"

setup_world() {
    # Whatever git configuration the environment injects stays out of these throwaway repositories.
    export GIT_CONFIG_COUNT=0 GIT_CONFIG_NOSYSTEM=1 GIT_CONFIG_GLOBAL=/dev/null
    export GIT_AUTHOR_NAME=test GIT_AUTHOR_EMAIL=test@example.invalid
    export GIT_COMMITTER_NAME=test GIT_COMMITTER_EMAIL=test@example.invalid
    local t=$BATS_TEST_TMPDIR
    export FAKE_GH="$t/gh" FAKE_ORIGIN="$t/origin.git" SCENARIO="$t/scenario" PHASEPR_REPO=owner/demo
    export REPO="$t/repo"
    mkdir -p "$FAKE_GH" "$SCENARIO"

    git init -q --bare -b main "$FAKE_ORIGIN"
    git init -q -b main "$REPO"
    cp -R "$PHASEPR_SRC/tests/fixtures/repo/." "$REPO/"
    mv "$REPO/gitignore" "$REPO/.gitignore"
    mkdir -p "$REPO/tools"
    cp -R "$PHASEPR_SRC" "$REPO/tools/phasepr"
    rm -rf "$REPO/tools/phasepr/tests"
    cat > "$REPO/tools/phasepr/phasepr-config.local.yml" <<'EOF'
model: "test-model"
review_timeout: 1
review_poll_interval: 0
EOF
    git -C "$REPO" add -A
    git -C "$REPO" commit -q -m "initial"
    git -C "$REPO" remote add origin "$FAKE_ORIGIN"
    git -C "$REPO" push -q origin main
    git -C "$REPO" checkout -q -b 042-demo
    git -C "$REPO" push -q -u origin 042-demo

    export PATH="$PHASEPR_SRC/tests/shims:$PATH"
    cd "$REPO"
}

phasepr() {
    bash tools/phasepr/scripts/bash/phasepr-loop.sh "$@"
}

gh_review() {
    bash tools/phasepr/scripts/bash/gh-review.sh "$@"
}

# scenario <file> <bash body>: what the fake claude runs for one kind (and optionally one call).
scenario() {
    printf '#!/usr/bin/env bash\nset -euo pipefail\n%s\n' "$2" > "$SCENARIO/$1"
    chmod +x "$SCENARIO/$1"
}

# threads_for_review <k> <count>: the fake Copilot's k-th review opens <count> threads.
threads_for_review() {
    local k=$1 count=$2 i
    mkdir -p "$FAKE_GH/review-queue"
    for ((i = 1; i <= count; i++)); do
        jq -n --arg id "T_${k}_$i" --argjson c "$((k * 100 + i))" \
            '{id: $id, isResolved: false, isOutdated: false, path: "src/a.cs", line: 3,
              comments: {nodes: [{databaseId: $c, author: {login: "Copilot"},
                                  body: "Consider renaming this.", url: "https://example.invalid/c"}]}}'
    done | jq -s . > "$FAKE_GH/review-queue/$k.json"
}

calls() { cat "$FAKE_GH/calls.log" 2>/dev/null || true; }

count_calls() { calls | grep -cE "$1" || true; }

pr_json() { cat "$FAKE_GH/pulls/$1.json"; }

state_value() { sed -n "s/^- $1: //p" specs/042-demo/phasepr/state.md | head -n1; }
