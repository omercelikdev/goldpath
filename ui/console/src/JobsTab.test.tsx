import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AdminClient, isPaused, nextFireAt, type TriggerInfo } from "./adminClient";
import { JobsTab } from "./JobsTab";

const trigger = (over: Partial<TriggerInfo> = {}): TriggerInfo => ({
  name: "eod-cron",
  state: "Normal",
  cronExpression: "0 0 3 * * ?",
  calendarName: null,
  nextFireAt: "2026-07-29T00:00:00Z",
  previousFireAt: "2026-07-28T00:00:00Z",
  type: "cron",
  priority: 5,
  misfireInstruction: 0,
  timeZoneId: "Europe/Istanbul",
  startAt: "2026-07-01T00:00:00Z",
  endAt: null,
  timesTriggered: null,
  repeatInterval: null,
  repeatCount: null,
  ...over,
});

function api(jobs: unknown) {
  const sent: { url: string; method: string; body: unknown }[] = [];
  const fetcher = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { "content-type": "application/json" } });
    if (init?.method && init.method !== "GET") {
      sent.push({ url, method: init.method, body: init.body ? JSON.parse(String(init.body)) : undefined });
      return json({ ok: true, message: "done" });
    }

    if (url.endsWith("/jobs")) return json(jobs);
    return new Response("not found", { status: 404 });
  }) as typeof fetch;
  return { client: new AdminClient({ fetcher }), sent };
}

describe("isPaused / nextFireAt — a job's state is a fact about its TRIGGERS", () => {
  it("a job is paused only when EVERY trigger is", () => {
    const job = { name: "eod", triggers: [trigger({ state: "Paused" }), trigger({ name: "b", state: "Normal" })] };

    // One live trigger means it still fires; calling that "paused" would tell an operator
    // the job is safe when it is not.
    expect(isPaused(job)).toBe(false);
    expect(isPaused({ name: "eod", triggers: [trigger({ state: "Paused" })] })).toBe(true);
  });

  it("a job with NO trigger is not paused — it is unscheduled", () => {
    expect(isPaused({ name: "eod", triggers: [] })).toBe(false);
    expect(nextFireAt({ name: "eod", triggers: [] })).toBeNull();
  });

  it("the next fire is the SOONEST of them, not the first in the list", () => {
    const job = {
      name: "eod",
      triggers: [
        trigger({ name: "late", nextFireAt: "2026-08-01T00:00:00Z" }),
        trigger({ name: "soon", nextFireAt: "2026-07-29T00:00:00Z" }),
        trigger({ name: "never", nextFireAt: null }),
      ],
    };

    expect(nextFireAt(job)).toBe("2026-07-29T00:00:00Z");
  });
});

