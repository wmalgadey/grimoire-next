// The browser half of ACCESS-001 and ACCESS-005: one form, posted with fetch, and one list of
// states and figures, polled. No build step and no framework (research.md R-10).

const form = document.getElementById("submission");
const text = document.getElementById("text");
const message = document.getElementById("message");
const submissions = document.getElementById("submissions");

// How often the list asks. There is no push channel (contracts/hub-http-api.md), and a run takes
// minutes, so a second is soon enough to feel live and rare enough to be nothing.
const pollEveryMs = 1000;

// Two refreshes can be in flight at once — the interval's and the one an accepted submission
// starts — and they can answer out of order. The newest request's answer is the current one; an
// older answer arriving after it would put a state back that has already moved on.
let newestRequest = 0;

function show(kind, words) {
  message.dataset.kind = kind;
  message.textContent = words;
}

form.addEventListener("submit", async (event) => {
  event.preventDefault();
  show("pending", "Submitting…");

  let response;
  try {
    response = await fetch("/api/submissions", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ text: text.value }),
    });
  } catch {
    show("refused", "Grimoire could not be reached.");
    return;
  }

  const body = await response.json().catch(() => null);

  // 202 means the submission is accepted and a run is under way; the user waits for none of it.
  if (response.status === 202) {
    text.value = "";
    show("accepted", "Submission accepted.");
    refresh();
    return;
  }

  // 422 names what was wrong with the submission, and carries a message written for the person
  // who submitted it. There is no refusal for a run being in progress: a text submitted while one
  // is under way is accepted and waits its turn (RUNS-002, contracts/hub-http-api.md).
  show("refused", body?.message ?? "The submission was refused.");
});

// UTC to the second. The wiki's own times are UTC too. To the second rather than to the minute,
// because the queue makes two submissions in one minute ordinary — and two texts that open with
// the same words would then be one row repeated, which is the opposite of what ACCESS-004 asks
// the time and the opening to do.
function whenSubmitted(submittedAt) {
  return `${new Date(submittedAt).toISOString().slice(0, 19).replace("T", " ")} UTC`;
}

// Whole numbers with a thin space between the thousands, which is what makes 2 004 118 readable at
// a glance. Tokens are the same quantity the cost ceiling counts and are never currency (DEC-015).
function figure(tokens) {
  return tokens.toLocaleString("en-GB").replace(/,/g, "\u2009");
}

// Each submission is one row: when it was made, the opening of the text, its state, and — where it
// has a run — that run's model, the tokens it has spent and the tool calls it has made (ACCESS-005).
// The opening is what lets the user tell one row from another, and which text a failed run was
// working on (ACCESS-004).
//
// **Rows are updated in place**, keyed by the submission's id, and never rebuilt. The list used to
// call replaceChildren every second, which was harmless while a row said one word; with two growing
// numbers it is not, and it also replaced the Acknowledge button under the user's finger once a
// second (research.md R-09).
function rowFor(submission) {
  const existing = submissions.querySelector(`li[data-id="${submission.id}"]`);
  if (existing) {
    return existing;
  }

  const item = document.createElement("li");
  item.dataset.id = submission.id;

  const when = document.createElement("time");
  when.dateTime = submission.submittedAt;
  when.textContent = whenSubmitted(submission.submittedAt);

  // textContent, never innerHTML: this is the user's own text coming back, and the server sends it as
  // it was given.
  const excerpt = document.createElement("span");
  excerpt.className = "excerpt";
  excerpt.textContent = submission.excerpt;

  const state = document.createElement("span");
  state.className = "state";

  item.append(when, " ", excerpt, " ", state);
  submissions.append(item);
  return item;
}

