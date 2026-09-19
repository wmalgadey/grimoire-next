#!/usr/bin/env node
// Quality gate 9: the ADR lint (constitution VI.1–VI.3, and the index's own rules).
//
//   node .github/adr-lint/lint.mjs               # check docs/adr, as CI runs it
//   node .github/adr-lint/lint.mjs path/to/adrs  # check another directory
//
// Checks each record against what can be checked without reading it for meaning:
//
//   - the fixed template: `# ADR-NNNN: Title`, then exactly Status, Context, Decision,
//     Consequences, Alternatives Considered as the second-level headings, in that order (VI.3);
//   - the status is Proposed, Accepted, or `Superseded by ADR-NNNN` (VI.3);
//   - a supersession names a record that exists, is not the record itself and came after it (VI.2);
//   - records do not cite one another outside the Status line (the index, "Cross-references");
//   - the index has exactly one row per record, its status matches the record's, and its Kind is
//     exactly one of the four kinds — the part of "one decision aspect" (VI.1) and "only the four
//     kinds" (VI.4) that a machine can see. Whether a record really decides one thing is PR review.

import { existsSync, readdirSync, readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const HEADINGS = ["Status", "Context", "Decision", "Consequences", "Alternatives Considered"];
const KINDS = ["Technology choice", "External port", "Security boundary", "Agent autonomy change"];
const RECORD_FILE = /^(\d{4})-[a-z0-9-]+\.md$/;
const STATUS = /^(Proposed|Accepted|Superseded by \[?ADR-(\d{4})\]?(\(\.\/\d{4}-[a-z0-9-]+\.md\))?)$/;
const CITATION = /ADR-\d{4}|\(\.{0,2}\/?(?:[^)\s]*\/)?\d{4}-[a-z0-9-]+\.md\)/;

export function lint(directory) {
  const problems = [];
  const files = readdirSync(directory).filter((name) => name.endsWith(".md") && name !== "index.md");
  const records = new Map();

  for (const file of files.sort()) {
    const match = RECORD_FILE.exec(file);
    if (!match) {
      problems.push(`${file}: a record's file name is NNNN-slug.md`);
      continue;
    }
    if (records.has(match[1])) {
      problems.push(`${file}: number ${match[1]} is already used by ${records.get(match[1]).file}`);
      continue;
    }
    const record = parse(file, match[1], readFileSync(join(directory, file), "utf8"), problems);
    records.set(match[1], record);
  }

  for (const record of records.values()) {
    const superseder = record.supersededBy;
    if (superseder === undefined) continue;
    if (superseder === record.id) {
      problems.push(`${record.file}: a record cannot supersede itself`);
    } else if (!records.has(superseder)) {
      problems.push(`${record.file}: superseded by ADR-${superseder}, which does not exist`);
    } else if (superseder < record.id) {
      problems.push(`${record.file}: superseded by ADR-${superseder}, which is older than it`);
    }
  }

  checkIndex(directory, records, problems);
  return problems;
}

