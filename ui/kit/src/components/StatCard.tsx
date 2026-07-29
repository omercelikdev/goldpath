import type { ReactNode } from "react";

export interface StatCardProps {
  /** The card's lucide icon — same sparse rule as the rail (v1.1 §7.1). */
  icon?: ReactNode;
  label: string;
  /** Already formatted: the card prints what it is given and computes nothing. */
  value: string;
  /**
   * Colours the NUMBER only, and only when the number deserves it — the reference keeps
   * the card itself neutral so a wall of cards stays readable.
   */
  tone?: "danger" | "warning";
  /** Present makes the whole card a button — the family's cards go somewhere. */
  onClick?: () => void;
}

const TONE: Record<NonNullable<StatCardProps["tone"]>, string> = {
  danger: "text-danger",
  warning: "text-warning",
};

/**
 * The family stat card (v1.1 §7.7, class-verbatim from the reference dashboard):
 * icon + small semibold label, then the large tabular number. Every number on one of
 * these is a count the API itself published — the console still invents no aggregate.
 */
export function StatCard({ icon, label, value, tone, onClick }: StatCardProps) {
  const body = (
    <div
      className="rounded-2xl border border-border bg-background p-4 transition-colors hover:border-border-strong"
      style={{ boxShadow: "var(--shadow-surface)" }}
    >
      <div className="flex items-center gap-2 text-xs font-semibold text-muted-foreground">
        {icon && <span aria-hidden="true" className="flex shrink-0 items-center [&>svg]:size-4">{icon}</span>}
        {label}
      </div>
      <div className={`mt-2.5 text-[27px] font-bold tracking-tight tabular-nums ${tone ? TONE[tone] : ""}`}>{value}</div>
    </div>
  );

  return onClick ? (
    <button type="button" className="block w-full text-start" onClick={onClick}>
      {body}
    </button>
  ) : (
    body
  );
}
