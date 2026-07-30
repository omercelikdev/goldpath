import { Pause, Play } from "lucide-react";
import { useEffect, useState } from "react";
import { Banner, shortStamp, StateBadge, Table, VerbButton } from "@goldpath/kit";
import type { AdminAuditRow, AdminClient, FleetStatus } from "./adminClient";
import { asOutcome } from "./verbs";

export interface FleetOverviewProps {
  client: AdminClient;
  fleet: string;
  /** Bumped by a verb: the panel re-READS, it does not remount. */
  refreshToken: number;
  onChanged: () => void;
}

/**
 * What the fleet IS, as opposed to what its jobs do (contract R2.1): is it alive, how big
 * is it, who is in the cluster — and the one verb an operator reaches for at 03:00.
 *
 * `pause-all` has been in the frozen contract since it shipped and had no screen until now
 * (open-threads T13). It is durable and cluster-wide: it pauses every trigger in the
 * STORE, so it survives a restart and applies to every node, unlike a scheduler-level
 * standby which would quietly stop one instance while the others kept firing.
 */
export function FleetOverview({ client, fleet, refreshToken, onChanged }: FleetOverviewProps) {
  const [status, setStatus] = useState<FleetStatus | null>(null);
  const [audit, setAudit] = useState<AdminAuditRow[] | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  useEffect(() => {
    let live = true;
    setProblem(null);
    client
      .fleetStatus(fleet)
      .then((found) => live && setStatus(found))
      .catch(() => live && setProblem(`${fleet} did not report its state`));
    client
      .jobsAudit(25)
      .then((rows) => live && setAudit(rows))
      // An audit an operator cannot read is worth saying out loud: it is the record of
      // who did what, and silence here reads as "nobody did anything".
      .catch(() => live && setAudit([]));
    return () => {
      live = false;
    };
  }, [client, fleet, refreshToken]);

  return (
    <div data-testid="fleet-overview" className="space-y-6">
      {problem && <Banner tone="danger">{problem}</Banner>}

      {status && (
        <section className="card">
          <div className="mb-3 flex flex-wrap items-center gap-3">
            <h3 className="text-sm font-medium">{status.schedulerName}</h3>
            <StateBadge state={status.isPaused ? "Suppressed" : "Running"} />
            <span className="text-xs text-faint">
              {status.isPaused
                ? "every trigger is paused — nothing will fire until someone resumes it"
                : "accepting fires"}
            </span>
            <span className="ml-auto flex gap-2">
              <VerbButton
                label="pause every job"
                icon={<Pause />}
                confirm={`Pause EVERY trigger in ${fleet}? This is cluster-wide and survives a restart — nothing scheduled will fire until it is resumed.`}
                execute={() => asOutcome(client.pauseFleet(fleet))}
                onDone={onChanged}
                destructive
              />
              <VerbButton
                label="resume every job"
                icon={<Play />}
                confirm={`Resume every trigger in ${fleet}?`}
                execute={() => asOutcome(client.resumeFleet(fleet))}
                onDone={onChanged}
              />
            </span>
          </div>

          <dl className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-3">
            <div>
              <dt className="text-xs text-muted-foreground">Jobs declared</dt>
              <dd>{status.jobCount}</dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground">Members</dt>
              <dd>{status.nodes.length}</dd>
            </div>
            <div>
              {/*
                Named for what it IS. These numbers describe the member that answered —
                which, on a management-mode head, is in standby with an idle pool while
                the executors run normally. Presenting them as the fleet's told an
                operator their fleet was holding fires when it was not.
              */}
              <dt className="text-xs text-muted-foreground">This console is connected through</dt>
              <dd className="max-w-full truncate font-mono text-xs" title={status.connection.instanceId}>{status.connection.instanceId}</dd>
            </div>
          </dl>

          <h4 className="control-label mb-1 mt-4 block">
            Cluster {status.nodes.length === 0 ? "— no member has checked in" : `(${status.nodes.length})`}
          </h4>
          <ul className="space-y-1">
            {status.nodes.map((node) => (
              <li key={node.instanceName} className="flex flex-wrap items-baseline gap-3 text-xs">
                <span className="inline-block max-w-[28ch] truncate font-mono align-bottom" title={node.instanceName}>{node.instanceName}</span>
                <span className="text-faint" title={node.lastCheckin}>last check-in {shortStamp(node.lastCheckin)}</span>
                <span className="text-faint">every {node.checkinInterval}</span>
              </li>
            ))}
          </ul>
        </section>
      )}

      <section>
        <h3 className="section-title">Recent admin crossings</h3>
        {/*
          Rendered in the audit's OWN shape rather than through the kit's AuditBlock: that
          component models entity CHANGES (entity type, property, old value, new value),
          and these rows are verb crossings. Forcing one into the other would put fields
          in the DOM that mean nothing.
        */}
        {audit === null ? (
          <p className="text-sm text-muted-foreground">Reading the audit…</p>
        ) : (
          <Table
            columns={[
              { header: "When", cell: (row) => <time className="text-xs text-faint" title={row.at}>{shortStamp(row.at)}</time> },
              { header: "Actor", cell: (row) => <span className="text-xs font-medium">{row.actor}</span> },
              { header: "Action", cell: (row) => <span className="chip">{row.action}</span> },
              { header: "Target", cell: (row) => <span className="font-mono text-xs">{row.fleet}/{row.target}</span> },
              { header: "Detail", cell: (row) => <span className="text-xs text-faint">{row.detail ?? "—"}</span> },
            ]}
            rows={audit}
            rowKey={(row) => String(row.id)}
            emptyMessage="Nobody has verbed this service yet."
          />
        )}
      </section>
    </div>
  );
}
