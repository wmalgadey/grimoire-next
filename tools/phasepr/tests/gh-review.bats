#!/usr/bin/env bats
# gh-review.sh against the fake GitHub of tests/shims/gh.

load test_helper

setup() {
    setup_world
    printf 'body\n' > "$BATS_TEST_TMPDIR/body.md"
}

open_phase_pr() {
    git checkout -q -b 042-demo-phase-2
    git commit -q --allow-empty -m "work"
    git push -q -u origin 042-demo-phase-2
    gh_review phase-open 042-demo-phase-2 042-demo "title" "$BATS_TEST_TMPDIR/body.md"
}

@test "draft-open opens a draft once and afterwards returns the open PR" {
    run gh_review draft-open 042-demo main "feat(042): demo" "$BATS_TEST_TMPDIR/body.md"
    [ "$output" = "101" ]
    pr_json 101 | jq -e '.draft == true'
    run gh_review draft-open 042-demo main "feat(042): demo" "$BATS_TEST_TMPDIR/body.md"
    [ "$output" = "101" ]
    [ "$(count_calls '^api -X POST repos/owner/demo/pulls ')" -eq 1 ]
}

@test "find tells an open PR from a merged one" {
    pr=$(open_phase_pr)
    [ "$(gh_review find 042-demo-phase-2 042-demo open)" = "$pr" ]
    [ -z "$(gh_review find 042-demo-phase-2 042-demo merged)" ]
    gh_review merge "$pr"
    [ -z "$(gh_review find 042-demo-phase-2 042-demo open)" ]
    [ "$(gh_review find 042-demo-phase-2 042-demo merged)" = "$pr" ]
}

@test "wait counts only a review of the given commit" {
    pr=$(open_phase_pr)
    old=$(git rev-parse HEAD)
    run gh_review wait "$pr" "$old" 0 0
    [ "$status" -eq 0 ]
    git commit -q --allow-empty -m "more"
    git push -q
    run gh_review wait "$pr" "$(git rev-parse HEAD)" 1 0
    [ "$status" -eq 2 ]
    gh_review rerequest "$pr"
    run gh_review wait "$pr" "$(git rev-parse HEAD)" 1 0
    [ "$status" -eq 0 ]
}

@test "rerequest asks for the configured reviewer login" {
    pr=$(open_phase_pr)
    PHASEPR_REVIEWER_LOGIN=someone gh_review rerequest "$pr"
    [ "$(count_calls "requested_reviewers -f reviewers\[\]=someone$")" -eq 1 ]
    gh_review rerequest "$pr"
    [ "$(count_calls 'requested_reviewers -f reviewers\[\]=copilot-pull-request-reviewer\[bot\]$')" -eq 1 ]
}

@test "threads lists the unresolved threads with what triage needs, and resolve closes one" {
    threads_for_review 1 2
    pr=$(open_phase_pr)
    run gh_review threads "$pr"
    [ "$(jq length <<< "$output")" -eq 2 ]
    jq -e '.[0] == {thread_id: "T_1_1", path: "src/a.cs", line: 3, outdated: false, comment_id: 101,
                    author: "Copilot", body: "Consider renaming this.",
                    url: "https://example.invalid/c", replies: []}' <<< "$output"
    gh_review resolve T_1_1
    run gh_review threads "$pr"
    [ "$(jq -r '[.[].thread_id] | join(",")' <<< "$output")" = "T_1_2" ]
}

@test "tick ticks one phase, leaves Phase 10 alone for Phase 1, and does not rewrite a ticked body" {
    printf 'Outcome.\n\n- [ ] Phase 1 — one\n- [ ] Phase 10 — ten\n' > "$BATS_TEST_TMPDIR/draft.md"
    gh_review draft-open 042-demo main "t" "$BATS_TEST_TMPDIR/draft.md"
    gh_review tick 101 1 102
    pr_json 101 | jq -r .body | grep -qx -- '- \[x\] Phase 1 — one — #102'
    pr_json 101 | jq -r .body | grep -qx -- '- \[ \] Phase 10 — ten'
    gh_review tick 101 1 102
    [ "$(count_calls '^api -X PATCH')" -eq 1 ]
}

@test "merge exits 3 on a conflict and merges nothing" {
    export FAKE_CONFLICT=1
    pr=$(open_phase_pr)
    run gh_review merge "$pr"
    [ "$status" -eq 3 ]
    [ "$(count_calls '^pr merge')" -eq 0 ]
}

@test "ready takes a draft out of draft and leaves a ready PR alone" {
    gh_review draft-open 042-demo main "t" "$BATS_TEST_TMPDIR/body.md"
    gh_review ready 101
    pr_json 101 | jq -e '.draft == false'
    gh_review ready 101
    [ "$(count_calls '^pr ready')" -eq 1 ]
}

@test "the repository is read from origin whatever form its URL has" {
    unset PHASEPR_REPO
    for url in https://github.com/acme/wiki.git git@github.com:acme/wiki.git \
               http://proxy@127.0.0.1:1234/git/acme/wiki; do
        git remote set-url origin "$url"
        PHASEPR_DRY_RUN=1 run gh_review rerequest 7
        [[ "$output" == *"repos/acme/wiki/pulls/7/requested_reviewers"* ]]
    done
}
