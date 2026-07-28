import { render, screen, within, waitFor } from "@testing-library/react";
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
  it("lands on TODAY — the operator's question is 'is anything wrong', not 'what exists'", async () => {
    const { fetcher } = estate();
    render(<ConsoleApp fetcher={fetcher} search="" />);

    expect(await screen.findByTestId("triage-home")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Today" })).toHaveAttribute("aria-current", "page");
  });

  it("lists the registry's services and navigates the first one's modules", async () => {
    const { fetcher } = estate();
    render(<ConsoleApp fetcher={fetcher} search="" />);

    const picker = await screen.findByLabelText(/service/i);
    expect(picker).toHaveValue("payments");
    await userEvent.click(await screen.findByRole("button", { name: "Bulk intake" }));
    expect(await screen.findByTestId("bulk-panel")).toBeInTheDocument();
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

  it("a forbidden capability is NAMED with the server's words", async () => {
    const fetcher = (async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("console.config.json")) return new Response("", { status: 404 });
      return new Response(JSON.stringify({ message: "the 'goldpath-ops' role is required" }), {
        status: 403,
        headers: { "content-type": "application/json" },
      });
    }) as typeof fetch;

    render(<ConsoleApp fetcher={fetcher} search="" />);

    await userEvent.click(await screen.findByRole("button", { name: "Runs" }));
    expect(await screen.findByText(/lacks the ops role/)).toHaveTextContent("the 'goldpath-ops' role is required");
  });

  it("a REFUSED capability is named too — composed, reachable, and saying no", async () => {
    const fetcher = (async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("console.config.json")) return new Response("", { status: 404 });
      return new Response(JSON.stringify({ title: "Tenant could not be resolved." }), {
        status: 400,
        headers: { "content-type": "application/json" },
      });
    }) as typeof fetch;

    render(<ConsoleApp fetcher={fetcher} search="" />);

    await userEvent.click(await screen.findByRole("button", { name: "Runs" }));
    expect(await screen.findByText(/composed here but refused this request/)).toHaveTextContent("Tenant could not be resolved.");
  });

  it("a service with NO Goldpath surface says so instead of an empty frame", async () => {
    const fetcher = (async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("console.config.json")) return new Response("", { status: 404 });
      return new Response("", { status: 404 });
    }) as typeof fetch;

    render(<ConsoleApp fetcher={fetcher} search="" />);

    // Today still answers — it just has nothing to report from a service that composes none.
    expect(await screen.findByTestId("triage-home")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Runs" })).toBeNull();
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

  it("a service that never answers is reported as unreachable, on Today and in its own screen", async () => {
    const fetcher = (async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("console.config.json")) return new Response("", { status: 404 });
      throw new TypeError("network down");   // what a blocked or dead service looks like
    }) as typeof fetch;

    render(<ConsoleApp fetcher={fetcher} search="" />);

    // Today says it first — a blind spot outranks everything, including nothing.
    expect(await screen.findByText(/did not answer at all/)).toBeInTheDocument();
    // And there is no module nav to click into: every link would say the same thing.
    expect(screen.queryByRole("button", { name: "Runs" })).toBeNull();
  });

  it("a triage row opens the panel it belongs to, on the SERVICE it belongs to", async () => {
    const fetcher = (async (input: RequestInfo | URL) => {
      const url = String(input);
      const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { "content-type": "application/json" } });
      if (url.includes("console.config.json")) {
        return json({
          services: [
            { name: "payments", adminBaseUrl: "https://payments.internal" },
            { name: "claims", adminBaseUrl: "https://claims.internal" },
          ],
        });
      }

      // Only CLAIMS has something wrong: a batch waiting at its four-eyes gate.
      if (url.startsWith("https://claims.internal")) {
        if (url.includes("/bulk/definitions")) {
          return json([{ name: "payouts", batchesByState: { Validated: 1 }, awaitingApproval: 1, oldestAwaitingApprovalSeconds: 7200 }]);
        }

        if (url.includes("/bulk/batches")) return json([]);
        return new Response("", { status: 404 });
      }

      if (url.includes("/jobs/fleets")) return json([]);
      return new Response("", { status: 404 });
    }) as typeof fetch;

    render(<ConsoleApp fetcher={fetcher} search="" />);

    const row = await screen.findByRole("button", { name: /awaiting approval in payouts/ });
    await userEvent.click(row);

    // The console switched service AND section — the row is a deep link, not a label.
    expect(await screen.findByTestId("bulk-panel")).toBeInTheDocument();
    expect(screen.getByLabelText(/service/i)).toHaveValue("claims");
  });

  it("a registry that failed to load is ANNOUNCED — an operator who configured four services must not silently see one", async () => {
    const { fetcher } = estate({ registryStatus: 500 });
    render(<ConsoleApp fetcher={fetcher} search="" />);

    expect(await screen.findByRole("alert")).toHaveTextContent(/service registry answered 500 — showing this service only/);
  });

  it("a partially broken registry names the loss — the console did NOT fall back, it is incomplete", async () => {
    const { fetcher } = estate({
      registry: { services: [{ name: "payments", adminBaseUrl: "https://payments.internal" }, { adminBaseUrl: "https://nameless.internal" }] },
    });
    render(<ConsoleApp fetcher={fetcher} search="" />);

    const banner = await screen.findByRole("alert");
    expect(banner).toHaveTextContent("1 registry entry has no name and was skipped");
    expect(banner).toHaveTextContent("missing a service you configured");
    expect(banner).not.toHaveTextContent("showing this service only");
  });

  it("?base= drives one named service, whatever the registry says", async () => {
    const { fetcher } = estate();
    render(<ConsoleApp fetcher={fetcher} search="?base=https://payments.internal" />);

    await screen.findByRole("button", { name: "Bulk intake" });
    expect(screen.queryByLabelText(/service/i)).toBeNull();   // one service: no picker
  });

  it("the rail renders ONE Modules group — the owner's amendment, pinned", async () => {
    render(<ConsoleApp fetcher={estate().fetcher} search="" />);
    const rail = await screen.findByTestId("shell-rail");
    await screen.findByRole("button", { name: "Runs" });

    // One family, one heading — and none of the four concern-groups B1 shipped with.
    expect(within(rail).getAllByText(/^Modules$/)).toHaveLength(1);
    for (const retired of ["Execution", "Intake", "Outbound", "Compliance"]) {
      expect(within(rail).queryByText(new RegExp(`^${retired}$`))).toBeNull();
    }
  });
});
