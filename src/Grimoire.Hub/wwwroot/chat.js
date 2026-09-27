// The browser half of ACCESS-007 and ACCESS-008: one conversation, sent as it happens. No build step
// and no framework (DEC-019).
//
// The rule the whole file is written around: **an element once drawn is never replaced.** A turn is
// drawn when its question arrives and only ever written into afterwards, the answer grows by appending
// to the text node that is already there, and a step is added after the last one. That is what keeps
// the scroll where the user put it, a step they have opened open, and text they are reading where they
// are reading it (ACCESS-007, docs/ux.md: live content grows in place).

const form = document.getElementById("question");
const text = document.getElementById("text");
const message = document.getElementById("message");
const chat = document.getElementById("chat");
const total = document.getElementById("total");

// The cost ceiling, as the last snapshot said it. Nothing is drawn against a ceiling this page
// invented: it comes with the chat because it is the hub's value and not the browser's — the owner
// revises it in `Ceilings.Fixed` (GUARD-004).
let costCeiling = 0;

// What the four states read as. The wire names are the contract's; these are what a person reads
// (ACCESS-007, contracts/hub-http-api.md).
const states = {
  waiting: "waiting its turn",
  answering: "being answered",
  answered: "answered",
  "no-answer": "got no answer",
};

function show(kind, words) {
  message.dataset.kind = kind;
  message.textContent = words;
}

form.addEventListener("submit", async (event) => {
  event.preventDefault();
  show("pending", "Asking…");

  let response;
  try {
    response = await fetch("/api/chat/questions", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ text: text.value }),
    });
  } catch {
    show("refused", "Grimoire could not be reached.");
    return;
  }

  const body = await response.json().catch(() => null);

  // 202 means the question is accepted and a run will answer it; the user waits for none of it
  // (QUERY-001). Nothing is drawn from this answer: the chat is what draws the question, and it is
  // sent the turn — so a question drawn here would be drawn twice, once per source.
  if (response.status === 202) {
    text.value = "";
    show("accepted", "Question asked.");
    return;
  }

  // 422 names which of the three it was, and carries a message written for the person who asked
  // (QUERY-003). There is no refusal for a run being in progress: a question asked while something
  // else runs is accepted and waits its turn (RUNS-002).
  show("refused", body?.message ?? "The question was refused.");
});

// UTC to the second, as every other time Grimoire shows is.
function whenAsked(askedAt) {
  return `${new Date(askedAt).toISOString().slice(0, 19).replace("T", " ")} UTC`;
}

// Whole numbers with a thin space between the thousands, which is what makes 2 004 118 readable at a
// glance. Never currency: what a run costs is counted in input-token equivalents, which have no unit
// of their own (DEC-015, GUARD-004).
function figure(number) {
  return number.toLocaleString("en-GB").replace(/,/g, " ");
}

// One turn, drawn once. Every part it can ever need exists from the start except the steps, which are
// added as they happen — so nothing below is ever rebuilt to make room for something new.
function turnFor(turn) {
  const existing = chat.querySelector(`li[data-id="${turn.id}"]`);
  if (existing) {
    return existing;
  }

  const item = document.createElement("li");
  item.dataset.id = turn.id;

  // textContent, never innerHTML: this is the user's own question coming back, and the server sends it
  // as it was given.
  const asked = document.createElement("p");
  asked.className = "asked";
  asked.textContent = turn.text;

  const about = document.createElement("p");
  about.className = "about";

  const when = document.createElement("time");
  when.dateTime = turn.askedAt;
  when.textContent = whenAsked(turn.askedAt);

  const state = document.createElement("span");
  state.className = "state";

  // Why it got no answer, and empty in every other case (QUERY-006's half of what the chat shows).
  const because = document.createElement("span");
  because.className = "because";

  // What this question's run has spent, against the ceiling it is held to: "x / y". No unit beside it,
  // because the quantity has none, and the ceiling is what makes the bare number mean something —
  // 12 000 of 2 000 000 has spent almost nothing, and 12 000 alone says neither that nor the opposite
  // (ACCESS-008, GUARD-004).
  const cost = document.createElement("span");
  cost.className = "figure cost";

  about.append(when, state, because, cost);

  // The answer's element and its text node both exist from the start, so that the first piece of the
  // answer is appended to a node that is already on the page rather than replacing one.
  const answer = document.createElement("p");
  answer.className = "answer";
  answer.append(document.createTextNode(""));

  item.append(asked, about, answer);
  chat.append(item);
  return item;
}

// The answer grows by **appending to the text node that is there** — not by assigning textContent,
// which replaces the node and takes the user's selection and the reader's place with it (ACCESS-007).
function answerGrew(item, append) {
  item.querySelector(".answer").firstChild.appendData(append);
}

// The steps, in the shape a run's record is read in: shut by default, one openable at a time, and the
// call and what it returned whole (ACCESS-006, ACCESS-007).
function stepsOf(item) {
  const drawn = item.querySelector(":scope > details.steps");
  if (drawn) {
    return drawn;
  }

  const steps = document.createElement("details");
  steps.className = "steps";
  steps.append(document.createElement("summary"));
  item.append(steps);
  return steps;
}

