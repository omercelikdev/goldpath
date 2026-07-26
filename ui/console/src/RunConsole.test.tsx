import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AdminClient, type RunDetail, type RunSummary } from "./adminClient";
import { RunConsole } from "./RunConsole";

const run = (over: Partial<RunSummary> = {}): RunSummary => ({
  id: "run-9f21",
  jobName: "eod-reconciliation",
  status: "Completed",
  startedAt: "2026-07-26T10:00:00Z",
  finishedAt: "2026-07-26T10:05:00Z",
  totalChunks: 10,
  completedChunks: 10,
  failedChunks: 0,
  totalItems: 1000,
  itemFailures: 0,
  ...over,
});

// The contract's real shape: a nested record with chunk COUNTS per status.
const detail = (over: Partial<RunDetail> = {}): RunDetail => ({
  run: run(),
  chunksByStatus: { Completed: 9, Failed: 1 },
  openFailures: [
    { id: 1, runId: "run-9f21", chunkIndex: 1, itemKey: "ORD-77", reason: "bank refused", failedAt: "2026-07-26T10:04:00Z" },
  ],
  ...over,
});

interface Routes {
  fleets?: unknown;
  jobs?: unknown;
  runs?: unknown;
  run?: unknown;
  verb?: { status: number; body: unknown };
}

function client(routes: Routes) {
  const posted: { url: string; body: BodyInit | null | undefined }[] = [];
  const fetcher = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    const json = (body: unknown, status = 200) =>
      new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });

    if (init?.method === "POST") {
      posted.push({ url, body: init.body });
      const verb = routes.verb ?? { status: 200, body: { ok: true, message: "done" } };
      return json(verb.body, verb.status);
    }

    // Route matching mirrors the contract's SHAPE, not loose substrings: every jobs
    // route lives under /fleets, so a sloppy include() would serve the wrong payload.
    if (url.includes("/jobs/runs/")) return json(routes.run ?? detail());
    if (url.includes("/runs?")) return json(routes.runs ?? [run()]);
    if (url.endsWith("/jobs")) return json(routes.jobs ?? [{ name: "eod-reconciliation", paused: false }]);
    if (url.endsWith("/fleets")) return json(routes.fleets ?? [{ schedulerName: "it-cluster", jobCount: 2, nodes: [{ instanceId: "node-a" }] }]);
    return new Response("not found", { status: 404 });
  }) as typeof fetch;

  return { client: new AdminClient({ fetcher }), posted };
}

describe("the run console (console RFC §2 — a client of the frozen contract)", () => {
  it("walks fleets → jobs → runs from the API alone", async () => {
    const { client: api } = client({});
    render(<RunConsole client={api} />);

    expect(await screen.findByRole("button", { name: /it-cluster/ })).toBeInTheDocument();
    // The job appears in the job list AND in its run's row — both are the point.
    await waitFor(() => expect(screen.getAllByText("eod-reconciliation")).toHaveLength(2));
    expect(await screen.findByRole("button", { name: "run-9f21" })).toBeInTheDocument();
  });

  it("opening a run shows its chunk breakdown and repair queue", async () => {
    const { client: api } = client({});
    render(<RunConsole client={api} now={new Date("2026-07-26T10:06:00Z")} />);

    await userEvent.click(await screen.findByRole("button", { name: "run-9f21" }));

    const panel = await screen.findByTestId("run-detail");
    expect(panel).toHaveTextContent("Completed: 9");
    expect(panel).toHaveTextContent("Failed: 1");
    expect(panel).toHaveTextContent("ORD-77");
    expect(panel).toHaveTextContent("bank refused");
  });

  it("a verb goes through confirm and posts the CONTRACT's route", async () => {
    const { client: api, posted } = client({});
    render(<RunConsole client={api} />);

    await userEvent.click(await screen.findByRole("button", { name: "trigger" }));
    await userEvent.click(screen.getByRole("alertdialog").querySelector("button")!);

    await waitFor(() => expect(posted).toHaveLength(1));
    expect(posted[0].url).toContain("/goldpath/admin/jobs/fleets/it-cluster/jobs/eod-reconciliation/trigger");
  });

  it("a refusal surfaces verbatim — the console never paraphrases the server", async () => {
    const { client: api } = client({
      verb: { status: 400, body: { ok: false, message: "the job is paused — resume it before triggering" } },
    });
    render(<RunConsole client={api} />);

    await userEvent.click(await screen.findByRole("button", { name: "trigger" }));
    await userEvent.click(screen.getByRole("alertdialog").querySelector("button")!);

    expect(await screen.findByText(/resume it before triggering/)).toBeInTheDocument();
  });

  it("replay-items appears only when the repair queue has something to replay", async () => {
    const { client: api } = client({ run: detail({ openFailures: [] }) });
    render(<RunConsole client={api} />);

    await userEvent.click(await screen.findByRole("button", { name: "run-9f21" }));
    await screen.findByTestId("run-detail");

    expect(screen.queryByRole("button", { name: "replay-items" })).toBeNull();
    expect(screen.getByText(/Repair queue — empty/)).toBeInTheDocument();
  });

  it("replaying posts the frozen route with NO body — the server scopes it, not the UI", async () => {
    const { client: api, posted } = client({});
    render(<RunConsole client={api} />);

    await userEvent.click(await screen.findByRole("button", { name: "run-9f21" }));
    await userEvent.click(await screen.findByRole("button", { name: "replay-items" }));

    // The copy must not promise a scoped replay the server does not perform.
    const dialog = screen.getByRole("alertdialog");
    expect(dialog).toHaveTextContent(/Replay all open repair items/);
    await userEvent.click(dialog.querySelector("button")!);

    await waitFor(() => expect(posted).toHaveLength(1));
    expect(posted[0].url).toContain("/goldpath/admin/jobs/runs/run-9f21/replay-items");
    expect(posted[0].body).toBeUndefined();   // no body: the server scopes the redrive
  });

  it("a fleet list that will not load says so instead of showing an empty console", async () => {
    const fetcher = (async () => new Response("", { status: 503 })) as typeof fetch;
    render(<RunConsole client={new AdminClient({ fetcher })} />);

    expect(await screen.findByRole("alert")).toHaveTextContent(/fleet list could not be loaded/i);
  });
});
