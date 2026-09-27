#!/usr/bin/env bash
#
# phasepr-loop.sh - implement a Spec Kit feature one phase PR at a time
#
# For the feature branch <NNN-slug> (spec.md, plan.md and tasks.md in specs/<NNN-slug>/):
#
#   setup      a draft PR <NNN-slug> -> main whose body an agent writes from spec.md, plus the
#              phase checklist. An existing open PR is kept as it is.
#   per phase  branch off the feature branch; fresh agent iterations of /speckit-implement scoped
#              to the phase until its tasks are checked, the tree is clean and gates.sh is green;
#              push; PR phase -> feature; wait for the reviewer; fresh triage iterations until a
#              review leaves no open thread; CI green; merge commit; tick the checklist.
#   at the end scripts/mutation.sh, its table into specs/<NNN-slug>/mutation.md, draft -> ready.
#              Never merges to main.
#
# Every iteration is a fresh `claude -p`. What one iteration learns reaches the next through
# specs/<NNN-slug>/phasepr/memory.md; where the run is lives in specs/<NNN-slug>/phasepr/state.md.
# Both are git-ignored. Before each iteration HEAD is recorded, and only the commits on top of it
# are judged: history before it must survive, the branch must not change, other phases' checkboxes
# must not move. This script never amends, resets, rebases or force-pushes.
#
# Exit: 0 done (or the one --phase done), 1 halted (summary on stdout, state in state.md),
#       2 usage or configuration error, 130 interrupted.
#
# Usage:
#   phasepr-loop.sh [--feature NNN-slug] [--phase N] [--dry-run] [--status]
#                   [--model ID] [--max-review-rounds N] [--review-timeout S] [--max-turns N]
#                   [--skip-mutation]

# shellcheck disable=SC2016 # backticks and $vars in single quotes are Markdown and GraphQL
set -euo pipefail
if (( BASH_VERSINFO[0] < 4 )); then
    printf 'phasepr: bash 4 or newer is needed (macOS: brew install bash)\n' >&2
    exit 2
fi
# `&` in a ${var//pattern/replacement} replacement is the match in bash 5.2; prompts contain `&`.
shopt -u patsub_replacement 2>/dev/null || true

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
EXT_ROOT=$(cd "$SCRIPT_DIR/../.." && pwd)
TASKS_SH="$SCRIPT_DIR/tasks.sh"
GH_REVIEW="$SCRIPT_DIR/gh-review.sh"
GATES_SH="$SCRIPT_DIR/gates.sh"

# Ralph's circuit breaker: this many agent iterations in a row without progress halt the run.
MAX_NO_PROGRESS=3
# Constitution I.11: at most three review rounds per PR.
MAX_ROUNDS_ALLOWED=3

#region Configuration

CFG_MODEL=""
CFG_AGENT_CLI="claude"
CFG_MAX_TURNS=200
CFG_MAX_IMPL=8
CFG_MAX_ROUNDS=3
CFG_REVIEW_TIMEOUT=600
CFG_POLL=20
CFG_REVIEWER="copilot-pull-request-reviewer[bot]"
CFG_PERMISSION_MODE="auto"
CFG_RUN_MUTATION="true"

FEATURE=""
ONLY_PHASE=""
DRY_RUN=false
STATUS_ONLY=false

die_usage() {
    printf 'phasepr: %s\n' "$*" >&2
    exit 2
}

usage() {
    sed -n '3,29p' "$0" | sed 's/^# \{0,1\}//'
    exit "${1:-0}"
}

unquote() {
    local v=$1
    if [[ "$v" =~ ^\"([^\"]*)\" ]]; then
        v=${BASH_REMATCH[1]}
    elif [[ "$v" =~ ^\'([^\']*)\' ]]; then
        v=${BASH_REMATCH[1]}
    else
        v=${v%%[[:space:]]#*}
        v=${v%"${v##*[![:space:]]}"}
    fi
    printf '%s\n' "$v"
}

load_config() {
    local file=$1 line key value
    [[ -f "$file" ]] || return 0
    while IFS= read -r line || [[ -n "$line" ]]; do
        [[ "$line" =~ ^([a-z_]+):[[:space:]]*(.*)$ ]] || continue
        key=${BASH_REMATCH[1]}
        value=$(unquote "${BASH_REMATCH[2]}")
        case "$key" in
            model) CFG_MODEL=$value ;;
            agent_cli) CFG_AGENT_CLI=$value ;;
            max_turns) CFG_MAX_TURNS=$value ;;
            max_implement_iterations) CFG_MAX_IMPL=$value ;;
            max_review_rounds) CFG_MAX_ROUNDS=$value ;;
            review_timeout) CFG_REVIEW_TIMEOUT=$value ;;
            review_poll_interval) CFG_POLL=$value ;;
            reviewer_login) CFG_REVIEWER=$value ;;
            permission_mode) CFG_PERMISSION_MODE=$value ;;
            run_mutation) CFG_RUN_MUTATION=$value ;;
            *) die_usage "unknown key '$key' in $file" ;;
        esac
    done < "$file"
}

load_config "$EXT_ROOT/phasepr-config.yml"
load_config "$EXT_ROOT/phasepr-config.local.yml"

need_value() { [[ $# -ge 2 && -n "$2" ]] || die_usage "$1 needs a value"; }

while [[ $# -gt 0 ]]; do
    case "$1" in
        --feature) need_value "$@"; FEATURE=$2; shift 2 ;;
        --phase) need_value "$@"; ONLY_PHASE=$2; shift 2 ;;
        --dry-run) DRY_RUN=true; shift ;;
        --status) STATUS_ONLY=true; shift ;;
        --skip-mutation) CFG_RUN_MUTATION=false; shift ;;
        --model) need_value "$@"; CFG_MODEL=$2; shift 2 ;;
        --max-review-rounds) need_value "$@"; CFG_MAX_ROUNDS=$2; shift 2 ;;
        --review-timeout) need_value "$@"; CFG_REVIEW_TIMEOUT=$2; shift 2 ;;
        --max-turns) need_value "$@"; CFG_MAX_TURNS=$2; shift 2 ;;
        -h|--help) usage 0 ;;
        *) die_usage "unknown option: $1 (see --help)" ;;
    esac
done

for pair in "max_turns:$CFG_MAX_TURNS" "max_implement_iterations:$CFG_MAX_IMPL" \
            "max_review_rounds:$CFG_MAX_ROUNDS" "review_timeout:$CFG_REVIEW_TIMEOUT" \
            "review_poll_interval:$CFG_POLL"; do
    [[ "${pair#*:}" =~ ^[0-9]+$ ]] || die_usage "${pair%%:*} must be a whole number, got '${pair#*:}'"
