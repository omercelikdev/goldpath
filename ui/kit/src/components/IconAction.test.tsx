import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { IconAction } from "./IconAction";

describe("the icon action (v1.3 §9.2 — a utility, not a verb)", () => {
  it("carries the NAME even with no visible word, and says it in a tooltip on focus", async () => {
    const onClick = vi.fn();
    render(<IconAction icon={<svg data-icon="r" />} label="Refresh" onClick={onClick} />);
    const button = screen.getByRole("button", { name: "Refresh" });
    await userEvent.tab();
    expect(await screen.findByRole("tooltip")).toHaveTextContent("Refresh");
    await userEvent.click(button);
    expect(onClick).toHaveBeenCalledTimes(1);
  });
});
