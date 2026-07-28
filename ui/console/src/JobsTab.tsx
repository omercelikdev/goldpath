import { useEffect, useState } from "react";
import { Banner, StateBadge, VerbButton } from "@goldpath/kit";
import type { VerbOutcome } from "@goldpath/kit";
import { isPaused, nextFireAt, type AdminClient, type JobInfo, type TriggerInfo } from "./adminClient";
import { asOutcome } from "./verbs";

export interface JobsTabProps {
  client: AdminClient;
  fleet: string;
  /** Bumped by a verb: the panel re-READS, it does not remount. */
  refreshToken: number;
  onChanged: () => void;
}

/**
 * The jobs of a fleet, each with the triggers that decide WHEN it runs.
 *
 * Triggers live inside their job rather than in a list of their own: a trigger without
 * the job it fires is a name and a cron string, and correlating two lists during an
 * incident is work the screen should have done.
 *
 * Nothing here can create or delete a JOB — that is the manifest's and the code's
 * (ADR-0001), and the admin surface has no such route to call. What an operator changes
 * here is the SCHEDULE.
 */
export function JobsTab({ client, fleet, refreshToken, onChanged }: JobsTabProps) {
  const [jobs, setJobs] = useState<JobInfo[] | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [open, setOpen] = useState<string | null>(null);

  useEffect(() => {
    let live = true;
    setProblem(null);
    client
      .jobs(fleet)
      .then((found) => live && setJobs(found))
      .catch(() => live && setProblem(`the jobs of ${fleet} could not be read`));
    return () => {
      live = false;
    };
  }, [client, fleet, refreshToken]);

  if (jobs === null) {
    return problem
      ? <Banner tone="danger">{problem}</Banner>
      : <p className="text-sm text-muted-foreground">Reading the jobs…</p>;
  }

  return (
    <div data-testid="jobs-tab" className="space-y-3">
      {/*
        A read that fails AFTER the panel has content does not blank it. Replacing the
        whole tab with an error would also destroy the outcome strip of the verb the
        operator just sent — which is exactly when a service tends to stop answering. The
        rows stay, marked as possibly stale, and the failure is said out loud.
      */}
      {problem && <Banner tone="danger">{problem} — the rows below are the last ones it did answer with.</Banner>}

      {jobs.length === 0 && <p className="text-sm text-muted-foreground">This fleet declares no job.</p>}

      {jobs.map((job) => {
        const paused = isPaused(job);
        const next = nextFireAt(job);
        return (
          <section key={job.name} className="rounded-md border border-border/60 p-3">
            <div className="flex flex-wrap items-center gap-3">
              <button
                className="text-sm font-medium underline-offset-2 hover:underline"
                aria-expanded={open === job.name}
                onClick={() => setOpen(open === job.name ? null : job.name)}
              >
                {job.name}
              </button>
              {paused && <StateBadge state="Suppressed" />}
              {job.triggers.length === 0 && (
                // Not the same thing as paused, and saying so matters: a job with no
                // trigger will never fire and no amount of resuming will change that.
                <span className="text-xs text-warning">no trigger — nothing will fire it</span>
              )}
              {next && <span className="text-xs text-faint">next {next}</span>}
              <span className="text-xs text-faint">
                {job.triggers.length} trigger{job.triggers.length === 1 ? "" : "s"}
              </span>

              <span className="ml-auto flex flex-wrap gap-2">
                <VerbButton
                  label="trigger"
                  confirm={`Fire ${job.name} now? The run is recorded as started by hand.`}
                  execute={() => asOutcome(client.triggerJob(fleet, job.name))}
                  onDone={onChanged}
                />
                {paused ? (
                  <VerbButton
                    label="resume"
                    confirm={`Resume ${job.name}?`}
                    execute={() => asOutcome(client.resumeJob(fleet, job.name))}
                    onDone={onChanged}
                  />
                ) : (
                  <VerbButton
                    label="pause"
                    confirm={`Pause ${job.name}? Its triggers stop firing until resumed.`}
                    execute={() => asOutcome(client.pauseJob(fleet, job.name))}
                    onDone={onChanged}
                    destructive
                  />
                )}
              </span>
            </div>

            {open === job.name && (
              <div className="mt-3 space-y-3 border-t border-border/60 pt-3">
                {job.description && <p className="text-xs text-muted-foreground">{job.description}</p>}

                <Triggers client={client} fleet={fleet} job={job} onChanged={onChanged} />

                {job.dataMap && Object.keys(job.dataMap).length > 0 && (
                  <div>
                    <h4 className="mb-1 text-xs text-muted-foreground">
                      Job data — read-only: these come from the code that declares the job
                    </h4>
                    <dl className="flex flex-wrap gap-x-4 gap-y-1 text-xs">
                      {Object.entries(job.dataMap).map(([key, value]) => (
                        <div key={key} className="flex gap-1">
                          <dt className="text-faint">{key}</dt>
                          <dd className="font-mono">{value}</dd>
                        </div>
                      ))}
                    </dl>
                  </div>
                )}
              </div>
            )}
          </section>
        );
      })}
    </div>
  );
}

