import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Select } from "./Select";

const OPTIONS = [{ value: "a" }, { value: "b", label: "bee" }];

describe("the family select (v1.2 §8.7, modernized — the family's OWN listbox)", () => {
  it("opens the family menu, walks it, and reports the chosen VALUE", async () => {
    const onChange = vi.fn();
    render(<Select aria-label="archive" value="a" onChange={onChange} options={OPTIONS} />);

    // The trigger is a combobox with an accessible name — not a platform popup.
    const trigger = screen.getByRole("combobox", { name: "archive" });
    await userEvent.click(trigger);
    expect(trigger).toHaveAttribute("aria-expanded", "true");

    // Labels win over raw values; selection reports the VALUE and closes the list.
    await userEvent.click(await screen.findByRole("option", { name: "bee" }));
    expect(onChange).toHaveBeenCalledWith("b");
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });

  it("shows the chosen option's LABEL on the closed trigger", () => {
    render(<Select aria-label="x" value="b" onChange={() => {}} options={OPTIONS} />);
    expect(screen.getByRole("combobox")).toHaveTextContent("bee");
  });

  it("walks with the keyboard: arrows move, Enter chooses, Escape closes empty-handed", async () => {
    const onChange = vi.fn();
    render(<Select aria-label="k" value="a" onChange={onChange} options={OPTIONS} />);
    const trigger = screen.getByRole("combobox", { name: "k" });

    trigger.focus();
    await userEvent.keyboard("{ArrowDown}");        // opens at the current value
    await userEvent.keyboard("{ArrowDown}{Enter}"); // walks to "b", chooses it
    expect(onChange).toHaveBeenCalledWith("b");

    await userEvent.keyboard("{ArrowDown}");
    await userEvent.keyboard("{Escape}");           // closes without choosing again
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });
});
