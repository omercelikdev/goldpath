import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { FacetFilter } from "./FacetFilter";

const OPTIONS = [
  { value: "Validated", count: 3 },
  { value: "Failed", label: "Failed rows" },
];

describe("the family facet (v1.1 §7.5 — a server-side filter that looks like one)", () => {
  it("the trigger carries the label, and the count badge only once something is on", () => {
    const { rerender } = render(
      <FacetFilter label="State" options={OPTIONS} selected={new Set()} onToggle={() => {}} onClear={() => {}} />,
    );
    const trigger = screen.getByRole("button", { name: /State/ });
    expect(trigger.textContent).not.toContain("1");

    rerender(
      <FacetFilter label="State" options={OPTIONS} selected={new Set(["Validated"])} onToggle={() => {}} onClear={() => {}} />,
    );
    expect(screen.getByRole("button", { name: /State/ }).textContent).toContain("1");
  });

  it("options are menuitemcheckboxes with aria-checked — the checkmark speaks out loud", async () => {
    render(
      <FacetFilter label="State" options={OPTIONS} selected={new Set(["Validated"])} onToggle={() => {}} onClear={() => {}} />,
    );
    await userEvent.click(screen.getByRole("button", { name: /State/ }));

    const on = await screen.findByRole("menuitemcheckbox", { name: /Validated/ });
    expect(on).toHaveAttribute("aria-checked", "true");
    // The custom label wins over the raw value, and the count rides along.
    expect(screen.getByRole("menuitemcheckbox", { name: /Failed rows/ })).toHaveAttribute("aria-checked", "false");
    expect(on.textContent).toContain("3");
  });

  it("toggling reports the VALUE and keeps the menu open — facets toggle in batches", async () => {
    const onToggle = vi.fn();
    render(<FacetFilter label="State" options={OPTIONS} selected={new Set()} onToggle={onToggle} onClear={() => {}} />);
    await userEvent.click(screen.getByRole("button", { name: /State/ }));
    await userEvent.click(await screen.findByRole("menuitemcheckbox", { name: /Validated/ }));

    expect(onToggle).toHaveBeenCalledWith("Validated");
    expect(screen.getByRole("menuitemcheckbox", { name: /Failed rows/ })).toBeInTheDocument();
  });

  it("clear appears only with an active selection, and calls onClear", async () => {
    const onClear = vi.fn();
    const { rerender } = render(
      <FacetFilter label="State" options={OPTIONS} selected={new Set()} onToggle={() => {}} onClear={onClear} />,
    );
    await userEvent.click(screen.getByRole("button", { name: /State/ }));
    await screen.findByRole("menuitemcheckbox", { name: /Validated/ });
    expect(screen.queryByRole("menuitem", { name: "clear" })).not.toBeInTheDocument();

    rerender(<FacetFilter label="State" options={OPTIONS} selected={new Set(["Failed"])} onToggle={() => {}} onClear={onClear} />);
    await userEvent.click(await screen.findByRole("menuitem", { name: "clear" }));
    expect(onClear).toHaveBeenCalledTimes(1);
  });

  it("the menu is NON-modal — the page it filters stays reachable while it is open", async () => {
    render(
      <div>
        <FacetFilter label="State" options={OPTIONS} selected={new Set()} onToggle={() => {}} onClear={() => {}} />
        <button>clear filters</button>
      </div>,
    );
    await userEvent.click(screen.getByRole("button", { name: /State/ }));
    await screen.findByRole("menuitemcheckbox", { name: /Validated/ });
    // A modal menu would strip the rest of the page from the a11y tree (the b3b lesson).
    expect(screen.getByRole("button", { name: "clear filters" })).toBeInTheDocument();
  });
});
