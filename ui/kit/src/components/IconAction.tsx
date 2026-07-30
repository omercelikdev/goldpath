import type { ReactNode } from "react";
import { Tooltip } from "./Tooltip";

export interface IconActionProps {
  /** The action's lucide icon. */
  icon: ReactNode;
  /** What it does — the tooltip and the accessible name (v1.3 §9.2). */
  label: string;
  onClick: () => void;
  disabled?: boolean;
  /** For toggles: reported as aria-pressed so the state is audible, not just visible. */
  pressed?: boolean;
}

/**
 * A toolbar utility as an ICON (v1.3 §9.2): refresh and its siblings lose the word;
 * the name lives in the family tooltip and the accessible name. NOT for verbs —
 * mutating actions stay VerbButtons with their confirm step.
 */
export function IconAction({ icon, label, onClick, disabled, pressed }: IconActionProps) {
  return (
    <Tooltip label={label}>
      <button
        aria-label={label}
        aria-pressed={pressed}
        disabled={disabled}
        className="inline-flex h-9 items-center justify-center rounded-lg border border-border bg-background px-2.5 transition-colors hover:bg-accent disabled:opacity-50"
        onClick={onClick}
      >
        <span aria-hidden="true" className="flex items-center [&>svg]:size-4">{icon}</span>
      </button>
    </Tooltip>
  );
}
