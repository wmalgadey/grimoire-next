// The browser half of ACCESS-001 and ACCESS-002: one form, posted with fetch, and one list of
// states, polled. No build step and no framework — the browser surface is those two things and
// nothing more (research.md R-10).

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
    show("accepted", "Submission accepted. A run is under way.");
    refresh();
    return;
  }

  // 422 names what was wrong with the submission, 409 that a run is already in progress. Both
  // carry a message written for the person who submitted (contracts/hub-http-api.md).
  show("refused", body?.message ?? "The submission was refused.");
});

// UTC to the minute. The wiki's own times are UTC, and a submission is placed by the hour it was
// made rather than by the second.
function whenSubmitted(submittedAt) {
  return `${new Date(submittedAt).toISOString().slice(0, 16).replace("T", " ")} UTC`;
}

// Each submission becomes one row: when it was made, and its state. Nothing else about the run
// is here to render — the response carries no more (ACCESS-002).
function row(submission) {
  const item = document.createElement("li");
  item.dataset.id = submission.id;

  const when = document.createElement("time");
  when.dateTime = submission.submittedAt;
  when.textContent = whenSubmitted(submission.submittedAt);

  const state = document.createElement("span");
  state.className = "state";
  state.textContent = submission.state;

  item.append(when, " ", state);
  return item;
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

  submissions.replaceChildren(...body.submissions.map(row));
}

setInterval(refresh, pollEveryMs);
refresh();
