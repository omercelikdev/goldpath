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
}

/**
 * The ONE table (v1.1 §7.4): the family's container — rounded card, quiet header, hover
 * rows — over a REAL html table, because screen readers get column semantics for free.
 * Ad-hoc lists that were tables in disguise retire onto this.
 */
export function Table<T>({ columns, rows, rowKey, onRowClick, emptyMessage = "Nothing here yet." }: TableProps<T>) {
  return (
    <div className="overflow-hidden rounded-lg border border-border bg-background" style={{ boxShadow: "var(--shadow-surface)" }}>
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b border-border bg-muted/40 text-left text-xs text-muted-foreground">
            {columns.map((column) => (
              <th key={column.header} className={`px-3 py-2 font-medium ${column.align === "right" ? "text-right" : ""}`}>
                {column.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-border/60">
          {rows.map((row) => (
            <tr
              key={rowKey(row)}
              className={onRowClick ? "cursor-pointer transition-colors hover:bg-muted/40" : "hover:bg-muted/40"}
              onClick={onRowClick ? () => onRowClick(row) : undefined}
            >
              {columns.map((column) => (
                <td key={column.header} className={`px-3 py-2 ${column.align === "right" ? "text-right" : ""}`}>
                  {column.cell(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
      {rows.length === 0 && <p className="py-6 text-center text-sm text-muted-foreground">{emptyMessage}</p>}
    </div>
  );
}
