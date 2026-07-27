import { expect, test } from "@playwright/test";

/**
 * The shape adopters actually deploy (console RFC D1): the console served BY the app from
 * `MapGoldpathConsole()`, out of the package's embedded assets — no Node anywhere near the
 * running system, no CORS in the path, and the registry coming from the app's own
 * configuration rather than a file someone remembered to copy.
 *
 * The dev server proves the console's behaviour; this proves the console adopters get.
 */
const served = process.env.GOLDPATH_SERVED_CONSOLE_URL ?? "http://localhost:5310/goldpath/console/";

test.describe("the console served by the app itself", () => {
  test("loads from the embedded assets and reads the registry the APP configured", async ({ page }) => {
    // Only the console's OWN assets are judged here: the capability probes are supposed
    // to draw 401s and 400s from the auth-floored and tenant-scoped services — that is the
    // discovery working, not the page failing.
    const failures: string[] = [];
    page.on("response", (response) => {
      if (response.url().startsWith(served) && response.status() >= 400) {
        failures.push(`${response.status()} ${response.url()}`);
      }
    });

    await page.goto(served);

    // The page came from the package, and its assets came with it.
    await expect(page.getByTestId("triage-home")).toBeVisible();
    expect(failures, "the served console must not 404 its own assets").toEqual([]);

    // The registry is the app's configuration — three services, named by the host.
    const picker = page.getByLabel(/service/i);
    await expect(picker).toBeVisible();
    await expect(picker.locator("option")).toHaveText(["open", "auth-floored", "tenant-scoped"]);
  });

  test("an unknown path 404s instead of serving a page that would render blank", async ({ page }) => {
    // The console has no client-side routes yet: sections are state, not URLs. Answering
    // this with the page would be 200 + a blank screen, because the page's relative asset
    // URLs would resolve one directory too deep. Honest 404 until real routes exist
    // (open-threads T9), and this test changes with them.
    const response = await page.request.get(`${served}runs/it-cluster`);

    expect(response.status()).toBe(404);
  });

  test("a missing ASSET is a 404, never the page dressed as a stylesheet", async ({ page }) => {
    const response = await page.request.get(`${served}assets/never-built.css`);

    expect(response.status()).toBe(404);
  });
});
