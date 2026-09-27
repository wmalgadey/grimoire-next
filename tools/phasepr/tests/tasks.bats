#!/usr/bin/env bats
# tasks.sh: phases and checkboxes read out of a tasks.md.

setup() {
    TASKS_SH="$BATS_TEST_DIRNAME/../scripts/bash/tasks.sh"
    FILE="$BATS_TEST_DIRNAME/fixtures/repo/specs/042-demo/tasks.md"
}

@test "phases lists every phase with its task counts, a phase without tasks included" {
    run "$TASKS_SH" phases "$FILE"
    [ "$status" -eq 0 ]
    [ "${lines[0]}" = "$(printf '1\tSetup (shared)\t0\t0')" ]
    [ "${lines[1]}" = "$(printf '2\tFoundational — the base\t2\t0')" ]
    [ "${lines[2]}" = "$(printf '3\tUser Story 1 — Use it (Priority: P1) 🎯 MVP\t1\t0')" ]
    [ "${#lines[@]}" -eq 3 ]
}

@test "a checkbox in the Format section belongs to no phase" {
    run "$TASKS_SH" others "$FILE" 2
    [ "$output" = "T003 -" ]
}

@test "open and complete follow the checkboxes, lower and upper case x alike" {
    sed 's/- \[ \] T001/- [x] T001/; s/- \[ \] T002/- [X] T002/' "$FILE" > "$BATS_TEST_TMPDIR/t.md"
    run "$TASKS_SH" open "$BATS_TEST_TMPDIR/t.md" 2
    [ -z "$output" ]
    run "$TASKS_SH" complete "$BATS_TEST_TMPDIR/t.md" 2
    [ "$status" -eq 0 ]
    run "$TASKS_SH" complete "$BATS_TEST_TMPDIR/t.md" 3
    [ "$status" -eq 1 ]
}

@test "reqs names the requirement IDs and leaves out OUT- and DEC-" {
    run "$TASKS_SH" reqs "$FILE" 3
    [ "$output" = "DEMO-002" ]
    run "$TASKS_SH" reqs "$FILE" 2
    [ "$output" = "DEMO-001" ]
}

@test "purpose joins a wrapped Purpose paragraph into one line, and Goal counts as purpose" {
    run "$TASKS_SH" purpose "$FILE" 2
    [ "$output" = "what the user story stands on; nothing here without a consumer." ]
    run "$TASKS_SH" purpose "$FILE" 3
    [ "$output" = "the user uses it." ]
}

@test "branch is the backticked Branch line, and nothing where a phase declares none" {
    run "$TASKS_SH" branch "$FILE" 2
    [ "$output" = "042-demo-phase-2-base" ]
    run "$TASKS_SH" branch "$FILE" 3
    [ -z "$output" ]
}

@test "a section heading after the last phase ends that phase" {
    printf -- '- [ ] T777 stray\n' >> "$BATS_TEST_TMPDIR/tail.md"
    cat "$FILE" "$BATS_TEST_TMPDIR/tail.md" > "$BATS_TEST_TMPDIR/t.md"
    run "$TASKS_SH" ids "$BATS_TEST_TMPDIR/t.md" 3
    [ "$output" = "T003" ]
}

@test "a missing file or a bad phase number is a usage error" {
    run "$TASKS_SH" phases /nonexistent
    [ "$status" -eq 2 ]
    run "$TASKS_SH" open "$FILE" two
    [ "$status" -eq 2 ]
}
