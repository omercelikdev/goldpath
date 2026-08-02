import { RotateCcw } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { DensityToggle, FacetFilter, KeysetTable, RunProgress, SearchBox, Sheet, StateBadge, VerbButton, shortStamp } from "@goldpath/kit";
import type { AdminClient, RunDetail, RunSummary } from "./adminClient";
import { asOutcome } from "./verbs";

export interface RunHistoryProps {
  client: AdminClient;
  fleet: string;
  refreshToken: number;
  onChanged: () => void;
  now?: Date;
  /** Cross-screen link (v1.1 §7.9): the run's JOB opens on the Jobs tab. */
  onOpenJob?: (jobName: string) => void;
  /** Another screen asked for this run — a fresh object each ask, so asking twice works. */
  openRunRequest?: { id: string } | null;
  /** Called once the ask is consumed, so the OWNER clears it — an intent left standing
      replays on every remount of this panel (review R3 on #118). */
  onRunRequestConsumed?: () => void;
}

const STATES = ["Running", "Completed", "Failed"];

/**
 * The run history (contract R2.4): the same list as before, but answerable — "yesterday's
 * failures" is a filter now rather than a scroll, and the walk is a real KEYSET one.
 *
 * Why the cursor matters here specifically: runs are inserted at the HEAD while an
 * operator reads, so an offset-paged second page would skip rows that shifted down. The
 * server continues strictly after the last row we were given.
 */
