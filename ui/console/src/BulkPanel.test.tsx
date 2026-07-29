import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AdminClient, type BulkBatchInfo, type BulkDefinitionStatus, type BulkRowError } from "./adminClient";
import { BulkPanel } from "./BulkPanel";

const batch = (over: Partial<BulkBatchInfo> = {}): BulkBatchInfo => ({
  id: "3f7a0c9e-0000-4000-8000-000000000001",
  definition: "payments",
  state: "Validated",
  tenant: "acme",
  totalRows: 120,
  validRows: 118,
  invalidRows: 2,
  executedRows: 0,
  failedRows: 0,
  runId: null,
  receivedAt: "2026-07-27T09:00:00Z",
  validatedAt: "2026-07-27T09:01:00Z",
  ...over,
});

const definition = (over: Partial<BulkDefinitionStatus> = {}): BulkDefinitionStatus => ({
  name: "payments",
  batchesByState: { Validated: 1, Completed: 4 },
  awaitingApproval: 1,
  oldestAwaitingApprovalSeconds: 7200,
  ...over,
});

const finding = (row: number): BulkRowError => ({
  id: row,
  batchId: batch().id,
  rowNumber: row,
  field: "Amount",
  message: "amount must be greater than zero",
});

interface Routes {
  definitions?: unknown;
  batches?: unknown;
  batch?: unknown;
  errors?: (afterRow: number) => BulkRowError[];
  verb?: { status: number; body: unknown };
  upload?: { status: number; body: unknown };
}

function client(routes: Routes = {}) {
  const posted: { url: string; body: BodyInit | null | undefined }[] = [];
  const fetched: string[] = [];
  const fetcher = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    const json = (body: unknown, status = 200) =>
      new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });

    if (init?.method === "POST") {
      posted.push({ url, body: init.body });
      if (/\/bulk\/batches\/[^/?]+\?/.test(url)) {
        const uploaded = routes.upload ?? { status: 200, body: batch({ id: "new-batch", state: "Received" }) };
        return json(uploaded.body, uploaded.status);
      }

      const verb = routes.verb ?? { status: 200, body: { ok: true, message: "approved" } };
      return json(verb.body, verb.status);
    }

    fetched.push(url);
    if (url.includes("/errors?")) {
      const afterRow = Number(new URL(url, "http://x").searchParams.get("afterRow") ?? 0);
      return json(routes.errors ? routes.errors(afterRow) : [finding(4), finding(9)]);
    }

    if (url.includes("/bulk/batches?")) return json(routes.batches ?? [batch()]);
    if (url.includes("/bulk/batches/")) return json(routes.batch ?? batch());
    if (url.endsWith("/bulk/definitions")) return json(routes.definitions ?? [definition()]);
    return new Response("not found", { status: 404 });
  }) as typeof fetch;

  return { client: new AdminClient({ fetcher }), posted, fetched };
}

const openBatch = async (id = batch().id) => userEvent.click(await screen.findByRole("button", { name: id }));

