import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it } from "vitest";
import { TabPanel, TabStrip } from "./TabStrip";

function Harness({ initial = "jobs" }: { initial?: string }) {
  const [active, setActive] = useState(initial);
  return (
    <>
      <TabStrip
        label="Fleet sections"
        activeId={active}
        onSelect={setActive}
        items={[
          { id: "overview", label: "Overview" },
          { id: "jobs", label: "Jobs", hint: "3" },
          { id: "triggers", label: "Triggers" },
        ]}
      />
      <TabPanel id="overview" activeId={active}>the fleet</TabPanel>
      <TabPanel id="jobs" activeId={active}>the jobs</TabPanel>
      <TabPanel id="triggers" activeId={active}>the triggers</TabPanel>
    </>
  );
}

describe("TabStrip", () => {
  it("shows only the active panel, and names it by its tab", () => {
    render(<Harness />);

    const panel = screen.getByRole("tabpanel");
    expect(panel).toHaveTextContent("the jobs");
    expect(panel).toHaveAttribute("aria-labelledby", "tab-jobs");
    expect(screen.queryByText("the fleet")).not.toBeInTheDocument();
  });

  it("puts ONLY the active tab in the tab order", () => {
    render(<Harness />);

    const tabs = screen.getAllByRole("tab");
    expect(tabs.map((tab) => tab.getAttribute("tabindex"))).toEqual(["-1", "0", "-1"]);
    expect(tabs.map((tab) => tab.getAttribute("aria-selected"))).toEqual(["false", "true", "false"]);
  });

  it("walks with the arrow keys, and focus follows the selection", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.tab();

    await user.keyboard("{ArrowRight}");

    expect(screen.getByRole("tabpanel")).toHaveTextContent("the triggers");
    // Selection without focus would read as nothing having happened to a keyboard user.
    expect(screen.getByRole("tab", { name: "Triggers" })).toHaveFocus();
  });

  it("wraps at both ends rather than dead-ending", async () => {
    const user = userEvent.setup();
    render(<Harness initial="overview" />);
    await user.tab();

    await user.keyboard("{ArrowLeft}");

    expect(screen.getByRole("tab", { name: "Triggers" })).toHaveFocus();
  });

  it("jumps to the first and last tab with Home and End", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.tab();

    await user.keyboard("{End}");
    expect(screen.getByRole("tabpanel")).toHaveTextContent("the triggers");

    await user.keyboard("{Home}");
    expect(screen.getByRole("tabpanel")).toHaveTextContent("the fleet");
  });

  it("leaves other keys to the browser", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.tab();

    await user.keyboard("{ArrowDown}");

    expect(screen.getByRole("tabpanel")).toHaveTextContent("the jobs");
  });

  it("carries a hint next to the label without it being the only signal", () => {
    render(<Harness />);

    // The count reads as part of the tab's accessible name; the label still carries the
    // meaning on its own.
    expect(screen.getByRole("tab", { name: /Jobs/ })).toBeInTheDocument();
  });
});
