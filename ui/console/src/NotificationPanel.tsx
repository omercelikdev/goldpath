import { useCallback, useEffect, useState } from "react";
import { Banner, FacetFilter, KeysetTable, Sheet, StateBadge, Table, humanizeSeconds } from "@goldpath/kit";
import type { AdminClient, NotificationInfo, NotificationTemplateStatus } from "./adminClient";

export interface NotificationPanelProps {
  client: AdminClient;
}

/** The evidence row's state machine, in its own order. */
const STATES = ["Requested", "Suppressed", "Sent", "Failed"] as const;

/** The three lenses the contract exposes: the full list, and its two focused cuts. */
const LENSES = [
  { key: "all", label: "All notifications" },
  { key: "failures", label: "Failures" },
  { key: "suppressions", label: "Suppressions" },
] as const;

type Lens = (typeof LENSES)[number]["key"];

/**
 * .NET serializes a TimeSpan as `d.hh:mm:ss` — shown as-is would read like a timestamp,
 * so the retention promise is spelled out in days/hours.
 */
export function retentionWords(deleteBodyAfter: string | null | undefined): string {
  if (!deleteBodyAfter) return "kept";
  const match = /^(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})/.exec(deleteBodyAfter);
  if (!match) return deleteBodyAfter;
  const [, days, hours, minutes] = match;
  const totalHours = Number(days ?? 0) * 24 + Number(hours);
  if (totalHours >= 48) return `body deleted after ${Math.round(totalHours / 24)}d`;
  if (totalHours >= 1) return `body deleted after ${totalHours}h`;
  return `body deleted after ${Number(minutes)}m`;
}

/**
 * The notification evidence panel (console RFC §3). The surface is READ-ONLY by contract
 * and so is this screen: requesting belongs to the app (a console that could inject
 * messages would be an evidence hole) and re-sending belongs to the run console. What the
 * operator gets here is the evidence: who was written to (masked), which template hash
 * rendered it, when it was claimed, sent or failed, and why.
 */
