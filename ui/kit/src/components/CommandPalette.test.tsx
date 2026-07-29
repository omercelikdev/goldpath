import { describe, expect, it, vi } from "vitest";
import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CommandPalette, openCommand } from "./CommandPalette";

const groups = (run = vi.fn()) => [
  {
    heading: "Go to",
    items: [
      { id: "today", label: "Today", run },
      { id: "runs", label: "Runs", run: vi.fn() },
    ],
  },
];

describe("the command palette (v1.1 §7.14 — every destination one keystroke away)", () => {
  it("stays closed until asked — the dialog is not in the tree at rest", () => {
    render(<CommandPalette groups={groups()} />);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("⌘K opens it, and ⌘K again closes it — the shortcut is a TOGGLE", async () => {
    render(<CommandPalette groups={groups()} />);
    await userEvent.keyboard("{Meta>}k{/Meta}");
    expect(await screen.findByRole("dialog")).toBeInTheDocument();
    expect(screen.getByText("Go to")).toBeInTheDocument();
    await userEvent.keyboard("{Meta>}k{/Meta}");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("the sidebar trigger's openCommand() event opens it too", async () => {
    render(<CommandPalette groups={groups()} />);
    act(() => openCommand());
    expect(await screen.findByRole("dialog")).toBeInTheDocument();
  });

  it("selecting a command CLOSES first, then runs — and it ran exactly once", async () => {
    const run = vi.fn();
    render(<CommandPalette groups={groups(run)} />);
    act(() => openCommand());
    await userEvent.click(await screen.findByText("Today"));
    expect(run).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("typing filters the list down to what matches", async () => {
    render(<CommandPalette groups={groups()} />);
    act(() => openCommand());
    await userEvent.type(await screen.findByRole("combobox"), "run");
    expect(screen.getByText("Runs")).toBeInTheDocument();
    expect(screen.queryByText("Today")).not.toBeInTheDocument();
  });
});