export function RunHistory({ client, fleet, refreshToken, onChanged, now, onOpenJob, openRunRequest, onRunRequestConsumed }: RunHistoryProps) {
  const [statuses, setStatuses] = useState<string[]>([]);
  const [job, setJob] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null);
  const [selectedRun, setSelectedRun] = useState<RunDetail | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  // The intent arrives as an OBJECT so the same run can be asked for twice in a row —
  // and is ACKED as consumed, or the leftover ask would replay on the next remount.
  useEffect(() => {
    if (openRunRequest) {
      setSelectedRunId(openRunRequest.id);
      onRunRequestConsumed?.();
    }
    // The ack callback is deliberately not a dependency: the effect answers the ASK.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [openRunRequest]);

  const loadRuns = useCallback(
    async (cursor: string | null, take: number) => {
      const runs = await client.runs(fleet, {
        take,
        job: job || undefined,
        status: statuses.length > 0 ? statuses : undefined,
        // The inputs are date-only; the window an operator means by "from the 27th" runs
        // to the END of the 27th, so the upper bound is stretched to that day's last
        // instant rather than its first — otherwise "from X to X" returns nothing.
        from: from ? `${from}T00:00:00Z` : undefined,
        to: to ? `${to}T23:59:59Z` : undefined,
        afterId: cursor ?? undefined,
      });
      // A short page is the end of the walk; a full one may have more behind it.
      return { items: runs, nextCursor: runs.length < take ? null : (runs[runs.length - 1]?.id ?? null) };
    },
    [client, fleet, job, statuses, from, to, refreshToken],
  );

  useEffect(() => {
    if (!selectedRunId) {
      setSelectedRun(null);
      return;
    }

    let live = true;
    client
      .run(selectedRunId)
      // The open run RE-FETCHES rather than closing on refresh: the verb outcome strip
      // lives inside this panel, and tearing it down would hide the message the operator
      // just produced (the U2 lesson).
      .then((detail) => live && setSelectedRun(detail))
      .catch(() => live && setProblem(`run ${selectedRunId} could not be opened`));
    return () => {
      live = false;
    };
  }, [client, selectedRunId, refreshToken]);

  return (
    <div data-testid="run-history" className="space-y-4">


      <KeysetTable<RunSummary>
        toolbar={
          <>
        <SearchBox value={job} onCommit={setJob} label="Search by job" placeholder="job name…" />
        {/* Truly multi since contract R3: several states OR together on the SERVER —
            the console still never merges take-bounded pages client-side. */}
        <FacetFilter
          label="State"
          options={STATES.map((state) => ({ value: state }))}
          selected={new Set(statuses)}
          onToggle={(value) => setStatuses((current) => (current.includes(value) ? current.filter((v) => v !== value) : [...current, value]))}
          onClear={() => setStatuses([])}
        />
        {/* Inline labels keep the dates on the SAME baseline as the other controls —
            the stacked label pushed them below the row (owner: "tarihler kayık"). */}
        <label className="flex items-center gap-1.5 text-xs text-muted-foreground">
          From
          <input type="date" className="control" value={from} onChange={(event) => setFrom(event.target.value)} />
        </label>
        <label className="flex items-center gap-1.5 text-xs text-muted-foreground">
          To
          <input type="date" className="control" value={to} onChange={(event) => setTo(event.target.value)} />
        </label>
        <span className="ms-auto"><DensityToggle /></span>
        {(statuses.length > 0 || from || to || job) && (
          <button
            className="link-action"
            onClick={() => {
              setStatuses([]);
              setFrom("");
              setTo("");
              setJob("");
            }}
          >
            clear filters
          </button>
        )}
      
          </>
        }
        columns={[
          {
            header: "Run",
            cell: (run) => (
              <button className="font-mono text-xs underline-offset-2 hover:underline" onClick={() => setSelectedRunId(run.id)}>
                {run.id}
              </button>
            ),
          },
          {
            header: "Job",
            // The relationship reads ON the screen (v1.1 §7.9): a run's job is one
            // click from its schedule, not a name to remember and go find.
            cell: (run) =>
              onOpenJob ? (
                <button className="underline-offset-2 hover:underline" onClick={() => onOpenJob(run.jobName)}>
                  {run.jobName}
                </button>
              ) : (
                run.jobName
              ),
          },
          { header: "State", cell: (run) => <StateBadge state={run.status} /> },
          // Who put this run on the schedule, and which node ran it: the two questions a
          // run list is asked the morning after (contract R2.3).
          { header: "Started by", cell: (run) => (
            <span className="flex items-baseline gap-1 text-xs">
              {run.triggeredBy ?? "not recorded"}
              {run.startedBy && (
                // The instance name is an identity, not prose: it truncates rather than
                // wrapping the row, and the full name stays a hover away.
                <span className="inline-block max-w-[14ch] truncate align-bottom text-faint" title={run.startedBy}>
                  {run.startedBy}
                </span>
              )}
            </span>
          ) },
          { header: "Started", cell: (run) => <time className="text-xs" title={run.startedAt}>{shortStamp(run.startedAt)}</time> },
          { header: "Chunks", align: "right", cell: (run) => `${run.completedChunks}/${run.totalChunks}` },
        ]}
        loadPage={loadRuns}
        rowKey={(run) => run.id}
        emptyMessage={statuses.length > 0 || from || to ? "No run matches these filters." : "No runs recorded for this fleet yet."}
      />

      {problem && <p className="text-sm text-danger">{problem}</p>}

      <Sheet
        open={selectedRun !== null}
        onOpenChange={(open) => {
          if (!open) setSelectedRunId(null);
        }}
        title={selectedRun ? `Run ${selectedRun.run.id}` : ""}
        description={selectedRun ? `${selectedRun.run.jobName} — chunks, progress, and the repair queue.` : undefined}
      >
        {selectedRun && (
          <section data-testid="run-detail" className="space-y-4">
            <div className="flex flex-wrap items-center gap-2">
              <VerbButton
                label="rerun"
                icon={<RotateCcw />}
                iconOnly
                confirm={`Rerun ${selectedRun.run.id}?`}
                execute={() => asOutcome(client.rerun(selectedRun.run.id))}
                onDone={onChanged}
              />
              {selectedRun.openFailures.length > 0 && (
                <VerbButton
                  label="replay-items"
                  confirm="Replay all open repair items of this run?"
                  execute={() => asOutcome(client.replayItems(selectedRun.run.id))}
                  onDone={onChanged}
                />
              )}
            </div>

            <RunProgress run={selectedRun.run} now={now} />

            <div>
              <h4 className="control-label mb-1 block">Chunks by status</h4>
              <div className="flex flex-wrap gap-1">
                {Object.entries(selectedRun.chunksByStatus).map(([state, count]) => (
                  <span key={state} className="chip">{state}: {count}</span>
                ))}
              </div>
            </div>

            <div>
              <h4 className="control-label mb-1 block">
                Repair queue{selectedRun.openFailures.length === 0 ? " — empty" : ` (${selectedRun.openFailures.length} shown)`}
              </h4>
              <ul className="space-y-1">
                {selectedRun.openFailures.map((failure) => (
                  <li key={failure.id} className="flex items-baseline gap-2 text-xs">
                    <span className="font-mono">{failure.itemKey}</span>
                    <span className="text-faint">chunk {failure.chunkIndex}</span>
                    <span className="text-danger">{failure.reason}</span>
                  </li>
                ))}
              </ul>
            </div>
          </section>
        )}
      </Sheet>
    </div>
  );
}