export function NotificationPanel({ client }: NotificationPanelProps) {
  const [templates, setTemplates] = useState<NotificationTemplateStatus[] | null>(null);
  const [lens, setLens] = useState<Lens>("all");
  const [state, setState] = useState<string>("");
  const [template, setTemplate] = useState<string>("");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [selected, setSelected] = useState<NotificationInfo | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [refreshToken, setRefreshToken] = useState(0);

  const refresh = () => setRefreshToken((token) => token + 1);

  useEffect(() => {
    let live = true;
    client
      .notificationTemplates()
      .then((found) => live && setTemplates(found))
      .catch(() => live && setError("the notification templates could not be loaded"));
    return () => {
      live = false;
    };
  }, [client, refreshToken]);

  const loadRows = useCallback(
    async (_cursor: string | null, take: number) => {
      // Each lens is the contract's OWN route — the focused cuts are not this screen
      // re-filtering the broad list, so a server-side change to either is visible here.
      const rows =
        lens === "failures"
          ? await client.notificationFailures(take)
          : lens === "suppressions"
            ? await client.notificationSuppressions(take)
            : await client.notifications({ state: state || undefined, template: template || undefined, take });
      return { items: rows, nextCursor: null };
    },
    [client, lens, state, template, refreshToken],
  );

  // The open row RE-READS by id: a notification is evidence in MOTION (Requested → Sent
  // or Failed), so a row captured minutes ago is not what the operator should judge.
  useEffect(() => {
    if (!selectedId) {
      setSelected(null);
      return;
    }

    let live = true;
    client
      .notification(selectedId)
      .then((found) => live && setSelected(found))
      .catch(() => live && setError(`notification ${selectedId} could not be read`));
    return () => {
      live = false;
    };
  }, [client, selectedId, refreshToken]);

  const waiting = (templates ?? []).filter((entry) => (entry.oldestRequestedSeconds ?? 0) > 0);

  return (
    <div data-testid="notification-panel" className="space-y-6">
      {error && <Banner tone="danger">{error}</Banner>}

      {waiting.length > 0 && (
        <Banner tone="info" live="status">
          {waiting
            .map((entry) => `${entry.key}: oldest request waiting ${humanizeSeconds(entry.oldestRequestedSeconds ?? 0)}`)
            .join(" · ")}
        </Banner>
      )}

      <section>
        <h2 className="section-title">Templates</h2>
        <Table
          columns={[
            { header: "Template", cell: (entry) => <span className="font-medium">{entry.key}</span> },
            {
              header: "Body hash",
              cell: (entry) => (
                // The hash is what proves WHICH text was sent — truncated for the eye, whole on hover.
                <span className="font-mono text-xs text-faint" title={entry.hash}>{entry.hash.slice(0, 12)}</span>
              ),
            },
            { header: "Retention", cell: (entry) => <span className="text-xs text-faint">{retentionWords(entry.deleteBodyAfter)}</span> },
            {
              header: "By state",
              cell: (entry) => (
                <span className="flex flex-wrap gap-2">
                  {Object.entries(entry.byState).map(([key, count]) => (
                    <span key={key} className="text-xs text-faint">{key}: {count}</span>
                  ))}
                  {Object.keys(entry.byState ?? {}).length === 0 && <span className="text-xs text-faint">nothing requested yet</span>}
                </span>
              ),
            },
          ]}
          rows={templates ?? []}
          rowKey={(entry) => entry.key}
          emptyMessage="No templates are registered in this app."
        />
      </section>

      <section data-testid="evidence">
        <div className="mb-2 flex flex-wrap items-center gap-3">
          {/* Choose-one-of-few = the pill rail (§8.6); the black pill retires here too. */}
          <div className="inline-flex gap-1 rounded-xl bg-muted p-1">
            {LENSES.map((entry) => (
              <button
                key={entry.key}
                aria-pressed={lens === entry.key}
                className={`rounded-lg px-3.5 py-1.5 text-sm font-semibold transition-colors ${
                  lens === entry.key ? "bg-background shadow-sm" : "text-muted-foreground hover:text-foreground"
                }`}
                onClick={() => {
                  setLens(entry.key);
                  setSelectedId(null);
                }}
              >
                {entry.label}
              </button>
            ))}
          </div>

          {lens === "all" && (
            <>
              {/* Single-commit facets: the frozen ?state=/?template= take one value each. */}
              <FacetFilter
                label="State"
                options={STATES.map((option) => ({ value: option }))}
                selected={new Set(state ? [state] : [])}
                onToggle={(value) => {
                  setState(value === state ? "" : value);
                  setSelectedId(null);
                }}
                onClear={() => {
                  setState("");
                  setSelectedId(null);
                }}
              />
              <FacetFilter
                label="Template"
                options={(templates ?? []).map((entry) => ({ value: entry.key }))}
                selected={new Set(template ? [template] : [])}
                onToggle={(value) => {
                  setTemplate(value === template ? "" : value);
                  setSelectedId(null);
                }}
                onClear={() => {
                  setTemplate("");
                  setSelectedId(null);
                }}
              />
            </>
          )}

          <button
            className="btn-quiet ml-auto"
            onClick={refresh}
          >
            refresh
          </button>
        </div>

        <KeysetTable<NotificationInfo>
          key={`${lens}-${state}-${template}-${refreshToken}`}
          columns={[
            {
              header: "Recipient (masked)",
              cell: (row) => (
                <button className="font-mono text-xs underline-offset-2 hover:underline" onClick={() => setSelectedId(row.id)}>
                  {row.maskedRecipient}
                </button>
              ),
            },
            { header: "Template", cell: (row) => row.template },
            { header: "Channel", cell: (row) => row.channel },
            { header: "State", cell: (row) => <StateBadge state={row.state} /> },
            { header: "Attempts", align: "right", cell: (row) => row.attempts },
            { header: "Requested", cell: (row) => row.requestedAt },
          ]}
          loadPage={loadRows}
          rowKey={(row) => row.id}
          emptyMessage={
            lens === "failures"
              ? "No failed notifications."
              : lens === "suppressions"
                ? "Nothing was suppressed."
                : "No notifications match this filter."
          }
        />
        <p className="mt-1 text-xs text-faint">
          Recipients are masked by the API, not by this screen — the full address never leaves the store. Requesting
          belongs to the app and re-sending to the run console, so this surface has no verbs at all.
        </p>
      </section>

      {/* Evidence opens in the right Sheet (§8.14) — the inline-below card retires. */}
      <Sheet
        open={selected !== null}
        onOpenChange={(open) => {
          if (!open) setSelectedId(null);
        }}
        title={selected ? `${selected.template} → ${selected.maskedRecipient}` : ""}
        description={selected ? "The evidence row: identity, timeline, and the transport's words." : undefined}
      >
      {selected && (
        <section data-testid="notification-detail">
          <div className="mb-3 flex flex-wrap items-center gap-3">
            <StateBadge state={selected.state} />
            <span className="text-xs text-faint">{selected.channel} · {selected.culture || "default culture"}</span>
          </div>

          <dl className="grid grid-cols-2 gap-x-6 gap-y-2 text-xs sm:grid-cols-3">
            <div>
              <dt className="text-faint">Dedup key</dt>
              {/* The business identity that makes a retry storm land once. */}
              <dd className="font-mono break-all">{selected.dedupKey}</dd>
            </div>
            <div>
              <dt className="text-faint">Template hash</dt>
              <dd className="font-mono break-all">{selected.templateHash}</dd>
            </div>
            <div>
              <dt className="text-faint">Attempts</dt>
              <dd>{selected.attempts}</dd>
            </div>
            <div>
              <dt className="text-faint">Requested</dt>
              <dd>{selected.requestedAt}</dd>
            </div>
            <div>
              <dt className="text-faint">Not before</dt>
              <dd>{selected.notBefore ?? "—"}</dd>
            </div>
            <div>
              <dt className="text-faint">Claimed</dt>
              <dd>{selected.claimedAt ?? "—"}</dd>
            </div>
            <div>
              <dt className="text-faint">Sent</dt>
              <dd>{selected.sentAt ?? "—"}</dd>
            </div>
            <div>
              <dt className="text-faint">Failed</dt>
              <dd className={selected.failedAt ? "text-danger" : undefined}>{selected.failedAt ?? "—"}</dd>
            </div>
            <div>
              <dt className="text-faint">Body</dt>
              {/* Retention is a promise the module keeps; the row records when it was kept. */}
              <dd>{selected.bodyDeletedAt ? `deleted ${selected.bodyDeletedAt}` : "retained"}</dd>
            </div>
            {selected.tenant && (
              <div>
                <dt className="text-faint">Tenant</dt>
                <dd>{selected.tenant}</dd>
              </div>
            )}
            {selected.correlationId && (
              <div>
                <dt className="text-faint">Correlation</dt>
                <dd className="font-mono break-all">{selected.correlationId}</dd>
              </div>
            )}
          </dl>

          {selected.detail && (
            // The server's own words: the transport's refusal, or why it was suppressed.
            <p className={`mt-3 text-xs ${selected.state === "Failed" ? "text-danger" : "text-muted-foreground"}`}>
              {selected.detail}
            </p>
          )}
        </section>
      )}
      </Sheet>
    </div>
  );
}
