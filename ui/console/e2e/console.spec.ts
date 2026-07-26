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
    // This host composes jobs + bulk; the other three surfaces answer 404, so no panels.
    await expect(page.getByRole("button", { name: "Bulk intake" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Campaigns" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Archival" })).toHaveCount(0);
  });

  test("triggers a job, watches the run finish, and replays its repair item", async ({ page }) => {
    await page.goto(`/?base=${encodeURIComponent(service)}`);

    // The fleet appears because the executor exists (zero-config discovery).
    await expect(page.getByRole("button", { name: /console-smoke/ })).toBeVisible();

    // Scope the verb to ITS job row: this fleet also carries the bulk validate/execute
    // jobs, so an unscoped "trigger" would be three buttons and one ambiguous click.
    const smokeJob = page.locator("li", { hasText: "SmokeJob" });
    await smokeJob.getByRole("button", { name: "trigger" }).click();
    const dialog = page.getByRole("alertdialog");
    await expect(dialog).toContainText("audited");
    await dialog.getByRole("button", { name: "trigger" }).click();
    await expect(page.getByRole("status")).toBeVisible();

    // The run appears and reaches a terminal state — polled through the real API.
    const smokeRun = page.getByRole("row", { name: /SmokeJob/ }).first();
    await expect(async () => {
      await page.goto(`/?base=${encodeURIComponent(service)}`);
      await expect(smokeRun).toContainText("Completed", { timeout: 5_000 });
    }).toPass({ timeout: 60_000 });

    await smokeRun.getByRole("button").first().click();
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

  test("uploads a batch, reads the real validation report, and works the four-eyes gate", async ({ page }) => {
    const openBulk = async () => {
      await page.goto(`/?base=${encodeURIComponent(service)}`);
      await page.getByRole("button", { name: "Bulk intake" }).click();
      await expect(page.getByTestId("bulk-panel")).toBeVisible();
    };

    const upload = async (name: string, csv: string) => {
      await page.getByLabel("batch file").setInputFiles({ name, mimeType: "text/csv", buffer: Buffer.from(csv) });
      await page.getByRole("button", { name: "upload" }).click();
      const dialog = page.getByRole("alertdialog", { name: "confirm upload" });
      await expect(dialog).toContainText(`Upload ${name} into payments?`);
      await dialog.getByRole("button", { name: "upload" }).click();
      await expect(page.getByText(/Received — batch .* is queued for validation/)).toBeVisible();
    };

    // The newest batch is the first row (the contract orders by receipt, newest first).
    const newest = page.getByRole("row", { name: /payments/ }).first();
    const waitForState = async (state: string) =>
      expect(async () => {
        await openBulk();
        // Read the state from the batch ROW: the filter's <option> list carries every
        // state name too, so an unscoped text match would pass before anything happened.
        await expect(newest).toContainText(state, { timeout: 5_000 });
      }).toPass({ timeout: 60_000 });

    // ── act 1: a file with a structurally broken line
    await openBulk();
    await upload("payments.csv", "EndToEndId,Amount\nE2E-1,10.00\nE2E-2,20.00\nE2E-3,30.00,stray\n");
    await waitForState("Validated");

    await newest.getByRole("button").first().click();
    const detail = page.getByTestId("batch-detail");
    // The report the ENGINE wrote — the broken line, named by its own message.
    await expect(detail).toContainText("expected 2 fields");

    // The gate refuses to fire without evidence.
    await page.getByRole("button", { name: "reject" }).click();
    const rejectDialog = page.getByRole("alertdialog", { name: "confirm reject" });
    await expect(rejectDialog.getByRole("button", { name: "reject" })).toBeDisabled();
    await rejectDialog.getByRole("button", { name: "cancel" }).click();

    // And the ENGINE refuses this approval: invalid rows block it. The console shows the
    // refusal verbatim — that message is how the operator learns what to do next.
    await page.getByRole("button", { name: "approve" }).click();
    const blocked = page.getByRole("alertdialog", { name: "confirm approve" });
    await expect(blocked).toContainText("audited");
    await blocked.getByRole("button", { name: "approve" }).click();
    await expect(page.getByText(/1 invalid rows block approval/)).toBeVisible();

    // ── act 2: the operator rejects it, with the reason the contract demands
    await page.getByRole("button", { name: "reject" }).click();
    const rejecting = page.getByRole("alertdialog", { name: "confirm reject" });
    await rejecting.getByLabel("reason (required)").fill("line 3 is malformed — resend the export");
    await rejecting.getByRole("button", { name: "reject" }).click();
    await waitForState("Rejected");

    await newest.getByRole("button").first().click();
    await expect(detail).toContainText("line 3 is malformed — resend the export");

    // ── act 3: the fixed file passes the gate, and the decision becomes a server fact
    await openBulk();
    await upload("payments-fixed.csv", "EndToEndId,Amount\nE2E-4,10.00\nE2E-5,20.00\n");
    await waitForState("Validated");

    await newest.getByRole("button").first().click();
    await page.getByRole("button", { name: "approve" }).click();
    const approving = page.getByRole("alertdialog", { name: "confirm approve" });
    await approving.getByLabel("note (optional)").fill("checked against the ledger");
    await approving.getByRole("button", { name: "approve" }).click();
    await expect(page.getByText("approved", { exact: true })).toBeVisible();

    await waitForState("Approved");
    await newest.getByRole("button").first().click();
    await expect(detail).toContainText("checked against the ledger");
  });
});
