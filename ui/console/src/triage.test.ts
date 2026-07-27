import { AdminClient, MODULES, type Capability, type ModuleName } from "./adminClient";
import type { Capabilities } from "./sections";
import { collectServiceTriage, orderTriage, type TriageRow } from "./triage";

const NOW = new Date("2026-07-27T12:00:00Z");

/** Every module present unless said otherwise — the triage reads what a service composes. */
function capabilities(over: Partial<Record<ModuleName, Capability>> = {}): Capabilities {
  return Object.fromEntries(
    MODULES.map((module) => [module, over[module] ?? { kind: "present" }]),
  ) as Capabilities;
}

const run = (over: Record<string, unknown> = {}) => ({
  id: "r1",
  jobName: "eod-reconciliation",
  status: "Completed",
  startedAt: "2026-07-27T11:00:00Z",
  totalChunks: 4,
  completedChunks: 4,
  failedChunks: 0,
  itemFailures: 0,
  ...over,
});

function client(routes: Record<string, unknown>, fail?: (url: string) => boolean) {
  const fetcher = (async (input: RequestInfo | URL) => {
    const url = String(input);
    if (fail?.(url)) return new Response("", { status: 503 });
    // The DEEPEST match wins: `/goldpath/admin/jobs/fleets/it/runs?take=50` contains both
    // "/jobs/fleets" and "/runs?", and answering it with the fleet list would quietly make
    // every run assertion vacuous.
    const match = Object.keys(routes)
      .filter((key) => url.includes(key))
      .sort((left, right) => url.indexOf(right) - url.indexOf(left))[0];
    return new Response(JSON.stringify(match ? routes[match] : []), {
      status: 200,
      headers: { "content-type": "application/json" },
    });
  }) as typeof fetch;
  return new AdminClient({ fetcher });
}

const headlines = (rows: TriageRow[]) => rows.map((row) => row.headline);

