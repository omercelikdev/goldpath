import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { Dialog } from "./Dialog";

function Host() {
  const [open, setOpen] = useState(false);
  return (
    <>
      <button onClick={() => setOpen(true)}>add a trigger</button>
      <Dialog open={open} onOpenChange={setOpen} title="Add a trigger" description="A new schedule.">
        <input aria-label="cron" />
      </Dialog>
    </>
  );
}

describe("the family dialog (v1.3 §9.5 — forms open centred, never below)", () => {
  it("opens as a REAL dialog with its title and description, and closes by name and by Escape", async () => {
    render(<Host />);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

    await userEvent.click(screen.getByText("add a trigger"));
    const dialog = await screen.findByRole("dialog", { name: "Add a trigger" });
    expect(dialog).toHaveTextContent("A new schedule.");
    expect(screen.getByLabelText("cron")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "close" }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

    await userEvent.click(screen.getByText("add a trigger"));
    await screen.findByRole("dialog");
    await userEvent.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
});
