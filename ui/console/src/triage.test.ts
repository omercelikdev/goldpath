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

    expect((await collectServiceTriage("payments", api, capabilities(), NOW)).rows).toEqual([]);
  });

  it("failed runs are DANGER and name the jobs", async () => {
    const api = client({
      "/jobs/fleets": [{ schedulerName: "it", jobCount: 2, nodes: [] }],
      "/runs?": [run({ status: "Failed", jobName: "settlement" }), run()],
    });

    const { rows } = await collectServiceTriage("payments", api, capabilities(), NOW);

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

    const { rows } = await collectServiceTriage("payments", api, capabilities(), NOW);

    expect(rows.find((row) => row.tone === "danger")?.headline).toBe("1 run past the deadline in it");
    expect(rows.find((row) => row.tone === "warning")?.headline).toBe("1 run predicted to overrun in it");
  });

  it("the repair queue is counted across the runs it was given", async () => {
    const api = client({
      "/jobs/fleets": [{ schedulerName: "it", jobCount: 1, nodes: [] }],
      "/runs?": [run({ itemFailures: 3 }), run({ id: "r2", itemFailures: 4 })],
    });

    const { rows } = await collectServiceTriage("payments", api, capabilities(), NOW);

    expect(headlines(rows)).toContain("7 items waiting in the repair queue of it");
  });

  it("a four-eyes gate holding batches is a warning, with how long the oldest has waited", async () => {
    const api = client({
      "/bulk/definitions": [{ name: "payouts", batchesByState: {}, awaitingApproval: 2, oldestAwaitingApprovalSeconds: 7200 }],
    });

    const { rows } = await collectServiceTriage("payments", api, capabilities({ jobs: { kind: "absent" } }), NOW);

    expect(rows[0].headline).toBe("2 batches awaiting approval in payouts");
    expect(rows[0].detail).toBe("the oldest has waited 2h");
  });

  it("a paused campaign is reported — someone stopped it and nobody resumed it", async () => {
    const api = client({
      "/campaign/?": [
        { id: "c1", name: "june-welcome", state: "Paused", failedCount: 0, succeededCount: 10, remaining: 90, tps: 2, releasedThrough: 10, enumeratedThrough: 100, enumerationComplete: true, inFlight: 0, releasedToday: 10, maxInFlight: 5, timeZoneId: "UTC", windowOpenNow: true, createdAt: "", createdBy: "" },
      ],
    });

    const { rows } = await collectServiceTriage("payments", api, capabilities({ jobs: { kind: "absent" } }), NOW);

    expect(headlines(rows)).toContain("june-welcome is paused");
    expect(rows[0].detail).toContain("90 items");
  });

  it("failed notifications are DANGER; a queue that has not moved in a quarter of an hour is a warning", async () => {
    const api = client({
      "/notification/templates": [
        { key: "welcome", hash: "h", byState: { Failed: 2, Sent: 8 }, oldestRequestedSeconds: 1800 },
      ],
    });

    const { rows } = await collectServiceTriage("payments", api, capabilities({ jobs: { kind: "absent" } }), NOW);

    expect(rows[0]).toMatchObject({ tone: "danger", headline: "2 notifications failed on welcome" });
    expect(headlines(rows)).toContain("welcome has a request waiting 30m");
  });

  it("an archive that has not caught up is a warning", async () => {
    const api = client({
      "/archival/definitions": [{ name: "policies", entries: 10, dueBacklog: 4, activeHolds: 0, chainHead: 10, purgedThrough: 0 }],
    });

    const { rows } = await collectServiceTriage("payments", api, capabilities({ jobs: { kind: "absent" } }), NOW);

    expect(rows[0].headline).toBe("4 aggregates due to archive in policies");
  });

  it("an ABSENT capability contributes nothing; a REFUSING one contributes the blind spot, never a guessed number", async () => {
    // The bulk surface would answer "9 awaiting approval" if we asked — but the capability
    // probe was refused, so triage must not read it and must not pretend it did.
    const api = client({ "/bulk/definitions": [{ name: "payouts", batchesByState: {}, awaitingApproval: 9 }] });

    const { rows } = await collectServiceTriage(
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

    const { rows } = await collectServiceTriage("payments", api, capabilities({ bulk: { kind: "absent" }, campaign: { kind: "absent" }, notification: { kind: "absent" }, archival: { kind: "absent" } }), NOW);

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

  it("orders BLIND first, then danger, then warning — a partial picture must say so above the picture", () => {
    const row = (tone: "danger" | "warning", service: string, blind?: true): TriageRow => ({
      service,
      section: "jobs",
      tone,
      ...(blind ? { blind } : {}),
      headline: `${blind ? "blind" : tone} on ${service}`,
      detail: "",
    });

    expect(
      headlines(
        orderTriage([
          row("warning", "b"),
          row("danger", "z"),
          row("danger", "m", true),
          row("warning", "a"),
          row("danger", "a"),
        ]),
      ),
    ).toEqual(["blind on m", "danger on a", "danger on z", "warning on a", "warning on b"]);
  });

  it("failed campaign items are reported with what did land", async () => {
    const api = client({
      "/campaign/?": [
        { id: "c1", name: "june-welcome", state: "Running", failedCount: 1, succeededCount: 40, remaining: 59, tps: 2, releasedThrough: 41, enumeratedThrough: 100, enumerationComplete: true, inFlight: 1, releasedToday: 41, maxInFlight: 5, timeZoneId: "UTC", windowOpenNow: true, createdAt: "", createdBy: "" },
      ],
    });

    const { rows } = await collectServiceTriage("payments", api, capabilities({ jobs: { kind: "absent" } }), NOW);

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

      const { rows } = await collectServiceTriage("payments", api, only, NOW);

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

    const { rows } = await collectServiceTriage("payments", api, capabilities({ bulk: { kind: "absent" }, campaign: { kind: "absent" }, notification: { kind: "absent" }, archival: { kind: "absent" } }), NOW);

    expect(headlines(rows)).toContain("2 failed runs in it");
    expect(headlines(rows)).toContain("1 item waiting in the repair queue of it");
  });

  it("a gate with no age still says a gate is holding — an absent number is not a zero", async () => {
    const api = client({
      "/bulk/definitions": [{ name: "payouts", batchesByState: {}, awaitingApproval: 1, oldestAwaitingApprovalSeconds: null }],
    });

    const { rows } = await collectServiceTriage("payments", api, capabilities({ jobs: { kind: "absent" } }), NOW);

    expect(rows[0].headline).toBe("1 batch awaiting approval in payouts");
    expect(rows[0].detail).toBe("a four-eyes gate is holding them");
  });

  it("one failed notification reads in the singular, and a young queue says nothing at all", async () => {
    const api = client({
      "/notification/templates": [{ key: "welcome", hash: "h", byState: { Failed: 1 }, oldestRequestedSeconds: 60 }],
    });

    const { rows } = await collectServiceTriage("payments", api, capabilities({ jobs: { kind: "absent" } }), NOW);

    expect(headlines(rows)).toEqual(["1 notification failed on welcome"]);
  });

  it("one aggregate due to archive reads in the singular", async () => {
    const api = client({
      "/archival/definitions": [{ name: "policies", entries: 1, dueBacklog: 1, activeHolds: 0, chainHead: 1, purgedThrough: 0 }],
    });

    const { rows } = await collectServiceTriage("payments", api, capabilities({ jobs: { kind: "absent" } }), NOW);

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

    const { rows } = await collectServiceTriage("auth-floored", api, blind, NOW);

    expect(rows).toHaveLength(1);
    expect(rows[0]).toMatchObject({ tone: "danger", blind: true, service: "auth-floored", section: "jobs" });
    expect(rows[0].headline).toBe("6 surfaces on auth-floored cannot be read");
    expect(rows[0].detail).toContain("the 'goldpath-ops' role is required");
  });

  it("a service that never answered is named as such, not as five unreadable surfaces", async () => {
    const dead = Object.fromEntries(MODULES.map((module) => [module, { kind: "unreachable" }])) as Capabilities;

    const { rows } = await collectServiceTriage("claims", client({}), dead, NOW);

    expect(rows).toHaveLength(1);
    expect(rows[0]).toMatchObject({ blind: true, tone: "danger" });
    expect(rows[0].headline).toBe("claims did not answer at all");
    expect(rows[0].detail).toContain("blocking this console's origin");
  });

  it("ONE dead probe among honest refusals is not an outage — the count is told, not dramatised", async () => {
    const mixed = Object.fromEntries(
      MODULES.map((module, index) => [
        module,
        index === 0 ? { kind: "unreachable" } : { kind: "forbidden", message: "the 'goldpath-ops' role is required" },
      ]),
    ) as Capabilities;

    const { rows } = await collectServiceTriage("payments", client({}), mixed, NOW);

    expect(rows).toHaveLength(1);
    expect(rows[0].headline).toBe("6 surfaces on payments cannot be read");
    expect(rows[0].headline).not.toContain("did not answer at all");
  });

  it("one unreadable surface reads in the singular, and without a message says what it can", async () => {
    const { rows } = await collectServiceTriage(
      "payments",
      client({}),
      capabilities({ jobs: { kind: "refused" }, bulk: { kind: "absent" }, campaign: { kind: "absent" }, notification: { kind: "absent" }, archival: { kind: "absent" } }),
      NOW,
    );

    expect(rows[0].headline).toBe("1 surface on payments cannot be read");
    expect(rows[0].detail).toBe("this operator may not see them, or the request cannot be scoped");
  });
});

describe("triage stats — the numbers the Today cards print", () => {
  it("a readable module reports its count even at ZERO — a quiet zero is still a claim", async () => {
    const api = client({
      "/jobs/fleets": [{ schedulerName: "it", jobCount: 1, nodes: [] }],
      "/runs?": [run()],
      "/bulk/definitions": [{ name: "payments", awaitingApproval: 0 }],
    });

    const { stats } = await collectServiceTriage(
      "payments",
      api,
      capabilities({ campaign: { kind: "absent" }, notification: { kind: "absent" }, archival: { kind: "absent" }, approvals: { kind: "absent" } }),
      NOW,
    );

    expect(stats).toEqual({ jobs: 0, bulk: 0 });
  });

  it("counts SUM across fleets and definitions, straight from the lists it read", async () => {
    const api = client({
      "/jobs/fleets": [
        { schedulerName: "it", jobCount: 1, nodes: [] },
        { schedulerName: "ops", jobCount: 1, nodes: [] },
      ],
      "/runs?": [run({ status: "Failed" }), run()],
      "/bulk/definitions": [
        { name: "payments", awaitingApproval: 2, oldestAwaitingApprovalSeconds: 60 },
        { name: "claims", awaitingApproval: 1, oldestAwaitingApprovalSeconds: 60 },
      ],
      "/notification/templates": [
        { key: "ops-alert", byState: { Failed: 3 } },
        { key: "welcome", byState: { Sent: 5 } },
      ],
      "/archival/definitions": [{ name: "ledger", dueBacklog: 4 }],
      "/campaign/?": [{ name: "renewal", state: "Running", failedCount: 2, succeededCount: 1, remaining: 7 }],
      "/approvals/requests": [
        { id: "a1", ladder: "credit-limit", subject: "K26-1", amount: 500000, requestedBy: "maker", requestedAt: "2026-07-27T06:00:00Z", pendingRole: "expert", pendingSince: "2026-07-27T06:00:00Z", status: "Pending", signatureCount: 0, requiredApprovals: 1 },
      ],
    });

    const { stats } = await collectServiceTriage("payments", api, capabilities(), NOW);

    // One failed run PER FLEET (both fleets answer the same run list here).
    expect(stats).toEqual({ jobs: 2, bulk: 3, campaign: 2, notification: 3, archival: 4, approvals: 1 });
  });

  it("a surface that DIED reports no number at all — a count we could not read is not a zero", async () => {
    const api = client(
      {
        "/jobs/fleets": [{ schedulerName: "it", jobCount: 1, nodes: [] }],
        "/runs?": [run()],
        "/bulk/definitions": [{ name: "payments", awaitingApproval: 1 }],
      },
      (url) => url.includes("/jobs/fleets"),
    );

    const { rows, stats } = await collectServiceTriage(
      "payments",
      api,
      capabilities({ campaign: { kind: "absent" }, notification: { kind: "absent" }, archival: { kind: "absent" }, approvals: { kind: "absent" } }),
      NOW,
    );

    expect(stats).toEqual({ bulk: 1 });   // jobs is absent from stats, not zero
    expect(headlines(rows)).toContain("the run surface could not be read");
  });

  it("a module that is absent, forbidden or refusing contributes no number either", async () => {
    const { stats } = await collectServiceTriage(
      "payments",
      client({}),
      capabilities({
        jobs: { kind: "absent" },
        bulk: { kind: "forbidden" },
        campaign: { kind: "refused" },
        notification: { kind: "absent" },
        archival: { kind: "absent" },
        approvals: { kind: "absent" },
      }),
      NOW,
    );

    expect(stats).toEqual({});
  });
});
