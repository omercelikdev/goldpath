import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { DensityProvider, DensityToggle, DENSITY_KEY } from "./Density";
import { Table } from "./Table";

const rows = [{ id: "1", name: "eod" }];
const columns = [{ header: "Job", cell: (row: { name: string }) => row.name }];

describe("the density feature (v1.3 §9.3 — one rhythm for every family table)", () => {
  it("defaults comfortable, toggles compact, and every table follows", async () => {
    localStorage.removeItem(DENSITY_KEY);
    render(
      <DensityProvider>
        <DensityToggle />
        <Table columns={columns} rows={rows} rowKey={(row) => row.id} />
      </DensityProvider>,
    );

    expect(screen.getByText("eod").closest("td")).toHaveClass("py-3");
    await userEvent.click(screen.getByRole("button", { name: "Compact rows" }));
    expect(screen.getByText("eod").closest("td")).toHaveClass("py-1.5");
    // The choice persists like the rail state — the operator meant it.
    expect(localStorage.getItem(DENSITY_KEY)).toBe("compact");
    expect(screen.getByRole("button", { name: "Comfortable rows" })).toHaveAttribute("aria-pressed", "true");
  });

  it("outside a provider the table still renders, comfortable", () => {
    render(<Table columns={columns} rows={rows} rowKey={(row) => row.id} />);
    expect(screen.getByText("eod").closest("td")).toHaveClass("py-3");
  });
});
