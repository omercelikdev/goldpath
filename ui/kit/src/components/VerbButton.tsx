import { useState } from "react";
import type { VerbOutcome } from "../adminResult";

export interface VerbButtonProps {
  /** The verb's label — kebab-case on the wire, human words here. */
  label: string;
  /** The confirm question. Confirm-before-verb is NOT optional (ui-standard §3). */
  confirm: string;
  /** Executes the verb — usually `() => executeVerb(url, init)`. */
  execute: () => Promise<VerbOutcome>;
  /** Fired after every settled outcome (refresh tables, close panels...). */
  onDone?: (outcome: VerbOutcome) => void;
  /** Marks destructive verbs (reject, erase, pause-all) — the tone, not the flow. */
  destructive?: boolean;
}

type Phase =
  | { at: "rest" }
  | { at: "confirming" }
  | { at: "executing" }
  | { at: "settled"; outcome: VerbOutcome };

/**
 * The verb button of ui-standard-v1 §4: every mutating admin verb goes through the
 * confirm dialog, the `GoldpathAdminResult` message is surfaced VERBATIM (refusals
 * TEACH — the UI never paraphrases them), and the audit hint reminds the operator the
 * server records every verb (the actor comes from the token, never the UI).
 */
export function VerbButton({ label, confirm, execute, onDone, destructive = false }: VerbButtonProps) {
  const [phase, setPhase] = useState<Phase>({ at: "rest" });

  const run = async () => {
    setPhase({ at: "executing" });
    let outcome: VerbOutcome;
    try {
      outcome = await execute();
    } catch {
      outcome = { kind: "error", status: 0 };   // transport failure — the verb may not have run
    }

    setPhase({ at: "settled", outcome });
    onDone?.(outcome);
  };

  if (phase.at === "confirming") {
    return (
      <span role="alertdialog" aria-label={`confirm ${label}`} className="inline-flex items-center gap-2 rounded-md border border-border bg-background px-3 py-1.5 text-sm">
        <span>{confirm}</span>
        <span className="text-xs text-faint">· audited</span>
        <button
          className={`rounded-md border px-2 py-0.5 text-xs font-medium ${destructive ? "border-danger-border bg-danger-bg text-danger" : "border-border bg-background hover:bg-accent"}`}
          onClick={() => void run()}
        >
          {label}
        </button>
        <button className="rounded-md border border-border px-2 py-0.5 text-xs hover:bg-accent" onClick={() => setPhase({ at: "rest" })}>
          cancel
        </button>
      </span>
    );
  }

  return (
    <span className="inline-flex items-center gap-2">
      <button
        className={`rounded-md border px-3 py-1.5 text-sm font-medium disabled:opacity-50 ${destructive ? "border-danger-border text-danger hover:bg-danger-bg" : "border-border bg-background hover:bg-accent"}`}
        disabled={phase.at === "executing"}
        onClick={() => setPhase({ at: "confirming" })}
      >
        {phase.at === "executing" ? "working…" : label}
      </button>

      {phase.at === "settled" && phase.outcome.kind === "ok" && (
        <span role="status" className="rounded-md border border-success-border bg-success-bg px-2 py-0.5 text-xs text-success">
          {phase.outcome.message}
        </span>
      )}

      {phase.at === "settled" && phase.outcome.kind === "refused" && (
        // The refusal surface: the envelope's message VERBATIM — it teaches the fix.
        <span role="alert" className="rounded-md border border-danger-border bg-danger-bg px-2 py-0.5 text-xs text-danger">
          {phase.outcome.message}
        </span>
      )}

      {phase.at === "settled" && phase.outcome.kind === "error" && (
        <span role="alert" className="rounded-md border border-warning-border bg-warning-bg px-2 py-0.5 text-xs text-warning">
          {phase.outcome.status === 0
            ? "the request did not reach the server — the verb may not have run"
            : `unexpected ${phase.outcome.status} — check the service logs`}
        </span>
      )}
    </span>
  );
}
