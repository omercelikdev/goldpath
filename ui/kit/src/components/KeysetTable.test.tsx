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

  it("an empty INTERMEDIATE page never shows the empty message while more remains", async () => {
    const load = vi
      .fn<(cursor: string | null, take: number) => Promise<KeysetPage<Row>>>()
      .mockResolvedValueOnce(page([], "c1"))
      .mockResolvedValueOnce(page([{ id: "1", name: "late" }], null));
    render(<KeysetTable columns={columns} loadPage={load} rowKey={(r) => r.id} emptyMessage="Empty." />);

    expect(await screen.findByRole("button", { name: /load more/i })).toBeInTheDocument();
    expect(screen.queryByText("Empty.")).toBeNull();   // not the end — not empty

    await userEvent.click(screen.getByRole("button", { name: /load more/i }));
    expect(await screen.findByText("late")).toBeInTheDocument();
  });

  it("a mid-walk failure retries with the PENDING cursor and appends", async () => {
    const load = vi
      .fn<(cursor: string | null, take: number) => Promise<KeysetPage<Row>>>()
      .mockResolvedValueOnce(page([{ id: "1", name: "first" }], "c1"))
      .mockRejectedValueOnce(new Error("boom"))
      .mockResolvedValueOnce(page([{ id: "2", name: "second" }], null));
    render(<KeysetTable columns={columns} loadPage={load} rowKey={(r) => r.id} />);

    await screen.findByText("first");
    await userEvent.click(screen.getByRole("button", { name: /load more/i }));
    expect(await screen.findByRole("alert")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /retry/i }));
    expect(await screen.findByText("second")).toBeInTheDocument();
    expect(screen.getByText("first")).toBeInTheDocument();          // appended, not replaced
    expect(load).toHaveBeenNthCalledWith(3, "c1", 50);              // the PENDING cursor
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

  it("a superseded load never overwrites the newer one — the page the operator sees is the last they asked for", async () => {
    // Page 1 resolves LAST: without the generation guard it would land on top of the
    // second page and the table would silently show stale rows.
    const gates: ((rows: string[]) => void)[] = [];
    const loadPage = (cursor: string | null) =>
      new Promise<KeysetPage<string>>((resolve) => {
        gates.push((rows) => resolve({ items: rows, nextCursor: cursor === null ? "c1" : null }));
      });

    const { rerender } = render(
      <KeysetTable<string>
        columns={[{ header: "Row", cell: (row) => row }]}
        loadPage={loadPage}
        rowKey={(row) => row}
      />,
    );

    // A second load supersedes the first (a filter change does exactly this).
    rerender(
      <KeysetTable<string>
        columns={[{ header: "Row", cell: (row) => row }]}
        loadPage={(cursor) => loadPage(cursor)}
        rowKey={(row) => row}
      />,
    );

    await waitFor(() => expect(gates).toHaveLength(2));
    gates[1](["fresh"]);
    gates[0](["stale"]);

    expect(await screen.findByText("fresh")).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByText("stale")).toBeNull());
  });

  it("right-aligned columns align their header AND their cells", () => {
    render(
      <KeysetTable<{ n: number }>
        columns={[{ header: "Count", align: "right", cell: (row) => row.n }]}
        loadPage={async () => ({ items: [{ n: 7 }], nextCursor: null })}
        rowKey={(row) => String(row.n)}
      />,
    );

    return waitFor(() => {
      expect(screen.getByRole("columnheader", { name: "Count" })).toHaveClass("text-end");
      expect(screen.getByRole("cell", { name: "7" })).toHaveClass("text-end");
    });
  });
});
