import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AdminClient, type ApprovalRequestDetail, type ApprovalRequestInfo } from "./adminClient";
import { ApprovalsPanel } from "./ApprovalsPanel";

const row = (over: Partial<ApprovalRequestInfo> = {}): ApprovalRequestInfo => ({
  id: "a1b2c3d4-0000-4000-8000-000000000001",
  ladder: "payment-run",
  subject: "P26-1",
  amount: 1_000_000,
  requestedBy: "maker",
  requestedAt: "2026-08-28T06:00:00Z",
  pendingRole: "manager",
  pendingSince: "2026-08-28T06:00:00Z",
  status: "Pending",
  signatureCount: 1,
  requiredApprovals: 2,
  ...over,
});

const detail = (over: Partial<ApprovalRequestInfo> = {}): ApprovalRequestDetail => ({
  request: row(over),
  trail: [
    { at: "2026-08-28T06:00:00Z", actor: "maker", action: "requested", detail: "routed to manager for 1000000" },
    { at: "2026-08-28T06:10:00Z", actor: "manager-one", action: "signed", detail: "1/2 at manager: first signature" },
  ],
  signatures: [{ requestId: row().id, signedBy: "manager-one", role: "manager", at: "2026-08-28T06:10:00Z" }],
});

interface Routes {
  list?: unknown;
  detail?: unknown;
  decide?: { ok: boolean; message: string };
}

function client(routes: Routes = {}) {
  const fetched: string[] = [];
  const fetcher = (async (input: RequestInfo | URL) => {
    const url = String(input);
    fetched.push(url);
    const json = (body: unknown, status = 200) =>
      new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });

    if (url.includes("/approve") || url.includes("/reject")) {
      const verdict = routes.decide ?? { ok: true, message: "Applied" };
      return json(verdict, verdict.ok ? 200 : 400);
    }

    // The by-id route must be matched BEFORE the list route it is nested under.
    if (/\/requests\/[^/?]+$/.test(url)) return json(routes.detail ?? detail());
    if (url.includes("/requests")) return json(routes.list ?? [row()]);
    return json([], 404);
  }) as typeof fetch;

  return { api: new AdminClient({ fetcher }), fetched };
}

describe("ApprovalsPanel — the worklist and its rules", () => {
  it("lists the queue with the quorum said as a number", async () => {
    const { api, fetched } = client();
    render(<ApprovalsPanel client={api} />);

    expect(await screen.findByText("P26-1")).toBeInTheDocument();
    expect(screen.getByText("1/2")).toBeInTheDocument();
    expect(screen.getByText("manager")).toBeInTheDocument();
    // The default lens is the WORKLIST: pending only, said in the query itself.
    expect(fetched.some((url) => url.includes("status=Pending"))).toBe(true);
  });

  it("a decided row shows no pending role and no quorum", async () => {
    const { api } = client({ list: [row({ status: "Granted", decidedBy: "manager-two", signatureCount: 2 })] });
    render(<ApprovalsPanel client={api} />);

    await screen.findByText("P26-1");
    // Both placeholders: the pending-role column and the quorum column.
    expect(screen.getAllByText("—").length).toBeGreaterThanOrEqual(2);
  });

  it("toggling a status facet re-reads with R3 repeats", async () => {
    const { api, fetched } = client();
    render(<ApprovalsPanel client={api} />);
    await screen.findByText("P26-1");

    await userEvent.click(screen.getByRole("button", { name: /status/i }));
    await userEvent.click(await screen.findByRole("menuitemcheckbox", { name: /Rejected/ }));
    await userEvent.keyboard("{Escape}");

    await waitFor(() => expect(fetched.some((url) => url.includes("status=Pending") && url.includes("status=Rejected"))).toBe(true));
  });

  it("opening a row shows the story: quorum, signatures, trail", async () => {
    const { api } = client();
    render(<ApprovalsPanel client={api} />);

    await userEvent.click(await screen.findByRole("button", { name: "P26-1" }));

    const sheet = await screen.findByTestId("approval-detail");
    expect(within(sheet).getByText("1/2")).toBeInTheDocument();
    expect(within(sheet).getByText("manager-one")).toBeInTheDocument();
    expect(within(sheet).getByText(/1\. requested/)).toBeInTheDocument();
    expect(within(sheet).getByText(/2\. signed/)).toBeInTheDocument();
  });

  it("a decided request opens with decider and reason, and no decide form", async () => {
    const { api } = client({
      list: [row({ status: "Rejected" })],
      detail: detail({ status: "Rejected", decidedBy: "checker", reason: "collateral missing" }),
    });
    render(<ApprovalsPanel client={api} />);

    await userEvent.click(await screen.findByRole("button", { name: "P26-1" }));

    const sheet = await screen.findByTestId("approval-detail");
    expect(within(sheet).getByText("checker")).toBeInTheDocument();
    expect(within(sheet).getByText("collateral missing")).toBeInTheDocument();
    expect(within(sheet).queryByRole("button", { name: "Approve" })).not.toBeInTheDocument();
  });

  it("approving posts the rung's role and reports Applied", async () => {
    const { api, fetched } = client();
    render(<ApprovalsPanel client={api} />);

    await userEvent.click(await screen.findByRole("button", { name: "P26-1" }));
    await userEvent.click(await screen.findByRole("button", { name: "Approve" }));

    expect(await screen.findByTestId("verb-message")).toHaveTextContent("Applied");
    expect(fetched.some((url) => url.includes("/approve"))).toBe(true);
  });

  it("a refusal shows the RULE's name verbatim — the engine explained itself", async () => {
    const { api } = client({ decide: { ok: false, message: "FourEyesViolation" } });
    render(<ApprovalsPanel client={api} />);

    await userEvent.click(await screen.findByRole("button", { name: "P26-1" }));
    await userEvent.click(await screen.findByRole("button", { name: "Reject" }));

    expect(await screen.findByTestId("verb-message")).toHaveTextContent("FourEyesViolation");
  });

  it("the supersedes link surfaces when the request is a resubmission", async () => {
    const { api } = client({ detail: detail({ supersedesId: "00000000-0000-4000-8000-00000000dead" }) });
    render(<ApprovalsPanel client={api} />);

    await userEvent.click(await screen.findByRole("button", { name: "P26-1" }));

    const sheet = await screen.findByTestId("approval-detail");
    expect(within(sheet).getByText("00000000-0000-4000-8000-00000000dead")).toBeInTheDocument();
  });
});
