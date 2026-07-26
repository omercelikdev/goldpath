import { AdminClient, AdminHttpError, MODULES } from "./adminClient";

type Route = string;
const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });

function fakeFetch(routes: Record<Route, Response | (() => Response)>) {
  const calls: { url: string; init?: RequestInit }[] = [];
  const fetcher = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    calls.push({ url, init });
    const key = Object.keys(routes).find((route) => url.endsWith(route) || url.includes(route));
    if (!key) return new Response("not found", { status: 404 });
    const entry = routes[key];
    return typeof entry === "function" ? entry() : entry.clone();
  }) as typeof fetch;
  return { fetcher, calls };
}

describe("the admin client (the console's only door — the FROZEN contract)", () => {
  it("discovers capabilities from the contract's own list roots", async () => {
    const { fetcher } = fakeFetch({
      "/goldpath/admin/jobs/fleets": json([]),
      "/goldpath/admin/bulk/definitions": json([]),
      "/goldpath/admin/campaign/": json([]),
      // archival + notification are simply not composed into this app.
    });

    const capabilities = await new AdminClient({ fetcher }).discoverCapabilities();

    expect(capabilities.jobs).toBe("present");
    expect(capabilities.bulk).toBe("present");
    expect(capabilities.campaign).toBe("present");
    expect(capabilities.archival).toBe("absent");        // 404 = never composed
    expect(capabilities.notification).toBe("absent");
  });

  it("tells 'forbidden' apart from 'absent' — the panel exists, this operator may not see it", async () => {
    const { fetcher } = fakeFetch({
      "/goldpath/admin/jobs/fleets": new Response("", { status: 403 }),
      "/goldpath/admin/archival/definitions": new Response("", { status: 401 }),
    });

    const capabilities = await new AdminClient({ fetcher }).discoverCapabilities();

    expect(capabilities.jobs).toBe("forbidden");
    expect(capabilities.archival).toBe("forbidden");
    expect(capabilities.bulk).toBe("absent");
  });

  it("an unreachable service yields no panels instead of crashing the console", async () => {
    const fetcher = (async () => {
      throw new TypeError("network down");
    }) as typeof fetch;

    const capabilities = await new AdminClient({ fetcher }).discoverCapabilities();

    expect(MODULES.every((module) => capabilities[module] === "absent")).toBe(true);
  });

  it("clamps take to the contract's [1,500] before the request leaves the browser", async () => {
    const { fetcher, calls } = fakeFetch({ "/runs?": json([]) });
    const client = new AdminClient({ fetcher });

    await client.runs("it-cluster", { take: 10_000 });
    await client.runs("it-cluster", { take: 0 });

    expect(calls[0].url).toContain("take=500");
    expect(calls[1].url).toContain("take=1");
  });

  it("encodes fleet and job names — a name with a slash never forges a route", async () => {
    const { fetcher, calls } = fakeFetch({ "/jobs/": json([]) });

    await new AdminClient({ fetcher }).jobs("eod/nightly");

    expect(calls[0].url).toContain("fleets/eod%2Fnightly/jobs");
    expect(calls[0].url).not.toContain("fleets/eod/nightly/jobs");
  });

  it("returns the verb envelope for BOTH 200 and 400 — refusals are data, not errors", async () => {
    const { fetcher } = fakeFetch({
      "/trigger": json({ ok: true, message: "run 42 scheduled" }),
      "/pause": json({ ok: false, message: "the job is already paused" }, 400),
    });
    const client = new AdminClient({ fetcher });

    expect(await client.triggerJob("it", "eod")).toEqual({ ok: true, message: "run 42 scheduled" });
    expect(await client.pauseJob("it", "eod")).toEqual({ ok: false, message: "the job is already paused" });
  });

  it("throws on an unexpected status so the UI can say something honest", async () => {
    const { fetcher } = fakeFetch({ "/trigger": new Response("", { status: 503 }) });

    await expect(new AdminClient({ fetcher }).triggerJob("it", "eod")).rejects.toBeInstanceOf(AdminHttpError);
  });

  it("carries credentials on every call — the ops floor is the same for the console", async () => {
    const { fetcher, calls } = fakeFetch({ "/fleets": json([]) });

    await new AdminClient({ fetcher }).fleets();

    expect(calls[0].init?.credentials).toBe("include");
  });
});
