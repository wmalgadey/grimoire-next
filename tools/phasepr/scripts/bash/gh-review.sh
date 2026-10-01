#!/usr/bin/env bash
#
# gh-review.sh - every GitHub call phasepr makes, and the ones its triage agent makes
#
# Usage:
#   gh-review.sh find        <head> <base> [open|merged]  PR number, or nothing
#   gh-review.sh draft-open  <head> <base> <title> <body-file>   PR number (existing open PR wins)
#   gh-review.sh phase-open  <head> <base> <title> <body-file>   PR number (existing open PR wins)
#   gh-review.sh wait        <pr> <sha> <timeout-s> <interval-s> exit 0 once the reviewer has
#                                                     reviewed <sha>, exit 2 on timeout
#   gh-review.sh rerequest   <pr>                     ask the reviewer for a new review
#   gh-review.sh threads     <pr>                     unresolved review threads as a JSON array
#   gh-review.sh reply       <pr> <comment-id> <body> answer a thread (comment-id = its first comment)
#   gh-review.sh resolve     <thread-id>              resolve a thread (GraphQL resolveReviewThread)
#   gh-review.sh comment     <pr> <body>              a PR conversation comment
#   gh-review.sh approved    <pr> <sha>               exit 0 when the owner approved exactly <sha>
#   gh-review.sh head        <pr>                     the SHA the PR's head branch points at on GitHub
#   gh-review.sh owner                                the owner's login (PHASEPR_OWNER_LOGIN, else
#                                                     the owner part of the repository)
#   gh-review.sh checks      <pr>                     wait for the PR's checks; exit != 0 when red
#   gh-review.sh merge       <pr>                     merge commit + delete branch; exit 3 on conflict
#   gh-review.sh tick        <draft-pr> <phase> <phase-pr>   tick the phase in the draft PR checklist
#   gh-review.sh ready       <pr>                     take the PR out of draft (no-op when it is not)
#
# Environment:
#   PHASEPR_REVIEWER_LOGIN  the reviewer that `wait` waits for and `rerequest` asks
#                           (default copilot-pull-request-reviewer[bot]; see README, "Reviewer login")
#   PHASEPR_REPO            owner/name; default: parsed from the `origin` remote
#   PHASEPR_OWNER_LOGIN     who owns the decisions (I.1, I.11); default: the repository's owner
#   PHASEPR_DRY_RUN=1       print every gh call instead of running it
#   PHASEPR_AGENT_PR        set by phasepr for an agent: the one PR it may touch, or empty for none.
#                           Then only threads, reply, resolve and comment run, and only for that PR.
#
# REST wherever REST can do it; GraphQL only for review threads, which REST does not expose, and
# for the three porcelain calls the orchestrator is specified to use (gh pr merge/ready/checks).

# shellcheck disable=SC2016 # backticks and $vars in single quotes are Markdown and GraphQL
set -euo pipefail

REVIEWER=${PHASEPR_REVIEWER_LOGIN:-copilot-pull-request-reviewer[bot]}
DRY_RUN=${PHASEPR_DRY_RUN:-0}

die() { printf 'gh-review.sh: %s\n' "$*" >&2; exit 1; }

