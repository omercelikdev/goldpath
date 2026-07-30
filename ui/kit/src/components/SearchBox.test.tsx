import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it } from "vitest";
import { SearchBox } from "./SearchBox";

function Harness({ onCommit }: { onCommit: (value: string) => void }) {
  const [value, setValue] = useState("");
  return <SearchBox value={value} onCommit={(next) => { setValue(next); onCommit(next); }} label="Search by job" />;
}

describe("SearchBox (v1.1 §7.5)", () => {
  it("commits on Enter, once — not per keystroke", async () => {
    const user = userEvent.setup();
    const commits: string[] = [];
    render(<Harness onCommit={(value) => commits.push(value)} />);

    await user.type(screen.getByLabelText("Search by job"), "eod{Enter}");

    expect(commits).toEqual(["eod"]);
  });

  it("commits on blur ONLY when the draft actually changed", async () => {
    const user = userEvent.setup();
    const commits: string[] = [];
    render(<><Harness onCommit={(value) => commits.push(value)} /><button>elsewhere</button></>);

    await user.click(screen.getByLabelText("Search by job"));
    await user.click(screen.getByRole("button", { name: "elsewhere" }));
    expect(commits).toEqual([]);   // nothing typed → no phantom re-query

    await user.type(screen.getByLabelText("Search by job"), "sweep");
    await user.click(screen.getByRole("button", { name: "elsewhere" }));
    expect(commits).toEqual(["sweep"]);
  });

  it("the clear control resets AND commits the empty search", async () => {
    const user = userEvent.setup();
    const commits: string[] = [];
    render(<Harness onCommit={(value) => commits.push(value)} />);
    await user.type(screen.getByLabelText("Search by job"), "eod{Enter}");

    await user.click(screen.getByRole("button", { name: "clear search" }));

    // Clearing must ASK again with no filter — a cleared box that keeps the old results
    // shows a search nobody is searching for.
    expect(commits).toEqual(["eod", ""]);
    expect(screen.getByLabelText("Search by job")).toHaveValue("");
  });

  it("§8.10: the clear icon never SHIFTS the row — it is laid out even when empty", () => {
    render(<SearchBox value="" onCommit={() => {}} label="Search" />);
    // Present in layout (reserving its width), just invisible and out of the tab order.
    const clear = screen.getByRole("button", { hidden: true });
    expect(clear).toHaveClass("invisible");
    expect(clear).toHaveAttribute("tabindex", "-1");
  });
});
