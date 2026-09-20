// The browser half of ACCESS-001: one form, posted with fetch. No build step and no framework —
// the browser surface is one form and, with T036, one list of states (research.md R-10).

const form = document.getElementById("submission");
const text = document.getElementById("text");
const message = document.getElementById("message");

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
    return;
  }

  // 422 names what was wrong with the submission, 409 that a run is already in progress. Both
  // carry a message written for the person who submitted (contracts/hub-http-api.md).
  show("refused", body?.message ?? "The submission was refused.");
});
