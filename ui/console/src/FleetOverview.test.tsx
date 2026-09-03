import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AdminClient } from "./adminClient";
import { FleetOverview } from "./FleetOverview";

const STATUS = {
  schedulerName: "it-cluster",
  jobCount: 3,
  isPaused: false,
  connection: {
    instanceId: "node-a",
    runningSince: "2026-07-28T02:00:00Z",
    threadPoolSize: 10,
    jobsExecuted: 42,
    isShutdown: false,
    // The management head that answers is in standby — and the SCREEN must not read
    // that as the fleet holding fires.
    inStandbyMode: true,
  },
  nodes: [
    { instanceName: "node-a", lastCheckin: "2026-07-28T03:00:00Z", checkinInterval: "00:00:10" },
    { instanceName: "node-b", lastCheckin: "2026-07-28T02:59:55Z", checkinInterval: "00:00:10" },
  ],
};

const AUDIT = [
  { id: 2, at: "2026-07-28T03:00:00Z", actor: "ops@acme", action: "pause-all", fleet: "it-cluster", target: "*", detail: null },
  { id: 1, at: "2026-07-28T02:00:00Z", actor: "ops@acme", action: "reschedule", fleet: "it-cluster", target: "eod", detail: "0 0 2 * * ? -> 0 0 3 * * ?" },
];

function api(overrides: { status?: unknown; statusFails?: boolean; audit?: unknown; auditFails?: boolean } = {}) {
  const posted: { url: string; method: string }[] = [];
  const fetcher = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { "content-type": "application/json" } });
    if (init?.method && init.method !== "GET") {
      posted.push({ url, method: init.method });
      return json({ ok: true, message: "fleet paused" });
    }

    if (url.includes("/status")) {
      return overrides.statusFails ? new Response("", { status: 503 }) : json(overrides.status ?? STATUS);
    }

    if (url.includes("/audit")) {
      return overrides.auditFails ? new Response("", { status: 403 }) : json(overrides.audit ?? AUDIT);
    }

    return new Response("not found", { status: 404 });
  }) as typeof fetch;
  return { client: new AdminClient({ fetcher }), posted };
}

describe("the fleet overview (contract R2.1 + the frozen fleet verbs)", () => {
  it("reports the scheduler's own state and every cluster member", async () => {
    const { client } = api();
    render(<FleetOverview client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    // WAIT for the status to land, not merely for the panel's shell: the shell renders
    // before the fetch answers, and on a slow runner the shell alone was found and the
    // assertion raced the data (a hosted-CI flake, 2026-09-03).
    await screen.findByText(/accepting fires/);
    const panel = screen.getByTestId("fleet-overview");
    // The fleet is accepting fires even though the member that ANSWERED is in standby:
    // Quartz metadata is per-instance, and the console asks through a management head.
    expect(panel).toHaveTextContent("accepting fires");
    expect(panel).toHaveTextContent("node-a");
    expect(panel).toHaveTextContent("node-b");
    expect(panel).toHaveTextContent("Cluster (2)");
    expect(panel).toHaveTextContent("This console is connected through");
  });

  it("a PAUSED fleet says nothing will fire — the state pause-all leaves in the store", async () => {
    const { client } = api({ status: { ...STATUS, isPaused: true } });
    render(<FleetOverview client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    expect(await screen.findByText(/nothing will fire until someone resumes it/)).toBeInTheDocument();
  });

  it("an EMPTY cluster says nobody has checked in — not a bare zero", async () => {
    const { client } = api({ status: { ...STATUS, nodes: [] } });
    render(<FleetOverview client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    expect(await screen.findByText(/no member has checked in/)).toBeInTheDocument();
  });

  it("pause-all warns that it is cluster-wide and durable BEFORE it goes", async () => {
    const user = userEvent.setup();
    const { client, posted } = api();
    render(<FleetOverview client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    await user.click(await screen.findByRole("button", { name: "pause every job" }));

    const dialog = screen.getByRole("alertdialog");
    // The confirm must say what the operator cannot see: this outlives the process and
    // reaches every node.
    expect(dialog).toHaveTextContent(/cluster-wide and survives a restart/);
    await user.click(dialog.querySelector("button")!);

    await waitFor(() => expect(posted).toHaveLength(1));
    expect(posted[0].url).toContain("/goldpath/admin/jobs/fleets/it-cluster/pause-all");
    expect(posted[0].method).toBe("POST");
  });

  it("resume-all posts the frozen route too", async () => {
    const user = userEvent.setup();
    const { client, posted } = api();
    render(<FleetOverview client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    await user.click(await screen.findByRole("button", { name: "resume every job" }));
    await user.click(screen.getByRole("alertdialog").querySelector("button")!);

    await waitFor(() => expect(posted).toHaveLength(1));
    expect(posted[0].url).toContain("/goldpath/admin/jobs/fleets/it-cluster/resume-all");
  });

  it("shows who crossed the surface, in the audit's own words", async () => {
    const { client } = api();
    render(<FleetOverview client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    expect(await screen.findByText("pause-all")).toBeInTheDocument();
    expect(screen.getByText("it-cluster/eod")).toBeInTheDocument();
    expect(screen.getByText(/0 0 2 \* \* \? -> 0 0 3 \* \* \?/)).toBeInTheDocument();
  });

  it("an empty audit says nobody has verbed it, rather than showing nothing", async () => {
    const { client } = api({ audit: [] });
    render(<FleetOverview client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    expect(await screen.findByText(/Nobody has verbed this service yet/)).toBeInTheDocument();
  });

  it("an audit this operator may not read does not hold the screen hostage", async () => {
    const { client } = api({ auditFails: true });
    render(<FleetOverview client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    // The fleet's state is still worth showing; the audit simply reports nothing.
    expect(await screen.findByText(/accepting fires/)).toBeInTheDocument();
    expect(await screen.findByText(/Nobody has verbed this service yet/)).toBeInTheDocument();
  });

  it("a fleet that will not report its state says so", async () => {
    const { client } = api({ statusFails: true });
    render(<FleetOverview client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    expect(await screen.findByRole("alert")).toHaveTextContent(/did not report its state/);
  });

  it("the connection's own standby NEVER reads as the fleet being stopped", async () => {
    const { client } = api({ status: { ...STATUS, isPaused: false, connection: { ...STATUS.connection, inStandbyMode: true, isShutdown: true, jobsExecuted: 0 } } });
    render(<FleetOverview client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    // The regression the console smoke caught: a healthy fleet reported as "in standby —
    // holding fires" because the management head that answered was.
    expect(await screen.findByText("accepting fires")).toBeInTheDocument();
    expect(screen.queryByText(/holding fires/)).toBeNull();
  });
});
