// The ADR lint's own tests: each rule fails a record that breaks it, and passes one that keeps it.
//
//   node --test .github/adr-lint/

import assert from "node:assert/strict";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, test } from "node:test";

import { lint } from "./lint.mjs";

const directories = [];
afterEach(() => directories.splice(0).forEach((d) => rmSync(d, { recursive: true, force: true })));

function record(id, { title = "A decision", status = "Accepted", body = "Some forces." } = {}) {
  return [
    `# ADR-${id}: ${title}`, "", "## Status", "", status, "",
    "## Context", "", body, "", "## Decision", "", "Do it.", "",
    "## Consequences", "", "Costs.", "", "## Alternatives Considered", "", "Others.", "",
  ].join("\n");
}

function row(id, file, { kind = "Technology choice", status = "Accepted" } = {}) {
  return `| [${id}](./${file}) | A decision | ${kind} | ${status} | [001](../../specs/001/plan.md) |`;
}

function adrs(files, rows) {
  const directory = mkdtempSync(join(tmpdir(), "adr-lint-"));
  directories.push(directory);
  for (const [name, text] of Object.entries(files)) writeFileSync(join(directory, name), text);
  if (rows !== null) {
    writeFileSync(join(directory, "index.md"), ["| ID | Decision | Kind | Status | Introduced by |", "|---|---|---|---|---|", ...rows].join("\n"));
  }
  return lint(directory);
}

function one(files, rows) {
  const problems = adrs(files, rows);
  assert.equal(problems.length, 1, problems.join("\n"));
  return problems[0];
}

test("the repository's own records pass", () => {
  assert.deepEqual(lint(new URL("../../docs/adr", import.meta.url).pathname), []);
});

test("a well-formed record with its index row passes", () => {
  assert.deepEqual(adrs({ "0001-a.md": record("0001") }, [row("0001", "0001-a.md")]), []);
});

test("a missing heading fails", () => {
  const text = record("0001").replace("## Consequences\n\nCosts.\n\n", "");
  assert.match(one({ "0001-a.md": text }, [row("0001", "0001-a.md")]), /headings must be/);
});

test("headings out of order fail", () => {
  const text = record("0001").replace("## Context", "## TEMP").replace("## Decision", "## Context").replace("## TEMP", "## Decision");
  assert.match(one({ "0001-a.md": text }, [row("0001", "0001-a.md")]), /in that order/);
});

test("an extra second-level heading fails", () => {
  const text = `${record("0001")}\n## Notes\n\nMore.\n`;
  assert.match(one({ "0001-a.md": text }, [row("0001", "0001-a.md")]), /headings must be/);
});

test("a heading inside a fenced block is not structure", () => {
  const text = record("0001", { body: "```markdown\n## Notes\nADR-0002\n```" });
  assert.deepEqual(adrs({ "0001-a.md": text }, [row("0001", "0001-a.md")]), []);
});

test("a title that does not match the file's number fails", () => {
  assert.match(one({ "0001-a.md": record("0002") }, [row("0001", "0001-a.md")]), /title must read '# ADR-0001/);
});

test("a status outside the three fails", () => {
  const problems = adrs({ "0001-a.md": record("0001", { status: "Deprecated" }) }, [row("0001", "0001-a.md", { status: "Deprecated" })]);
  assert.equal(problems.length, 1, problems.join("\n"));
  assert.match(problems[0], /status 'Deprecated' is not/);
});

test("a supersession by a later record passes", () => {
  const files = { "0001-a.md": record("0001", { status: "Superseded by ADR-0002" }), "0002-b.md": record("0002") };
  const rows = [row("0001", "0001-a.md", { status: "Superseded by 0002" }), row("0002", "0002-b.md")];
  assert.deepEqual(adrs(files, rows), []);
});

test("a supersession naming a record that does not exist fails", () => {
  const rows = [row("0001", "0001-a.md", { status: "Superseded by ADR-0009" })];
  assert.match(one({ "0001-a.md": record("0001", { status: "Superseded by ADR-0009" }) }, rows), /ADR-0009, which does not exist/);
});

test("a supersession by an older record fails", () => {
  const files = { "0001-a.md": record("0001"), "0002-b.md": record("0002", { status: "Superseded by ADR-0001" }) };
  const rows = [row("0001", "0001-a.md"), row("0002", "0002-b.md", { status: "Superseded by ADR-0001" })];
  assert.match(one(files, rows), /older than it/);
});

test("a record superseding itself fails", () => {
  const rows = [row("0001", "0001-a.md", { status: "Superseded by ADR-0001" })];
  assert.match(one({ "0001-a.md": record("0001", { status: "Superseded by ADR-0001" }) }, rows), /cannot supersede itself/);
});

test("a record citing another record fails", () => {
  const files = { "0001-a.md": record("0001", { body: "As ADR-0002 decided." }), "0002-b.md": record("0002") };
  assert.match(one(files, [row("0001", "0001-a.md"), row("0002", "0002-b.md")]), /0001-a\.md:9: cites 'ADR-0002'/);
});

test("a record linking to another record fails", () => {
  const files = { "0001-a.md": record("0001", { body: "See [the proxy](./0002-b.md)." }), "0002-b.md": record("0002") };
  assert.match(one(files, [row("0001", "0001-a.md"), row("0002", "0002-b.md")]), /cites '\(\.\/0002-b\.md\)'/);
});

test("a record without an index row fails", () => {
  assert.match(one({ "0001-a.md": record("0001") }, []), /ADR-0001 \(0001-a\.md\) has no row/);
});

test("an index row without a record fails", () => {
  assert.match(one({ "0001-a.md": record("0001") }, [row("0001", "0001-a.md"), row("0002", "0002-b.md")]), /ADR-0002 has a row but no record/);
});

test("a missing index fails", () => {
  assert.match(one({ "0001-a.md": record("0001") }, null), /index\.md: missing/);
});

test("an index status that disagrees with the record fails", () => {
  assert.match(one({ "0001-a.md": record("0001") }, [row("0001", "0001-a.md", { status: "Proposed" })]), /'Proposed' in the index but 'Accepted'/);
});

test("a kind that is not exactly one of the four fails", () => {
  const rows = [row("0001", "0001-a.md", { kind: "Technology choice, Security boundary" })];
  assert.match(one({ "0001-a.md": record("0001") }, rows), /exactly one of/);
});

test("a file name that is not NNNN-slug.md fails", () => {
  assert.match(one({ "0001-a.md": record("0001"), "notes.md": "x" }, [row("0001", "0001-a.md")]), /notes\.md: a record's file name/);
});
