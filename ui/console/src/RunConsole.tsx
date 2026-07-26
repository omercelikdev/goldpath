import { useCallback, useEffect, useState } from "react";
import { Banner, KeysetTable, RunProgress, StateBadge, VerbButton } from "@goldpath/kit";
import type { VerbOutcome } from "@goldpath/kit";
import type { AdminClient, FleetInfo, JobInfo, RunDetail, RunSummary } from "./adminClient";

export interface RunConsoleProps {
  client: AdminClient;
  /** Injected in tests; the composite reads the clock only for rate/prediction display. */
  now?: Date;
}

/** The verb envelope, adapted to the kit's outcome type — refusals stay data. */
async function asOutcome(call: Promise<{ ok: boolean; message: string }>): Promise<VerbOutcome> {
  const result = await call;
  return result.ok ? { kind: "ok", message: result.message } : { kind: "refused", message: result.message };
}

/**
 * The run console (console RFC §2, the core screen): fleets → jobs → runs → chunk
 * breakdown → repair queue. Everything is a client of the frozen contract; the screen
 * holds no state the API does not own.
 */
export function RunConsole({ client, now }: RunConsoleProps) {
  const [fleets, setFleets] = useState<FleetInfo[]>([]);
  const [fleet, setFleet] = useState<string | null>(null);
  const [jobs, setJobs] = useState<JobInfo[]>([]);
  const [selectedRun, setSelectedRun] = useState<RunDetail | null>(null);
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

  useEffect(() => {
    if (!fleet) return;
    let live = true;
    client
      .jobs(fleet)
      .then((found) => live && setJobs(found))
      .catch(() => live && setError(`the jobs of ${fleet} could not be loaded`));
    return () => {
      live = false;
    };
  }, [client, fleet, refreshToken]);

  const loadRuns = useCallback(
    async (_cursor: string | null, take: number) => {
      // The runs surface is take-bounded, not cursor-paged (frozen contract): one page,
      // honestly ended — the table's walk stops immediately instead of faking a cursor.
      const runs = fleet ? await client.runs(fleet, { take }) : [];
      return { items: runs, nextCursor: null };
    },
    [client, fleet, refreshToken],
  );

  const openRun = async (run: RunSummary) => {
    try {
      setSelectedRun(await client.run(run.id));
    } catch {
      setError(`run ${run.id} could not be opened`);
    }
  };

  const refresh = () => {
    setRefreshToken((token) => token + 1);
    setSelectedRun(null);
  };

  if (fleets.length === 0 && !error) {
    return <p className="text-sm text-muted-foreground">Discovering fleets…</p>;
  }

  return (
    <div data-testid="run-console" className="space-y-6">
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
              onClick={() => {
                setFleet(entry.schedulerName);
                setSelectedRun(null);
              }}
            >
              {entry.schedulerName}
              <span className="ml-2 text-xs opacity-70">{entry.jobCount} jobs · {entry.nodes?.length ?? 0} nodes</span>
            </button>
          ))}
        </div>
      </section>

      {fleet && (
        <section>
          <h2 className="mb-2 text-sm font-medium text-muted-foreground">Jobs in {fleet}</h2>
          <ul className="space-y-2">
            {jobs.map((job) => (
              <li key={job.name} className="flex flex-wrap items-center gap-3 rounded-md border border-border/60 px-3 py-2">
                <span className="text-sm font-medium">{job.name}</span>
                {job.paused && <StateBadge state="Suppressed" />}
                {job.nextFireTime && <span className="text-xs text-faint">next {job.nextFireTime}</span>}
                <span className="ml-auto flex gap-2">
                  <VerbButton
                    label="trigger"
                    confirm={`Trigger ${job.name} now?`}
                    execute={() => asOutcome(client.triggerJob(fleet, job.name))}
                    onDone={refresh}
                  />
                  {job.paused ? (
                    <VerbButton
                      label="resume"
                      confirm={`Resume ${job.name}?`}
                      execute={() => asOutcome(client.resumeJob(fleet, job.name))}
                      onDone={refresh}
                    />
                  ) : (
                    <VerbButton
                      label="pause"
                      confirm={`Pause ${job.name}?`}
                      execute={() => asOutcome(client.pauseJob(fleet, job.name))}
                      onDone={refresh}
                      destructive
                    />
                  )}
                </span>
              </li>
            ))}
          </ul>
        </section>
      )}

      {fleet && (
        <section>
          <h2 className="mb-2 text-sm font-medium text-muted-foreground">Runs</h2>
          <KeysetTable<RunSummary>
            key={`${fleet}-${refreshToken}`}
            columns={[
              { header: "Run", cell: (run) => (
                <button className="font-mono text-xs underline-offset-2 hover:underline" onClick={() => void openRun(run)}>
                  {run.id}
                </button>
              ) },
              { header: "Job", cell: (run) => run.jobName },
              { header: "State", cell: (run) => <StateBadge state={run.status} /> },
              { header: "Chunks", align: "right", cell: (run) => `${run.completedChunks}/${run.totalChunks}` },
            ]}
            loadPage={loadRuns}
            rowKey={(run) => run.id}
            emptyMessage="No runs recorded for this fleet yet."
          />
        </section>
      )}

      {selectedRun && (
        <section data-testid="run-detail" className="rounded-lg border border-border p-4">
          <div className="mb-3 flex items-center justify-between">
            <h2 className="text-sm font-medium">Run {selectedRun.id} · {selectedRun.jobName}</h2>
            <span className="flex gap-2">
              <VerbButton
                label="rerun"
                confirm={`Rerun ${selectedRun.id}?`}
                execute={() => asOutcome(client.rerun(selectedRun.id))}
                onDone={refresh}
              />
              {selectedRun.failures.length > 0 && (
                <VerbButton
                  label="replay-items"
                  // The verb redrives ALL open items; the listed failures are a capped
                  // VIEW, so a count here would understate what the operator triggers.
                  confirm="Replay all open repair items of this run?"
                  execute={() => asOutcome(client.replayItems(selectedRun.id))}
                  onDone={refresh}
                />
              )}
            </span>
          </div>

          <RunProgress run={selectedRun} now={now} />

          <h3 className="mb-1 mt-4 text-xs text-muted-foreground">Chunks</h3>
          <div className="flex flex-wrap gap-1">
            {selectedRun.chunks.map((chunk) => (
              <span
                key={chunk.index}
                title={`chunk ${chunk.index} · ${chunk.status} · ${chunk.attempts} attempts`}
                className="rounded border border-border px-1.5 py-0.5 text-[11px]"
              >
                {chunk.index}:{chunk.status}
              </span>
            ))}
          </div>

          <h3 className="mb-1 mt-4 text-xs text-muted-foreground">
            Repair queue{selectedRun.failures.length === 0 ? " — empty" : ` (${selectedRun.failures.length} shown)`}
          </h3>
          <ul className="space-y-1">
            {selectedRun.failures.map((failure) => (
              <li key={failure.itemKey} className="flex items-baseline gap-2 text-xs">
                <span className="font-mono">{failure.itemKey}</span>
                <span className="text-faint">chunk {failure.chunkIndex}</span>
                <span className="text-danger">{failure.reason}</span>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}
