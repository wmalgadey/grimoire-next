#!/usr/bin/env bash
#
# tasks.sh - read phases and checkbox state out of a Spec Kit tasks.md
#
# A phase is everything under a "## Phase N" heading up to the next "## " heading. A task is a
# checkbox line whose first word is a task ID: "- [ ] T012 ..." or "- [X] T012 ...". Checkboxes
# outside a phase (the Format section's examples, for one) belong to no phase and are ignored.
#
# Usage:
#   tasks.sh phases   <tasks.md>        N<TAB>title<TAB>total<TAB>done, one line per phase, in order
#   tasks.sh title    <tasks.md> <N>    the heading text after "Phase N:"
#   tasks.sh ids      <tasks.md> <N>    every task ID of phase N
#   tasks.sh open     <tasks.md> <N>    the task IDs of phase N that are still unchecked
#   tasks.sh complete <tasks.md> <N>    exit 0 when every task of phase N is checked
#   tasks.sh reqs     <tasks.md> <N>    the requirement IDs phase N names (OUT-/DEC- excluded)
#   tasks.sh purpose  <tasks.md> <N>    the **Goal**/**Purpose** paragraph of phase N, one line
#   tasks.sh branch   <tasks.md> <N>    the backticked **Branch** of phase N, if it declares one
#   tasks.sh others   <tasks.md> <N>    ID<SPACE>state of every task outside phase N
#
# Reads only. Exit 2 on a usage error or a missing file.

set -euo pipefail

usage() {
    sed -n '3,21p' "$0" | sed 's/^# \{0,1\}//' >&2
    exit 2
}

[[ $# -ge 2 ]] || usage
cmd=$1
file=$2
phase=${3:-}
[[ -f "$file" ]] || { printf 'tasks.sh: no such file: %s\n' "$file" >&2; exit 2; }
case "$cmd" in
    phases) ;;
    title|ids|open|complete|reqs|purpose|branch|others)
        [[ "$phase" =~ ^[0-9]+$ ]] || usage
        ;;
    *) usage ;;
esac

# One pass over the file emits a flat record stream the subcommands filter:
#   P <N> <title>          a phase heading
#   T <N> <ID> <x|space>   a task and its state (N empty outside any phase)
#   L <N> <line>           any other line inside phase N
records() {
    awk '
        /^## / {
            if (match($0, /^## Phase [0-9]+/)) {
                n = substr($0, 10, RLENGTH - 9)
                title = substr($0, RLENGTH + 1)
                sub(/^[[:space:]]*[:.-]?[[:space:]]*/, "", title)
                phase = n
                print "P\t" n "\t" title
            } else {
                phase = ""
            }
            next
        }
        /^[[:space:]]*- \[[ xX]\] T[0-9]+([^0-9A-Za-z]|$)/ {
            state = ($0 ~ /^[[:space:]]*- \[[xX]\]/) ? "x" : " "
            match($0, /T[0-9]+/)
            print "T\t" phase "\t" substr($0, RSTART, RLENGTH) "\t" state
            if (phase != "") print "L\t" phase "\t" $0
            next
        }
        phase != "" { print "L\t" phase "\t" $0 }
    ' "$file"
}

phase_lines() {
    records | awk -F'\t' -v n="$phase" '$1 == "L" && $2 == n { sub(/^L\t[0-9]+\t/, ""); print }'
}

case "$cmd" in
    phases)
        records | awk -F'\t' '
            $1 == "P" { order[++count] = $2; title[$2] = $3; total[$2] += 0; done[$2] += 0 }
            $1 == "T" && $2 != "" { total[$2]++; if ($4 == "x") done[$2]++ }
            END { for (i = 1; i <= count; i++) { n = order[i]; print n "\t" title[n] "\t" total[n] "\t" done[n] } }
        '
        ;;
    title)
        records | awk -F'\t' -v n="$phase" '$1 == "P" && $2 == n { print $3; exit }'
        ;;
    ids)
        records | awk -F'\t' -v n="$phase" '$1 == "T" && $2 == n { print $3 }'
        ;;
    open)
        records | awk -F'\t' -v n="$phase" '$1 == "T" && $2 == n && $4 != "x" { print $3 }'
        ;;
    complete)
        open_count=$(records | awk -F'\t' -v n="$phase" '$1 == "T" && $2 == n && $4 != "x"' | wc -l)
        [[ "$open_count" -eq 0 ]]
        ;;
    reqs)
        { phase_lines | grep -oE '[A-Z][A-Z]+-[0-9]{3}' || true; } \
            | grep -vE '^(OUT|DEC)-' | awk '!seen[$0]++' || true
        ;;
    purpose)
        phase_lines | awk '
            /^\*\*(Goal|Purpose)\*\*:/ { on = 1; sub(/^\*\*(Goal|Purpose)\*\*:[[:space:]]*/, "") }
            on && /^[[:space:]]*$/ { exit }
            on { gsub(/^[[:space:]]+|[[:space:]]+$/, ""); text = text (text == "" ? "" : " ") $0 }
            END { if (text != "") print text }
        '
        ;;
    branch)
        phase_lines | awk '
            /^\*\*Branch\*\*:/ && match($0, /`[^`]+`/) { print substr($0, RSTART + 1, RLENGTH - 2); exit }
        '
        ;;
    others)
        records | awk -F'\t' -v n="$phase" '$1 == "T" && $2 != n { print $3 " " ($4 == "x" ? "x" : "-") }'
        ;;
esac
