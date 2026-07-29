import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { StatCard } from "./StatCard";

describe("the family stat card (v1.1 §7.7 — a published number, never an invented one)", () => {
  it("prints the label and the value it was GIVEN, computing nothing", () => {
    render(<StatCard label="Failed runs" value="1,204" />);
    expect(screen.getByText("Failed runs")).toBeInTheDocument();
    // The value arrives formatted; the card must not re-shape it.
    expect(screen.getByText("1,204")).toBeInTheDocument();
  });

  it("the tone colours the NUMBER only — the card frame stays neutral", () => {
    render(<StatCard label="Failed runs" value="3" tone="danger" />);
    expect(screen.getByText("3")).toHaveClass("text-danger");
    expect(screen.getByText("Failed runs")).not.toHaveClass("text-danger");
  });

  it("without onClick it is a plain card — nothing to press, no button role", () => {
    render(<StatCard label="Due to archive" value="0" />);
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("with onClick the WHOLE card is the button, named by its label", async () => {
    const onClick = vi.fn();
    render(<StatCard label="Awaiting approval" value="2" tone="warning" onClick={onClick} />);
    await userEvent.click(screen.getByRole("button", { name: /Awaiting approval/ }));
    expect(onClick).toHaveBeenCalledTimes(1);
  });
});
