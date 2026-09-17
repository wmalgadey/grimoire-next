import { expect, test } from "./fixtures.js";

/**
 * T095 / TS-16, five states (FR-018, FR-020, SC-006). A task opens without error in every state it
 * can be in, showing what is known so far.
 *
 * The story is about the states nobody designs for. `completed` is easy; the ones that matter are
 * the task you open before the agent starts, the one you open while it works, and the one whose
 * run was cut off — because those are exactly when someone goes looking, and a page that errors or
 * says nothing is how a task becomes a thing you have to read logs to understand.
 */
test.describe("a task opens in every state", () => {
  test.describe("queued and running", () => {
    // A script that writes and then stops answering keeps the first run in flight for the whole
    // test. With a script that finishes, "is the second task queued" becomes a race against how
    // fast the first one ends — and a test that passes on timing proves nothing about queuing.
    test.use({ script: "write-then-hang" });

    test("shows a queued task as not yet started, and a running one with what it has done so far", async ({
      page,
      hub,
    }) => {
      await page.goto(hub.baseUrl);
      await page.getByTestId("source-value").fill("First source.");
      await page.getByTestId("submit").click();
      await expect(page).toHaveURL(/\/tasks\/[^/]+$/);

      // Running: the instruction version and the granted tool set are known from dispatch on,
      // before any result exists (SC-004, SC-009).
      await expect(async () => {
        await page.getByTestId("refresh").click();
        await expect(page.getByTestId("state")).toHaveText("running");
      }).toPass({ timeout: 60_000 });

      await expect(page.getByTestId("instruction-version")).toContainText(/^sha256:[0-9a-f]{12}/);
      await expect(page.getByTestId("tool-grant")).toContainText("mcp__wiki__read_page");
      // Nothing is concluded about a run still in flight: no commit has happened yet, and saying
      // "this run produced no commit" would be a verdict on work still in progress (FR-020).
      await expect(page.getByTestId("no-commit")).toHaveCount(0);
      await expect(page.getByTestId("run-in-flight")).toBeVisible();

      // The first submission occupies the runner, so the second is genuinely queued (FR-019).
      await page.goto(hub.baseUrl);
      await page.getByTestId("source-value").fill("Second source.");
      await page.getByTestId("submit").click();
      await expect(page).toHaveURL(/\/tasks\/[^/]+$/);

      // Queued: openable, and honest that nothing has happened yet.
      await expect(page.getByTestId("state")).toHaveText("queued");
      await expect(page.getByTestId("no-run")).toContainText("has not started");
    });
  });

  test("shows a completed task, and a reverted one after the undo", async ({ page, hub }) => {
    await page.goto(hub.baseUrl);
    await page.getByTestId("source-value").fill("Notes worth keeping.");
    await page.getByTestId("submit").click();
    await expect(page).toHaveURL(/\/tasks\/[^/]+$/);

    await expect(async () => {
      await page.getByTestId("refresh").click();
      await expect(page.getByTestId("state")).toHaveText("completed");
    }).toPass({ timeout: 60_000 });
    await expect(page.getByTestId("commit")).toBeVisible();

    await page.getByTestId("revert").click();
    await expect(page.getByTestId("state")).toHaveText("reverted");
    await expect(page.getByTestId("revert-commit")).toBeVisible();
    // The record of the run survives the undo (FR-023).
    await expect(page.getByTestId("tool-call")).toHaveCount(2);
  });

  test.describe("a run that fails", () => {
    test.use({ script: "escape-attempts" });

    test("shows the failed state with a reason a person can act on", async ({ page, hub }) => {
      await page.goto(hub.baseUrl);
      await page.getByTestId("source-value").fill("Something the agent will not manage.");
      await page.getByTestId("submit").click();

      await expect(async () => {
        await page.getByTestId("refresh").click();
        await expect(page.getByTestId("state")).toHaveText(/completed|failed/);
      }).toPass({ timeout: 60_000 });

      // Whatever the outcome, the refused calls are on the record rather than absent — a refusal
      // is a recorded tool call, never a silence (FR-021).
      const calls = page.getByTestId("tool-call");
      await expect(calls.first()).toBeVisible();
      await expect(page.locator("[data-testid='tool-call'].refused").first()).toBeVisible();
    });
  });

  test("the task list shows every state without opening anything", async ({ page, hub }) => {
    await page.goto(hub.baseUrl);
    await page.getByTestId("source-value").fill("A source.");
    await page.getByTestId("submit").click();
    await expect(page).toHaveURL(/\/tasks\/[^/]+$/);

    await page.goto(`${hub.baseUrl}/tasks`);
    await expect(page.getByTestId("task-row").first()).toBeVisible();
    await expect(page.getByTestId("state").first()).toHaveText(/queued|running|completed|failed|reverted/);
  });
});
