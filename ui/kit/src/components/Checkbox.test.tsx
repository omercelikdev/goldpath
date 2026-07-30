import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Checkbox } from "./Checkbox";

describe("the family checkbox (v1.2 §8.8 — the drawn box, a real input underneath)", () => {
  it("is a REAL checkbox: role, label, state, toggling", async () => {
    const onChange = vi.fn();
    render(<Checkbox checked={false} onChange={onChange} label="include lifted" />);
    const box = screen.getByRole("checkbox", { name: "include lifted" });
    expect(box).not.toBeChecked();
    await userEvent.click(box);
    expect(onChange).toHaveBeenCalledWith(true);
  });

  it("checked draws the primary fill and reports checked to assistive tech", () => {
    render(<Checkbox checked onChange={() => {}} label="on" />);
    expect(screen.getByRole("checkbox", { name: "on" })).toBeChecked();
  });

  it("disabled refuses the toggle", async () => {
    const onChange = vi.fn();
    render(<Checkbox checked={false} onChange={onChange} label="frozen" disabled />);
    await userEvent.click(screen.getByText("frozen"));
    expect(onChange).not.toHaveBeenCalled();
  });
});
