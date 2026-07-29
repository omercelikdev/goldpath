import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AdminClient } from "./adminClient";
import { RunConsole } from "./RunConsole";

const FLEETS = [
  { schedulerName: "it-cluster", jobCount: 1, nodes: [{ instanceName: "node-a", lastCheckin: "2026-07-28T03:00:00Z", checkinInterval: "00:00:10" }] },
  { schedulerName: "night-cluster", jobCount: 1, nodes: [] },
];

const STATUS = {
  schedulerName: "it-cluster",
  jobCount: 1,
  isPaused: false,
  nodes: FLEETS[0].nodes,
  connection: {
    instanceId: "node-a",
    runningSince: "2026-07-28T02:00:00Z",
    threadPoolSize: 10,
    jobsExecuted: 42,
    isShutdown: false,
    inStandbyMode: false,
  },
};

function api(overrides: { jobs?: unknown; status?: unknown } = {}) {
  const fetcher = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { "content-type": "application/json" } });
    if (init?.method && init.method !== "GET") return json({ ok: true, message: "done" });
    if (url.includes("/status")) return json(overrides.status ?? STATUS);
    if (url.includes("/audit")) return json([]);
    if (url.includes("/calendars")) return json([]);
    if (url.includes("/runs?")) return json([]);
    if (url.endsWith("/jobs")) {
      return json(url.includes("night-cluster")
        ? [{ name: "nightly-sweep", triggers: [] }]
        : overrides.jobs ?? [{ name: "eod-reconciliation", triggers: [] }]);
    }

    if (url.endsWith("/fleets")) return json(FLEETS);
    return new Response("not found", { status: 404 });
  }) as typeof fetch;
  return new AdminClient({ fetcher });
}

