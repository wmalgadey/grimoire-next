// The browser half of ACCESS-006: one run's record, read as the file holds it. No Markdown renderer
// and no build step — DEC-019 rules both out, and docs/ux.md asks for monospace wherever the content
// is a log or a file. The only structure read here is the record's own line shape, which
// contracts/run-record.md writes down and a Fast test pins (research.md R-08).

const frame = document.getElementById("frame");
const record = document.getElementById("record");
const missing = document.getElementById("missing");
const message = document.getElementById("message");

// Which submission's run. The link carries the submission's identifier, never the run's: a submission
// has exactly one run (INGEST-002), so naming it names the run (contracts/hub-http-api.md).
const submission = new URLSearchParams(window.location.search).get("submission");

// How many segments are already on the page. Segments are only ever appended, so the record's own
// order is the page's order and what is already there is never touched.
let shown = 0;

// The five first lines a segment can have, after the time — plus the one a record gets when entries
// could not be written. A `## ` line that matches none of them is not a boundary: the agent's own
// text is prose and goes in unfenced, so it may hold one, and treating that as a segment would split
// what the agent said in two (contracts/run-record.md, rules 1 and 2).
const openings = [
  /^called .+$/,
  /^.+ returned$/,
  /^the agent$/,
  /^Grimoire$/,
  /^ended (done|failed) — .+$/,
  /^\d+ entries of this run could not be written$/,
];

// A line that is nothing but backticks, at column one. The record's fences are always a run of their
// own on a line, which is what makes them findable without parsing Markdown.
const fenceLine = /^(`{3,})\s*$/;

// What a segment's first line says it is, after the time — or null where the line is no boundary at
// all. The agent's own text is prose and goes in unfenced, so it may hold a `## ` line of its own;
// treating that as a boundary would split what the agent said in two (contracts/run-record.md, rule 2).
function opening(line) {
  if (!line.startsWith("## ")) {
    return null;
  }

  const parts = line.slice(3).split(" · ");
  if (parts.length < 2) {
    return null;
  }

  const said = parts.slice(1).join(" · ");

  return openings.some((shape) => shape.test(said)) ? said : null;
}

// Only a call's arguments and a call's result are fenced blocks. The agent's own text and what
// Grimoire said are prose, and prose is shown whole — an agent writes fenced code as a matter of
// course, and reading such a segment as a result would fold the code and throw away every word around
// it (ACCESS-006, contracts/run-record.md rule 3).
function isFenced(said) {
  return said.startsWith("called ") || said.endsWith(" returned");
}

// The record, split into the frame and its segments. A reader finds the fence first and skips to its
// close, which is the one rule a naive split on `## ` would get wrong: a tool result may hold such a
// line, and nothing of a result is escaped (contracts/run-record.md, rules 3 and 4).
function split(text) {
  const frameLines = [];
  const segments = [];
  let current = null;
  let openFence = 0;

  for (const line of text.split("\n")) {
    const fence = fenceLine.exec(line);

    if (openFence === 0 && fence) {
      openFence = fence[1].length;
    } else if (openFence > 0 && fence && fence[1].length >= openFence) {
      // CommonMark's own rule: a fence closes only on one at least as long. The record opens with one
      // longer than anything inside, so nothing a tool returned can close it early.
      openFence = 0;
    } else if (openFence === 0 && opening(line) !== null) {
      current = { heading: line.slice(3), said: opening(line), body: [] };
      segments.push(current);
      continue;
    }

    (current ? current.body : frameLines).push(line);
  }

  return { frame: frameLines.join("\n").trim(), segments };
}

// What the one fenced block of a segment holds, or null where the segment is prose. Byte for byte:
// the fences come off and nothing between them is touched.
function fencedIn(body) {
  const opening = body.findIndex((line) => fenceLine.test(line));
  if (opening < 0) {
    return null;
  }

  const length = body[opening].trim().length;
  const closing = body.findIndex(
    (line, at) => at > opening && fenceLine.test(line) && line.trim().length >= length,
  );

  return body.slice(opening + 1, closing < 0 ? body.length : closing).join("\n");
}

// One element per moment. A call and a result are one line with the block folded under them, so the
// user can follow what the run did without reading the results in full and still reach any one of
// them; the agent's own text and what Grimoire said are prose and are simply shown (ACCESS-006).
function element(segment) {
  const item = document.createElement("li");

  const heading = document.createElement("div");
  heading.className = "heading";
  heading.textContent = segment.heading;
  item.append(heading);

  const fenced = isFenced(segment.said) ? fencedIn(segment.body) : null;

  if (fenced === null) {
    const prose = document.createElement("div");
    prose.className = "prose";

    // textContent, never innerHTML: this is what the agent wrote and what a tool returned.
    prose.textContent = segment.body.join("\n").trim();
    item.append(prose);
    return item;
  }

  const folded = document.createElement("details");
  folded.className = "result";

  const label = document.createElement("summary");
  label.textContent = `${fenced.split("\n").length} lines`;

  const block = document.createElement("pre");
  block.textContent = fenced;

  folded.append(label, block);
  item.append(folded);
  return item;
}

async function refresh() {
  let response;
  try {
    // no-store, because a polled record answered from the browser's cache is a run that has stopped
    // growing on the screen and not in fact (contracts/hub-http-api.md).
    response = await fetch(`/api/submissions/${submission}/record`, { cache: "no-store" });
  } catch {
    // Grimoire could not be reached. What is on the screen stays: a record that has not been
    // contradicted is still the last one known.
    return;
  }

  if (response.status === 404) {
    message.textContent = "There is no record for this run.";
    return;
  }

  if (!response.ok) {
    return;
  }

  const { frame: head, segments } = split(await response.text());

  // The frame is replaced rather than appended to, because it is the one part that grows in place:
  // while the run is under way it is the head alone, and the tail arrives as a segment of its own.
  frame.textContent = head;

  // Appended, and only what is not already there. An element already on the page is never replaced,
  // which is what keeps the scroll where the user left it and a result they had opened open.
  for (const segment of segments.slice(shown)) {
    record.append(element(segment));
  }

  shown = segments.length;
  message.textContent = "";
}

// How many entries of this run's record could not be written. It comes off the list, which is where
// the count lives: the figures are state and the record is prose (research.md R-06).
async function refreshMissing() {
  let response;
  try {
    response = await fetch("/api/submissions", { cache: "no-store" });
  } catch {
    return;
  }

  if (!response.ok) {
    return;
  }

  const body = await response.json().catch(() => null);
  const mine = body?.submissions?.find((s) => s.id === submission);

  // Said only where lines are actually missing. The run went on; a gap that passed for an agent doing
  // nothing would be worse than the gap (RUNS-007).
  missing.textContent = mine?.entriesLost
    ? `${mine.entriesLost} entries of this run could not be written, and are missing below.`
    : "";
}

refresh();
refreshMissing();
