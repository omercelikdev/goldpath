import { useEffect, useState } from "react";
import { Banner, StateBadge } from "@goldpath/kit";
import type { AdminClient, ModuleName } from "./adminClient";
import type { Capabilities } from "./sections";
import { collectServiceTriage, orderTriage, TRIAGE_SCOPE, type TriageRow } from "./triage";

export interface TriageService {
  name: string;
  client: AdminClient;
  capabilities: Capabilities | null;
}

export interface TriageHomeProps {
  services: TriageService[];
  /** Opens the panel a row belongs to, on the service it belongs to. */
  onOpen: (service: string, section: ModuleName) => void;
  now?: Date;
}

/**
 * The operator's first screen (console RFC D2): "is anything wrong", across every service
 * in the registry. Fleet browsing is one click away, never the landing page — an operator
 * opens a console to answer that question, not to browse.
 *
 * Every row is a deep link into the panel that owns it. Nothing here is a metric the API
 * does not already publish: the triage reads the same take-bounded lists an operator would
 * and counts what it was given, which is why the scope is printed rather than implied.
 */
export function TriageHome({ services, onOpen, now }: TriageHomeProps) {
  const [rows, setRows] = useState<TriageRow[] | null>(null);
  const [refreshToken, setRefreshToken] = useState(0);

  const ready = services.filter((service) => service.capabilities !== null);
  const readyKey = ready.map((service) => service.name).join("|");

  useEffect(() => {
    if (ready.length === 0) return;
    let live = true;
    setRows(null);
    void Promise.all(
      ready.map((service) => collectServiceTriage(service.name, service.client, service.capabilities!, now)),
    ).then((collected) => live && setRows(orderTriage(collected.flat())));
    return () => {
      live = false;
    };
    // `readyKey` is the honest dependency: the set of services whose capabilities are known.
  }, [readyKey, refreshToken]);

  const waiting = services.length - ready.length;

  return (
    <div data-testid="triage-home" className="space-y-4">
      <div className="flex flex-wrap items-baseline gap-3">
        <h2 className="text-sm font-medium">Today</h2>
        <span className="text-xs text-faint">across {services.length} service{services.length === 1 ? "" : "s"} · {TRIAGE_SCOPE}</span>
        <button
          className="ml-auto rounded-md border border-border bg-background px-3 py-1 text-sm hover:bg-accent"
          onClick={() => setRefreshToken((token) => token + 1)}
        >
          refresh
        </button>
      </div>

      {waiting > 0 && (
        <p className="text-xs text-muted-foreground">
          {waiting} service{waiting === 1 ? " is" : "s are"} still answering — this list will grow.
        </p>
      )}

      {rows === null && ready.length > 0 && <p className="text-sm text-muted-foreground">Reading every surface…</p>}

      {rows !== null && rows.length === 0 && (
        // The quiet morning. Said plainly, and scoped — "nothing found HERE" is not the
        // same claim as "nothing is wrong", and the console must not make the second one.
        <Banner tone="success" live="status">
          Nothing is waiting for you in {TRIAGE_SCOPE}.
        </Banner>
      )}

      {rows !== null && rows.length > 0 && (
        <ul className="space-y-2">
          {rows.map((row, index) => (
            <li
              key={`${row.service}-${row.section}-${row.headline}-${index}`}
              className="flex flex-wrap items-center gap-3 rounded-md border border-border/60 px-3 py-2"
            >
              {/* The word carries the meaning; the tone only reinforces it. Colour alone
                  would leave the row unreadable to whoever cannot see the difference. */}
              <StateBadge
                state={row.blind ? "cannot see" : row.tone === "danger" ? "went wrong" : "will, unless"}
                tone={row.tone}
              />
              <button
                className="text-sm font-medium underline-offset-2 hover:underline"
                onClick={() => onOpen(row.service, row.section)}
              >
                {row.headline}
              </button>
              <span className="text-xs text-muted-foreground">{row.detail}</span>
              <span className="ml-auto text-xs text-faint">{row.service}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
