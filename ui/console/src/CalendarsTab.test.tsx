import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AdminClient } from "./adminClient";
import { CalendarsTab } from "./CalendarsTab";

function api(calendars: unknown) {
  const sent: { url: string; method: string; body: unknown }[] = [];
  const fetcher = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { "content-type": "application/json" } });
    if (init?.method && init.method !== "GET") {
      sent.push({ url, method: init.method, body: init.body ? JSON.parse(String(init.body)) : undefined });
      return json({ ok: true, message: "done" });
    }

    if (url.includes("/calendars")) return json(calendars);
    return new Response("not found", { status: 404 });
  }) as typeof fetch;
  return { client: new AdminClient({ fetcher }), sent };
}

describe("the calendars tab (frozen CRUD, first screened in U5)", () => {
  it("lists what each calendar is and who rides it", async () => {
    const { client } = api([
      { name: "tr-holidays", description: "public holidays", usedByTriggers: ["eod-cron", "sweep-cron"] },
    ]);
    render(<CalendarsTab client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    expect(await screen.findByText("tr-holidays")).toBeInTheDocument();
    expect(screen.getByText(/used by eod-cron, sweep-cron/)).toBeInTheDocument();
  });

  it("deleting a calendar names the triggers that will LOSE its exclusions", async () => {
    const user = userEvent.setup();
    const { client, sent } = api([{ name: "tr-holidays", description: null, usedByTriggers: ["eod-cron"] }]);
    render(<CalendarsTab client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    await user.click(await screen.findByRole("button", { name: "delete" }));

    // The damage is invisible otherwise: the trigger keeps firing, just without the
    // exclusions someone set up for a reason.
    const dialog = screen.getByRole("alertdialog");
    expect(dialog).toHaveTextContent(/1 trigger ride it \(eod-cron\) and will lose its exclusions/);
    await user.click(dialog.querySelector("button")!);

    await waitFor(() => expect(sent).toHaveLength(1));
    expect(sent[0].method).toBe("DELETE");
    expect(sent[0].url).toContain("/fleets/it-cluster/calendars/tr-holidays");
  });

  it("a deletion reports itself even though the row it lived on is gone", async () => {
    const user = userEvent.setup();
    const fetcher = (async (input: RequestInfo | URL, init?: RequestInit) => {
      const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { "content-type": "application/json" } });
      if (init?.method && init.method !== "GET") return json({ ok: true, message: "calendar 'tr-holidays' deleted" });
      return json([{ name: "tr-holidays", description: null, usedByTriggers: [] }]);
    }) as typeof fetch;
    render(<CalendarsTab client={new AdminClient({ fetcher })} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    await user.click(await screen.findByRole("button", { name: "delete" }));
    await user.click(screen.getByRole("alertdialog").querySelector("button")!);

    expect(await screen.findByText("calendar 'tr-holidays' deleted")).toBeInTheDocument();
  });

  it("an unused calendar gets the plain confirm — no invented consequence", async () => {
    const user = userEvent.setup();
    const { client } = api([{ name: "spare", description: null, usedByTriggers: [] }]);
    render(<CalendarsTab client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    await user.click(await screen.findByRole("button", { name: "delete" }));

    expect(screen.getByRole("alertdialog")).toHaveTextContent("Delete calendar spare?");
    expect(screen.getByRole("alertdialog")).not.toHaveTextContent(/lose its exclusions/);
  });

  it("creates a HOLIDAY calendar with the dates it was given", async () => {
    const user = userEvent.setup();
    const { client, sent } = api([]);
    render(<CalendarsTab client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    await user.click(await screen.findByRole("button", { name: "add a calendar" }));
    await user.type(screen.getByLabelText("Name"), "tr-holidays");
    await user.type(screen.getByLabelText(/Excluded dates/), "2026-01-01, 2026-04-23");
    await user.click(screen.getByRole("button", { name: "create it" }));
    await user.click(screen.getByRole("alertdialog").querySelector("button")!);

    await waitFor(() => expect(sent).toHaveLength(1));
    expect(sent[0].method).toBe("PUT");
    expect(sent[0].body).toMatchObject({ type: "holiday", excludedDates: ["2026-01-01", "2026-04-23"] });
  });

  it("creates a WEEKLY calendar from the days that were ticked", async () => {
    const user = userEvent.setup();
    const { client, sent } = api([]);
    render(<CalendarsTab client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    await user.click(await screen.findByRole("button", { name: "add a calendar" }));
    await user.type(screen.getByLabelText("Name"), "weekends");
    await user.selectOptions(screen.getByLabelText("Type"), "weekly");
    await user.click(screen.getByLabelText("Saturday"));
    await user.click(screen.getByLabelText("Sunday"));
    await user.click(screen.getByRole("button", { name: "create it" }));
    await user.click(screen.getByRole("alertdialog").querySelector("button")!);

    await waitFor(() => expect(sent).toHaveLength(1));
    // Sunday is 0 and Saturday 6 — the wire carries DayOfWeek, not labels.
    expect(sent[0].body).toMatchObject({ type: "weekly", excludedDays: [6, 0] });
  });

  it("creates a CRON calendar and sends no date list", async () => {
    const user = userEvent.setup();
    const { client, sent } = api([]);
    render(<CalendarsTab client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    await user.click(await screen.findByRole("button", { name: "add a calendar" }));
    await user.type(screen.getByLabelText("Name"), "nights");
    await user.selectOptions(screen.getByLabelText("Type"), "cron");
    await user.type(screen.getByLabelText(/Excluded times/), "0 0 0-6 * * ?");
    await user.click(screen.getByRole("button", { name: "create it" }));
    await user.click(screen.getByRole("alertdialog").querySelector("button")!);

    await waitFor(() => expect(sent).toHaveLength(1));
    expect(sent[0].body).toMatchObject({ type: "cron", cronExpression: "0 0 0-6 * * ?", excludedDates: null, excludedDays: null });
  });

  it("offers nothing to send until the shape is complete", async () => {
    const user = userEvent.setup();
    const { client } = api([]);
    render(<CalendarsTab client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    await user.click(await screen.findByRole("button", { name: "add a calendar" }));
    await user.type(screen.getByLabelText("Name"), "tr-holidays");

    // Named but empty: a holiday calendar with no dates excludes nothing.
    expect(screen.queryByRole("button", { name: "create it" })).toBeNull();
  });

  it("a fleet with no calendar says so", async () => {
    const { client } = api([]);
    render(<CalendarsTab client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    expect(await screen.findByText(/has no calendar/)).toBeInTheDocument();
  });

  it("a calendars surface that will not answer says so", async () => {
    const fetcher = (async () => new Response("", { status: 503 })) as typeof fetch;
    render(<CalendarsTab client={new AdminClient({ fetcher })} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    expect(await screen.findByRole("alert")).toHaveTextContent(/could not be read/);
  });
});