repo() {
    if [[ -n "${PHASEPR_REPO:-}" ]]; then
        printf '%s\n' "$PHASEPR_REPO"
        return
    fi
    local url
    url=$(git remote get-url origin 2>/dev/null) || die "no origin remote; set PHASEPR_REPO"
    url=${url%.git}
    url=${url%/}
    # https://github.com/o/r, git@github.com:o/r and proxied http://host/git/o/r all end in o/r.
    url=${url//://}
    printf '%s/%s\n' "$(basename "$(dirname "$url")")" "$(basename "$url")"
}

owner() {
    local r
    r=$(repo)
    printf '%s\n' "${PHASEPR_OWNER_LOGIN:-${r%%/*}}"
}

# gh, or in dry-run its command line on stderr and no output.
ghx() {
    if [[ "$DRY_RUN" == "1" ]]; then
        printf '[dry-run] gh' >&2
        printf ' %q' "$@" >&2
        printf '\n' >&2
        return 0
    fi
    gh "$@"
}

# Paginated REST GET, the pages merged into one JSON array.
get_all() {
    ghx api --paginate "$1" | jq -s 'add // []'
}

pr_number_of() {
    local head=$1 base=$2 state=${3:-open}
    local owner r
    r=$(repo)
    owner=${r%%/*}
    case "$state" in
        open)
            ghx api "repos/$r/pulls?state=open&head=$owner:$head&base=$base&per_page=100" \
                | jq -r '.[0].number // empty'
            ;;
        merged)
            ghx api "repos/$r/pulls?state=closed&head=$owner:$head&base=$base&per_page=100" \
                | jq -r '[.[] | select(.merged_at != null)][0].number // empty'
            ;;
        *) die "find: state must be open or merged" ;;
    esac
}

open_pr() {
    local draft=$1 head=$2 base=$3 title=$4 body_file=$5
    local existing
    existing=$(pr_number_of "$head" "$base" open)
    if [[ -n "$existing" ]]; then
        printf '%s\n' "$existing"
        return
    fi
    [[ -f "$body_file" || "$DRY_RUN" == "1" ]] || die "no body file: $body_file"
    ghx api -X POST "repos/$(repo)/pulls" \
        -f "title=$title" -f "head=$head" -f "base=$base" -F "draft=$draft" -F "body=@$body_file" \
        | jq -r '.number // empty'
}

reviewed_sha() {
    local pr=$1 sha=$2
    get_all "repos/$(repo)/pulls/$pr/reviews?per_page=100" \
        | jq --arg login "$REVIEWER" --arg sha "$sha" \
            '[.[] | select(.user.login == $login and .commit_id == $sha)] | length'
}

wait_review() {
    local pr=$1 sha=$2 timeout=$3 interval=$4
    local deadline count
    if [[ "$DRY_RUN" == "1" ]]; then
        reviewed_sha "$pr" "$sha" >/dev/null
        return 0
    fi
    deadline=$(( $(date +%s) + timeout ))
    while :; do
        count=$(reviewed_sha "$pr" "$sha")
        [[ "$count" -gt 0 ]] && return 0
        [[ $(date +%s) -ge $deadline ]] && return 2
        sleep "$interval"
    done
}

# The first 100 threads of a PR and the first 50 comments of each. A PR with more threads than
# that fails here rather than being read as having fewer: a truncated list could look closed.
THREADS_QUERY='query($owner: String!, $name: String!, $number: Int!) {
  repository(owner: $owner, name: $name) {
    pullRequest(number: $number) {
      reviewThreads(first: 100) {
        pageInfo { hasNextPage }
        nodes {
          id isResolved isOutdated path line
          comments(first: 50) { nodes { databaseId author { login } body url } }
        }
      }
    }
  }
}'

threads() {
    local pr=$1 r
    r=$(repo)
    if [[ "$DRY_RUN" == "1" ]]; then
        ghx api graphql -f "query=<reviewThreads>" -F "owner=${r%%/*}" -F "name=${r#*/}" -F "number=$pr"
        printf '[]\n'
        return
    fi
    ghx api graphql -f "query=$THREADS_QUERY" -F "owner=${r%%/*}" -F "name=${r#*/}" -F "number=$pr" \
        | jq 'if .errors then error("GraphQL: \(.errors | map(.message) | join("; "))") else . end
               | .data.repository.pullRequest.reviewThreads // error("no review threads in the answer")
               | if .pageInfo.hasNextPage then error("more than 100 review threads") else . end
               | [.nodes[]
               | select(.isResolved | not)
               | {thread_id: .id, path, line, outdated: .isOutdated,
                  comment_id: .comments.nodes[0].databaseId,
                  author: .comments.nodes[0].author.login,
                  body: .comments.nodes[0].body,
                  url: .comments.nodes[0].url,
                  replies: [.comments.nodes[1:][] | {author: .author.login, body}]}]'
}

resolve() {
    ghx api graphql \
        -f 'query=mutation($id: ID!) { resolveReviewThread(input: {threadId: $id}) { thread { isResolved } } }' \
        -F "id=$1" >/dev/null
}

checks() {
    local pr=$1 out rc
    # Right after a push the checks may not be registered yet; gh then reports "no checks".
    for _ in 1 2 3 4 5 6; do
        set +e
        out=$(ghx pr checks "$pr" --watch --interval 20 2>&1)
        rc=$?
        set -e
        printf '%s\n' "$out" >&2
        if [[ $rc -ne 0 ]] && grep -qi 'no checks reported' <<< "$out"; then
            [[ "$DRY_RUN" == "1" ]] && return 0
            sleep 20
            continue
        fi
        return "$rc"
    done
    die "checks: no checks were reported on PR #$pr"
}

mergeable_state() {
    ghx api "repos/$(repo)/pulls/$1" | jq -r '.mergeable_state // "unknown"'
}

merge() {
    local pr=$1 state
    for _ in $(seq 1 12); do
        state=$(mergeable_state "$pr")
        [[ "$state" != "unknown" || "$DRY_RUN" == "1" ]] && break
        sleep 5
    done
    if [[ "$state" == "dirty" ]]; then
        printf 'gh-review.sh: PR #%s has a merge conflict\n' "$pr" >&2
        exit 3
    fi
    if ! ghx pr merge "$pr" --merge --delete-branch; then
        [[ "$(mergeable_state "$pr")" == "dirty" ]] && exit 3
        die "merge: gh pr merge failed for PR #$pr"
    fi
}

tick() {
    local draft=$1 phase=$2 phase_pr=$3 tmp r
    r=$(repo)
    tmp=$(mktemp)
    ghx api "repos/$r/pulls/$draft" | jq -r '.body // ""' > "$tmp"
    # "- [ ] Phase N — title" becomes "- [x] Phase N — title — #PR"; a ticked line stays as it is.
    sed -E "s/^- \[ \] (Phase $phase( |$).*)$/- [x] \1${phase_pr:+ — #$phase_pr}/" "$tmp" > "$tmp.new"
    if [[ "$DRY_RUN" == "1" ]] || ! cmp -s "$tmp" "$tmp.new"; then
        ghx api -X PATCH "repos/$r/pulls/$draft" -F "body=@$tmp.new" >/dev/null
    fi
    rm -f "$tmp" "$tmp.new"
}

ready() {
    local pr=$1 draft
    draft=$(ghx api "repos/$(repo)/pulls/$pr" | jq -r '.draft // false')
    if [[ "$draft" == "true" || "$DRY_RUN" == "1" ]]; then
        ghx pr ready "$pr"
    fi
}

[[ $# -ge 1 ]] || { sed -n '3,27p' "$0" | sed 's/^# \{0,1\}//' >&2; exit 2; }
cmd=$1
shift

# An agent gets this script's path to answer review threads. Everything else it can do — merge,
# ready, open a PR, request reviews — is phasepr's alone, and the agent's PR is the only one it may
# touch: a prompt-injected "merge #12" is refused here, outside the model.
if [[ -n "${PHASEPR_AGENT_PR+set}" ]]; then
    case "$cmd" in
        threads|reply|comment)
            [[ -n "$PHASEPR_AGENT_PR" && "${1:-}" == "$PHASEPR_AGENT_PR" ]] \
                || die "an agent may only use '$cmd' on PR #${PHASEPR_AGENT_PR:-(none)}"
            ;;
        resolve)
            if [[ -z "$PHASEPR_AGENT_PR" ]] || ! threads "$PHASEPR_AGENT_PR" \
                    | jq -e --arg id "${1:-}" 'any(.[]; .thread_id == $id)' >/dev/null; then
                die "an agent may only resolve an open thread of PR #${PHASEPR_AGENT_PR:-(none)}"
            fi
            ;;
        *) die "an agent may not use '$cmd'" ;;
    esac