done
[[ -z "$ONLY_PHASE" || "$ONLY_PHASE" =~ ^[0-9]+$ ]] || die_usage "--phase must be a number"
if (( CFG_MAX_ROUNDS < 1 || CFG_MAX_ROUNDS > MAX_ROUNDS_ALLOWED )); then
    die_usage "max_review_rounds must be 1..$MAX_ROUNDS_ALLOWED (Constitution I.11), got $CFG_MAX_ROUNDS"
fi
case "$CFG_AGENT_CLI" in
    claude) ;;
    *) die_usage "agent_cli '$CFG_AGENT_CLI' is not implemented; only 'claude' is" ;;
esac
case "$CFG_RUN_MUTATION" in
    true|false) ;;
    *) die_usage "run_mutation must be true or false, got '$CFG_RUN_MUTATION'" ;;
esac
# Headless, a permission prompt has nobody to answer it: only the modes that never ask will do.
case "$CFG_PERMISSION_MODE" in
    auto|bypassPermissions) ;;
    *) die_usage "permission_mode must be auto or bypassPermissions, got '$CFG_PERMISSION_MODE'" ;;
esac

export PHASEPR_REVIEWER_LOGIN=$CFG_REVIEWER
[[ "$DRY_RUN" == "true" ]] && export PHASEPR_DRY_RUN=1

#endregion

#region Repository and feature

REPO_ROOT=$(git rev-parse --show-toplevel 2>/dev/null) || die_usage "not inside a git repository"
cd "$REPO_ROOT"

current_branch() { git branch --show-current; }

if [[ -z "$FEATURE" ]]; then
    FEATURE=$(current_branch)
    # On a phase branch after a halt: the feature is the part before "-phase-N".
    if [[ "$FEATURE" =~ ^([0-9]{3}-.+)-phase-[0-9]+ ]]; then
        FEATURE=${BASH_REMATCH[1]}
    fi
fi
[[ "$FEATURE" =~ ^[0-9]{3}-[a-z0-9-]+$ ]] \
    || die_usage "'$FEATURE' is not a feature branch (NNN-slug); pass --feature"
FEATURE_NUM=${FEATURE%%-*}
FEATURE_DIR="specs/$FEATURE"
TASKS="$FEATURE_DIR/tasks.md"
PHASEPR_DIR="$FEATURE_DIR/phasepr"
STATE="$PHASEPR_DIR/state.md"
MEMORY="$PHASEPR_DIR/memory.md"
LOG_DIR="$PHASEPR_DIR/logs"

git rev-parse --verify -q "refs/heads/$FEATURE" >/dev/null \
    || die_usage "branch '$FEATURE' does not exist locally"

# tasks.md as the feature branch has it: that is where a phase counts as done.
feature_tasks() {
    local out
    out=$(mktemp)
    git show "$FEATURE:$TASKS" > "$out" 2>/dev/null || die_usage "$TASKS is not on branch $FEATURE"
    printf '%s\n' "$out"
}

#endregion

#region Output, state and halts

log() { printf '[phasepr %s] %s\n' "$(date -u +%H:%M:%S)" "$*" >&2; }

# A command that changes something: run it, or in dry-run print it.
run() {
    if [[ "$DRY_RUN" == "true" ]]; then
        printf '[dry-run]' >&2
        printf ' %q' "$@" >&2
        printf '\n' >&2
        return 0
    fi
    "$@"
}

