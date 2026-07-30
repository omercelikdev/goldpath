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
  /** Another SECTION asked for a run (a batch's run id) — open History with it. */
  openRunRequest?: { id: string } | null;
  /** Threaded to History's ack: the section OWNER clears the ask once it landed. */
  onRunRequestConsumed?: () => void;
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
export function RunConsole({ client, now, openRunRequest, onRunRequestConsumed }: RunConsoleProps) {
  const [fleets, setFleets] = useState<FleetInfo[]>([]);
  const [fleet, setFleet] = useState<string | null>(null);
  const [tab, setTab] = useState("overview");
  // Cross-screen intents (v1.1 §7.9): a history row asks for its JOB, another section
  // asks for a RUN. Fresh objects each ask, so repeating an ask still lands.
  const [jobRequest, setJobRequest] = useState<{ name: string } | null>(null);

  useEffect(() => {
    if (openRunRequest) setTab("history");
  }, [openRunRequest]);
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
        <h2 className="section-title">Fleets</h2>
        {/* Choose-one-of-few is the PILL pattern here too (§8.6) — the black pill retires. */}
        <div className="inline-flex flex-wrap gap-1 rounded-xl bg-muted p-1">
          {fleets.map((entry) => (
            <button
              key={entry.schedulerName}
              aria-pressed={entry.schedulerName === fleet}
              className={`rounded-lg px-3.5 py-1.5 text-sm font-semibold transition-colors ${
                entry.schedulerName === fleet ? "bg-background shadow-sm" : "text-muted-foreground hover:text-foreground"
              }`}
              onClick={() => setFleet(entry.schedulerName)}
            >
              {entry.schedulerName}
              <span className="ml-2 text-xs font-normal text-faint">
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
            <JobsTab
              key={fleet}
              client={client}
              fleet={fleet}
              refreshToken={refreshToken}
              onChanged={refresh}
              openJobRequest={jobRequest}
              onJobRequestConsumed={() => setJobRequest(null)}
              onShowCalendars={() => setTab("calendars")}
            />
          </TabPanel>
          <TabPanel id="calendars" activeId={tab}>
            <CalendarsTab key={fleet} client={client} fleet={fleet} refreshToken={refreshToken} onChanged={refresh} />
          </TabPanel>
          <TabPanel id="history" activeId={tab}>
            <RunHistory
              client={client}
              fleet={fleet}
              refreshToken={refreshToken}
              onChanged={refresh}
              now={now}
              openRunRequest={openRunRequest}
              onRunRequestConsumed={onRunRequestConsumed}
              onOpenJob={(name) => {
                setJobRequest({ name });
                setTab("jobs");
              }}
            />
          </TabPanel>
        </>
      )}
    </div>
  );
}
