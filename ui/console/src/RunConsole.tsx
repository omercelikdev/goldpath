import { useEffect, useState } from "react";
import { Banner, TabPanel, TabStrip } from "@goldpath/kit";
import type { AdminClient, FleetInfo } from "./adminClient";
import { FleetOverview } from "./FleetOverview";
import { JobsTab } from "./JobsTab";
import { CalendarsTab } from "./CalendarsTab";
import { RunHistory } from "./RunHistory";

export interface RunConsoleProps {
  client: AdminClient;
  /** Injected in tests; the composite reads the clock only for rate/prediction display. */
  now?: Date;
}

const TABS = [
  { id: "overview", label: "Overview" },
  { id: "jobs", label: "Jobs" },
  { id: "calendars", label: "Calendars" },
  { id: "history", label: "History" },
];

/**
 * The scheduling surface (console RFC U5, admin contract R2): fleets, then what a fleet
 * IS (overview), what it runs and when (jobs and their triggers), what it excludes
 * (calendars), and what it has done (history).
 *
 * Everything here is a client of the frozen contract; the screen holds no state the API
 * does not own, and offers no verb the API does not have — notably, no way to create or
 * delete a job, because composition belongs to the manifest (ADR-0001).
 */
export function RunConsole({ client, now }: RunConsoleProps) {
  const [fleets, setFleets] = useState<FleetInfo[]>([]);
  const [fleet, setFleet] = useState<string | null>(null);
  const [tab, setTab] = useState("overview");
  const [error, setError] = useState<string | null>(null);
  const [refreshToken, setRefreshToken] = useState(0);

  useEffect(() => {
    let live = true;
    client
      .fleets()
      .then((found) => {
        if (!live) return;
        setFleets(found);
        setFleet((current) => current ?? found[0]?.schedulerName ?? null);
      })
      .catch(() => live && setError("the fleet list could not be loaded"));
    return () => {
      live = false;
    };
  }, [client]);

  const refresh = () => setRefreshToken((token) => token + 1);

  if (fleets.length === 0 && !error) {
    return <p className="text-sm text-muted-foreground">Discovering fleets…</p>;
  }

  return (
    <div data-testid="run-console" className="space-y-4">
      {error && <Banner tone="danger">{error}</Banner>}

      <section>
        <h2 className="mb-2 text-sm font-medium text-muted-foreground">Fleets</h2>
        <div className="flex flex-wrap gap-2">
          {fleets.map((entry) => (
            <button
              key={entry.schedulerName}
              aria-pressed={entry.schedulerName === fleet}
              className={`rounded-md border px-3 py-1.5 text-sm ${
                entry.schedulerName === fleet ? "border-border bg-primary text-primary-foreground" : "border-border bg-background hover:bg-accent"
              }`}
              onClick={() => setFleet(entry.schedulerName)}
            >
              {entry.schedulerName}
              <span className="ml-2 text-xs opacity-70">
                {entry.jobCount} jobs · {entry.nodes?.length ?? 0} nodes
              </span>
            </button>
          ))}
        </div>
      </section>

      {fleet && (
        <>
          <TabStrip label={`Sections of ${fleet}`} items={TABS} activeId={tab} onSelect={setTab} />

          {/*
            Panels are keyed by FLEET only, and refresh travels as a prop.
            Keying them by the refresh token too would remount a panel after every verb —
            and a remounted panel takes the verb's outcome strip with it, so a REFUSAL
            would vanish the instant it arrived. That is the U2 teardown lesson: the
            operator must keep reading the server's own sentence after acting.
          */}
          <TabPanel id="overview" activeId={tab}>
            <FleetOverview key={fleet} client={client} fleet={fleet} refreshToken={refreshToken} onChanged={refresh} />
          </TabPanel>
          <TabPanel id="jobs" activeId={tab}>
            <JobsTab key={fleet} client={client} fleet={fleet} refreshToken={refreshToken} onChanged={refresh} />
          </TabPanel>
          <TabPanel id="calendars" activeId={tab}>
            <CalendarsTab key={fleet} client={client} fleet={fleet} refreshToken={refreshToken} onChanged={refresh} />
          </TabPanel>
          <TabPanel id="history" activeId={tab}>
            <RunHistory client={client} fleet={fleet} refreshToken={refreshToken} onChanged={refresh} now={now} />
          </TabPanel>
        </>
      )}
    </div>
  );
}