render() {
    local text kv key
    text=$(<"$1")
    shift
    for kv in "$@"; do
        key=${kv%%=*}
        text=${text//"{{$key}}"/"${kv#*=}"}
    done
    printf '%s\n' "$text"
}

S_DRAFT_PR=""
S_PHASE=""
S_PHASE_BRANCH=""
S_PHASE_PR=""
S_PHASE_PRS=""
S_STEP="setup"
S_REVIEW_ROUND=0
S_ITER=0
S_NO_PROGRESS=0
S_GATE_SIG=""
S_GREEN_HEAD=""
S_MUTATION=""
S_OWNER_ASKED=""
S_HALT="none"
S_HALT_SUMMARY="none"
REREQUEST_ON_RESUME=false
TRIAGE_DONE=false

state_value() {
    local v
    v=$(sed -n "s/^- $1: //p" "$STATE" | head -n1)
    [[ "$v" == "-" ]] && v=""
    printf '%s\n' "$v"
}

state_load() {
    [[ -f "$STATE" ]] || return 0
    S_DRAFT_PR=$(state_value draft_pr)
    S_PHASE=$(state_value phase)
    S_PHASE_BRANCH=$(state_value phase_branch)
    S_PHASE_PR=$(state_value phase_pr)
    S_PHASE_PRS=$(state_value phase_prs)
    S_STEP=$(state_value step)
    S_REVIEW_ROUND=$(state_value review_round); S_REVIEW_ROUND=${S_REVIEW_ROUND:-0}
    S_ITER=$(state_value implement_iterations); S_ITER=${S_ITER:-0}
    S_NO_PROGRESS=$(state_value no_progress); S_NO_PROGRESS=${S_NO_PROGRESS:-0}
    S_GATE_SIG=$(state_value last_gate_signature)
    S_GREEN_HEAD=$(state_value green_head)
    S_MUTATION=$(state_value mutation)
    S_OWNER_ASKED=$(state_value owner_review_asked)
    S_HALT=$(state_value halt); S_HALT=${S_HALT:-none}
}

phases_table() {
    local tasks n title total checked pr
    tasks=$(feature_tasks)
    while IFS=$'\t' read -r n title total checked; do
        pr=$(tr ' ' '\n' <<< "$S_PHASE_PRS" | sed -n "s/^$n=//p" | head -n1)
        printf '| %s | %s | %s/%s | %s |\n' "$n" "$title" "$checked" "$total" "${pr:+#$pr}"
    done < <("$TASKS_SH" phases "$tasks")
    rm -f "$tasks"
}

state_write() {
    [[ "$DRY_RUN" == "true" ]] && return 0
    local d="-"
    render "$EXT_ROOT/templates/state.md" \
        "FEATURE=$FEATURE" "DRAFT_PR=${S_DRAFT_PR:-$d}" "PHASE=${S_PHASE:-$d}" \
        "PHASE_BRANCH=${S_PHASE_BRANCH:-$d}" "PHASE_PR=${S_PHASE_PR:-$d}" "STEP=${S_STEP:-$d}" \
        "REVIEW_ROUND=$S_REVIEW_ROUND" "IMPLEMENT_ITERATIONS=$S_ITER" "NO_PROGRESS=$S_NO_PROGRESS" \
        "LAST_GATE_SIGNATURE=${S_GATE_SIG:-$d}" "GREEN_HEAD=${S_GREEN_HEAD:-$d}" \
        "MUTATION=${S_MUTATION:-$d}" "HALT=$S_HALT" "UPDATED=$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
        "PHASES_TABLE=$(phases_table)" "HALT_SUMMARY=$S_HALT_SUMMARY" > "$STATE.tmp"
    # Two keys the template does not show a reader, kept below the table.
    printf '\n<!-- machine -->\n- phase_prs: %s\n- owner_review_asked: %s\n' \
        "${S_PHASE_PRS:-$d}" "${S_OWNER_ASKED:-$d}" >> "$STATE.tmp"
    mv "$STATE.tmp" "$STATE"
}

open_threads_text() {
    local threads
    [[ -n "$S_PHASE_PR" && "$DRY_RUN" != "true" ]] || { printf 'none known\n'; return; }
    threads=$("$GH_REVIEW" threads "$S_PHASE_PR" 2>/dev/null) || { printf 'unknown (GitHub did not answer)\n'; return; }
    jq -r 'if length == 0 then "none" else
             "\(length)\n" + (map("  - \(.path):\(.line // "-") — \(.author): \(.body | split("\n")[0] | .[0:120]) (\(.url))") | join("\n"))
           end' <<< "$threads"
}

halt() {
    local code=$1 decision=$2 phase_title=""
    S_HALT=$code
    [[ -n "$S_PHASE" ]] && phase_title=" — $(phase_title "$S_PHASE")"
    S_HALT_SUMMARY=$(cat <<EOF
phasepr halted: $code
Feature:        $FEATURE (draft PR ${S_DRAFT_PR:+#}${S_DRAFT_PR:-none})
Phase:          ${S_PHASE:-none}$phase_title
Phase PR:       ${S_PHASE_PR:+#}${S_PHASE_PR:-none}
Step:           $S_STEP
Open threads:   $(open_threads_text)
Owner decision: $decision
State:          $STATE
EOF
)
    state_write
    printf '%s\n' "$S_HALT_SUMMARY"
    exit 1
}

# shellcheck disable=SC2317,SC2329 # invoked by the trap
on_interrupt() {
    trap - INT TERM
    log "interrupted during step '$S_STEP'; state kept in $STATE"
    state_write
    exit 130
}

#endregion

#region Git

tree_clean() { [[ -z "$(git status --porcelain --untracked-files=all)" ]]; }

remote_has_branch() { git ls-remote --exit-code --heads origin "$1" >/dev/null 2>&1; }

# Every local branch but the phase branch, with where it points: an agent that commits elsewhere
# and comes back would otherwise slip that commit into a later phase without its review.
other_branches() {
    git for-each-ref --format='%(refname) %(objectname)' refs/heads \
        | grep -v "^refs/heads/$S_PHASE_BRANCH " || true
}

# Validates what an agent iteration did to history: judged are only the commits on top of the
# HEAD recorded before it ran.
validate_history() {
    local before=$1 branch=$2 branches=$3 now
    now=$(current_branch)
    if [[ "$now" != "$branch" ]]; then
        halt protocol-violation "The agent left branch '$branch' (now on '${now:-detached HEAD}'). Nothing was reset; inspect both branches, move the work back by hand, and rerun."
    fi
    if ! git merge-base --is-ancestor "$before" HEAD; then
        halt protocol-violation "The agent rewrote history below the recorded HEAD $before (amend, reset or rebase). Nothing was reset; restore the history by hand and rerun."
    fi
    if [[ "$(other_branches)" != "$branches" ]]; then
        halt protocol-violation "The agent moved a branch other than '$branch' ($(diff <(printf '%s\n' "$branches") <(other_branches) | sed -n 's/^> refs\/heads\///p' | cut -d' ' -f1 | paste -sd' ' -)). Nothing was reset; look at what it committed there and rerun."
    fi
    # A merge brings in another branch's commits unreviewed; the agent never merges (the prompt says
    # so, and the CLI refuses `git merge`), so a merge commit here means something went around both.
    if [[ -n "$(git rev-list --merges "$before..HEAD")" ]]; then
        halt protocol-violation "The agent made a merge commit on '$branch'. Nothing was reset; undo the merge by hand and rerun."
    fi
}

# The commits an agent made read like this repository's own: type(scope): subject, the scope this
# feature's number or a requirement ID in lower case — feat(003): …, test(runs-008): …. A commit
# that does not is the owner's to reword or accept; phasepr never rewrites history.
check_commit_subjects() {
    local before=$1 bad
    bad=$(git log --format='%h %s' "$before..HEAD" | while IFS=' ' read -r hash subject; do
        [[ "$subject" =~ ^(feat|fix|docs|test|refactor|chore|build|ci|perf|style|revert)\(($FEATURE_NUM|[a-z]+-[0-9]{3})\)!?:\ [^[:space:]] ]] \
            || printf '%s %s\n' "$hash" "$subject"
    done)
    [[ -z "$bad" ]] && return 0
    halt commit-subject "Commit(s) not in the form type($FEATURE_NUM|<capability>-nnn): subject — $(paste -sd';' - <<< "$bad" | sed 's/;/; /g'). phasepr rewrites nothing: reword them on the branch yourself, or accept them as they are; a rerun judges only commits made after it starts."
}

other_phases_unchanged() {
    local before=$1 n=$2
    if [[ "$("$TASKS_SH" others "$TASKS" "$n")" != "$before" ]]; then
        halt protocol-violation "The agent changed a checkbox outside phase $n in $TASKS. Nothing was reset; check the diff, revert what does not belong to phase $n in a new commit, and rerun."
    fi
}

push_branch() {
    local branch=$1
    if ! run git push -u origin "$branch"; then
        halt push-rejected "git push of '$branch' was rejected (someone else pushed?). phasepr never force-pushes; merge the remote branch in by hand and rerun."
    fi
}

#endregion

#region Tasks

phase_title() {
    local tasks t
    tasks=$(feature_tasks)
    t=$("$TASKS_SH" title "$tasks" "$1")
    rm -f "$tasks"
    # "User Story 1 — ... (Priority: P1) 🎯 MVP" reads as a title without its planning marks.
    sed -E 's/[[:space:]]*\(Priority:[^)]*\)//; s/[[:space:]]*🎯.*$//' <<< "$t"
}

phase_branch_of() {
    local tasks b
    tasks=$(feature_tasks)
    b=$("$TASKS_SH" branch "$tasks" "$1")
    rm -f "$tasks"
    # "<feature>/phase-N" cannot exist beside a branch named <feature>: a ref cannot be both a file
    # and a directory. The repository's own convention is <feature>-phase-N-<slug>.
    printf '%s\n' "${b:-$FEATURE-phase-$1}"
}

done_count() {
    local total open
    total=$("$TASKS_SH" ids "$TASKS" "$1" | wc -l)
    open=$("$TASKS_SH" open "$TASKS" "$1" | wc -l)
    printf '%s\n' $(( total - open ))
}

#endregion

#region Agent iterations

AGENT_RESULT=""
AGENT_RC=0
AGENT_DENIED=""

# What an agent must never do, whatever the permission mode decides: phasepr pushes and merges
# itself, and judges history against its snapshot. The CLI refuses these outright
# (--disallowedTools); an agent that tries one anyway has done no harm, so such a refusal is only
# logged. Any other refusal means the agent could not do its work, and phasepr halts on it.
# The logins Copilot's review comments carry: `Copilot` over REST (verified); over GraphQL the bot
# may appear under its app name, which is not verified, so both are accepted.
TRUSTED_REVIEW_AUTHORS='["Copilot", "copilot-pull-request-reviewer", "copilot-pull-request-reviewer[bot]"]'

FORBIDDEN_COMMANDS=("git push" "git reset" "git rebase" "git merge" "git commit --amend" "gh pr merge")

invoke_agent() {
    local kind=$1 prompt=$2 base
    base="$LOG_DIR/$(date -u +%Y%m%dT%H%M%SZ)-$kind"
    printf '%s\n' "$prompt" > "$base.prompt.md"
    case "$CFG_AGENT_CLI" in
        claude) invoke_claude "$kind" "$prompt" "$base" ;;
    esac
}

invoke_claude() {
    local kind=$1 prompt=$2 base=$3
    local c forbidden
    local -a cmd=(claude -p "$prompt" --permission-mode "$CFG_PERMISSION_MODE" --output-format json
                  --max-turns "$CFG_MAX_TURNS")
    [[ -n "$CFG_MODEL" ]] && cmd+=(--model "$CFG_MODEL")
    # Last: the option takes every argument up to the next option.
    cmd+=(--disallowedTools)
    for c in "${FORBIDDEN_COMMANDS[@]}"; do
        cmd+=("Bash($c:*)")
    done
    if [[ "$DRY_RUN" == "true" ]]; then
        printf '[dry-run] claude -p "$(cat %q)"' "$base.prompt.md" >&2
        printf ' %q' "${cmd[@]:3}" >&2
        printf '\n' >&2
        AGENT_RESULT='{"changed": false, "halt": null}'
        AGENT_RC=0
        AGENT_DENIED=""
        return 0
    fi
    log "agent: $kind (log $base.json)"
    set +e
    "${cmd[@]}" > "$base.json" 2> "$base.err" < /dev/null
    AGENT_RC=$?
    set -e
    AGENT_RESULT=$(jq -r '.result // empty' "$base.json" 2>/dev/null || true)
    forbidden=$(printf '%s|' "${FORBIDDEN_COMMANDS[@]}")
    AGENT_DENIED=$(jq -r '.permission_denials[]?
            | if .tool_name == "Bash" then (.tool_input.command // "" | split("\n")[0]) else .tool_name end' \
        "$base.json" 2>/dev/null | sort -u || true)
    if grep -qE "^(${forbidden%|})( |$)" <<< "$AGENT_DENIED"; then
        log "refused, as it must be: $(grep -E "^(${forbidden%|})( |$)" <<< "$AGENT_DENIED" | paste -sd';' -)"
    fi
    AGENT_DENIED=$(grep -vE "^(${forbidden%|})( |$)" <<< "$AGENT_DENIED" || true)
    [[ "$AGENT_RC" -eq 0 ]] || log "agent exited $AGENT_RC ($(jq -r '.subtype // "no result"' "$base.json" 2>/dev/null || echo 'no JSON'))"
}

# Halts when the permission mode refused the agent something it needed.
check_denials() {
    [[ -n "$AGENT_DENIED" ]] || return 0
    halt permission-denied "The $1 agent was refused: $(paste -sd';' - <<< "$AGENT_DENIED" | sed 's/;/; /g'). permission_mode is '$CFG_PERMISSION_MODE'; auto mode refuses everything that needs approval when the model does not support it (seen with a Haiku model). Allow it (permissions.allow in .claude/settings.json, or permission_mode: bypassPermissions inside a sandbox), or take it out of the task, then rerun."
}

# The last line of the reply that is a JSON object, compacted; empty when there is none.
json_tail() {
    printf '%s\n' "$1" | grep -E '^[[:space:]]*\{.*\}[[:space:]]*$' | tail -n1 | jq -c . 2>/dev/null || true
}

record_progress() {
    local progressed=$1 what=$2
    if [[ "$progressed" == "true" ]]; then
        S_NO_PROGRESS=0
    else
        S_NO_PROGRESS=$((S_NO_PROGRESS + 1))
        log "$what made no progress ($S_NO_PROGRESS/$MAX_NO_PROGRESS)"
        if (( S_NO_PROGRESS >= MAX_NO_PROGRESS )); then
            halt circuit-breaker "$MAX_NO_PROGRESS agent iterations in a row made no progress (last: $what). Read the logs in $LOG_DIR and the handoff in $MEMORY, unblock the phase, and rerun."
        fi
    fi
    state_write
}

#endregion

#region Gates

GATE_LOG=""
GATE_NAME=""

run_gates() {
    if [[ "$DRY_RUN" == "true" ]]; then
        printf '[dry-run] bash %q\n' "$GATES_SH" >&2
        return 0
    fi
    GATE_LOG="$LOG_DIR/$(date -u +%Y%m%dT%H%M%SZ)-gates.log"
    log "gates on $(git rev-parse --short HEAD) (log $GATE_LOG)"
    bash "$GATES_SH" > "$GATE_LOG" 2>&1 && return 0
    GATE_NAME=$(sed -n 's/^=== gate failed: //p' "$GATE_LOG" | tail -n1)
    GATE_NAME=${GATE_NAME:-unknown}
    return 1
}

# The failing gate and a checksum of its error lines with what varies between runs taken out
# (every digit — durations, counts, line numbers — and the checkout's path): two runs with the same signature failed the same way.
gate_signature() {
    local lines
    lines=$(grep -iE 'error|fail' "$GATE_LOG" | grep -v '^=== ' \
        | sed -E -e "s#$REPO_ROOT/##g" -e 's/[0-9]+//g' -e 's/[[:space:]]+/ /g' \
        | sort -u || true)
    printf '%s:%s\n' "$GATE_NAME" "$(printf '%s' "$lines" | cksum | cut -d' ' -f1)"
}

gate_failure_block() {
    printf '## The gates are red\n\n'
    printf 'phasepr ran `%s` on %s after the last iteration and the gate `%s` failed. Fix that\n' \
        "$GATES_SH" "$(git rev-parse --short HEAD)" "$GATE_NAME"
    printf 'before anything else. The end of its output:\n\n~~~text\n'
    tail -n 80 "$GATE_LOG"
    printf '~~~\n'
}

#endregion

#region Phase steps

implement_iteration() {
    local n=$1 failure=$2 before branches others done_before prompt halt_reason progressed
    if (( S_ITER >= CFG_MAX_IMPL )); then
        halt iteration-limit "Phase $n used all $CFG_MAX_IMPL implement iterations without being done (tasks checked, tree clean, gates green). Read $MEMORY and the logs in $LOG_DIR, then rerun or finish the phase by hand."
    fi
    S_ITER=$((S_ITER + 1))
    S_STEP=implement
    state_write
    before=$(git rev-parse HEAD)
    branches=$(other_branches)
    others=$("$TASKS_SH" others "$TASKS" "$n")
    done_before=$(done_count "$n")
    prompt="/speckit-implement $(render "$EXT_ROOT/prompts/implement-phase.md" \
        "PHASE=$n" "PHASE_TITLE=$(phase_title "$n")" "FEATURE_DIR=$FEATURE_DIR" \
        "PHASE_BRANCH=$S_PHASE_BRANCH" "MEMORY_PATH=$MEMORY" "GATES_SCRIPT=$GATES_SH" \
        "FEATURE_NUM=$FEATURE_NUM" "OPEN_TASKS=$("$TASKS_SH" open "$TASKS" "$n" | paste -sd, - | sed 's/,/, /g')" \
        "GATE_FAILURE=$failure")"
    log "phase $n: implement iteration $S_ITER"
    invoke_agent implement "$prompt"
    [[ "$DRY_RUN" == "true" ]] && return 0

    validate_history "$before" "$S_PHASE_BRANCH" "$branches"
    other_phases_unchanged "$others" "$n"
    check_denials implement
    check_commit_subjects "$before"
    halt_reason=$(json_tail "$AGENT_RESULT" | jq -r '.halt // empty' 2>/dev/null || true)
    [[ -n "$halt_reason" ]] && halt agent-halt "$halt_reason"

    progressed=false
    if [[ "$(git rev-parse HEAD)" != "$before" || "$(done_count "$n")" != "$done_before" ]]; then
        progressed=true
    fi
    record_progress "$progressed" "implement iteration $S_ITER"
}

# Implements phase n until its tasks are all checked, the tree is clean and the gates are green.
ensure_green() {
    local n=$1 failure="" head sig
    if [[ "$DRY_RUN" == "true" ]]; then
        "$TASKS_SH" complete "$TASKS" "$n" || implement_iteration "$n" ""
        run_gates
        return 0
    fi
    while :; do
        if "$TASKS_SH" complete "$TASKS" "$n" && tree_clean; then
            head=$(git rev-parse HEAD)
            [[ "$head" == "$S_GREEN_HEAD" ]] && return 0
            S_STEP=gates
            state_write
            if run_gates; then
                log "gates green on $(git rev-parse --short HEAD)"
                S_GREEN_HEAD=$head
                S_GATE_SIG=""
                state_write
                return 0
            fi
            sig=$(gate_signature)
            if [[ "$sig" == "$S_GATE_SIG" ]]; then
                halt gates-red "Gate '$GATE_NAME' failed twice in a row for the same reason (log: $GATE_LOG). Decide whether the failure is this phase's to fix — then fix it or give the agent a hint in $MEMORY — and rerun."
            fi
            log "gate '$GATE_NAME' failed"
            S_GATE_SIG=$sig
            failure=$(gate_failure_block)
            state_write
        fi
        implement_iteration "$n" "$failure"
        failure=""
    done
}

ensure_phase_branch() {
    local branch=$1
    [[ "$(current_branch)" == "$branch" ]] && return 0
    if git rev-parse --verify -q "refs/heads/$branch" >/dev/null; then
        run git checkout "$branch"
    elif remote_has_branch "$branch"; then
        run git fetch origin "$branch"
        run git checkout -b "$branch" --track "origin/$branch"
    else
        run git checkout "$FEATURE"
        if remote_has_branch "$FEATURE"; then
            run git pull --ff-only origin "$FEATURE"
        fi
        run git checkout -b "$branch"
    fi
}

open_phase_pr() {
    local n=$1 pr body title total
    pr=$("$GH_REVIEW" find "$S_PHASE_BRANCH" "$FEATURE" open)
    if [[ -z "$pr" ]]; then
        body="$LOG_DIR/phase-$n-body.md"
        total=$("$TASKS_SH" phases "$TASKS" | wc -l)
        {
            printf '**Phase %s of %s** of `%s`%s. Base: `%s`.\n\n' "$n" "$total" "$FEATURE" \
                "${S_DRAFT_PR:+ (draft PR #$S_DRAFT_PR)}" "$FEATURE"
            printf '## Goal\n\n%s\n\n' "$("$TASKS_SH" purpose "$TASKS" "$n" | sed 's/^$/—/')"
            printf '## Tasks\n\n%s\n\n' "$("$TASKS_SH" ids "$TASKS" "$n" | paste -sd, - | sed 's/,/, /g')"
            printf '## Requirements\n\n%s\n\n' "$("$TASKS_SH" reqs "$TASKS" "$n" | paste -sd, - | sed 's/,/, /g; s/^$/none named/')"
            printf 'Opened by phasepr once every task of the phase was checked and `gates.sh` (build, '
            printf 'Fast suite within its time budget, trace-check) was green on %s.\n' "$(git rev-parse --short HEAD)"
        } > "$body"
        # "User Story 1 — Read back …" titles the PR as "phase 3 — read back …".
        title=$(phase_title "$n" | sed -E 's/^User Story [0-9]+[[:space:]]*(—|-)[[:space:]]*//')
        pr=$("$GH_REVIEW" phase-open "$S_PHASE_BRANCH" "$FEATURE" \
            "feat($FEATURE_NUM): phase $n — ${title,}" "$body")
        [[ -n "$pr" || "$DRY_RUN" == "true" ]] || halt github-error "Opening the PR for '$S_PHASE_BRANCH' returned no number."
        log "phase $n: opened PR #${pr:-DRY}"
    fi
    S_PHASE_PR=${pr:-DRY}
    state_write
}

triage_round() {
    local n=$1 threads=$2 before branches others prompt json halt_reason changed_json changed_git remaining
    S_STEP=triage
    state_write
    before=$(git rev-parse HEAD)
    branches=$(other_branches)
    others=$("$TASKS_SH" others "$TASKS" "$n")
    prompt=$(render "$EXT_ROOT/prompts/triage-review.md" \
        "PR=$S_PHASE_PR" "PHASE=$n" "PHASE_TITLE=$(phase_title "$n")" "FEATURE_DIR=$FEATURE_DIR" \
        "PHASE_BRANCH=$S_PHASE_BRANCH" "ROUND=$((S_REVIEW_ROUND + 1))" "MAX_ROUNDS=$CFG_MAX_ROUNDS" \
        "THREADS_JSON=$threads" "GH_REVIEW=$GH_REVIEW" "MEMORY_PATH=$MEMORY" "GATES_SCRIPT=$GATES_SH" \
        "FEATURE_NUM=$FEATURE_NUM")
    log "phase $n: triage of $(jq length <<< "$threads") thread(s), round $((S_REVIEW_ROUND + 1))"
    TRIAGE_DONE=false
    invoke_agent triage "$prompt"
    if [[ "$DRY_RUN" == "true" ]]; then
        TRIAGE_DONE=true
        return 0
    fi

    validate_history "$before" "$S_PHASE_BRANCH" "$branches"
    other_phases_unchanged "$others" "$n"
    check_denials triage
    check_commit_subjects "$before"
    json=$(json_tail "$AGENT_RESULT")
    if [[ -z "$json" ]] || ! jq -e 'has("changed")' <<< "$json" >/dev/null; then
        record_progress false "triage (no {\"changed\", \"halt\"} line)"
        return 0
    fi
    halt_reason=$(jq -r '.halt // empty' <<< "$json")
    [[ -n "$halt_reason" ]] && halt agent-halt "$halt_reason"
    changed_json=$(jq -r '.changed' <<< "$json")
    changed_git=false
    [[ "$(git rev-parse HEAD)" != "$before" ]] && changed_git=true
    if [[ "$changed_json" != "$changed_git" ]]; then
        halt triage-mismatch "The triage agent reported changed=$changed_json, but its commits say $changed_git. Check its replies on PR #$S_PHASE_PR against the branch and rerun."
    fi
    if ! tree_clean; then
        record_progress false "triage (left the tree dirty)"
        return 0
    fi

    if [[ "$changed_git" == "true" ]]; then
        record_progress true triage
        ensure_green "$n"
        push_branch "$S_PHASE_BRANCH"
        S_REVIEW_ROUND=$((S_REVIEW_ROUND + 1))
        state_write
        "$GH_REVIEW" rerequest "$S_PHASE_PR"
        if (( S_REVIEW_ROUND >= CFG_MAX_ROUNDS )); then
            halt review-rounds "Round $S_REVIEW_ROUND of $CFG_MAX_ROUNDS changed code again, so the review is not closed (I.11). Read the new review on PR #$S_PHASE_PR; resolve or answer what is left, then rerun — phasepr merges once a review of the head leaves no open thread."
        fi
        return 0
    fi

    remaining=$("$GH_REVIEW" threads "$S_PHASE_PR" | jq length)
    if (( remaining > 0 )); then
        record_progress false "triage ($remaining thread(s) left unresolved)"
        return 0
    fi
    record_progress true triage
    TRIAGE_DONE=true
}

# Returns once a review of the current head leaves no open thread.
review_loop() {
    local n=$1 head rc threads count owner strangers
    owner=$("$GH_REVIEW" owner)
    while :; do
        head=$(git rev-parse HEAD)
        S_STEP=review
        state_write
        if [[ "$REREQUEST_ON_RESUME" == "true" ]]; then
            REREQUEST_ON_RESUME=false
            if ! "$GH_REVIEW" wait "$S_PHASE_PR" "$head" 0 0; then
                log "resumed: re-requesting a review of $head"
                "$GH_REVIEW" rerequest "$S_PHASE_PR"
            fi
        fi
        log "phase $n: waiting for $CFG_REVIEWER on $(git rev-parse --short HEAD) (PR #$S_PHASE_PR)"
        rc=0
        "$GH_REVIEW" wait "$S_PHASE_PR" "$head" "$CFG_REVIEW_TIMEOUT" "$CFG_POLL" || rc=$?
        if [[ $rc -eq 2 ]]; then
            halt review-timeout "No review from $CFG_REVIEWER on $(git rev-parse --short HEAD) within ${CFG_REVIEW_TIMEOUT}s. Check that Copilot code review is on for this repository and has quota; a rerun re-requests the review."
        elif [[ $rc -ne 0 ]]; then
            halt github-error "Waiting for the review of PR #$S_PHASE_PR failed (exit $rc)."
        fi
        threads=$("$GH_REVIEW" threads "$S_PHASE_PR") \
            || halt github-error "Reading the review threads of PR #$S_PHASE_PR failed — more than 100 threads, or GitHub did not answer."
        count=$(jq length <<< "$threads")
        if (( count == 0 )); then
            log "phase $n: the review of the head left no open thread"
            return 0
        fi
        if (( S_REVIEW_ROUND >= CFG_MAX_ROUNDS )); then
            halt review-rounds "$count thread(s) are open after the last of $CFG_MAX_ROUNDS review rounds (I.11). Answer and resolve them on PR #$S_PHASE_PR, then rerun."
        fi
        # What a thread says goes into the agent's prompt, and the agent can commit and reply. Only
        # the reviewer's and the owner's threads get there; anyone else's the owner answers first.
        strangers=$(jq -r --arg owner "$owner" --argjson trusted "$TRUSTED_REVIEW_AUTHORS" \
            '[.[] | select(.author as $a | ($trusted + [$owner]) | index($a) | not) | .author] | unique | join(", ")' \
            <<< "$threads")
        if [[ -n "$strangers" ]]; then
            halt untrusted-review "PR #$S_PHASE_PR has open threads by $strangers. phasepr hands an agent only the reviewer's and the owner's threads; answer and resolve those yourself, then rerun."
        fi
        triage_round "$n" "$threads"
        [[ "$TRIAGE_DONE" == "true" ]] && return 0
    done
}

merge_phase() {
    local n=$1 owner_paths rc
    S_STEP=merge
    state_write
    run git fetch origin "$FEATURE"
    # What the owner reviews before a merge: an instruction, a decision, the constitution (I.11),
    # and the product file, which the owner writes (I.1).
    owner_paths=$(git diff --name-only "origin/$FEATURE...HEAD" 2>/dev/null \
        | grep -E '^(instructions/|docs/decisions\.md$|docs/product\.md$|\.specify/memory/constitution\.md$)' \
        | paste -sd' ' - || true)
    if [[ -n "$owner_paths" ]] && ! "$GH_REVIEW" approved "$S_PHASE_PR" "$(git rev-parse HEAD)"; then
        if [[ "$S_OWNER_ASKED" != "$S_PHASE_PR" ]]; then
            "$GH_REVIEW" comment "$S_PHASE_PR" "phasepr stops before merging: this PR changes $owner_paths, and the owner reviews those before they merge: an instruction, a decision or the constitution (I.11), or the product file (I.1). An approving review lets phasepr merge it on its next run."
            S_OWNER_ASKED=$S_PHASE_PR
        fi
        halt owner-review "PR #$S_PHASE_PR changes $owner_paths (I.1, I.11). Review it: approve its head as $("$GH_REVIEW" owner) and rerun (phasepr merges), or merge it yourself and rerun."
    fi
    if ! "$GH_REVIEW" checks "$S_PHASE_PR"; then
        halt ci-red "CI is red on PR #$S_PHASE_PR though gates.sh was green here. Look at the failing check; fix it on '$S_PHASE_BRANCH' (or tell the agent in $MEMORY) and rerun."
    fi
    rc=0
    "$GH_REVIEW" merge "$S_PHASE_PR" || rc=$?
    if [[ $rc -eq 3 ]]; then
        halt merge-conflict "PR #$S_PHASE_PR conflicts with '$FEATURE'. Merge '$FEATURE' into '$S_PHASE_BRANCH' (no rebase), resolve, commit, and rerun: phasepr pushes it, and the new head goes through review again."
    elif [[ $rc -ne 0 ]]; then
        halt github-error "Merging PR #$S_PHASE_PR failed (exit $rc)."
    fi
    log "phase $n: merged PR #$S_PHASE_PR"
    run git fetch origin "$FEATURE"
    run git checkout "$FEATURE"
    run git pull --ff-only origin "$FEATURE"
    "$GH_REVIEW" tick "$S_DRAFT_PR" "$n" "$S_PHASE_PR"
    S_PHASE_PRS="${S_PHASE_PRS:+$S_PHASE_PRS }$n=$S_PHASE_PR"
    S_PHASE_PR=""
    S_REVIEW_ROUND=0
    S_ITER=0
    S_NO_PROGRESS=0
    S_GATE_SIG=""
    S_GREEN_HEAD=""
    S_STEP=merged
    state_write
}

run_phase() {
    local n=$1 title total checked tasks branch merged
    tasks=$(feature_tasks)
    title="" total=0 checked=0
    IFS=$'\t' read -r _ title total checked < <("$TASKS_SH" phases "$tasks" | awk -F'\t' -v n="$n" '$1 == n') || true
    rm -f "$tasks"
    [[ -n "$title" ]] || die_usage "there is no Phase $n in $TASKS"
    branch=$(phase_branch_of "$n")
    if [[ "$total" -eq 0 ]]; then
        log "phase $n has no tasks; nothing to do"
        return 0
    fi
    if [[ "$checked" -eq "$total" ]]; then
        merged=$("$GH_REVIEW" find "$branch" "$FEATURE" merged)
        "$GH_REVIEW" tick "$S_DRAFT_PR" "$n" "$merged"
        log "phase $n is done on $FEATURE${merged:+ (PR #$merged)}"
        return 0
    fi

    if [[ "$S_PHASE" != "$n" ]]; then
        S_PHASE=$n
        S_PHASE_PR=""
        S_REVIEW_ROUND=0
        S_ITER=0
        S_GATE_SIG=""
        S_GREEN_HEAD=""
    fi
    S_PHASE_BRANCH=$branch
    printf '\n=== phasepr: %s phase %s — %s (branch %s)\n' "$FEATURE" "$n" "$(phase_title "$n")" "$branch"
    ensure_phase_branch "$branch"
    ensure_green "$n"
    push_branch "$branch"
    open_phase_pr "$n"
    review_loop "$n"
    merge_phase "$n"
}

#endregion

#region Setup and finish

ensure_ignored() {
    [[ "$DRY_RUN" == "true" ]] && return 0
    mkdir -p "$LOG_DIR"
    if ! git check-ignore -q "$STATE"; then
        printf '/specs/*/phasepr/\n' >> "$(git rev-parse --git-path info/exclude)"
    fi
    [[ -f "$MEMORY" ]] || render "$EXT_ROOT/templates/memory.md" "FEATURE=$FEATURE" > "$MEMORY"
}

setup_draft() {
    local body title tasks n t total checked mark
    S_STEP=setup
    remote_has_branch "$FEATURE" || run git push -u origin "$FEATURE"
    S_DRAFT_PR=$("$GH_REVIEW" find "$FEATURE" main open)
    if [[ -n "$S_DRAFT_PR" ]]; then
        state_write
        return 0
    fi
    body="$LOG_DIR/draft-body.md"
    while :; do
        invoke_agent draft-body "$(render "$EXT_ROOT/prompts/draft-body.md" \
            "FEATURE=$FEATURE" "FEATURE_DIR=$FEATURE_DIR")"
        check_denials draft-body
        [[ -n "$AGENT_RESULT" ]] && break
        record_progress false "draft body"
    done
    tasks=$(feature_tasks)
    {
        printf '%s\n\n## Phases\n\n' "$AGENT_RESULT"
        printf '<!-- phasepr ticks a phase when its PR is merged into %s. -->\n' "$FEATURE"
        while IFS=$'\t' read -r n t total checked; do
            mark=" "
            [[ "$checked" -eq "$total" ]] && mark="x"
            t=$(sed -E 's/[[:space:]]*\(Priority:[^)]*\)//; s/[[:space:]]*🎯.*$//' <<< "$t")
            printf -- '- [%s] Phase %s — %s%s\n' "$mark" "$n" "$t" "$([[ "$total" -eq 0 ]] && printf ' (no tasks)')"
        done < <("$TASKS_SH" phases "$tasks")
    } > "$body"
    rm -f "$tasks"
    title=$(sed -n 's/^# Feature Specification:[[:space:]]*//p' "$FEATURE_DIR/spec.md" | head -n1)
    S_DRAFT_PR=$("$GH_REVIEW" draft-open "$FEATURE" main "feat($FEATURE_NUM): ${title:-$FEATURE}" "$body")
    [[ -n "$S_DRAFT_PR" || "$DRY_RUN" == "true" ]] || halt github-error "Opening the draft PR returned no number."
    S_DRAFT_PR=${S_DRAFT_PR:-DRY}
    log "draft PR #$S_DRAFT_PR"
    state_write
}

all_phases_done() {
    local tasks rc=0
    tasks=$(feature_tasks)
    "$TASKS_SH" phases "$tasks" | awk -F'\t' '$3 != $4 { bad = 1 } END { exit bad }' || rc=1
    rm -f "$tasks"
    return $rc
}

mutation_table() {
    printf '| Project | Created | Tested | Not covered | Score |\n| --- | ---: | ---: | ---: | ---: |\n'
    local report
    for report in StrykerOutput/*/reports/mutation-report.json; do
        [[ -f "$report" ]] || continue
        jq -r --arg project "$(basename "$(dirname "$(dirname "$report")")")" '
            [.files[].mutants[].status] as $s
            | ($s | map(select(. == "Killed")) | length) as $killed
            | ($s | map(select(. == "Timeout")) | length) as $timeout
            | ($s | map(select(. == "Survived")) | length) as $survived
            | ($s | map(select(. == "NoCoverage")) | length) as $uncovered
            | ($killed + $timeout + $survived + $uncovered) as $valid
            | "| \($project) | \($s | length) | \($killed + $timeout + $survived) | \($uncovered) | "
              + (if $valid == 0 then "none" else "\((($killed + $timeout) * 1000 / $valid | round) / 10)%" end)
              + " |"' "$report"
    done
}

record_mutation() {
    local file="$FEATURE_DIR/mutation.md" section log
    [[ "$S_MUTATION" == "done" ]] && return 0
    S_STEP=mutation
    state_write
    if [[ "$DRY_RUN" == "true" ]]; then
        run ./scripts/mutation.sh
        run git commit -m "docs($FEATURE_NUM): record the mutation measurement" -- "$file"
        run git push origin "$FEATURE"
        return 0
    fi
    log="$LOG_DIR/$(date -u +%Y%m%dT%H%M%SZ)-mutation.log"
    log "mutation measurement (log $log)"
    ./scripts/mutation.sh > "$log" 2>&1 \
        || halt mutation-failed "scripts/mutation.sh failed (log: $log). The phases are merged; fix the measurement and rerun."
    section=$(
        printf '<!-- phasepr:mutation:start -->\n## Mutation measurement\n\n'
        printf 'Measured by `scripts/mutation.sh` on `%s`, %s, when the last phase was merged. A\n' \
            "$(git rev-parse --short HEAD)" "$(date -u +%Y-%m-%d)"
        printf 'measurement, not a gate: phasepr classifies no survivor; a person does, from the reports.\n\n'
        mutation_table
        printf '<!-- phasepr:mutation:end -->\n'
    )
    if [[ -f "$file" ]] && grep -q '^<!-- phasepr:mutation:start -->$' "$file"; then
        awk -v section="$section" '
            /^<!-- phasepr:mutation:start -->$/ { print section; skip = 1; next }
            /^<!-- phasepr:mutation:end -->$/ { skip = 0; next }
            !skip { print }
        ' "$file" > "$file.tmp" && mv "$file.tmp" "$file"
    else
        [[ -s "$file" ]] && printf '\n' >> "$file"
        printf '%s\n' "$section" >> "$file"
    fi
    if ! git diff --quiet -- "$file" || ! git ls-files --error-unmatch "$file" >/dev/null 2>&1; then
        git add -- "$file"
        git commit -q -m "docs($FEATURE_NUM): record the mutation measurement" -- "$file"
        push_branch "$FEATURE"
    fi
    S_MUTATION="done"
    state_write
}

finish() {
    if [[ "$DRY_RUN" != "true" ]] && ! all_phases_done; then
        halt phases-open "Not every phase of $TASKS is checked on '$FEATURE' although phasepr went through all of them. Check tasks.md on the feature branch and rerun."
    fi
    run git checkout "$FEATURE"
    if [[ "$CFG_RUN_MUTATION" == "true" ]]; then
        record_mutation
    else
        log "mutation measurement skipped (run_mutation: false or --skip-mutation)"
    fi
    S_STEP=ready
    state_write
    "$GH_REVIEW" ready "$S_DRAFT_PR"
    S_STEP="done"
    state_write
    printf '\nphasepr: %s is complete. Draft PR #%s is ready for review; it is not merged to main.\n' \
        "$FEATURE" "$S_DRAFT_PR"
}

show_status() {
    local tasks
    printf 'phasepr status — %s\n\n' "$FEATURE"
    if [[ -f "$STATE" ]]; then
        sed -n '/^- feature:/,/^- updated:/p' "$STATE"
        printf '\n'
        sed -n '/^## Last halt/,/^<!-- machine -->/p' "$STATE" | sed '$d'
    else
        printf 'No state yet (%s does not exist).\n' "$STATE"
    fi
    printf '\nPhases on %s:\n' "$FEATURE"
    tasks=$(feature_tasks)
    "$TASKS_SH" phases "$tasks" | awk -F'\t' '{ printf "  Phase %s — %s: %s/%s done\n", $1, $2, $4, $3 }'
    rm -f "$tasks"
}

#endregion

#region Main

for f in spec.md plan.md tasks.md; do
    git cat-file -e "$FEATURE:$FEATURE_DIR/$f" 2>/dev/null || die_usage "$FEATURE_DIR/$f is not on branch $FEATURE"
done

if [[ "$STATUS_ONLY" == "true" ]]; then
    show_status
    exit 0
fi

for tool in git jq; do
    command -v "$tool" >/dev/null || die_usage "$tool is not installed"
done
if [[ "$DRY_RUN" != "true" ]]; then
    for tool in gh "$CFG_AGENT_CLI"; do
        command -v "$tool" >/dev/null || die_usage "$tool is not installed"
    done
fi

state_load
if [[ "$S_HALT" != "none" ]]; then
    log "resuming after halt '$S_HALT' in phase ${S_PHASE:-none}, step $S_STEP"
    # A halt while waiting for or answering a review may have lost the review request.
    [[ "$S_STEP" == "review" || "$S_STEP" == "triage" ]] && REREQUEST_ON_RESUME=true
    S_HALT=none
    S_HALT_SUMMARY=none
    S_NO_PROGRESS=0
    S_GATE_SIG=""
fi

if [[ "$DRY_RUN" == "true" ]]; then
    LOG_DIR=$(mktemp -d)
    log "dry run: nothing is changed; prompts go to $LOG_DIR"
fi
ensure_ignored

# A dirty tree is fine only on the phase branch an interrupted iteration left it on.
if ! tree_clean && [[ "$(current_branch)" != "$S_PHASE_BRANCH" || -z "$S_PHASE_BRANCH" ]]; then
    die_usage "the working tree is not clean on '$(current_branch)'; commit or stash first"
fi

trap on_interrupt INT TERM

printf '=== phasepr: %s%s\n' "$FEATURE" "${ONLY_PHASE:+ (phase $ONLY_PHASE only)}"
setup_draft

if [[ -n "$ONLY_PHASE" ]]; then
    run_phase "$ONLY_PHASE"
    S_STEP="done"
    state_write
    printf '\nphasepr: phase %s of %s is done.\n' "$ONLY_PHASE" "$FEATURE"
    exit 0
fi

tasks_file=$(feature_tasks)
mapfile -t PHASES < <("$TASKS_SH" phases "$tasks_file" | cut -f1)
rm -f "$tasks_file"
for n in "${PHASES[@]}"; do
    run_phase "$n"
done
finish
exit 0

#endregion
