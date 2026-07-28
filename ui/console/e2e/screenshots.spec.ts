import { expect, test } from "@playwright/test";

/**
 * The README's pictures, captured from a REAL console driving REAL Goldpath modules —
 * never a mockup. Run through `scripts/console-screenshots.sh`, which brings the stack up,
 * gives it something to show, and writes into docs/assets.
 *
 * Excluded from the normal gate (its own project in playwright.config.ts): a screenshot is
 * a document, not an assertion, and a gate that fails on pixels gets muted.
 */
const service = process.env.GOLDPATH_SHOT_SERVICE_URL ?? "http://localhost:5330";
const out = process.env.GOLDPATH_SHOT_DIR ?? "../../docs/assets";

test.use({ viewport: { width: 1280, height: 800 } });

test("the console, as an operator sees it", async ({ page }) => {
  await page.goto(`${service}/goldpath/console/`);

  // 1. TODAY — the landing screen: what is wrong, across the estate.
  await expect(page.getByTestId("triage-home")).toBeVisible();
  await expect(page.getByText(/awaiting approval/)).toBeVisible();
  await page.screenshot({ path: `${out}/console-today.png` });

  // 2. The run console — fleets discovered from the store, a run with its repair queue.
  // Since U5 the Runs section opens on the fleet OVERVIEW; the run list lives in History.
  await page.getByRole("button", { name: "Runs" }).click();
  await expect(page.getByTestId("run-console")).toBeVisible();
  await page.getByRole("tab", { name: "History" }).click();
  await page.locator("table button").first().click();
  const runDetail = page.getByTestId("run-detail");
  await expect(runDetail).toBeVisible();
  // The SUBJECT must be in frame: clicking a row scrolls, and a picture of the scroll
  // position is not a picture of the run.
  await runDetail.scrollIntoViewIfNeeded();
  await page.screenshot({ path: `${out}/console-runs.png` });

  // 3. The four-eyes gate — the engine's own validation report under it.
  await page.getByRole("button", { name: "Bulk intake" }).click();
  await expect(page.getByTestId("bulk-panel")).toBeVisible();
  await page.locator("table button").first().click();
  const batchDetail = page.getByTestId("batch-detail");
  await expect(batchDetail).toBeVisible();
  await batchDetail.scrollIntoViewIfNeeded();
  await page.screenshot({ path: `${out}/console-gate.png` });
});