describe("triage — what is wrong, read from the contract's own lists", () => {
  it("a quiet estate produces NO rows: silence is the answer, not an empty metric", async () => {
    const api = client({ "/jobs/fleets": [{ schedulerName: "it", jobCount: 1, nodes: [] }], "/runs?": [run()] });

    expect(await collectServiceTriage("payments", api, capabilities(), NOW)).toEqual([]);
  });

  it("failed runs are DANGER and name the jobs", async () => {
    const api = client({
      "/jobs/fleets": [{ schedulerName: "it", jobCount: 2, nodes: [] }],
      "/runs?": [run({ status: "Failed", jobName: "settlement" }), run()],
    });

    const rows = await collectServiceTriage("payments", api, capabilities(), NOW);

    expect(rows[0]).toMatchObject({ tone: "danger", section: "jobs", service: "payments" });
    expect(rows[0].headline).toBe("1 failed run in it");
    expect(rows[0].detail).toBe("settlement");
  });

  it("a run still going past its deadline is DANGER; one merely predicted to overrun is a warning", async () => {
    const api = client({
      "/jobs/fleets": [{ schedulerName: "it", jobCount: 2, nodes: [] }],
      "/runs?": [
        run({ id: "late", status: "Running", deadlineAt: "2026-07-27T11:30:00Z", jobName: "nightly" }),
        run({ id: "soon", status: "Running", deadlineAt: "2026-07-27T13:00:00Z", predictedFinishAt: "2026-07-27T13:30:00Z", jobName: "sweep" }),
      ],
    });

    const rows = await collectServiceTriage("payments", api, capabilities(), NOW);

    expect(rows.find((row) => row.tone === "danger")?.headline).toBe("1 run past the deadline in it");
    expect(rows.find((row) => row.tone === "warning")?.headline).toBe("1 run predicted to overrun in it");
  });

  it("the repair queue is counted across the runs it was given", async () => {
    const api = client({
      "/jobs/fleets": [{ schedulerName: "it", jobCount: 1, nodes: [] }],
      "/runs?": [run({ itemFailures: 3 }), run({ id: "r2", itemFailures: 4 })],
    });

    const rows = await collectServiceTriage("payments", api, capabilities(), NOW);

    expect(headlines(rows)).toContain("7 items waiting in the repair queue of it");
  });

  it("a four-eyes gate holding batches is a warning, with how long the oldest has waited", async () => {
    const api = client({
      "/bulk/definitions": [{ name: "payouts", batchesByState: {}, awaitingApproval: 2, oldestAwaitingApprovalSeconds: 7200 }],
    });

    const rows = await collectServiceTriage("payments", api, capabilities({ jobs: { kind: "absent" } }), NOW);

    expect(rows[0].headline).toBe("2 batches awaiting approval in payouts");
    expect(rows[0].detail).toBe("the oldest has waited 2h");
  });

  it("a paused campaign is reported — someone stopped it and nobody resumed it", async () => {
    const api = client({
      "/campaign/?": [
        { id: "c1", name: "june-welcome", state: "Paused", failedCount: 0, succeededCount: 10, remaining: 90, tps: 2, releasedThrough: 10, enumeratedThrough: 100, enumerationComplete: true, inFlight: 0, releasedToday: 10, maxInFlight: 5, timeZoneId: "UTC", windowOpenNow: true, createdAt: "", createdBy: "" },
      ],
    });

    const rows = await collectServiceTriage("payments", api, capabilities({ jobs: { kind: "absent" } }), NOW);

    expect(headlines(rows)).toContain("june-welcome is paused");
    expect(rows[0].detail).toContain("90 items");
  });

  it("failed notifications are DANGER; a queue that has not moved in a quarter of an hour is a warning", async () => {
    const api = client({
      "/notification/templates": [
        { key: "welcome", hash: "h", byState: { Failed: 2, Sent: 8 }, oldestRequestedSeconds: 1800 },
      ],
    });

    const rows = await collectServiceTriage("payments", api, capabilities({ jobs: { kind: "absent" } }), NOW);

    expect(rows[0]).toMatchObject({ tone: "danger", headline: "2 notifications failed on welcome" });
    expect(headlines(rows)).toContain("welcome has a request waiting 30m");
  });

  it("an archive that has not caught up is a warning", async () => {
    const api = client({
      "/archival/definitions": [{ name: "policies", entries: 10, dueBacklog: 4, activeHolds: 0, chainHead: 10, purgedThrough: 0 }],
    });

    const rows = await collectServiceTriage("payments", api, capabilities({ jobs: { kind: "absent" } }), NOW);

    expect(rows[0].headline).toBe("4 aggregates due to archive in policies");
  });

  it("an ABSENT capability contributes nothing; a REFUSING one contributes the blind spot, never a guessed number", async () => {
    // The bulk surface would answer "9 awaiting approval" if we asked — but the capability
    // probe was refused, so triage must not read it and must not pretend it did.
    const api = client({ "/bulk/definitions": [{ name: "payouts", batchesByState: {}, awaitingApproval: 9 }] });

    const rows = await collectServiceTriage(
      "payments",
      api,
      capabilities({
        jobs: { kind: "absent" },
        campaign: { kind: "absent" },
        notification: { kind: "absent" },
        archival: { kind: "absent" },
        bulk: { kind: "refused", message: "no ambient tenant" },
      }),
      NOW,
    );

    expect(rows).toHaveLength(1);
    expect(rows[0].headline).toBe("1 surface on payments cannot be read");
    expect(rows.some((row) => row.headline.includes("9"))).toBe(false);
  });

  it("a surface that DIES mid-read becomes a row of its own — triage never drops a service quietly", async () => {
    const api = client({ "/jobs/fleets": [{ schedulerName: "it", jobCount: 1, nodes: [] }] }, (url) => url.includes("/runs?"));

    const rows = await collectServiceTriage("payments", api, capabilities({ bulk: { kind: "absent" }, campaign: { kind: "absent" }, notification: { kind: "absent" }, archival: { kind: "absent" } }), NOW);

    expect(rows).toEqual([
      {
        service: "payments",
        section: "jobs",
        tone: "danger",
        headline: "the run surface could not be read",
        detail: "the surface answered, then stopped — triage cannot speak for it",
      },
    ]);
  });

  it("orders danger before warning, then by service — the worst row is always first", () => {
    const row = (tone: "danger" | "warning", service: string): TriageRow => ({
      service,
      section: "jobs",
      tone,
      headline: `${tone} on ${service}`,
      detail: "",
    });

    expect(headlines(orderTriage([row("warning", "b"), row("danger", "z"), row("warning", "a"), row("danger", "a")]))).toEqual([
      "danger on a",
      "danger on z",
      "warning on a",
      "warning on b",
    ]);
  });

  it("failed campaign items are reported with what did land", async () => {
    const api = client({
      "/campaign/?": [
        { id: "c1", name: "june-welcome", state: "Running", failedCount: 1, succeededCount: 40, remaining: 59, tps: 2, releasedThrough: 41, enumeratedThrough: 100, enumerationComplete: true, inFlight: 1, releasedToday: 41, maxInFlight: 5, timeZoneId: "UTC", windowOpenNow: true, createdAt: "", createdBy: "" },
      ],
    });

    const rows = await collectServiceTriage("payments", api, capabilities({ jobs: { kind: "absent" } }), NOW);

    // Singular, because one item is one item — the count is the operator's first read.
    expect(rows[0].headline).toBe("1 failed item in june-welcome");
    expect(rows[0].detail).toBe("40 succeeded · 59 remaining");
  });

  it("every surface reports its OWN death, not just the run surface", async () => {
    for (const [surface, route, what] of [
      ["bulk", "/bulk/definitions", "the intake surface"],
      ["campaign", "/campaign/?", "the campaign surface"],
      ["notification", "/notification/templates", "the notification surface"],
      ["archival", "/archival/definitions", "the archival surface"],
    ] as const) {
      const only = Object.fromEntries(
        MODULES.map((module) => [module, module === surface ? { kind: "present" } : { kind: "absent" }]),
      ) as Capabilities;
      const api = client({}, (url) => url.includes(route.replace("?", "")));

      const rows = await collectServiceTriage("payments", api, only, NOW);

      expect(rows).toEqual([
        {
          service: "payments",
          section: surface,
          tone: "danger",
          headline: `${what} could not be read`,
          detail: "the surface answered, then stopped — triage cannot speak for it",
        },
      ]);
    }
  });

  it("plurals follow the count, in every row the operator reads", async () => {
    const api = client({
      "/jobs/fleets": [{ schedulerName: "it", jobCount: 3, nodes: [] }],
      "/runs?": [
        run({ status: "Failed" }),
        run({ id: "r2", status: "Failed", jobName: "settlement" }),
        run({ id: "r3", itemFailures: 1 }),
      ],
    });

    const rows = await collectServiceTriage("payments", api, capabilities({ bulk: { kind: "absent" }, campaign: { kind: "absent" }, notification: { kind: "absent" }, archival: { kind: "absent" } }), NOW);

    expect(headlines(rows)).toContain("2 failed runs in it");
    expect(headlines(rows)).toContain("1 item waiting in the repair queue of it");
  });

  it("a gate with no age still says a gate is holding — an absent number is not a zero", async () => {
    const api = client({
      "/bulk/definitions": [{ name: "payouts", batchesByState: {}, awaitingApproval: 1, oldestAwaitingApprovalSeconds: null }],
    });

    const rows = await collectServiceTriage("payments", api, capabilities({ jobs: { kind: "absent" } }), NOW);

    expect(rows[0].headline).toBe("1 batch awaiting approval in payouts");
    expect(rows[0].detail).toBe("a four-eyes gate is holding them");
  });

  it("one failed notification reads in the singular, and a young queue says nothing at all", async () => {
    const api = client({
      "/notification/templates": [{ key: "welcome", hash: "h", byState: { Failed: 1 }, oldestRequestedSeconds: 60 }],
    });

    const rows = await collectServiceTriage("payments", api, capabilities({ jobs: { kind: "absent" } }), NOW);

    expect(headlines(rows)).toEqual(["1 notification failed on welcome"]);
  });

  it("one aggregate due to archive reads in the singular", async () => {
    const api = client({
      "/archival/definitions": [{ name: "policies", entries: 1, dueBacklog: 1, activeHolds: 0, chainHead: 1, purgedThrough: 0 }],
    });

    const rows = await collectServiceTriage("payments", api, capabilities({ jobs: { kind: "absent" } }), NOW);

    expect(rows[0].headline).toBe("1 aggregate due to archive in policies");
  });

  it("a service the console CANNOT READ is one row, not five — and it names the reason", async () => {
    const api = client({});
    const blind = Object.fromEntries(
      MODULES.map((module) => [
        module,
        module === "jobs"
          ? { kind: "forbidden", message: "the 'goldpath-ops' role is required" }
          : { kind: "forbidden" },
      ]),
    ) as Capabilities;

    const rows = await collectServiceTriage("auth-floored", api, blind, NOW);

    expect(rows).toHaveLength(1);
    expect(rows[0]).toMatchObject({ tone: "warning", service: "auth-floored", section: "jobs" });
    expect(rows[0].headline).toBe("5 surfaces on auth-floored cannot be read");
    expect(rows[0].detail).toContain("the 'goldpath-ops' role is required");
  });

  it("one unreadable surface reads in the singular, and without a message says what it can", async () => {
    const rows = await collectServiceTriage(
      "payments",
      client({}),
      capabilities({ jobs: { kind: "refused" }, bulk: { kind: "absent" }, campaign: { kind: "absent" }, notification: { kind: "absent" }, archival: { kind: "absent" } }),
      NOW,
    );

    expect(rows[0].headline).toBe("1 surface on payments cannot be read");
    expect(rows[0].detail).toBe("this operator may not see them, or the request cannot be scoped");
  });
});