// One element added once and then only ever written to, so that a rising figure changes a number and
// nothing else. A submission with no run gets none of them at all — not zeros (ACCESS-005).
function ensureRunParts(item, submission) {
  if (item.querySelector(".model")) {
    return;
  }

  const model = document.createElement("span");
  model.className = "model";
  model.textContent = submission.model;

  const tokens = document.createElement("span");
  tokens.className = "figure tokens";

  const calls = document.createElement("span");
  calls.className = "figure calls";

  // The record is a second job, so it is a page of its own — which gives the back button and a
  // shareable URL for nothing (ACCESS-006, research.md R-08). The link carries the submission's
  // identifier, never the run's.
  const open = document.createElement("a");
  open.className = "open-run";
  open.href = `run.html?submission=${submission.id}`;
  open.textContent = "Open";

  item.append(" ", model, " ", tokens, " ", calls, " ", open);
}

function textOf(element, words) {
  // Assigned only where it has actually changed. Writing the same text back is one more DOM mutation
  // a second for every row on the page, and the point of updating in place is to make none.
  if (element.textContent !== words) {
    element.textContent = words;
  }
}

function update(item, submission) {
  textOf(item.querySelector(".state"), submission.state);

  if (submission.model !== undefined) {
    ensureRunParts(item, submission);
    textOf(item.querySelector(".tokens"), figure(submission.tokensUsed));
    textOf(item.querySelector(".calls"), figure(submission.toolCalls));
  }

  // Lines of this run's record could not be written. The run went on; saying so is what keeps the gap
  // from passing for an agent that did nothing (RUNS-007).
  if (submission.entriesLost !== undefined) {
    let missing = item.querySelector(".missing");
    if (!missing) {
      missing = document.createElement("span");
      missing.className = "missing";
      item.append(" ", missing);
    }

    textOf(missing, `${figure(submission.entriesLost)} entries missing`);
  }

  // One control, and only on the row whose failure is still waiting to be acknowledged. A row without
  // it offers nothing: a control that did nothing would be a lie to the user (ACCESS-003). Added and
  // removed rather than rebuilt, so it is not replaced under the user's finger.
  const offered = item.querySelector(".acknowledge");

  if (submission.awaitingAcknowledgement && !offered) {
    const acknowledge = document.createElement("button");
    acknowledge.type = "button";
    acknowledge.className = "acknowledge";
    acknowledge.textContent = "Acknowledge";
    acknowledge.addEventListener("click", () => acknowledged(submission.id));
    item.append(" ", acknowledge);
  } else if (!submission.awaitingAcknowledgement && offered) {
    offered.remove();
  }
}

// Newest first, which is the order the server sends. Only ever reordered where the order has actually
// changed — which is when a submission is added — because moving an element is a mutation too.
function reorder(wanted) {
  const here = [...submissions.children].map((item) => item.dataset.id);

  if (here.length === wanted.length && here.every((id, at) => id === wanted[at])) {
    return;
  }

  for (const id of wanted) {
    submissions.append(submissions.querySelector(`li[data-id="${id}"]`));
  }
}

// Acknowledging a failure is what lets the queue move on (RUNS-003). The list is refreshed
// straight afterwards rather than waited for: the acknowledged row still reads failed, and the
// control it offered is gone, which is the user's confirmation.
async function acknowledged(id) {
  try {
    await fetch(`/api/submissions/${id}/acknowledgement`, { method: "POST" });
  } catch {
    // Grimoire could not be reached. Nothing was acknowledged, the row still offers the control,
    // and the next poll puts back what is true.
    return;
  }

  refresh();
}

async function refresh() {
  const request = ++newestRequest;

  let response;
  try {
    // no-store, because a polled list answered from the browser's cache is a state that has
    // already moved on.
    response = await fetch("/api/submissions", { cache: "no-store" });
  } catch {
    // Grimoire could not be reached. The next poll tries again; what is on the screen stays,
    // because a state that has not been contradicted is still the last one known.
    return;
  }

  if (!response.ok) {
    return;
  }

  const body = await response.json().catch(() => null);
  if (!body || request !== newestRequest) {
    return;
  }

  for (const submission of body.submissions) {
    update(rowFor(submission), submission);
  }

  reorder(body.submissions.map((s) => s.id));
}

setInterval(refresh, pollEveryMs);
refresh();
