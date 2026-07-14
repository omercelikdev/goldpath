// ui-standard-v1 §5: domain states map onto the SEMANTIC ramp — the accent never
// carries meaning, the ramp never carries brand. One table, every badge.
export type StatusTone = "success" | "info" | "warning" | "danger" | "violet" | "neutral";

const MAP: Record<string, StatusTone> = {
  // run model
  Completed: "success",
  Running: "info",
  Failed: "danger",
  Recovering: "violet",
  Resumed: "violet",
  // bulk batch
  Received: "info",
  Validating: "info",
  Validated: "warning", // awaiting the gate
  Executing: "info",
  CompletedWithFailures: "danger",
  Rejected: "danger",
  // notification
  Requested: "info",
  Sent: "success",
  Suppressed: "warning",
  // payments (sample vocabulary — adopters extend via `extra`)
  Submitted: "info",
  PendingApproval: "warning",
  Executed: "success",
};

/** Resolves a domain state to its ramp tone; unknown states are honest neutrals. */
export function statusTone(state: string, extra?: Record<string, StatusTone>): StatusTone {
  return extra?.[state] ?? MAP[state] ?? "neutral";
}
