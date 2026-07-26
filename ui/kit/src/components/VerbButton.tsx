import { useState } from "react";
import { Banner } from "./Banner";
import type { VerbOutcome } from "../adminResult";

export interface VerbButtonProps {
  /** The verb's label — kebab-case on the wire, human words here. */
  label: string;
  /** The confirm question. Confirm-before-verb is NOT optional (ui-standard §3). */
  confirm: string;
  /** Executes the verb; receives the evidence note when the verb collects one. */
  execute: (note?: string) => Promise<VerbOutcome>;
  /**
   * Turns the confirm step into an EVIDENCE step: the operator must type why before the
   * verb runs (four-eyes gates, holds, erasures — the note is the audit trail's reason,
   * and the server stores it). Omit for verbs that carry no reason.
   */
  note?: { label: string; required?: boolean };
  /** Fired after every settled outcome (refresh tables, close panels...). */
  onDone?: (outcome: VerbOutcome) => void;
  /** Marks destructive verbs (reject, erase, pause-all) — the tone, not the flow. */
  destructive?: boolean;
  /**
   * Suppresses the button's OWN outcome strip, for verbs whose control legitimately
   * disappears once the verb lands (a four-eyes gate vanishes the moment the batch
   * leaves the gated state). The composite must then render the outcome itself from
   * `onDone` — otherwise the operator's confirmation dies with the button.
   */
  quiet?: boolean;
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
export function VerbButton({ label, confirm, execute, onDone, destructive = false, note, quiet = false }: VerbButtonProps) {
  const [phase, setPhase] = useState<Phase>({ at: "rest" });
  const [reason, setReason] = useState("");
  const missingReason = note?.required === true && reason.trim().length === 0;

  const run = async () => {
    setPhase({ at: "executing" });
    let outcome: VerbOutcome;
    try {
      outcome = await execute(note ? reason.trim() : undefined);
    } catch {
      outcome = { kind: "error", status: 0 };   // transport failure — the verb may not have run
    }

    setPhase({ at: "settled", outcome });
    setReason("");
    onDone?.(outcome);
  };

  if (phase.at === "confirming") {
    return (
      <span role="alertdialog" aria-label={`confirm ${label}`} className="inline-flex flex-wrap items-center gap-2 rounded-md border border-border bg-background px-3 py-1.5 text-sm">
        <span>{confirm}</span>
        <span className="text-xs text-faint">· audited</span>
        {note && (
          <input
            aria-label={note.label}
            placeholder={note.label}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            className="w-56 rounded-md border border-border bg-background px-2 py-0.5 text-xs"
          />
        )}
        <button
          className={`rounded-md border px-2 py-0.5 text-xs font-medium disabled:opacity-50 ${destructive ? "border-danger-border bg-danger-bg text-danger" : "border-border bg-background hover:bg-accent"}`}
          disabled={missingReason}
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

      {!quiet && phase.at === "settled" && phase.outcome.kind === "ok" && (
        <Banner tone="success" live="status" dense>{phase.outcome.message}</Banner>
      )}

      {!quiet && phase.at === "settled" && phase.outcome.kind === "refused" && (
        // The refusal surface: the envelope's message VERBATIM — it teaches the fix.
        <Banner tone="danger" dense>{phase.outcome.message}</Banner>
      )}

      {!quiet && phase.at === "settled" && phase.outcome.kind === "error" && (
        <Banner tone="warning" dense>
          {phase.outcome.status === 0
            ? "the request did not reach the server — the verb may not have run"
            : `unexpected ${phase.outcome.status} — check the service logs`}
        </Banner>
      )}
    </span>
  );
}
