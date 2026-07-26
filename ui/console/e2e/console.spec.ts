import { expect, test } from "@playwright/test";

/**
 * Drives the REAL console against a REAL Goldpath app (Postgres + Quartz + the frozen
 * admin surface). Every assertion is behaviour the operator would see; nothing is stubbed.
 */
const service = process.env.GOLDPATH_SERVICE_URL ?? "http://localhost:5310";

test.describe("the run console against a real Goldpath app", () => {
  test("discovers the composed capability and hides what the app never composed", async ({ page }) => {
    await page.goto(`/?base=${encodeURIComponent(service)}`);

    await expect(page.getByRole("button", { name: "Runs" })).toBeVisible();
    // This host composes jobs ONLY — the other four surfaces answer 404, so no panels.
    await expect(page.getByRole("button", { name: "Bulk intake" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Campaigns" })).toHaveCount(0);
  });

  test("triggers a job, watches the run finish, and replays its repair item", async ({ page }) => {
    await page.goto(`/?base=${encodeURIComponent(service)}`);

    // The fleet appears because the executor exists (zero-config discovery).
    await expect(page.getByRole("button", { name: /console-smoke/ })).toBeVisible();

    // Trigger through the confirm gate — the console never fires a verb on one click.
    await page.getByRole("button", { name: "trigger" }).click();
    const dialog = page.getByRole("alertdialog");
    await expect(dialog).toContainText("audited");
    await dialog.getByRole("button", { name: "trigger" }).click();
    await expect(page.getByRole("status")).toBeVisible();

    // The run appears and reaches a terminal state — polled through the real API.
    await expect(async () => {
      await page.goto(`/?base=${encodeURIComponent(service)}`);
      await expect(page.getByText("Completed").first()).toBeVisible({ timeout: 5_000 });
    }).toPass({ timeout: 60_000 });

    await page.locator("table button").first().click();
    const detail = page.getByTestId("run-detail");
    await expect(detail).toBeVisible();
    await expect(detail).toContainText("Completed:");            // chunk counts by status
    await expect(detail).toContainText("Repair queue (1 shown)"); // the isolated item
    await expect(detail).toContainText("the bank refused this instruction");

    // Replay the repair queue — the verb the operator reaches for after a bad night.
    await page.getByRole("button", { name: "replay-items" }).click();
    const replayDialog = page.getByRole("alertdialog");
    await expect(replayDialog).toContainText("Replay all open repair items");
    await replayDialog.getByRole("button", { name: "replay-items" }).click();
    await expect(page.getByRole("status")).toBeVisible();
  });
});
