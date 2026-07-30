import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Select } from "./Select";

describe("the family select (v1.2 §8.7 — the native element in the one field skin)", () => {
  it("stays a REAL select: options, value, change events, accessible name", async () => {
    const onChange = vi.fn();
    render(
      <Select aria-label="archive" value="a" onChange={onChange}>
        <option value="a">a</option>
        <option value="b">b</option>
      </Select>,
    );
    const select = screen.getByRole("combobox", { name: "archive" });
    await userEvent.selectOptions(select, "b");
    expect(onChange).toHaveBeenCalled();
  });

  it("the platform chevron is hidden and the family's is drawn — but never interactive", () => {
    const { container } = render(
      <Select aria-label="x" value="a" onChange={() => {}}>
        <option value="a">a</option>
      </Select>,
    );
    const chevron = container.querySelector("svg");
    expect(chevron).toHaveClass("pointer-events-none");
    expect(screen.getByRole("combobox")).toHaveClass("appearance-none");
  });
});
