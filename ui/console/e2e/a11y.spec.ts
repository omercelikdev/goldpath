import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";

/**
 * The accessibility gate. An operations console is used at 3am by whoever is on call,
 * often over a remote session with a keyboard and no mouse — so this is a correctness
 * property, not a nicety.
 *
 * WCAG 2.1 A + AA, serious and critical violations only: the ramp of "moderate" findings
 * (landmark preferences, heading order in a dense panel) is advice, and a gate that fails
 * on advice gets muted, which costs the real findings too.
 */
const service = process.env.GOLDPATH_SERVICE_URL ?? "http://localhost:5310";

const PANELS = [
  { nav: "Runs", ready: "run-console" },
  { nav: "Bulk intake", ready: "bulk-panel" },
  { nav: "Campaigns", ready: "campaign-panel" },
  { nav: "Notifications", ready: "notification-panel" },
  { nav: "Archival", ready: "archival-panel" },
] as const;

async function violations(page: import("@playwright/test").Page) {
  const result = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  return result.violations
    .filter((violation) => violation.impact === "serious" || violation.impact === "critical")
    .map((violation) => `${violation.id} (${violation.impact}) — ${violation.nodes.length} node(s): ${violation.help}`);
}

test.describe("the console is operable without a mouse or a perfect screen", () => {
  for (const panel of PANELS) {
    test(`${panel.nav} has no serious accessibility violation`, async ({ page }) => {
      await page.goto(`/?base=${encodeURIComponent(service)}`);
      await page.getByRole("button", { name: panel.nav }).click();
      await expect(page.getByTestId(panel.ready)).toBeVisible();

      expect(await violations(page)).toEqual([]);
    });
  }

  test("a confirm dialog traps nothing and is reachable by keyboard alone", async ({ page }) => {
    await page.goto(`/?base=${encodeURIComponent(service)}`);
    await expect(page.getByTestId("run-console")).toBeVisible();

    await page.locator("li", { hasText: "SmokeJob" }).getByRole("button", { name: "trigger" }).first().click();
    const dialog = page.getByRole("alertdialog");
    await expect(dialog).toBeVisible();

    // The dialog itself must be clean — this is where a destructive verb is confirmed.
    expect(await violations(page)).toEqual([]);

    // And it must be dismissible from the keyboard: an operator who opened it by mistake
    // should not have to hunt for the mouse.
    await page.keyboard.press("Escape");
    await expect(dialog).toHaveCount(0);
  });
});
