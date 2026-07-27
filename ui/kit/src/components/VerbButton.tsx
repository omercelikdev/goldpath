import { useEffect, useRef, useState } from "react";
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
  const dialog = useRef<HTMLSpanElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const [reason, setReason] = useState("");
  const missingReason = note?.required === true && reason.trim().length === 0;

  /**
   * Leaving the confirm without firing — the same exit whichever way the operator took.
   * Focus goes BACK to the button that opened it: the dialog is about to unmount, and a
   * keyboard operator whose focus falls to <body> has lost their place on the page.
   */
  const cancel = () => {
    setPhase({ at: "rest" });
    setReason("");
    requestAnimationFrame(() => trigger.current?.focus());
  };

  // Focus moves to the dialog ONCE, when it opens: re-focusing on every render would
  // yank the caret out of the note field between keystrokes.
  useEffect(() => {
    if (phase.at === "confirming") dialog.current?.focus();
  }, [phase.at]);

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
      <span
        role="alertdialog"
        aria-label={`confirm ${label}`}
        // ESCAPE closes it. An operator who opened a destructive confirm by mistake must
        // be able to back out with the key every dialog on earth uses, without hunting
        // for a mouse (found by the a11y gate). Keyed on the wrapper so it works whether
        // focus is on the note field or a button.
        onKeyDown={(event) => {
          if (event.key === "Escape") {
            event.stopPropagation();
            cancel();
          }
        }}
        ref={dialog}
        tabIndex={-1}
        className="inline-flex flex-wrap items-center gap-2 rounded-md border border-border bg-background px-3 py-1.5 text-sm outline-none"
      >
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
        <button className="rounded-md border border-border px-2 py-0.5 text-xs hover:bg-accent" onClick={cancel}>
          cancel
        </button>
      </span>
    );
  }

  return (
    <span className="inline-flex items-center gap-2">
      <button
        ref={trigger}
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