function parse(file, id, text, problems) {
  const record = { file, id, status: undefined, supersededBy: undefined };
  const lines = withoutFences(text.split("\n"));

  const titles = lines.filter((line) => /^# /.test(line));
  if (titles.length !== 1) {
    problems.push(`${file}: expected exactly one '# ADR-${id}: Title' heading, found ${titles.length}`);
  } else if (!new RegExp(`^# ADR-${id}: \\S`).test(titles[0])) {
    problems.push(`${file}: the title must read '# ADR-${id}: <title>', not '${titles[0]}'`);
  }

  const headings = lines.filter((line) => /^## /.test(line)).map((line) => line.slice(3).trim());
  if (headings.join("|") !== HEADINGS.join("|")) {
    problems.push(`${file}: the headings must be ${HEADINGS.join(", ")} in that order; found ${headings.join(", ") || "none"}`);
  }

  const status = section(lines, "Status").filter((line) => line.trim() !== "");
  if (status.length !== 1) {
    problems.push(`${file}: the Status section must be a single line, found ${status.length}`);
  } else {
    const statusMatch = STATUS.exec(status[0].trim());
    if (!statusMatch) {
      problems.push(`${file}: status '${status[0].trim()}' is not Proposed, Accepted or 'Superseded by ADR-NNNN'`);
    } else {
      record.status = statusMatch[2] ? `Superseded by ADR-${statusMatch[2]}` : statusMatch[1];
      record.supersededBy = statusMatch[2];
    }
  }

  // The title and the Status line are the only places a record may name a record.
  const statusStart = lines.indexOf("## Status");
  const statusEnd = statusStart + 1 + section(lines, "Status").length;
  lines.forEach((line, index) => {
    if (/^# /.test(line) || (index > statusStart && index < statusEnd)) return;
    const citation = CITATION.exec(line);
    if (citation) {
      problems.push(`${file}:${index + 1}: cites '${citation[0]}' — records state the fact they depend on, not the record`);
    }
  });

  return record;
}

function checkIndex(directory, records, problems) {
  const indexPath = join(directory, "index.md");
  if (!existsSync(indexPath)) {
    problems.push("index.md: missing");
    return;
  }
  const rows = new Map();
  for (const line of readFileSync(indexPath, "utf8").split("\n")) {
    const row = /^\|\s*\[(\d{4})\]\(\.\/([^)]+)\)\s*\|(.*)\|\s*$/.exec(line);
    if (!row) continue;
    const [, id, target, rest] = row;
    const cells = rest.split("|").map((cell) => cell.trim());
    if (rows.has(id)) problems.push(`index.md: ADR-${id} has more than one row`);
    rows.set(id, { target, kind: cells[1], status: cells[2] });
  }

  for (const [id, record] of records) {
    const row = rows.get(id);
    if (!row) {
      problems.push(`index.md: ADR-${id} (${record.file}) has no row`);
      continue;
    }
    if (row.target !== record.file) {
      problems.push(`index.md: ADR-${id} links to ${row.target}, not ${record.file}`);
    }
    if (!KINDS.includes(row.kind)) {
      problems.push(`index.md: ADR-${id} has kind '${row.kind}'; a record is exactly one of ${KINDS.join(", ")}`);
    }
    if (record.status !== undefined && comparable(row.status) !== comparable(record.status)) {
      problems.push(`index.md: ADR-${id} is '${row.status}' in the index but '${record.status}' in ${record.file}`);
    }
  }
  for (const id of rows.keys()) {
    if (!records.has(id)) problems.push(`index.md: ADR-${id} has a row but no record`);
  }
}

// The index may write a supersession as `Superseded by 0013`, `ADR-0013` or a link to either.
function comparable(status) {
  return status.replace(/\[|\]|\(.*?\)|ADR-/g, "").replace(/\s+/g, " ").trim();
}

function section(lines, heading) {
  const start = lines.indexOf(`## ${heading}`);
  if (start === -1) return [];
  const end = lines.findIndex((line, index) => index > start && /^#{1,2} /.test(line));
  return lines.slice(start + 1, end === -1 ? lines.length : end);
}

// Headings and citations inside a fenced block are examples, not structure. Fenced lines are
// blanked rather than removed so that reported line numbers stay the file's own.
function withoutFences(lines) {
  let fenced = false;
  return lines.map((line) => {
    if (/^\s*(```|~~~)/.test(line)) {
      fenced = !fenced;
      return "";
    }
    return fenced ? "" : line.replace(/\r$/, "");
  });
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const root = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
  const directory = resolve(process.argv[2] ?? join(root, "docs/adr"));
  const problems = lint(directory);
  for (const problem of problems) console.error(problem);
  if (problems.length > 0) {
    console.error(`\nADR lint: ${problems.length} problem(s) in ${directory}.`);
    process.exit(1);
  }
  console.log(`ADR lint: every record in ${directory} passes.`);
}
