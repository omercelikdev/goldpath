import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { KeysetTable, type KeysetPage, clampTake } from "./KeysetTable";

interface Row {
  id: string;
  name: string;
}

const page = (items: Row[], nextCursor: string | null): KeysetPage<Row> => ({ items, nextCursor });
const columns = [{ header: "Name", cell: (row: Row) => row.name }];

describe("the keyset table (ui-standard-v1 §4 — cursor pager, never offsets)", () => {
  it("renders the first page and appends the next; the walk ends at null", async () => {
    const pages: Record<string, KeysetPage<Row>> = {
      start: page([{ id: "1", name: "alpha" }, { id: "2", name: "beta" }], "c1"),
      c1: page([{ id: "3", name: "gamma" }], null),
    };
    const load = vi.fn(async (cursor: string | null) => pages[cursor ?? "start"]);
    render(<KeysetTable columns={columns} loadPage={load} rowKey={(r) => r.id} />);

    expect(await screen.findByText("alpha")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /load more/i }));

    expect(await screen.findByText("gamma")).toBeInTheDocument();
    expect(screen.getByText("alpha")).toBeInTheDocument();            // appended, not replaced
    expect(screen.queryByRole("button", { name: /load more/i })).toBeNull();   // ended
    expect(screen.getByText(/3 loaded · end/)).toBeInTheDocument();
  });

  it("NEVER shows a total count or page numbers — the honest footer only", async () => {
    const load = vi.fn(async () => page([{ id: "1", name: "only" }], null));
    const { container } = render(<KeysetTable columns={columns} loadPage={load} rowKey={(r) => r.id} />);

    await screen.findByText("only");
    expect(container.textContent).not.toMatch(/total|page \d|of \d/i);
  });

  it("clamps take to the contract's [1, 500] before ever calling the API", async () => {
    const load = vi.fn(async () => page([], null));
    render(<KeysetTable columns={columns} loadPage={load} rowKey={(r) => r.id} take={10_000} />);
    await waitFor(() => expect(load).toHaveBeenCalledWith(null, 500));

    expect(clampTake(0)).toBe(1);
    expect(clampTake(-5)).toBe(1);
    expect(clampTake(Number.NaN)).toBe(50);
    expect(clampTake(37.9)).toBe(37);
  });

  it("shows the empty message when the first page is empty", async () => {
    const load = vi.fn(async () => page([], null));
    render(<KeysetTable columns={columns} loadPage={load} rowKey={(r) => r.id} emptyMessage="No runs today." />);

    expect(await screen.findByText("No runs today.")).toBeInTheDocument();
  });

  it("a failed load surfaces the alert and retry re-asks for the SAME page", async () => {
    const load = vi
      .fn<(cursor: string | null, take: number) => Promise<KeysetPage<Row>>>()
      .mockRejectedValueOnce(new Error("boom"))
      .mockResolvedValueOnce(page([{ id: "1", name: "recovered" }], null));
    render(<KeysetTable columns={columns} loadPage={load} rowKey={(r) => r.id} />);

    expect(await screen.findByRole("alert")).toHaveTextContent(/could not be loaded/i);
    await userEvent.click(screen.getByRole("button", { name: /retry/i }));

    expect(await screen.findByText("recovered")).toBeInTheDocument();
    expect(load).toHaveBeenNthCalledWith(1, null, 50);
    expect(load).toHaveBeenNthCalledWith(2, null, 50);   // same page, fresh walk
  });
});