function Triggers({
  client,
  fleet,
  job,
  onChanged,
}: {
  client: AdminClient;
  fleet: string;
  job: JobInfo;
  onChanged: () => void;
}) {
  const [adding, setAdding] = useState(false);
  const [rescheduling, setRescheduling] = useState(false);
  // The outcome is held HERE, not inside the form: a form that closes on success takes
  // its own success message with it, and the operator is left guessing whether the verb
  // landed (the U3 lesson, re-learned when the console smoke found this exact hole).
  const [outcome, setOutcome] = useState<VerbOutcome | null>(null);

  const settle = (result: VerbOutcome, close: () => void) => {
    setOutcome(result);
    if (result.kind === "ok") close();
    onChanged();
  };

  return (
    <div>
      <div className="mb-1 flex items-center justify-between">
        <h4 className="text-xs text-muted-foreground">Triggers</h4>
        <span className="flex gap-3">
          <button className="text-xs underline underline-offset-2" onClick={() => setRescheduling(!rescheduling)}>
            {rescheduling ? "cancel" : "change the schedule"}
          </button>
          <button className="text-xs underline underline-offset-2" onClick={() => setAdding(!adding)}>
            {adding ? "cancel" : "add a trigger"}
          </button>
        </span>
      </div>

      {outcome && (
        <Banner tone={outcome.kind === "ok" ? "success" : "danger"} live={outcome.kind === "ok" ? "status" : "alert"} dense>
          {outcome.kind === "error"
            ? "the verb did not reach the service — check the service logs"
            : outcome.message}
        </Banner>
      )}

      {rescheduling && (
        <Reschedule client={client} fleet={fleet} job={job} onDone={(result) => settle(result, () => setRescheduling(false))} />
      )}

      {job.triggers.length === 0 && <p className="text-xs text-faint">None. This job is declared but unscheduled.</p>}

      <ul className="space-y-2">
        {job.triggers.map((trigger) => (
          <li key={trigger.name} className="flex flex-wrap items-baseline gap-2 text-xs">
            <span className="font-mono">{trigger.name}</span>
            <StateBadge state={trigger.state === "Normal" ? "Running" : trigger.state === "Paused" ? "Suppressed" : trigger.state} />
            <Schedule trigger={trigger} />
            {trigger.calendarName && <span className="text-faint">calendar {trigger.calendarName}</span>}
            <span className="text-faint">priority {trigger.priority}</span>
            {trigger.nextFireAt && <span className="text-faint">next {trigger.nextFireAt}</span>}
            <span className="ml-auto">
              <VerbButton
                label="remove"
                confirm={`Remove trigger ${trigger.name}? The JOB stays — it simply loses this schedule.`}
                execute={() => asOutcome(client.removeTrigger(fleet, job.name, trigger.name))}
                // Quiet because the ROW is about to disappear and would take the strip
                // with it; the outcome is rendered above the list instead. A quiet button
                // whose outcome nobody renders is a verb that reports nothing at all.
                onDone={(result) => settle(result, () => {})}
                destructive
                quiet
              />
            </span>
          </li>
        ))}
      </ul>

      {adding && (
        <AddTrigger client={client} fleet={fleet} job={job.name} onDone={(result) => settle(result, () => setAdding(false))} />
      )}
    </div>
  );
}

/**
 * The audited schedule override (the frozen D7 verb, first screened in U5 — open-threads
 * T13). "Run at 03:00 tonight" is an ops decision and does not wait for a merge; the job
 * DEFINITION stays in code, only its cron moves, and the crossing is on the audit.
 */
