import { expect, test } from "./fixtures.js";

/**
 * T084 / TS-16, US2 flow (FR-026, FR-027, SC-005). Revert is one action on the task view, and the
 * cases where it is not offered are explained rather than hidden.
 *
 * A disabled control tells the reader nothing. The acceptance criterion the story states is that a
 * superseded ingest says it was superseded by a later wiki commit — which is what tells someone
 * why undo reaches one ingest back and not further.
 */
test.describe("revert an ingest", () => {
  test("reverts in one action and shows the revert commit afterwards", async ({ page, hub }) => {
    await page.goto(hub.baseUrl);
    await page.getByTestId("source-value").fill("Notes worth keeping in the wiki.");
    await page.getByTestId("submit").click();
    await expect(page).toHaveURL(/\/tasks\/[^/]+$/);

    await expect(async () => {
      await page.getByTestId("refresh").click();
      await expect(page.getByTestId("state")).toHaveText("completed");
    }).toPass({ timeout: 60_000 });

    // The run changed the wiki, so the action is offered (FR-024).
    await expect(page.getByTestId("commit")).toBeVisible();
    await page.getByTestId("revert").click();

    // Zero manual steps: no confirmation dialog, no second page, no git command (SC-005).
    await expect(page.getByTestId("state")).toHaveText("reverted");
    await expect(page.getByTestId("revert-commit")).toBeVisible();
    await expect(page.getByTestId("revert-commit")).toContainText(/[0-9a-f]{12}/);

    // A reverted task offers no revert, and says why in words.
    await expect(page.getByTestId("revert")).toHaveCount(0);
    await expect(page.getByTestId("revert-reason")).toContainText("already been reverted");

    // The record of what the agent did survives the undo (FR-023).
    await expect(page.getByTestId("tool-call")).toHaveCount(2);
    await expect(page.getByTestId("file-diff")).toBeVisible();
  });

  test("a superseded ingest explains itself rather than showing a disabled control", async ({
    page,
    hub,
  }) => {
    await page.goto(hub.baseUrl);
    await page.getByTestId("source-value").fill("First source.");
    await page.getByTestId("submit").click();
    await expect(page).toHaveURL(/\/tasks\/[^/]+$/);
    const first = page.url();

    await expect(async () => {
      await page.getByTestId("refresh").click();
      await expect(page.getByTestId("state")).toHaveText("completed");
    }).toPass({ timeout: 60_000 });

    // Any later wiki commit supersedes the ingest, whoever made it.
    hub.wiki.commit("topics/edited-by-hand.md", "# Edited\n", "a later change to the wiki");

    await page.goto(first);
    await expect(page.getByTestId("revert")).toHaveCount(0);
    // The words the story asks for, not a greyed-out button (FR-027).
    const reason = page.getByTestId("revert-reason");
    await expect(reason).toContainText("superseded by a later wiki commit");
    await expect(reason).toContainText("one ingest back");
  });

  test.describe("a run that produced no commit", () => {
    test.use({ script: "read-only" });

    test("says there is nothing to undo", async ({ page, hub }) => {
      await page.goto(hub.baseUrl);
      await page.getByTestId("source-value").fill("Something the wiki already covers.");
      await page.getByTestId("submit").click();

      await expect(async () => {
        await page.getByTestId("refresh").click();
        await expect(page.getByTestId("state")).toHaveText("completed");
      }).toPass({ timeout: 60_000 });

      await expect(page.getByTestId("revert")).toHaveCount(0);
      await expect(page.getByTestId("revert-reason")).toContainText("nothing to undo");
    });
  });
});
