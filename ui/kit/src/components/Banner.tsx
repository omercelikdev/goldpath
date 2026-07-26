import type { ReactNode } from "react";
import type { StatusTone } from "../status";

export interface BannerProps {
  /** The semantic ramp only — the accent never carries meaning (ui-standard §1). */
  tone: StatusTone;
  children: ReactNode;
  /**
   * `alert` for something the operator must act on, `status` for a settled outcome.
   * Screen readers treat them differently, so the caller chooses deliberately.
   */
  live?: "alert" | "status";
  /** Inline pill next to a control (a verb's outcome) rather than a full-width box. */
  dense?: boolean;
}

const TONE: Record<StatusTone, string> = {
  success: "border-success-border bg-success-bg text-success",
  info: "border-info-border bg-info-bg text-info",
  warning: "border-warning-border bg-warning-bg text-warning",
  danger: "border-danger-border bg-danger-bg text-danger",
  violet: "border-violet-border bg-violet-bg text-violet",
  neutral: "border-border bg-muted text-muted-foreground",
};

/**
 * The one message box of the kit: every screen that must TELL the operator something
 * uses it, so the tone language stays identical across shipped panels and adopter-built
 * ones (console RFC §1 — the kit is everything a custom screen needs).
 */
export function Banner({ tone, children, live = "alert", dense = false }: BannerProps) {
  return (
    <div
      role={live}
      data-tone={tone}
      className={`rounded-md border ${TONE[tone]} ${dense ? "px-2 py-0.5 text-xs" : "px-3 py-2 text-sm"}`}
    >
      {children}
    </div>
  );
}