describe("the bulk intake panel (upload → report → four-eyes gate)", () => {
  it("leads with what is waiting at the gate, and how long it has waited", async () => {
    const { client: api } = client();
    render(<BulkPanel client={api} />);

    const banner = await screen.findByRole("status");
    expect(banner).toHaveTextContent("payments: 1 awaiting approval (oldest 2h)");
  });

  it("says nothing about the gate when nothing waits there", async () => {
    const { client: api } = client({
      definitions: [definition({ awaitingApproval: 0, oldestAwaitingApprovalSeconds: null })],
    });
    render(<BulkPanel client={api} />);

    await screen.findByRole("heading", { name: "Definitions" });
    expect(screen.queryByRole("status")).toBeNull();
  });

  it("filters batches by STATE only — the contract has no definition filter to send", async () => {
    const { client: api, fetched } = client();
    render(<BulkPanel client={api} />);

    await userEvent.click(await screen.findByRole("button", { name: /State/ }));
    await userEvent.click(await screen.findByRole("menuitemcheckbox", { name: /Validated/ }));
    await userEvent.keyboard("{Escape}");

    await waitFor(() => expect(fetched.some((url) => url.includes("state=Validated"))).toBe(true));
    expect(fetched.every((url) => !url.includes("definition="))).toBe(true);
  });

  it("opens a batch and shows the row ledger the server reported", async () => {
    const { client: api } = client();
    render(<BulkPanel client={api} />);

    await openBatch();

    const detail = await screen.findByTestId("batch-detail");
    expect(detail).toHaveTextContent("120");
    expect(detail).toHaveTextContent("118");
    // Two findings, same teaching message — the report lists both rows, not one.
    expect(within(detail).getAllByText("amount must be greater than zero")).toHaveLength(2);
  });

  it("walks the validation report by keyset — afterRow is the last row number seen", async () => {
    const pages: Record<number, BulkRowError[]> = {
      0: Array.from({ length: 100 }, (_, index) => finding(index + 1)),
      100: [finding(101)],
    };
    const { client: api, fetched } = client({ errors: (afterRow) => pages[afterRow] ?? [] });
    render(<BulkPanel client={api} />);

    await openBatch();
    await userEvent.click(await screen.findByRole("button", { name: /more/i }));

    await waitFor(() => expect(fetched.some((url) => url.includes("afterRow=100"))).toBe(true));
  });

  it("the gate appears only on a validated batch", async () => {
    const { client: api } = client({ batch: batch({ state: "Completed", invalidRows: 0 }), batches: [batch({ state: "Completed" })] });
    render(<BulkPanel client={api} />);

    await openBatch();
    await screen.findByTestId("batch-detail");

    expect(screen.queryByRole("button", { name: "approve" })).toBeNull();
    expect(screen.queryByRole("button", { name: "reject" })).toBeNull();
  });

  it("approving posts the frozen route through the confirm gate", async () => {
    const { client: api, posted } = client();
    render(<BulkPanel client={api} />);

    await openBatch();
    await userEvent.click(await screen.findByRole("button", { name: "approve" }));
    const dialog = screen.getByRole("alertdialog", { name: "confirm approve" });
    await userEvent.click(within(dialog).getByRole("button", { name: "approve" }));

    await waitFor(() => expect(posted).toHaveLength(1));
    expect(posted[0].url).toContain(`/goldpath/admin/bulk/batches/${batch().id}/approve`);
  });

  it("rejecting refuses to fire without the evidence note the contract demands", async () => {
    const { client: api, posted } = client();
    render(<BulkPanel client={api} />);

    await openBatch();
    await userEvent.click(await screen.findByRole("button", { name: "reject" }));

    const dialog = screen.getByRole("alertdialog", { name: "confirm reject" });
    const confirm = within(dialog).getByRole("button", { name: "reject" });
    expect(confirm).toBeDisabled();

    await userEvent.type(within(dialog).getByLabelText("reason (required)"), "duplicate file");
    await userEvent.click(confirm);

    await waitFor(() => expect(posted).toHaveLength(1));
    expect(posted[0].url).toContain(`/goldpath/admin/bulk/batches/${batch().id}/reject`);
    expect(JSON.parse(String(posted[0].body))).toEqual({ note: "duplicate file" });
  });

  it("a refusal from the engine surfaces verbatim — the console never paraphrases", async () => {
    const { client: api } = client({
      verb: { status: 400, body: { ok: false, message: "the batch is not awaiting approval" } },
    });
    render(<BulkPanel client={api} />);

    await openBatch();
    await userEvent.click(await screen.findByRole("button", { name: "approve" }));
    const dialog = screen.getByRole("alertdialog", { name: "confirm approve" });
    await userEvent.click(within(dialog).getByRole("button", { name: "approve" }));

    expect(await screen.findByText("the batch is not awaiting approval")).toBeInTheDocument();
  });

  it("uploads the file as a RAW body to the definition's route", async () => {
    const { client: api, posted } = client();
    render(<BulkPanel client={api} />);

    await userEvent.upload(
      await screen.findByLabelText("batch file"),
      new File(["id,amount\n1,10"], "payments.csv", { type: "text/csv" }),
    );
    await userEvent.click(screen.getByRole("button", { name: "upload" }));
    const dialog = screen.getByRole("alertdialog", { name: "confirm upload" });
    expect(dialog).toHaveTextContent("Upload payments.csv into payments?");
    await userEvent.click(within(dialog).getByRole("button", { name: "upload" }));

    await waitFor(() => expect(posted).toHaveLength(1));
    expect(posted[0].url).toContain("/goldpath/admin/bulk/batches/payments?fileName=payments.csv");
    expect(posted[0].body).toBeInstanceOf(File);
  });

  it("a failed upload never reads as success", async () => {
    const { client: api } = client({ upload: { status: 500, body: {} } });
    render(<BulkPanel client={api} />);

    await userEvent.upload(
      await screen.findByLabelText("batch file"),
      new File(["broken"], "payments.csv", { type: "text/csv" }),
    );
    await userEvent.click(screen.getByRole("button", { name: "upload" }));
    await userEvent.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "upload" }));

    expect(await screen.findByText(/did not reach the server/)).toBeInTheDocument();
  });

  it("a definition list that will not load says so instead of showing an empty panel", async () => {
    const fetcher = (async () => new Response("", { status: 503 })) as typeof fetch;
    render(<BulkPanel client={new AdminClient({ fetcher })} />);

    expect(await screen.findByRole("alert")).toHaveTextContent(/bulk definitions could not be loaded/i);
  });

  it("the gate's outcome OUTLIVES the gate — approving unmounts the buttons, not the message", async () => {
    // The server's truth changes under the panel: the batch leaves the gated state, so
    // the approve/reject controls disappear on the refresh that follows the verb.
    let state = "Validated";
    const fetcher = (async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { "content-type": "application/json" } });
      if (init?.method === "POST") {
        state = "Approved";
        return json({ ok: true, message: "approved" });
      }

      if (url.includes("/errors?")) return json([]);
      if (url.includes("/bulk/batches?")) return json([batch({ state })]);
      if (url.includes("/bulk/batches/")) return json(batch({ state }));
      return json([definition()]);
    }) as typeof fetch;

    render(<BulkPanel client={new AdminClient({ fetcher })} />);

    await openBatch();
    await userEvent.click(await screen.findByRole("button", { name: "approve" }));
    await userEvent.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "approve" }));

    await waitFor(() => expect(screen.queryByRole("button", { name: "approve" })).toBeNull());
    expect(screen.getByText("approved")).toBeInTheDocument();
  });

  it("a decision that never reached the server is NOT reported as recorded", async () => {
    // The verb envelope never came back — 503 is neither an ok nor a refusal, and the
    // operator must not walk away believing the gate moved.
    const { client: api } = client({ verb: { status: 503, body: {} } });
    render(<BulkPanel client={api} />);

    await openBatch();
    await userEvent.click(await screen.findByRole("button", { name: "approve" }));
    await userEvent.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "approve" }));

    expect(await screen.findByText(/did not reach the server — it may not have been recorded/)).toBeInTheDocument();
  });

  it("a decided batch carries its evidence: who, when, and why", async () => {
    const decided = batch({
      state: "Rejected",
      decidedAt: "2026-07-27T10:00:00Z",
      decidedBy: "ops@example.com",
      decisionNote: "line 3 is malformed",
    });
    const { client: api } = client({ batch: decided, batches: [decided] });
    render(<BulkPanel client={api} />);

    await openBatch();
    const detail = await screen.findByTestId("batch-detail");
    expect(detail).toHaveTextContent("Rejected by ops@example.com at 2026-07-27T10:00:00Z");
    expect(detail).toHaveTextContent("line 3 is malformed");
  });

  it("a definition with nothing in it says so, instead of showing a bare name", async () => {
    const { client: api } = client({ definitions: [definition({ batchesByState: {}, awaitingApproval: 0 })] });
    render(<BulkPanel client={api} />);

    expect(await screen.findByText("no batches yet")).toBeInTheDocument();
  });
});
