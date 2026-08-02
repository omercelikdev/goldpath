import { densityCell, useDensity } from "./Density";
import type { ReactNode } from "react";

export interface TableColumn<T> {
  header: string;
  cell: (row: T) => ReactNode;
  align?: "left" | "right";
}

export interface TableProps<T> {
  columns: TableColumn<T>[];
  rows: T[];
  rowKey: (row: T) => string;
  /** A row click opens the entity in a Sheet (v1.1 §7.4) — never an unfold below. */
  onRowClick?: (row: T) => void;
  emptyMessage?: string;
  /** Rendered INSIDE the card as its header strip (v1.3 §9.1 as corrected): search,
      facets and utilities belong to the TABLE, not to the page above it. */
  toolbar?: ReactNode;
}

/**
 * The ONE table (v1.1 §7.4): the family's container — rounded card, quiet header, hover
 * rows — over a REAL html table, because screen readers get column semantics for free.
 * Ad-hoc lists that were tables in disguise retire onto this.
 */
export function Table<T>({ columns, rows, rowKey, onRowClick, emptyMessage = "Nothing here yet.", toolbar }: TableProps<T>) {
  // The journal's density feature (§9.3): one rhythm for every family table.
  const cell = densityCell(useDensity());
  return (
    <div className="overflow-hidden rounded-2xl border border-border bg-background" style={{ boxShadow: "var(--shadow-surface)" }}>
      {toolbar && <div className="flex flex-wrap items-center gap-2 border-b border-border p-3">{toolbar}</div>}
      <div className="scroll-area overflow-x-auto">
        <table className="w-full border-collapse text-sm">
          <thead>
            <tr>
              {columns.map((column) => (
                <th
                  key={column.header}
                  className={`border-b border-border bg-muted/40 px-4 py-2.5 text-start text-[11px] font-semibold uppercase tracking-wide text-muted-foreground ${
                    column.align === "right" ? "text-end" : ""
                  }`}
                >
                  {column.header}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr>
                <td colSpan={columns.length}>
                  <p className="py-16 text-center text-sm text-muted-foreground">{emptyMessage}</p>
                </td>
              </tr>
            ) : (
              rows.map((row) => (
                <tr
                  key={rowKey(row)}
                  className={`border-b border-border transition-colors hover:bg-muted/40 ${onRowClick ? "cursor-pointer" : ""}`}
                  onClick={onRowClick ? () => onRowClick(row) : undefined}
                >
                  {columns.map((column) => (
                    <td key={column.header} className={`${cell} align-middle ${column.align === "right" ? "text-end" : ""}`}>
                      {column.cell(row)}
                    </td>
                  ))}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
