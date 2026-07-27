import { render, screen } from "@testing-library/react";
import { AdminClient, MODULES, type Capability, type ModuleName } from "./adminClient";
import { composedSections, ServicePanels, type Capabilities } from "./sections";

const capabilities = (
  over: Partial<Record<ModuleName, Capability>> = {},
  fallback: Capability["kind"] = "absent",
): Capabilities =>
  Object.fromEntries(MODULES.map((module) => [module, over[module] ?? { kind: fallback }])) as Capabilities;

const client = new AdminClient({ fetcher: (async () => new Response("[]", { status: 200 })) as typeof fetch });

describe("one service's panels", () => {
  it("says it is still asking, rather than showing an empty screen that means nothing", () => {
    render(<ServicePanels client={client} capabilities={null} section="jobs" />);

    expect(screen.getByText(/Discovering capabilities…/)).toBeInTheDocument();
  });

  it("a service that composes NOTHING says so — the console is not broken, the app has no surface", () => {
    render(<ServicePanels client={client} capabilities={capabilities()} section="jobs" />);

    expect(screen.getByText(/No Goldpath admin surface answered here/)).toBeInTheDocument();
  });

  it("each composed section renders ITS panel — the switch has no wrong branch", async () => {
    const answering = new AdminClient({
      fetcher: (async () => new Response("[]", { status: 200, headers: { "content-type": "application/json" } })) as typeof fetch,
    });

    for (const [section, testId] of [
      ["jobs", "run-console"],
      ["bulk", "bulk-panel"],
      ["campaign", "campaign-panel"],
      ["notification", "notification-panel"],
      ["archival", "archival-panel"],
    ] as const) {
      const view = render(
        <ServicePanels client={answering} capabilities={capabilities({ [section]: { kind: "present" } })} section={section} />,
      );
      // Panels that load before they draw (bulk, the run console) need their first answer
      // before the test id exists — which is itself the honest loading behaviour.
      if (section === "jobs") {
        expect(await view.findByText(/Discovering fleets|No runs recorded|Fleets/)).toBeTruthy();
      } else {
        expect(await view.findByTestId(testId)).toBeTruthy();
      }

      view.unmount();
    }
  });

  it("a service that did not answer says SO — it is not an app without modules", () => {
    render(<ServicePanels client={client} capabilities={capabilities({}, "unreachable")} section="jobs" />);

    expect(screen.getByRole("alert")).toHaveTextContent(/did not answer at all/);
    expect(screen.getByRole("alert")).toHaveTextContent(/CORS policy/);
    expect(screen.queryByText(/composes none of them/)).toBeNull();
  });

  it("composedSections keeps the standard order, whatever order the probes answered in", () => {
    expect(composedSections(capabilities({ campaign: { kind: "present" }, jobs: { kind: "forbidden" } }))).toEqual([
      "jobs",
      "campaign",
    ]);
    expect(composedSections(null)).toEqual([]);
  });
});