describe("the scheduling surface (console RFC U5 — a client of the frozen contract)", () => {
  it("lands on the fleet's OVERVIEW: what it is, before what it did", async () => {
    render(<RunConsole client={api()} />);

    expect(await screen.findByRole("button", { name: /it-cluster/ })).toBeInTheDocument();
    // The first question of an incident is whether the thing is alive.
    expect(await screen.findByTestId("fleet-overview")).toHaveTextContent("accepting fires");
    expect(screen.getByRole("tab", { name: "Overview" })).toHaveAttribute("aria-selected", "true");
  });

  it("offers the four sections, and each one reads its own surface", async () => {
    const user = userEvent.setup();
    render(<RunConsole client={api()} />);
    await screen.findByTestId("fleet-overview");

    await user.click(screen.getByRole("tab", { name: "Jobs" }));
    expect(await screen.findByTestId("jobs-tab")).toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: "Calendars" }));
    expect(await screen.findByTestId("calendars-tab")).toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: "History" }));
    expect(await screen.findByTestId("run-history")).toBeInTheDocument();
  });

  it("choosing another fleet re-reads that fleet, never relabels the first one's rows", async () => {
    const user = userEvent.setup();
    render(<RunConsole client={api()} />);
    await screen.findByTestId("fleet-overview");
    await user.click(screen.getByRole("tab", { name: "Jobs" }));
    expect(await screen.findByText("eod-reconciliation")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /night-cluster/ }));

    expect(await screen.findByText("nightly-sweep")).toBeInTheDocument();
    expect(screen.queryByText("eod-reconciliation")).toBeNull();
  });

  it("§7.9: a history row's JOB is a link — it lands on the Jobs tab with the sheet open", async () => {
    const fetcher = (async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { "content-type": "application/json" } });
      if (init?.method && init.method !== "GET") return json({ ok: true, message: "done" });
      if (url.includes("/status")) return json(STATUS);
      if (url.includes("/audit") || url.includes("/calendars")) return json([]);
      if (url.includes("/runs?")) {
        return json([{ id: "r-77", jobName: "eod-reconciliation", status: "Completed", startedAt: "2026-07-28T03:00:00Z", totalChunks: 1, completedChunks: 1, failedChunks: 0, itemFailures: 0 }]);
      }
      if (url.endsWith("/jobs")) return json([{ name: "eod-reconciliation", triggers: [] }]);
      if (url.endsWith("/fleets")) return json(FLEETS);
      return new Response("not found", { status: 404 });
    }) as typeof fetch;

    render(<RunConsole client={new AdminClient({ fetcher })} />);
    await userEvent.click(await screen.findByRole("tab", { name: "History" }));
    await userEvent.click(await screen.findByRole("button", { name: "eod-reconciliation" }));

    // The job's own sheet is already open (it is MODAL, so the tab strip is hidden
    // from the a11y tree behind it — the hidden query reads through).
    const sheet = await screen.findByTestId("sheet");
    expect(sheet).toHaveTextContent("eod-reconciliation");
    expect(screen.getByRole("tab", { name: "Jobs", hidden: true })).toHaveAttribute("aria-selected", "true");
  });

  it("§7.9: another section's run ask opens History with that run's sheet", async () => {
    const detail = {
      run: { id: "r-42", jobName: "bulk-execute", status: "Completed", startedAt: "2026-07-28T03:00:00Z", totalChunks: 1, completedChunks: 1, failedChunks: 0, itemFailures: 0 },
      chunksByStatus: { Completed: 1 },
      openFailures: [],
    };
    const fetcher = (async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { "content-type": "application/json" } });
      if (init?.method && init.method !== "GET") return json({ ok: true, message: "done" });
      if (url.includes("/status")) return json(STATUS);
      if (url.includes("/audit") || url.includes("/calendars") ) return json([]);
      if (url.includes("/runs/r-42")) return json(detail);
      if (url.includes("/runs?")) return json([]);
      if (url.endsWith("/jobs")) return json([{ name: "bulk-execute", triggers: [] }]);
      if (url.endsWith("/fleets")) return json(FLEETS);
      return new Response("not found", { status: 404 });
    }) as typeof fetch;

    render(<RunConsole client={new AdminClient({ fetcher })} openRunRequest={{ id: "r-42" }} />);

    // No clicks: the ask itself lands the operator on History, run open.
    await waitFor(() => expect(screen.getByRole("tab", { name: "History" })).toHaveAttribute("aria-selected", "true"));
    const sheet = await screen.findByTestId("sheet");
    expect(sheet).toHaveTextContent("Run r-42");
    expect(sheet).toHaveTextContent("bulk-execute");
  });

  it("R3: a consumed job ask does NOT replay — leave Jobs and return, no sheet reopens", async () => {
    const fetcher = (async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { "content-type": "application/json" } });
      if (init?.method && init.method !== "GET") return json({ ok: true, message: "done" });
      if (url.includes("/status")) return json(STATUS);
      if (url.includes("/audit") || url.includes("/calendars")) return json([]);
      if (url.includes("/runs?")) {
        return json([{ id: "r-77", jobName: "eod-reconciliation", status: "Completed", startedAt: "2026-07-28T03:00:00Z", totalChunks: 1, completedChunks: 1, failedChunks: 0, itemFailures: 0 }]);
      }
      if (url.endsWith("/jobs")) return json([{ name: "eod-reconciliation", triggers: [] }]);
      if (url.endsWith("/fleets")) return json(FLEETS);
      return new Response("not found", { status: 404 });
    }) as typeof fetch;

    render(<RunConsole client={new AdminClient({ fetcher })} />);
    await userEvent.click(await screen.findByRole("tab", { name: "History" }));
    await userEvent.click(await screen.findByRole("button", { name: "eod-reconciliation" }));
    await screen.findByTestId("sheet");

    // Close the sheet, walk away, come back: the OLD ask must not reopen it.
    await userEvent.keyboard("{Escape}");
    await waitFor(() => expect(screen.queryByTestId("sheet")).not.toBeInTheDocument());
    await userEvent.click(screen.getByRole("tab", { name: "Overview" }));
    await userEvent.click(screen.getByRole("tab", { name: "Jobs" }));

    await screen.findByRole("button", { name: "eod-reconciliation" });
    expect(screen.queryByTestId("sheet")).not.toBeInTheDocument();
  });

  it("R3: the run ask is ACKED so its owner can clear it", async () => {
    const consumed: string[] = [];
    render(<RunConsole client={api()} openRunRequest={{ id: "r-9" }} onRunRequestConsumed={() => consumed.push("acked")} />);

    await waitFor(() => expect(consumed).toEqual(["acked"]));
  });

  it("a fleet list that will not load says so instead of showing an empty console", async () => {
    const fetcher = (async () => new Response("", { status: 503 })) as typeof fetch;
    render(<RunConsole client={new AdminClient({ fetcher })} />);

    expect(await screen.findByRole("alert")).toHaveTextContent(/fleet list could not be loaded/i);
  });

  it("says it is still discovering rather than claiming a fleetless service", async () => {
    const fetcher = (() => new Promise<Response>(() => {})) as unknown as typeof fetch;
    render(<RunConsole client={new AdminClient({ fetcher })} />);

    expect(screen.getByText(/Discovering fleets/)).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByTestId("fleet-overview")).toBeNull());
  });
});
