import { statusTone, type StatusTone } from "../status";

const TONE_CLASSES: Record<StatusTone, string> = {
  success: "text-success bg-success-bg border-success-border",
  info: "text-info bg-info-bg border-info-border",
  warning: "text-warning bg-warning-bg border-warning-border",
  danger: "text-danger bg-danger-bg border-danger-border",
  violet: "text-violet bg-violet-bg border-violet-border",
  neutral: "text-muted-foreground bg-muted border-border",
};

export interface StateBadgeProps {
  state: string;
  /** Adopter vocabulary — extends, never replaces, the standard map. */
  extra?: Record<string, StatusTone>;
}

/** The state chip of ui-standard-v1 §5: semantic ramp only, never the accent. */
export function StateBadge({ state, extra }: StateBadgeProps) {
  const tone = statusTone(state, extra);
  return (
    <span
      data-tone={tone}
      className={`inline-flex items-center rounded-md border px-2 py-0.5 text-xs font-medium ${TONE_CLASSES[tone]}`}
    >
      {state}
    </span>
  );
}