function stepHappened(item, step) {
  const steps = stepsOf(item);

  const drawn = document.createElement("details");
  drawn.className = "step";

  const summary = document.createElement("summary");

  // "called read_page" and "read_page returned", which is how the record's own segments read — one
  // format to learn rather than two (ACCESS-006, contracts/run-record.md).
  summary.textContent =
    step.kind === "called" ? `called ${step.tool ?? ""}`.trim() : `${step.tool ?? ""} returned`.trim();

  const content = document.createElement("pre");
  content.textContent = step.content ?? "";

  drawn.append(summary, content);

  // Appended after the last step, inside a fold that is itself only written into — so a step arriving
  // leaves every step above it, and any the user has opened, exactly where they were.
  steps.append(drawn);

  // The fold says how many there are, which is what lets the user see something happened without
  // opening it. Singular where there is one: "1 steps" is the kind of thing a reader trips over, and
  // the first step of every answer would say it.
  const many = steps.querySelectorAll(":scope > details.step").length;

  steps.querySelector(":scope > summary").textContent = `${many} ${many === 1 ? "step" : "steps"}`;
}

// A question's state, its reason and its figure. Written, never rebuilt: each has an element of its
// own, so a figure rising changes a number and nothing else (ACCESS-007, ACCESS-008).
function questionChanged(item, turn) {
  textOf(item.querySelector(".state"), states[turn.state] ?? turn.state);
  textOf(item.querySelector(".because"), turn.because ?? "");

  // Absent where there is no run — and not a zero, which would claim a run that spent nothing rather
  // than no run at all (ACCESS-008).
  textOf(
    item.querySelector(".cost"),
    turn.costSpent === undefined || turn.costSpent === null
      ? ""
      : `${figure(turn.costSpent)} / ${figure(costCeiling)}`,
  );
}

function textOf(element, words) {
  // Assigned only where it has actually changed. Writing the same text back is one more DOM mutation
  // for nothing, and the point of writing in place is to make none.
  if (element.textContent !== words) {
    element.textContent = words;
  }
}

// What every question in this chat has spent altogether, **with no ceiling beside it**: each question
// carries its own, and "x / y" here would invent one that does not exist (ACCESS-008).
function spent(figures) {
  textOf(total, figure(figures));
}

// The whole chat, drawn. The snapshot every stream opens with, and what a reconnection is answered
// with — so this has to be able to draw a chat that is partly on the page already: `turnFor` returns
// the element that is there, and everything below writes into it rather than replacing it
// (ACCESS-007).
function drawn(body) {
  costCeiling = body.costCeiling;

  for (const turn of body.turns) {
    const item = turnFor(turn);

    // The answer, brought up to what the snapshot says it is. Appended rather than assigned, for the
    // reason `answerGrew` exists: after a reconnection the page may already hold the beginning of it.
    const held = item.querySelector(".answer").firstChild;
    if (turn.answer.startsWith(held.data)) {
      answerGrew(item, turn.answer.slice(held.data.length));
    } else {
      // The answer on the page is not a beginning of the one the snapshot carries, which nothing in
      // this feature produces — an answer only grows. Replaced rather than guessed at: what the
      // snapshot says is what the chat holds.
      held.replaceWith(document.createTextNode(turn.answer));
    }

    const already = item.querySelectorAll(":scope > details.steps > details.step").length;
    for (const step of turn.steps.slice(already)) {
      stepHappened(item, step);
    }

    questionChanged(item, turn);
  }

  spent(body.total);
}

// **Nothing polls.** The chat arrives as it changes: the stream opens with the whole of it and then
// carries the one thing that changed (contracts/hub-http-api.md, research.md R-01).
//
// A dropped connection is `EventSource`'s own to make again, and it opens with a fresh snapshot — which
// is what ACCESS-007's last clause asks for: the chat as it then stands, including what arrived while
// the browser was away. Nothing here reconnects, replays or reads a `Last-Event-ID`.
const events = new EventSource("/api/chat/events");

events.addEventListener("chat", (event) => drawn(JSON.parse(event.data)));

events.addEventListener("asked", (event) => {
  const turn = JSON.parse(event.data);

  // A question the user asked in this tab, or in another one: there is one chat and every browser
  // reads it (QUERY-005).
  questionChanged(turnFor(turn), turn);
});

events.addEventListener("answer", (event) => {
  const { id, append } = JSON.parse(event.data);
  const item = chat.querySelector(`li[data-id="${id}"]`);

  if (item) {
    answerGrew(item, append);
  }
});

events.addEventListener("step", (event) => {
  const { id, step } = JSON.parse(event.data);
  const item = chat.querySelector(`li[data-id="${id}"]`);

  if (item) {
    stepHappened(item, step);
  }
});

events.addEventListener("question", (event) => {
  const turn = JSON.parse(event.data);
  const item = chat.querySelector(`li[data-id="${turn.id}"]`);

  if (item) {
    questionChanged(item, turn);
  }

  // The total comes with the question whose spend moved it, so the two are never drawn a beat apart
  // (ACCESS-008).
  spent(turn.total);
});
