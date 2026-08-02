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
    // The picker is the family's own listbox now: open it to read the options.
    const picker = page.getByRole("combobox", { name: "service" });
    await expect(picker).toBeVisible();
    await picker.click();
    await expect(page.getByRole("option")).toHaveText(["open", "auth-floored", "tenant-scoped"]);
    await page.keyboard.press("Escape");
  });

  test("an app that configures NO service shows one service and NO warning at all", async ({ page }) => {
    // The single-app default — by far the most common adopter — and the seam that was
    // never tested: the package proved WHAT it serves for an unconfigured registry, the
    // console proved how it reads each answer, and nobody proved the two agreed. They did
    // not: an empty list read as a BROKEN registry, so every single-app operator's first
    // screen carried a warning that a service they configured had gone missing.
    //
    // The tenant-scoped host is that app: it runs without GOLDPATH_CONSOLE_SERVICES.
    const tenant = process.env.GOLDPATH_TENANT_URL ?? "http://localhost:5313";
    await page.goto(`${tenant}/goldpath/console/`);

    await expect(page.getByTestId("triage-home")).toBeVisible();
    // "registry" is the console's own word for EVERY registry problem it can report, so
    // its absence is the claim: not one of them fired. (This app does refuse its tenant-
    // scoped surfaces, and triage says so — that is a true statement about the service,
    // which is exactly what the false one was crowding out.)
    await expect(page.getByText(/registry/i)).toHaveCount(0);
    // One service needs no picker: there is nothing to pick between.
    await expect(page.getByLabel(/service/i)).toHaveCount(0);
    // And the answer underneath it, from the running app: absent, not empty.
    expect((await page.request.get(`${tenant}/goldpath/console/console.config.json`)).status()).toBe(404);
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

  test("the console served by an AUTH-FLOORED app refuses its own page", async ({ page }) => {
    // The most important property of the auth story: an adopter cannot ship an
    // unauthenticated console by accident. The page itself is behind the ops floor, so an
    // operator without a principal never reaches a screen to be confused by.
    const secured = process.env.GOLDPATH_SECURED_URL ?? "http://localhost:5312";
    const response = await page.request.get(`${secured}/goldpath/console/`);

    expect(response.status()).toBe(401);
  });
});
