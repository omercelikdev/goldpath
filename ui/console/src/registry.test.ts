import { loadRegistry, SAME_ORIGIN } from "./registry";

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });

const fetcherFor = (response: Response | (() => never)) =>
  (async () => (typeof response === "function" ? response() : response)) as typeof fetch;

describe("the service registry (config across services, discovery within one)", () => {
  it("reads the adopter's services, in the order they wrote them", async () => {
    const fetcher = fetcherFor(
      json({ services: [{ name: "payments", adminBaseUrl: "https://payments.internal" }, { name: "claims", adminBaseUrl: "https://claims.internal" }] }),
    );

    const { services, problem } = await loadRegistry(fetcher, "");

    expect(services).toEqual([
      { name: "payments", adminBaseUrl: "https://payments.internal" },
      { name: "claims", adminBaseUrl: "https://claims.internal" },
    ]);
    expect(problem).toBeUndefined();
  });

  it("no registry means ONE service — the app that served the console", async () => {
    const { services, problem } = await loadRegistry(fetcherFor(new Response("", { status: 404 })), "");

    expect(services).toEqual([SAME_ORIGIN]);
    expect(problem).toBeUndefined();   // a single-service console is normal, not a fault
  });

  it("?base= wins — the dev override and the way to point at a service that did not serve this console", async () => {
    const fetcher = fetcherFor(json({ services: [{ name: "payments", adminBaseUrl: "https://payments.internal" }] }));

    const { services } = await loadRegistry(fetcher, "?base=http://localhost:5310");

    expect(services).toEqual([{ name: "http://localhost:5310", adminBaseUrl: "http://localhost:5310" }]);
  });

  it("an EMPTY ?base= means same-origin, named as such", async () => {
    const { services } = await loadRegistry(fetcherFor(new Response("", { status: 404 })), "?base=");

    expect(services).toEqual([{ name: "same-origin", adminBaseUrl: "" }]);
  });

  it("a registry that cannot be READ says so — a quiet fallback would hide the other services", async () => {
    const { services, problem } = await loadRegistry(fetcherFor(new Response("", { status: 500 })), "");

    expect(services).toEqual([SAME_ORIGIN]);
    expect(problem).toMatch(/answered 500/);
  });

  it("a registry that is not JSON at all is a problem, not a crash", async () => {
    const { services, problem } = await loadRegistry(fetcherFor(new Response("<html>index</html>", { status: 200 })), "");

    expect(services).toEqual([SAME_ORIGIN]);
    expect(problem).toMatch(/could not be read/);
  });

  it("an unreachable registry is a problem, not a crash", async () => {
    const { problem } = await loadRegistry(
      fetcherFor(() => {
        throw new TypeError("network down");
      }),
      "",
    );

    expect(problem).toMatch(/could not be read/);
  });

  it("entries without a name are dropped, and a file with nothing usable says so", async () => {
    const { services, problem } = await loadRegistry(
      fetcherFor(json({ services: [{ adminBaseUrl: "https://nameless.internal" }, { name: "   " }] })),
      "",
    );

    expect(services).toEqual([SAME_ORIGIN]);
    expect(problem).toMatch(/no service with a name/);
  });

  it("a same-origin entry is legitimate: the console's own app, listed beside the others", async () => {
    const { services } = await loadRegistry(
      fetcherFor(json({ services: [{ name: "this app", adminBaseUrl: "" }, { name: "claims", adminBaseUrl: "https://claims.internal" }] })),
      "",
    );

    expect(services[0]).toEqual({ name: "this app", adminBaseUrl: "" });
  });
});
