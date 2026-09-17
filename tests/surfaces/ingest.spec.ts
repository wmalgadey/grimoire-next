import { expect, test } from "./fixtures.js";

/**
 * T056 / TS-16, US1 flow. Submit → task list → task view, against the built SvelteKit app and a
 * real hub.
 *
 * The acceptance criterion is the one the story states: the task view <b>alone</b> must answer
 * which instruction version ran, what the agent looked at, what it wrote, and what the wiki looks
 * like now versus before — without opening a terminal or the repository.
 */
test.describe("submit, run, inspect", () => {
  test("submitting pasted text creates one task and opens it", async ({ page, hub }) => {
    await page.goto(hub.baseUrl);

    await page.getByTestId("source-value").fill("Notes worth keeping in the wiki.");
    await page.getByTestId("submit").click();

    // The task is openable from the moment it is created (FR-002, SC-001).
    await expect(page).toHaveURL(/\/tasks\/[^/]+$/);
    await expect(page.getByTestId("task-id")).toBeVisible();
  });

  test("an empty submission is refused and creates no task", async ({ page, hub }) => {
    await page.goto(hub.baseUrl);

    await page.getByTestId("source-value").fill("   ");
    await expect(page.getByTestId("submit")).toBeDisabled();

    await page.goto(`${hub.baseUrl}/tasks`);
    await expect(page.getByTestId("empty")).toBeVisible();
  });

  test("the task view shows everything the story asks of it", async ({ page, hub }) => {
    await page.goto(hub.baseUrl);
    await page.getByTestId("source-value").fill("Notes worth keeping in the wiki.");
    await page.getByTestId("submit").click();
    await expect(page).toHaveURL(/\/tasks\/[^/]+$/);

    await expect(async () => {
      await page.getByTestId("refresh").click();
      await expect(page.getByTestId("state")).toHaveText("completed");
    }).toPass({ timeout: 60_000 });

    // Which instruction version ran — shown as `sha256:` plus the first 12 hex (SC-009).
    await expect(page.getByTestId("instruction-version")).toContainText(/^sha256:[0-9a-f]{12}/);

    // The granted tool set, deny-by-default and recorded (SC-004).
    const grant = page.getByTestId("tool-grant");
    await expect(grant).toContainText("mcp__wiki__read_page");
    await expect(grant).toContainText("mcp__wiki__write_page");

    // What the agent looked at, and what it wrote, in order (SC-010).
    const calls = page.getByTestId("tool-call");
    await expect(calls).toHaveCount(2);
    await expect(calls.nth(0)).toContainText("mcp__wiki__read_page");
    await expect(calls.nth(1)).toContainText("mcp__wiki__write_page");

    // What the wiki looks like now versus before (SC-002, SC-008).
    await expect(page.getByTestId("commit")).toBeVisible();
    await expect(page.getByTestId("file-diff")).toContainText("topics/scripted.md");
  });

  test("the task list shows every retained task, newest first, each openable", async ({ page, hub }) => {
    await page.goto(hub.baseUrl);
    await page.getByTestId("source-value").fill("First source.");
    await page.getByTestId("submit").click();
    await expect(page).toHaveURL(/\/tasks\/[^/]+$/);

    await page.goto(hub.baseUrl);
    await page.getByTestId("source-value").fill("Second source.");
    await page.getByTestId("submit").click();
    await expect(page).toHaveURL(/\/tasks\/[^/]+$/);

    await page.goto(`${hub.baseUrl}/tasks`);
    const rows = page.getByTestId("task-row");
    await expect(rows).toHaveCount(2);
    // Newest first (FR-030).
    await expect(rows.nth(0)).toContainText("Second source.");
    await expect(rows.nth(1)).toContainText("First source.");

    await rows.nth(0).click();
    await expect(page.getByTestId("task-id")).toBeVisible();
  });

  test.describe("a run that changed nothing", () => {
    test.use({ script: "read-only" });

    test("says so explicitly rather than showing an empty diff", async ({ page, hub }) => {
      await page.goto(hub.baseUrl);
      await page.getByTestId("source-value").fill("Something the wiki already covers.");
      await page.getByTestId("submit").click();

      await expect(async () => {
        await page.getByTestId("refresh").click();
        await expect(page.getByTestId("state")).toHaveText("completed");
      }).toPass({ timeout: 60_000 });

      // Completed, not failed, and stated in words (FR-016, SC-011).
      await expect(page.getByTestId("changed-nothing")).toBeVisible();
      // The record still shows that the agent looked, which is what tells a deliberate no-op
      // from a broken run.
      await expect(page.getByTestId("tool-call")).toHaveCount(1);
      await expect(page.getByTestId("revert-reason")).toContainText("nothing to undo");
    });
  });
});
