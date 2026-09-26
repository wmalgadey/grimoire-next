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

// Two polls can be in flight at once, and they can answer out of order. The newest request's answer is
// the current one: an older answer arriving after it would put `shown` back to a smaller number, and
// the next poll would append segments that are already on the page. `app.js` has guarded exactly this
// since `001-first-ingest`, and the record only grows, so the risk is real rather than theoretical —
// a large record's fetch takes longer than the second between polls.
let newestRequest = 0;

// And its own counter for the list, which is a second poll with a second answer that can arrive out of
// order. Shared with the record's, a slow record answer would silence a fresh warning — and the warning
// is the one thing on this page that says the record is incomplete (RUNS-007).
let newestMissingRequest = 0;

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

// A fence at column one. The opening one may name what it holds — the record says `json` for a call's
// arguments, which it writes itself — and CommonMark allows no such name on the closing fence, so the
// close is found by the backticks alone. The record's fences are always a run of their own on a line,
// which is what makes them findable without parsing Markdown.
const openingFence = /^(`{3,})([^`]*)$/;
const closingFence = /^(`{3,})\s*$/;

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
    const fence = openFence === 0 ? openingFence.exec(line) : closingFence.exec(line);

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
  const opening = body.findIndex((line) => openingFence.test(line));
  if (opening < 0) {
    return null;
  }

  const length = openingFence.exec(body[opening])[1].length;
  const closing = body.findIndex(
    (line, at) => at > opening && closingFence.test(line) && line.trim().length >= length,
  );

  return body.slice(opening + 1, closing < 0 ? body.length : closing).join("\n");
}

// JSON laid out to be read. The record holds what the tool returned or what the hub sent, byte for
// byte, and that is a single line with every newline and every non-ASCII character escaped — correct
// in the file and close to unreadable on a screen. Laid out here and only here: the file is untouched
// and the endpoint still serves it unaltered.
//
// A string holding newlines is written under its key as a block rather than on one escaped line,
// because that string is usually the whole point — a wiki page a tool returned.
function asReadableJson(text) {
  let value;
  try {
    value = JSON.parse(text);
  } catch {
    // Not JSON. Whatever it is, it goes on the screen as it stands.
    return null;
  }

  // A bare string or number gains nothing from being laid out, and a bare string that happens to
  // parse — `"42"` — would come back with its quotes stripped, which is not what the record holds.
  if (value === null || typeof value !== "object") {
    return null;
  }

  return laidOut(value, "");
}

function laidOut(value, indent) {
  const inner = `${indent}  `;

  if (Array.isArray(value)) {
    return value.length === 0
      ? "[]"
      : `[\n${value.map((v) => `${inner}${laidOut(v, inner)}`).join(",\n")}\n${indent}]`;
  }

  if (value !== null && typeof value === "object") {
    const keys = Object.keys(value);

    return keys.length === 0
      ? "{}"
      : `{\n${keys
          .map((k) => `${inner}${JSON.stringify(k)}: ${laidOut(value[k], inner)}`)
          .join(",\n")}\n${indent}}`;
  }

  if (typeof value === "string" && value.includes("\n")) {
    // The text itself, one line per line, indented under its key. Its escapes are already gone —
    // `JSON.parse` undid them — so an umlaut is an umlaut again.
    return `\n${value
      .split("\n")
      .map((line) => `${inner}${line}`)
      .join("\n")}`;
  }

  return JSON.stringify(value);
}

// The head, as something to read rather than as pipes and dashes. The record writes the run's frame as
// a two-column table, which is right for a file — an editor renders it — and is noise on a screen that
// shows the file as text.
//
// This reads the record's own shape and nothing else: a row is a line that starts and ends with a bar,
// and the row of dashes under the header is skipped. It is not a Markdown renderer, which DEC-019
// rules out; it is the same kind of reading `split` already does for the segments, and anything it
// does not recognise is left as the text it is.
function framed(text) {
  const lines = text.split("\n");
  const rows = lines
    .filter((line) => line.startsWith("|") && line.endsWith("|"))
    .map((line) => line.slice(1, -1).split("|").map((cell) => cell.trim()))
    .filter((cells) => cells.length === 2 && !cells.every((cell) => /^-+$/.test(cell)));

  if (rows.length === 0) {
    // Nothing that reads as one of the record's tables. Whatever it is, it goes on the screen as the
    // text it is.
    const asText = document.createElement("div");
    asText.className = "prose";
    asText.textContent = text.trim();
    return [asText];
  }

  const shown = [];
  const before = lines.filter((line) => !line.startsWith("|")).join("\n").trim();

  if (before.length > 0) {
    const title = document.createElement("div");
    title.className = "frame-title";
    title.textContent = before;
    shown.push(title);
  }

  const table = document.createElement("table");
  table.className = "frame-table";

  for (const [name, value] of rows) {
    const row = document.createElement("tr");

    const key = document.createElement("th");
    key.scope = "row";
    key.textContent = name;

    const held = document.createElement("td");
    held.textContent = value;

    row.append(key, held);
    table.append(row);
  }

  shown.push(table);
  return shown;
}

// A fenced block, folded. One per call: its arguments, and what it returned.
function folded(label, content) {
  const block = document.createElement("details");
  block.className = "result";

  const summary = document.createElement("summary");
  const lines = content.split("\n").length;
  summary.textContent = `${label} — ${lines} ${lines === 1 ? "line" : "lines"}`;

  const text = document.createElement("pre");
  text.textContent = asReadableJson(content) ?? content;

  block.append(summary, text);
  return block;
}

/// Which tool a segment's first line is about, or null where it is about none.
function toolOf(said) {
  if (said.startsWith("called ")) {
    return said.slice("called ".length);
  }

  return said.endsWith(" returned") ? said.slice(0, -" returned".length) : null;
}

// One element per tool call — the call and what it returned together, because they are one thing the
// run did: a question and its answer. The record keeps them as two moments in the order they happened
// (RUNS-009); pairing them is the page's reading of that, not a change to what is written.
//
// The agent's own text and what Grimoire said are prose and are simply shown. The frame's tail holds
// the same kind of two-column table the head does, and is read the same way.
function element(segment) {
  const item = document.createElement("li");

  const heading = document.createElement("div");
  heading.className = "heading";

  // The record's own first line, unchanged. A call keeps "called X" as its heading and gains the
  // answer beneath it, so an entry reads as the question it was and then what came back.
  heading.textContent = segment.heading;
  item.append(heading);

  const tool = toolOf(segment.said);

  const fenced = isFenced(segment.said) ? fencedIn(segment.body) : null;

  if (fenced === null) {
    // Prose, or one of the record's tables — the tail is a table just as the head is.
    item.append(...framed(segment.body.join("\n")));
    return item;
  }

  if (segment.said.startsWith("called ")) {
    // Named as waiting, so the result that follows knows the entry it belongs in.
    item.dataset.awaiting = tool;
  }

  item.append(folded(segment.said.startsWith("called ") ? "arguments" : "returned", fenced));
  return item;
}

// Where a result belongs: inside the entry of the call it answers, if that call is the last thing on
// the page and is still waiting. Appended to it — never replacing what is there, which is what keeps
// the scroll and an opened block where the user put them (ACCESS-006).
function resultBelongsTo(segment) {
  if (!segment.said.endsWith(" returned")) {
    return null;
  }

  const last = record.lastElementChild;
  if (last === null || last.dataset.awaiting !== toolOf(segment.said)) {
    return null;
  }

  return last;
}

async function refresh() {
  const request = ++newestRequest;

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
    // A run whose record is not there yet — or was never written at all. Said, and then asked again on
    // the next poll: the head is written as the run begins, so a page opened in that instant recovers
    // by itself (RUNS-007).
    if (request === newestRequest) {
      message.textContent = "There is no record for this run.";
    }

    return;
  }

  if (!response.ok) {
    return;
  }

  const text = await response.text();

  // An older answer than one already rendered says nothing true about the record now.
  if (request !== newestRequest) {
    return;
  }

  const { frame: head, segments } = split(text);

  // The frame is replaced rather than appended to, because it is the one part that grows in place:
  // while the run is under way it is the head alone, and the tail arrives as a segment of its own.
  frame.replaceChildren(...framed(head));

  // Appended, and only what is not already there. An element already on the page is never replaced,
  // which is what keeps the scroll where the user left it and a result they had opened open. A result
  // is appended *into* the entry of the call it answers, which is an addition and not a replacement.
  for (const segment of segments.slice(shown)) {
    const answers = resultBelongsTo(segment);

    if (answers === null) {
      record.append(element(segment));
      continue;
    }

    const fenced = fencedIn(segment.body);
    answers.append(fenced === null ? framed(segment.body.join("\n"))[0] : folded("returned", fenced));
    delete answers.dataset.awaiting;
  }

  shown = segments.length;
  message.textContent = "";
}

// How many entries of this run's record could not be written. It comes off the list, which is where
// the count lives: the figures are state and the record is prose (research.md R-06).
async function refreshMissing() {
  const request = ++newestMissingRequest;

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

  // An older answer than one already shown would put back a count that has since risen — hiding a
  // warning the newest state still calls for.
  if (request !== newestMissingRequest) {
    return;
  }

  const mine = body?.submissions?.find((s) => s.id === submission);

  // Said only where lines are actually missing. The run went on; a gap that passed for an agent doing
  // nothing would be worse than the gap (RUNS-007).
  missing.textContent = mine?.entriesLost
    ? `${mine.entriesLost} entries of this run could not be written, and are missing below.`
    : "";
}

// Once a second, the same interval the list uses: there is no push channel, and a run takes minutes,
// so a second is soon enough to feel live and rare enough to be nothing. Every poll appends only the
// segments that are not already on the page and never replaces one that is — which is what keeps the
// scroll where the user put it and a result they have opened open (ACCESS-006, research.md R-08).
const pollEveryMs = 1000;

function poll() {
  refresh();
  refreshMissing();
}

setInterval(poll, pollEveryMs);
poll();
