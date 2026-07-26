import { defineConfig } from "@playwright/test";

/**
 * The U2 exit gate (console RFC D3): behaviour against a REAL app — no screenshot-diff
 * theatre. `scripts/console-smoke.sh` brings up Postgres + a real Goldpath web app + the
 * console dev server, then runs this suite against them.
 */
export default defineConfig({
  testDir: "./e2e",
  timeout: 60_000,
  expect: { timeout: 20_000 },
  fullyParallel: false,
  retries: 0,
  reporter: [["list"]],
  use: {
    baseURL: process.env.GOLDPATH_CONSOLE_URL ?? "http://localhost:5200",
    trace: "retain-on-failure",
  },
});
