import { render, screen } from "@testing-library/react";
import { Console } from "./Console";

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });

/** A service that answers ONLY the given probe roots — everything else is 404 (absent). */
function service(present: Record<string, number | undefined>) {
  return (async (input: RequestInfo | URL) => {
    const url = String(input);
    const hit = Object.keys(present).find((route) => url.includes(route));
    if (!hit) return new Response("not found", { status: 404 });
    const status = present[hit] ?? 200;
    if (status !== 200) return new Response("", { status });
    if (url.includes("/fleets")) return json([{ schedulerName: "it-cluster", jobCount: 1, nodes: [] }]);
    return json([]);
  }) as typeof fetch;
}

describe("the console shell (capability discovery decides what EXISTS)", () => {
  it("shows only the sections the service actually composes", async () => {
    render(<Console fetcher={service({ "/jobs/fleets": 200, "/bulk/definitions": 200 })} />);

    expect(await screen.findByRole("button", { name: "Runs" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Bulk intake" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Campaigns" })).toBeNull();     // never composed
    expect(screen.queryByRole("button", { name: "Archival" })).toBeNull();
  });

  it("a forbidden capability is NAMED, not hidden — the operator learns why", async () => {
    render(<Console fetcher={service({ "/jobs/fleets": 403 })} />);

    expect(await screen.findByRole("button", { name: "Runs" })).toBeInTheDocument();
    expect(await screen.findByRole("alert")).toHaveTextContent(/lacks the ops role/i);
  });

  it("a service with no Goldpath surface says so instead of showing an empty frame", async () => {
    render(<Console fetcher={service({})} />);

    expect(await screen.findByText(/No Goldpath admin surface answered here/)).toBeInTheDocument();
  });

  it("a composed-but-unbuilt panel is honest instead of faking a screen", async () => {
    render(<Console fetcher={service({ "/campaign/": 200 })} />);

    expect(await screen.findByText(/its panel is not built yet/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Campaigns" })).toHaveAttribute("aria-current", "page");
  });

  it("lands on the bulk panel when bulk is the only composed module", async () => {
    render(<Console fetcher={service({ "/bulk/definitions": 200 })} />);

    expect(await screen.findByTestId("bulk-panel")).toBeInTheDocument();
  });

  it("lands on the run console when jobs is present", async () => {
    render(<Console fetcher={service({ "/jobs/fleets": 200 })} />);

    expect(await screen.findByTestId("run-console")).toBeInTheDocument();
  });
});