describe("the jobs tab", () => {
  it("shows a paused job as paused — read from its triggers, not from a field", async () => {
    const { client } = api([{ name: "eod", triggers: [trigger({ state: "Paused" })] }]);
    render(<JobsTab client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    // The regression this pins: the console used to read `job.paused`, which the contract
    // has never sent, so a paused job looked exactly like a running one.
    await screen.findByTestId("jobs-tab");
    expect(await screen.findByRole("button", { name: "resume" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "pause" })).toBeNull();
  });

  it("a job with no trigger says nothing will fire it — which is not the same as paused", async () => {
    const { client } = api([{ name: "eod", triggers: [] }]);
    render(<JobsTab client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    expect(await screen.findByText(/no trigger — nothing will fire it/)).toBeInTheDocument();
    // Still offers pause: resuming a job nobody scheduled would fix nothing.
    expect(screen.getByRole("button", { name: "pause" })).toBeInTheDocument();
  });

  it("§7.9: a trigger's calendar LINKS to the Calendars tab when the console can take you there", async () => {
    const user = userEvent.setup();
    const shown: string[] = [];
    const { client } = api([{ name: "eod", triggers: [trigger({ calendarName: "tr-holidays" })] }]);
    render(
      <JobsTab
        client={client}
        fleet="it-cluster"
        refreshToken={0}
        onChanged={() => {}}
        onShowCalendars={() => shown.push("calendars")}
      />,
    );

    await user.click(await screen.findByRole("button", { name: "eod" }));
    await user.click(await screen.findByRole("button", { name: /calendar tr-holidays/ }));
    expect(shown).toEqual(["calendars"]);
  });

  it("§7.9: a job ask from History opens the sheet without a click", async () => {
    const { client } = api([{ name: "eod", triggers: [trigger()] }]);
    render(
      <JobsTab client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} openJobRequest={{ name: "eod" }} />,
    );

    const sheet = await screen.findByTestId("sheet");
    expect(sheet).toHaveTextContent("0 0 3 * * ?");
  });

  it("opening a job shows its triggers with the facts a cron string cannot carry", async () => {
    const user = userEvent.setup();
    const { client } = api([{ name: "eod", triggers: [trigger()], dataMap: { window: "eod", batchSize: "500" } }]);
    render(<JobsTab client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    await user.click(await screen.findByRole("button", { name: "eod" }));

    expect(screen.getByText("0 0 3 * * ?")).toBeInTheDocument();
    expect(screen.getByText("Europe/Istanbul")).toBeInTheDocument();
    expect(screen.getByText("priority 5")).toBeInTheDocument();
    // The data map is visible and READ-ONLY — the screen says so in words.
    expect(screen.getByText(/read-only/)).toBeInTheDocument();
    expect(screen.getByText("500")).toBeInTheDocument();
  });

  it("a SIMPLE trigger reads as an interval, never as a blank cron", async () => {
    const user = userEvent.setup();
    const { client } = api([{
      name: "poller",
      triggers: [trigger({ type: "simple", cronExpression: null, timeZoneId: null, repeatInterval: "00:15:00", repeatCount: 4, timesTriggered: 2 })],
    }]);
    render(<JobsTab client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    await user.click(await screen.findByRole("button", { name: "poller" }));

    expect(screen.getByText(/every 00:15:00, 4 times · fired 2/)).toBeInTheDocument();
  });

  it("adding a trigger posts the R2.5 route and says it cannot create a job", async () => {
    const user = userEvent.setup();
    const { client, sent } = api([{ name: "eod", triggers: [] }]);
    render(<JobsTab client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    await user.click(await screen.findByRole("button", { name: "eod" }));
    await user.click(screen.getByRole("button", { name: "add a trigger" }));
    expect(screen.getByText(/cannot create one/)).toBeInTheDocument();

    await user.type(screen.getByLabelText("Name"), "month-end");
    await user.type(screen.getByLabelText("Cron"), "0 0 2 L * ?");
    await user.click(screen.getByRole("button", { name: "schedule it" }));
    await user.click(screen.getByRole("alertdialog").querySelector("button")!);

    await waitFor(() => expect(sent).toHaveLength(1));
    expect(sent[0].url).toContain("/goldpath/admin/jobs/fleets/it-cluster/jobs/eod/triggers");
    expect(sent[0].method).toBe("POST");
    expect(sent[0].body).toMatchObject({ name: "month-end", cron: "0 0 2 L * ?" });
  });

  it("an INTERVAL trigger sends an interval and no cron — one kind or the other", async () => {
    const user = userEvent.setup();
    const { client, sent } = api([{ name: "poller", triggers: [] }]);
    render(<JobsTab client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    await user.click(await screen.findByRole("button", { name: "poller" }));
    await user.click(screen.getByRole("button", { name: "add a trigger" }));
    await user.type(screen.getByLabelText("Name"), "every-15");
    await user.click(screen.getByRole("combobox", { name: "trigger kind" }));
    await user.click(await screen.findByRole("option", { name: "interval" }));
    await user.type(screen.getByLabelText("Interval"), "00:15:00");
    await user.click(screen.getByRole("button", { name: "schedule it" }));
    await user.click(screen.getByRole("alertdialog").querySelector("button")!);

    await waitFor(() => expect(sent).toHaveLength(1));
    expect(sent[0].body).toEqual({ name: "every-15", interval: "00:15:00" });
  });

  it("the form does not offer to send an unnamed or empty trigger", async () => {
    const user = userEvent.setup();
    const { client } = api([{ name: "eod", triggers: [] }]);
    render(<JobsTab client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    await user.click(await screen.findByRole("button", { name: "eod" }));
    await user.click(screen.getByRole("button", { name: "add a trigger" }));

    expect(screen.queryByRole("button", { name: "schedule it" })).toBeNull();
    await user.type(screen.getByLabelText("Name"), "month-end");
    expect(screen.queryByRole("button", { name: "schedule it" })).toBeNull();
  });

  it("removing a trigger says the JOB stays, and DELETEs the frozen route", async () => {
    const user = userEvent.setup();
    const { client, sent } = api([{ name: "eod", triggers: [trigger()] }]);
    render(<JobsTab client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    await user.click(await screen.findByRole("button", { name: "eod" }));
    await user.click(screen.getByRole("button", { name: "remove" }));

    const dialog = screen.getByRole("alertdialog");
    expect(dialog).toHaveTextContent(/The JOB stays/);
    await user.click(dialog.querySelector("button")!);

    await waitFor(() => expect(sent).toHaveLength(1));
    expect(sent[0].method).toBe("DELETE");
    expect(sent[0].url).toContain("/jobs/eod/triggers/eod-cron");
  });

  it("triggering a job warns that the run is recorded as started by hand", async () => {
    const user = userEvent.setup();
    const { client, sent } = api([{ name: "eod", triggers: [trigger()] }]);
    render(<JobsTab client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    await user.click(await screen.findByRole("button", { name: "trigger" }));
    expect(screen.getByRole("alertdialog")).toHaveTextContent(/recorded as started by hand/);
    await user.click(screen.getByRole("alertdialog").querySelector("button")!);

    await waitFor(() => expect(sent).toHaveLength(1));
    expect(sent[0].url).toContain("/jobs/eod/trigger");
  });

  it("rescheduling names the move it is about to make, and posts the frozen D7 verb", async () => {
    const user = userEvent.setup();
    const { client, sent } = api([{ name: "eod", triggers: [trigger()] }]);
    render(<JobsTab client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    await user.click(await screen.findByRole("button", { name: "eod" }));
    await user.click(screen.getByRole("button", { name: "change the schedule" }));

    // The form opens on the CURRENT schedule: an operator moving a job by an hour should
    // not have to retype a cron string from memory.
    const cron = screen.getByLabelText("Cron") as HTMLInputElement;
    expect(cron.value).toBe("0 0 3 * * ?");
    await user.clear(cron);
    await user.type(cron, "0 0 4 * * ?");
    await user.click(screen.getByRole("button", { name: "reschedule" }));

    const dialog = screen.getByRole("alertdialog");
    expect(dialog).toHaveTextContent("Move eod-cron from 0 0 3 * * ? to 0 0 4 * * ?");
    await user.click(dialog.querySelector("button")!);

    await waitFor(() => expect(sent).toHaveLength(1));
    expect(sent[0].url).toContain("/jobs/eod/reschedule");
    expect(sent[0].body).toEqual({ cron: "0 0 4 * * ?", timeZoneId: "Europe/Istanbul" });
  });

  it("reschedule shows the trigger the SERVER will move, not the first cron it finds", async () => {
    const user = userEvent.setup();
    // Two cron triggers, and the one the frozen verb acts on is NOT first in the list.
    const { client } = api([{
      name: "eod",
      triggers: [
        trigger({ name: "month-end", cronExpression: "0 0 2 L * ?" }),
        trigger({ name: "eod-cron", cronExpression: "0 0 3 * * ?" }),
      ],
    }]);
    render(<JobsTab client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    await user.click(await screen.findByRole("button", { name: "eod" }));
    await user.click(screen.getByRole("button", { name: "change the schedule" }));

    // Showing month-end's expression while the server moves eod-cron would hand the
    // operator a confident, wrong answer.
    expect((screen.getByLabelText("Cron") as HTMLInputElement).value).toBe("0 0 3 * * ?");
    expect(screen.getByText(/Other triggers on this job are untouched/)).toBeInTheDocument();
  });

  it("rescheduling a job that has no {job}-cron trigger says it will CREATE one", async () => {
    const user = userEvent.setup();
    const { client } = api([{ name: "eod", triggers: [trigger({ name: "month-end" })] }]);
    render(<JobsTab client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    await user.click(await screen.findByRole("button", { name: "eod" }));
    await user.click(screen.getByRole("button", { name: "change the schedule" }));

    expect(screen.getByText(/does not exist yet, so this creates it/)).toBeInTheDocument();
  });

  it("resuming a paused job posts the frozen route", async () => {
    const user = userEvent.setup();
    const { client, sent } = api([{ name: "eod", triggers: [trigger({ state: "Paused" })] }]);
    render(<JobsTab client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    await user.click(await screen.findByRole("button", { name: "resume" }));
    await user.click(screen.getByRole("alertdialog").querySelector("button")!);

    await waitFor(() => expect(sent).toHaveLength(1));
    expect(sent[0].url).toContain("/jobs/eod/resume");
  });

  it("the success message OUTLIVES the form that produced it", async () => {
    const user = userEvent.setup();
    const fetcher = (async (input: RequestInfo | URL, init?: RequestInit) => {
      const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { "content-type": "application/json" } });
      if (init?.method && init.method !== "GET") return json({ ok: true, message: "trigger 'month-end' scheduled" });
      return json([{ name: "eod", triggers: [] }]);
    }) as typeof fetch;
    render(<JobsTab client={new AdminClient({ fetcher })} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    await user.click(await screen.findByRole("button", { name: "eod" }));
    await user.click(screen.getByRole("button", { name: "add a trigger" }));
    await user.type(screen.getByLabelText("Name"), "month-end");
    await user.type(screen.getByLabelText("Cron"), "0 0 2 L * ?");
    await user.click(screen.getByRole("button", { name: "schedule it" }));
    await user.click(screen.getByRole("alertdialog").querySelector("button")!);

    // The form closes on success — and if the message lived INSIDE it, the operator would
    // be left guessing whether the verb landed. The console smoke found exactly that.
    await waitFor(() => expect(screen.queryByRole("button", { name: "schedule it" })).toBeNull());
    expect(screen.getByText("trigger 'month-end' scheduled")).toBeInTheDocument();
  });

  it("a removal reports itself even though the row it lived on is gone", async () => {
    const user = userEvent.setup();
    const fetcher = (async (input: RequestInfo | URL, init?: RequestInit) => {
      const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { "content-type": "application/json" } });
      if (init?.method && init.method !== "GET") return json({ ok: true, message: "trigger 'eod-cron' removed — the job itself is untouched" });
      return json([{ name: "eod", triggers: [trigger()] }]);
    }) as typeof fetch;
    render(<JobsTab client={new AdminClient({ fetcher })} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    await user.click(await screen.findByRole("button", { name: "eod" }));
    await user.click(screen.getByRole("button", { name: "remove" }));
    await user.click(screen.getByRole("alertdialog").querySelector("button")!);

    // The button is quiet BECAUSE its row disappears — so the message has to live above
    // the list. A quiet button whose outcome nobody renders reports nothing at all.
    expect(await screen.findByText(/removed — the job itself is untouched/)).toBeInTheDocument();
  });

  it("a REFUSAL is shown in the server's own words — the console never paraphrases", async () => {
    const user = userEvent.setup();
    const fetcher = (async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const json = (body: unknown, status = 200) =>
        new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });
      if (init?.method && init.method !== "GET") {
        return json({ ok: false, message: "'every other tuesday' is not a valid Quartz cron expression" }, 400);
      }

      return json([{ name: "eod", triggers: [] }]);
    }) as typeof fetch;
    render(<JobsTab client={new AdminClient({ fetcher })} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    await user.click(await screen.findByRole("button", { name: "eod" }));
    await user.click(screen.getByRole("button", { name: "add a trigger" }));
    await user.type(screen.getByLabelText("Name"), "nonsense");
    await user.type(screen.getByLabelText("Cron"), "every other tuesday");
    await user.click(screen.getByRole("button", { name: "schedule it" }));
    await user.click(screen.getByRole("alertdialog").querySelector("button")!);

    expect(await screen.findByText(/not a valid Quartz cron expression/)).toBeInTheDocument();
  });

  it("a jobs surface that will not answer says so", async () => {
    const fetcher = (async () => new Response("", { status: 503 })) as typeof fetch;
    render(<JobsTab client={new AdminClient({ fetcher })} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    expect(await screen.findByRole("alert")).toHaveTextContent(/could not be read/);
  });

  it("a service that dies AFTER the panel loaded keeps the rows and the verb's outcome", async () => {
    const user = userEvent.setup();
    let alive = true;
    const fetcher = (async (input: RequestInfo | URL, init?: RequestInit) => {
      const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { "content-type": "application/json" } });
      if (!alive) throw new TypeError("network down");
      if (init?.method && init.method !== "GET") {
        alive = false;   // the verb lands, and the service dies immediately after
        return json({ ok: true, message: "triggered" });
      }

      return json([{ name: "eod", triggers: [trigger()] }]);
    }) as typeof fetch;
    const { rerender } = render(<JobsTab client={new AdminClient({ fetcher })} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    await user.click(await screen.findByRole("button", { name: "trigger" }));
    await user.click(screen.getByRole("alertdialog").querySelector("button")!);
    rerender(<JobsTab client={new AdminClient({ fetcher })} fleet="it-cluster" refreshToken={1} onChanged={() => {}} />);

    // Blanking the panel here would erase the very message the operator is reading —
    // and a service tends to stop answering exactly when a verb has just been sent.
    expect(await screen.findByText(/last ones it did answer with/)).toBeInTheDocument();
    expect(screen.getByText("eod")).toBeInTheDocument();
    expect(screen.getByText("triggered")).toBeInTheDocument();
  });

  it("a fleet that declares no job says that, rather than showing an empty page", async () => {
    const { client } = api([]);
    render(<JobsTab client={client} fleet="it-cluster" refreshToken={0} onChanged={() => {}} />);

    expect(await screen.findByText(/declares no job/)).toBeInTheDocument();
  });
});
