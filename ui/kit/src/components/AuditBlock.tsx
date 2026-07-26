/** One audit row of the frozen log, as the console reads it. */
export interface AuditEntry {
  id: number | string;
  timestamp: string;
  user?: string | null;
  correlationId?: string | null;
  entityType: string;
  entityKey: string;
  action: string;
  propertyName: string;
  oldValue?: string | null;
  newValue?: string | null;
}

export interface AuditBlockProps {
  entries: AuditEntry[];
  /** Property names whose values are classified — rendered masked, NEVER in the DOM. */
  classified?: string[];
  emptyMessage?: string;
}

/** The system actor label — a null user is a SYSTEM flow, never an unknown human. */
export const SYSTEM_ACTOR = "system";

export function isClassified(propertyName: string, classified: string[] | undefined): boolean {
  return classified?.some((name) => name.toLowerCase() === propertyName.toLowerCase()) ?? false;
}

const MASK = "••••••";

function Value({ value, masked }: { value: string | null | undefined; masked: boolean }) {
  if (masked) {
    return <span className="font-mono text-xs text-faint" title="classified — masked by the console">{MASK}</span>;
  }

  if (value === null || value === undefined) {
    return <span className="text-xs text-faint">∅</span>;   // an absent value, not an empty string
  }

  return <span className="font-mono text-xs">{value}</span>;
}

/**
 * The audit trail block of ui-standard-v1 §4: old→new change rows with classified
 * fields masked. The mask is applied HERE as the second line of defence — the API
 * already refuses to hand out classified values — so a misconfigured server can never
 * leak through the console's DOM.
 */
export function AuditBlock({ entries, classified, emptyMessage = "No audited changes." }: AuditBlockProps) {
  if (entries.length === 0) {
    return <p className="py-6 text-center text-sm text-muted-foreground">{emptyMessage}</p>;
  }

  return (
    <div data-testid="audit-block" className="space-y-1">
      {entries.map((entry) => {
        const masked = isClassified(entry.propertyName, classified);
        return (
          <div key={entry.id} className="grid grid-cols-[auto_1fr] gap-x-4 rounded-md border border-border/60 px-3 py-2 text-sm">
            <div className="text-xs text-muted-foreground">
              <div>{new Date(entry.timestamp).toISOString().replace("T", " ").slice(0, 19)}</div>
              <div className="text-faint">{entry.user ?? SYSTEM_ACTOR}</div>
            </div>
            <div>
              <div className="flex flex-wrap items-baseline gap-x-2">
                <span className="font-medium">{entry.propertyName}</span>
                <span className="text-xs text-muted-foreground">
                  {entry.action} · {entry.entityType} {entry.entityKey}
                </span>
                {masked && <span className="text-xs text-faint">(classified)</span>}
              </div>
              <div className="mt-0.5 flex flex-wrap items-center gap-2">
                <Value value={entry.oldValue} masked={masked} />
                <span className="text-xs text-faint">→</span>
                <Value value={entry.newValue} masked={masked} />
              </div>
              {entry.correlationId && (
                <div className="mt-0.5 font-mono text-[11px] text-faint">corr {entry.correlationId}</div>
              )}
            </div>
          </div>
        );
      })}
    </div>
  );
}
