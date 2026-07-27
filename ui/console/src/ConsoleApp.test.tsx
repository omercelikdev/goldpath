import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ConsoleApp } from "./ConsoleApp";

/**
 * A fake estate: two services with DIFFERENT compositions, so switching has to re-discover
 * rather than reuse what it already drew.
 */
function estate(options: { registry?: unknown; registryStatus?: number } = {}) {
  const asked: string[] = [];
  const fetcher = (async (input: RequestInfo | URL) => {
    const url = String(input);
    const json = (body: unknown, status = 200) =>
      new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });

    if (url.includes("console.config.json")) {
      if (options.registryStatus && options.registryStatus !== 200) return new Response("", { status: options.registryStatus });
      return json(
        options.registry ?? {
          services: [
            { name: "payments", adminBaseUrl: "https://payments.internal" },
            { name: "claims", adminBaseUrl: "https://claims.internal" },
          ],
        },
      );
    }

    asked.push(url);
    // payments composes jobs + bulk; claims composes archival only.
    if (url.startsWith("https://payments.internal")) {
      if (url.includes("/jobs/fleets")) return json([]);
      if (url.includes("/bulk/definitions")) return json([]);
      return new Response("", { status: 404 });
    }

    if (url.startsWith("https://claims.internal")) {
      if (url.includes("/archival/definitions")) return json([]);
      return new Response("", { status: 404 });
    }

    return new Response("", { status: 404 });
  }) as typeof fetch;

  return { fetcher, asked };
}

describe("the console across services", () => {
  it("lists the registry's services and lands on the first", async () => {
    const { fetcher } = estate();
    render(<ConsoleApp fetcher={fetcher} search="" />);

    const picker = await screen.findByLabelText(/service/i);
    expect(picker).toHaveValue("payments");
    expect(await screen.findByRole("button", { name: "Bulk intake" })).toBeInTheDocument();
  });

  it("switching service RE-DISCOVERS — one service's panels never appear under another's name", async () => {
    const { fetcher, asked } = estate();
    render(<ConsoleApp fetcher={fetcher} search="" />);

    await screen.findByRole("button", { name: "Bulk intake" });

    await userEvent.selectOptions(await screen.findByLabelText(/service/i), "claims");

    // Claims composes archival only: bulk must be GONE, not carried over.
    expect(await screen.findByRole("button", { name: "Archival" })).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole("button", { name: "Bulk intake" })).toBeNull());

    // And the probes went to the claims service, not to payments again.
    expect(asked.some((url) => url.startsWith("https://claims.internal"))).toBe(true);
  });

  it("a single-service console shows no picker at all — nothing to choose between", async () => {
    const fetcher = (async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("console.config.json")) return new Response("", { status: 404 });
      return new Response(JSON.stringify([]), { status: 200, headers: { "content-type": "application/json" } });
    }) as typeof fetch;

    render(<ConsoleApp fetcher={fetcher} search="" />);

    await screen.findByRole("button", { name: "Runs" });
    expect(screen.queryByLabelText(/service/i)).toBeNull();
  });

  it("a registry that failed to load is ANNOUNCED — an operator who configured four services must not silently see one", async () => {
    const { fetcher } = estate({ registryStatus: 500 });
    render(<ConsoleApp fetcher={fetcher} search="" />);

    expect(await screen.findByRole("alert")).toHaveTextContent(/service registry answered 500 — showing this service only/);
  });

  it("?base= drives one named service, whatever the registry says", async () => {
    const { fetcher } = estate();
    render(<ConsoleApp fetcher={fetcher} search="?base=https://payments.internal" />);

    await screen.findByRole("button", { name: "Bulk intake" });
    expect(screen.queryByLabelText(/service/i)).toBeNull();   // one service: no picker
  });
});