function Reschedule({
  client,
  fleet,
  job,
  onDone,
}: {
  client: AdminClient;
  fleet: string;
  job: JobInfo;
  onDone: (outcome: VerbOutcome) => void;
}) {
  // The frozen verb acts on ONE trigger, `{job}-cron`, and creates it if it is missing.
  // Showing "the first cron trigger" instead would put another trigger's expression in
  // front of an operator while the server moved a different one — and since this PR lets
  // a job carry several cron triggers, that is not hypothetical (review R3).
  const targetName = `${job.name}-cron`;
  const current = job.triggers.find((trigger) => trigger.name === targetName);
  const [cron, setCron] = useState(current?.cronExpression ?? "");
  const [timeZoneId, setTimeZoneId] = useState(current?.timeZoneId ?? "");

  return (
    <div className="mb-3 space-y-2 rounded-md border border-border/60 p-3 text-xs">
      <p className="text-muted-foreground">
        Changes the schedule of <span className="font-mono">{targetName}</span>
        {current ? "" : " — which does not exist yet, so this creates it"}. The job itself
        stays exactly as the code declares it, and the change is written to the audit.
        {job.triggers.length > 1 && " Other triggers on this job are untouched; remove or add those individually."}
      </p>
      <div className="flex flex-wrap items-end gap-2">
        <label className="flex flex-col gap-1">
          Cron
          <input className="rounded border border-border px-2 py-1 font-mono" value={cron} onChange={(event) => setCron(event.target.value)} placeholder="0 0 3 * * ?" />
        </label>
        <label className="flex flex-col gap-1">
          Timezone
          <input className="rounded border border-border px-2 py-1" value={timeZoneId} onChange={(event) => setTimeZoneId(event.target.value)} placeholder="Europe/Istanbul" />
        </label>
        {cron.trim().length > 0 && (
          <VerbButton
            label="reschedule"
            confirm={
              current?.cronExpression
                ? `Move ${targetName} from ${current.cronExpression} to ${cron}?`
                : `Create ${targetName} at ${cron}?`
            }
            execute={() => asOutcome(client.reschedule(fleet, job.name, cron, timeZoneId || null))}
            onDone={onDone}
            quiet
          />
        )}
      </div>
    </div>
  );
}

/**
 * What this trigger actually means, in the reader's terms. A cron string alone does not
 * say when it fires — the timezone it is read in decides that, and a misfire instruction
 * decides what happens to a fire the process slept through (contract R2.2).
 */
function Schedule({ trigger }: { trigger: TriggerInfo }) {
  if (trigger.type === "cron") {
    return (
      <>
        <span className="font-mono">{trigger.cronExpression}</span>
        <span className="text-faint">{trigger.timeZoneId ?? "server time"}</span>
      </>
    );
  }

  if (trigger.type === "simple") {
    return (
      <span className="text-faint">
        every {trigger.repeatInterval}
        {trigger.repeatCount === -1 || trigger.repeatCount === null ? ", forever" : `, ${trigger.repeatCount} times`}
        {trigger.timesTriggered !== null && ` · fired ${trigger.timesTriggered}`}
      </span>
    );
  }

  return <span className="text-faint">{trigger.type}</span>;
}

function AddTrigger({
  client,
  fleet,
  job,
  onDone,
}: {
  client: AdminClient;
  fleet: string;
  job: string;
  onDone: (outcome: VerbOutcome) => void;
}) {
  const [name, setName] = useState("");
  const [kind, setKind] = useState<"cron" | "simple">("cron");
  const [cron, setCron] = useState("");
  const [timeZoneId, setTimeZoneId] = useState("");
  const [interval, setInterval] = useState("");

  // The server refuses both-or-neither anyway; the form does not offer the mistake.
  const request = kind === "cron"
    ? { name, cron, timeZoneId: timeZoneId || null }
    : { name, interval: interval || null };
  const ready = name.trim().length > 0 && (kind === "cron" ? cron.trim().length > 0 : interval.trim().length > 0);

  return (
    <div className="mt-3 space-y-2 rounded-md border border-border/60 p-3">
      <p className="text-xs text-muted-foreground">
        A trigger schedules a job the code already declares. It cannot create one.
      </p>
      <div className="flex flex-wrap items-end gap-2 text-xs">
        <label className="flex flex-col gap-1">
          Name
          <input className="rounded border border-border px-2 py-1" value={name} onChange={(event) => setName(event.target.value)} />
        </label>
        <span className="flex flex-col gap-1">
          <label htmlFor={`kind-${job}`}>Kind</label>
          <select
            id={`kind-${job}`}
            className="rounded border border-border px-2 py-1"
            value={kind}
            onChange={(event) => setKind(event.target.value as "cron" | "simple")}
          >
            <option value="cron">cron</option>
            <option value="simple">interval</option>
          </select>
        </span>
        {kind === "cron" ? (
          <>
            <label className="flex flex-col gap-1">
              Cron
              <input className="rounded border border-border px-2 py-1 font-mono" value={cron} onChange={(event) => setCron(event.target.value)} placeholder="0 0 3 * * ?" />
            </label>
            <label className="flex flex-col gap-1">
              Timezone
              <input className="rounded border border-border px-2 py-1" value={timeZoneId} onChange={(event) => setTimeZoneId(event.target.value)} placeholder="Europe/Istanbul" />
            </label>
          </>
        ) : (
          <label className="flex flex-col gap-1">
            Interval
            <input className="rounded border border-border px-2 py-1 font-mono" value={interval} onChange={(event) => setInterval(event.target.value)} placeholder="00:15:00" />
          </label>
        )}
        {ready && (
          <VerbButton
            label="schedule it"
            confirm={`Add trigger ${name} to ${job}?`}
            execute={() => asOutcome(client.addTrigger(fleet, job, request))}
            onDone={onDone}
            quiet
          />
        )}
      </div>
    </div>
  );
}