fi
case "$cmd" in
    find)        pr_number_of "$1" "$2" "${3:-open}" ;;
    draft-open)  open_pr true "$@" ;;
    phase-open)  open_pr false "$@" ;;
    wait)        wait_review "$@" ;;
    rerequest)   ghx api -X POST "repos/$(repo)/pulls/$1/requested_reviewers" -f "reviewers[]=$REVIEWER" >/dev/null ;;
    threads)     threads "$1" ;;
    reply)       ghx api -X POST "repos/$(repo)/pulls/$1/comments/$2/replies" -f "body=$3" >/dev/null ;;
    resolve)     resolve "$1" ;;
    comment)     ghx api -X POST "repos/$(repo)/issues/$1/comments" -f "body=$2" >/dev/null ;;
    approved)
        # The owner's approval of the head being merged: not another reviewer's, not an older head's.
        [[ "$DRY_RUN" == "1" ]] && { get_all "repos/$(repo)/pulls/$1/reviews?per_page=100" >/dev/null; exit 1; }
        get_all "repos/$(repo)/pulls/$1/reviews?per_page=100" \
            | jq -e --arg owner "$(owner)" --arg sha "$2" \
                '[.[] | select(.state == "APPROVED" and .user.login == $owner and .commit_id == $sha)]
                 | length > 0' >/dev/null
        ;;
    owner)       owner ;;
    head)        ghx api "repos/$(repo)/pulls/$1" | jq -r '.head.sha // empty' ;;
    checks)      checks "$1" ;;
    merge)       merge "$1" ;;
    tick)        tick "$@" ;;
    ready)       ready "$1" ;;
    *)           die "unknown subcommand: $cmd" ;;
esac
