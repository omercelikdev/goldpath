import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AppShell, COLLAPSE_KEY, initialCollapsed, type ShellNavItem } from "./AppShell";

const nav = (over: Partial<ShellNavItem> = {}): ShellNavItem => ({
  id: "runs",
  label: "Runs",
  onSelect: vi.fn(),
  ...over,
});

describe("the app shell (ui-standard-v1 §3 — the surface scrolls, never the page)", () => {
  it("renders only the nav it is GIVEN — a missing capability is an absent item", () => {
    render(
      <AppShell title="CorPay" nav={[nav(), nav({ id: "bulk", label: "Bulk intake" })]} activeId="runs">
        <p>content</p>
      </AppShell>,
    );

    expect(screen.getByRole("button", { name: "Runs" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Bulk intake" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /campaign/i })).toBeNull();   // not given, not shown
  });

  it("marks the active section for assistive tech, not just visually", () => {
    render(
      <AppShell title="CorPay" nav={[nav(), nav({ id: "bulk", label: "Bulk intake" })]} activeId="bulk">
        <p>content</p>
      </AppShell>,
    );

    expect(screen.getByRole("button", { name: "Bulk intake" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("button", { name: "Runs" })).not.toHaveAttribute("aria-current");
  });

  it("the CONTENT surface is the scroller — the shell frame never scrolls", () => {
    render(<AppShell title="CorPay" nav={[nav()]} activeId="runs"><p>content</p></AppShell>);

    expect(screen.getByTestId("app-shell").className).toContain("overflow-hidden");
    expect(screen.getByTestId("shell-surface").className).toContain("overflow-y-auto");
    expect(screen.getByTestId("shell-surface").className).toContain("scroll-area");
  });

  it("selecting a section calls its handler; the shell owns no routing", async () => {
    const onSelect = vi.fn();
    render(
      <AppShell title="CorPay" nav={[nav(), nav({ id: "bulk", label: "Bulk intake", onSelect })]} activeId="runs">
        <p>content</p>
      </AppShell>,
    );

    await userEvent.click(screen.getByRole("button", { name: "Bulk intake" }));
    expect(onSelect).toHaveBeenCalledTimes(1);
  });

  it("collapsing keeps every section reachable — labels survive as accessible names", async () => {
    const onToggle = vi.fn();
    const { rerender } = render(
      <AppShell title="CorPay" nav={[nav()]} activeId="runs" onToggleCollapsed={onToggle}>
        <p>content</p>
      </AppShell>,
    );

    await userEvent.click(screen.getByRole("button", { name: /collapse navigation/i }));
    expect(onToggle).toHaveBeenCalledTimes(1);

    rerender(
      <AppShell title="CorPay" nav={[nav()]} activeId="runs" collapsed onToggleCollapsed={onToggle}>
        <p>content</p>
      </AppShell>,
    );
    expect(screen.getByRole("button", { name: "Runs" })).toBeInTheDocument();   // still reachable
    expect(screen.getByRole("button", { name: /expand navigation/i })).toHaveAttribute("aria-expanded", "false");
  });

  it("the service switcher appears only with a registry, and switching calls its entry", async () => {
    const onSelect = vi.fn();
    const { rerender } = render(<AppShell title="CorPay" nav={[nav()]} activeId="runs"><p>c</p></AppShell>);
    expect(screen.queryByLabelText("service")).toBeNull();   // single-service console: no switcher

    rerender(
      <AppShell
        title="CorPay"
        nav={[nav()]}
        activeId="runs"
        services={[{ name: "api", onSelect: vi.fn() }, { name: "payments", onSelect }]}
        activeService="api"
      >
        <p>c</p>
      </AppShell>,
    );

    await userEvent.selectOptions(screen.getByLabelText("service"), "payments");
    expect(onSelect).toHaveBeenCalledTimes(1);
  });

  it("the RAIL scrolls on its own once many capabilities are composed", () => {
    const many = Array.from({ length: 24 }, (_, i) => nav({ id: `cap-${i}`, label: `Capability ${i}` }));
    render(<AppShell title="CorPay" nav={many} activeId="cap-0"><p>c</p></AppShell>);

    // The frame stays put, but the rail must not clip what it was given.
    expect(screen.getByTestId("app-shell").className).toContain("overflow-hidden");
    expect(screen.getByTestId("shell-rail").className).toContain("overflow-y-auto");
    expect(screen.getByRole("button", { name: "Capability 23" })).toBeInTheDocument();
  });

  it("the footer learns the collapsed state so it can shrink with the rail", () => {
    const footer = (collapsed: boolean) => <span>{collapsed ? "T" : "theme"}</span>;
    const { rerender } = render(
      <AppShell title="CorPay" nav={[nav()]} activeId="runs" footer={footer}><p>c</p></AppShell>,
    );
    expect(screen.getByText("theme")).toBeInTheDocument();

    rerender(<AppShell title="CorPay" nav={[nav()]} activeId="runs" collapsed footer={footer}><p>c</p></AppShell>);
    expect(screen.getByText("T")).toBeInTheDocument();
  });

  it("a badge shows a live count and stays silent at zero", () => {
    const { rerender } = render(
      <AppShell title="CorPay" nav={[nav({ badge: 3 })]} activeId="runs"><p>c</p></AppShell>,
    );
    expect(screen.getByText("3")).toBeInTheDocument();

    rerender(<AppShell title="CorPay" nav={[nav({ badge: 0 })]} activeId="runs"><p>c</p></AppShell>);
    expect(screen.queryByText("0")).toBeNull();
  });

  it("the service picker defaults to the first service and switches to the one chosen", async () => {
    const chosen: string[] = [];
    render(
      <AppShell
        title="Goldpath console"
        nav={[]}
        activeId=""
        services={[
          { name: "payments", onSelect: () => chosen.push("payments") },
          { name: "claims", onSelect: () => chosen.push("claims") },
        ]}
      >
        <p>body</p>
      </AppShell>,
    );

    // No activeService given: the picker shows the first, rather than an empty box.
    const picker = screen.getByLabelText(/service/i);
    expect(picker).toHaveValue("payments");

    await userEvent.selectOptions(picker, "claims");
    expect(chosen).toEqual(["claims"]);
  });
});

describe("the v1.1 rail (u7-b1)", () => {
  const item = (id: string, group?: string) => ({ id, label: id, group, icon: <svg data-icon={id} />, onSelect: () => {} });

  it("groups render in the order GIVEN, with their headings", () => {
    render(<AppShell title="t" nav={[item("a", "Execution"), item("b", "Intake"), item("c", "Execution")]} activeId="a">x</AppShell>);
    const rail = screen.getByTestId("shell-rail");
    const text = rail.textContent!;
    // One heading per group, and Execution precedes Intake because the caller said so.
    expect(text.indexOf("Execution")).toBeGreaterThan(-1);
    expect(text.indexOf("Execution")).toBeLessThan(text.indexOf("Intake"));
    expect(within(rail).getAllByText(/^Execution$/)).toHaveLength(1);
  });

  it("collapsed keeps the ICON as the item, and a REAL tooltip says its name (§8.5)", async () => {
    render(<AppShell title="t" nav={[item("runs", "Execution")]} activeId="runs" collapsed>x</AppShell>);
    const button = screen.getByRole("button", { name: "runs" });   // sr-only label keeps the name
    expect(button.querySelector("[data-icon=runs]")).not.toBeNull();
    // Focus opens the tooltip — the name is readable without a pointer.
    await userEvent.tab();
    expect(await screen.findAllByText("runs")).not.toHaveLength(0);
    expect(button).not.toHaveAttribute("title");   // the browser-delay tooltip retired
  });

  it("persists the rail state, and initialCollapsed reads it back", () => {
    localStorage.removeItem(COLLAPSE_KEY);
    expect(initialCollapsed()).toBe(false);
    render(<AppShell title="t" nav={[item("a")]} activeId="a" collapsed>x</AppShell>);
    expect(localStorage.getItem(COLLAPSE_KEY)).toBe("1");
    expect(initialCollapsed()).toBe(true);
  });

  it("the search trigger shows the field + ⌘K hint expanded, and calls onSearch", async () => {
    const onSearch = vi.fn();
    render(<AppShell title="t" nav={[item("a")]} activeId="a" onSearch={onSearch}>x</AppShell>);
    const trigger = screen.getByRole("button", { name: /Search/ });
    expect(trigger.textContent).toContain("⌘K");
    await userEvent.click(trigger);
    expect(onSearch).toHaveBeenCalledTimes(1);
  });

  it("collapsed, the search trigger is an icon — the hint goes, the reach stays", async () => {
    const onSearch = vi.fn();
    render(<AppShell title="t" nav={[item("a")]} activeId="a" collapsed onSearch={onSearch}>x</AppShell>);
    const trigger = screen.getByRole("button", { name: "Search" });
    expect(trigger.textContent).not.toContain("⌘K");
    await userEvent.click(trigger);
    expect(onSearch).toHaveBeenCalledTimes(1);
  });

  it("no onSearch, no trigger — a console without a palette shows no dead field", () => {
    render(<AppShell title="t" nav={[item("a")]} activeId="a">x</AppShell>);
    expect(screen.queryByRole("button", { name: /Search/ })).not.toBeInTheDocument();
  });

  it("a throwing localStorage never breaks the shell", () => {
    const real = Storage.prototype.getItem;
    Storage.prototype.getItem = () => { throw new Error("private mode"); };
    try {
      expect(initialCollapsed()).toBe(false);   // falls back rather than crashing the app
    } finally {
      Storage.prototype.getItem = real;
    }
  });
});
