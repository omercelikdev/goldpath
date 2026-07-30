import { Plus } from "lucide-react";
import { useEffect, useState } from "react";
import { Banner, Dialog, Table, VerbButton } from "@goldpath/kit";
import type { VerbOutcome } from "@goldpath/kit";
import type { AdminClient, CalendarInfo, CalendarSpec } from "./adminClient";
import { asOutcome } from "./verbs";

export interface CalendarsTabProps {
  client: AdminClient;
  fleet: string;
  /** Bumped by a verb: the panel re-READS, it does not remount. */
  refreshToken: number;
  onChanged: () => void;
}

/** The four shapes the contract accepts; exactly one payload field per type. */
const TYPES = [
  { id: "holiday", label: "holiday", hint: "specific dates excluded" },
  { id: "weekly", label: "weekly", hint: "weekdays excluded, every week" },
  { id: "annual", label: "annual", hint: "day and month excluded, every year" },
  { id: "cron", label: "cron", hint: "times matching an expression excluded" },
] as const;

const DAYS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

/**
 * Calendars — the frozen CRUD that had no screen until now (open-threads T13). A calendar
 * is how a fleet learns that a bank holiday exists; without one an operator's only lever
 * is pausing the job on the day and remembering to resume it.
 *
 * Deleting one is the dangerous verb, so the confirm names WHO is riding it: the contract
 * returns `usedByTriggers`, and a calendar removed underneath a trigger takes its
 * exclusions with it.
 */
export function CalendarsTab({ client, fleet, refreshToken, onChanged }: CalendarsTabProps) {
  const [calendars, setCalendars] = useState<CalendarInfo[] | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);
  // Held outside the form for the same reason as the trigger form: closing on success
  // must not swallow the sentence that says it succeeded.
  const [outcome, setOutcome] = useState<VerbOutcome | null>(null);

  useEffect(() => {
    let live = true;
    setProblem(null);
    client
      .calendars(fleet)
      .then((found) => live && setCalendars(found))
      .catch(() => live && setProblem(`the calendars of ${fleet} could not be read`));
    return () => {
      live = false;
    };
  }, [client, fleet, refreshToken]);

  if (calendars === null) {
    return problem
      ? <Banner tone="danger">{problem}</Banner>
      : <p className="text-sm text-muted-foreground">Reading the calendars…</p>;
  }

  return (
    <div data-testid="calendars-tab" className="space-y-4">
      {/* Same rule as the jobs tab: a later failure marks the rows, it does not erase
          them — and it must not take a verb's outcome with it. */}
      {problem && <Banner tone="danger">{problem} — the rows below are the last ones it did answer with.</Banner>}
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-medium text-muted-foreground">Calendars</h3>
        <button className="btn-quiet inline-flex items-center gap-1.5" onClick={() => setAdding(true)}>
          <Plus size={14} aria-hidden="true" /> add a calendar
        </button>
      </div>

      <Table
        columns={[
          { header: "Calendar", cell: (calendar) => <span className="font-medium">{calendar.name}</span> },
          { header: "Description", cell: (calendar) => <span className="text-xs text-muted-foreground">{calendar.description ?? "—"}</span> },
          {
            header: "Used by",
            cell: (calendar) => (
              <span className="text-xs text-faint">
                {calendar.usedByTriggers.length === 0 ? "no trigger uses it" : `used by ${calendar.usedByTriggers.join(", ")}`}
              </span>
            ),
          },
          {
            header: "",
            align: "right",
            cell: (calendar) => (
              <VerbButton
                label="delete"
                confirm={
                  calendar.usedByTriggers.length === 0
                    ? `Delete calendar ${calendar.name}?`
                    : `Delete calendar ${calendar.name}? ${calendar.usedByTriggers.length} trigger${
                        calendar.usedByTriggers.length === 1 ? "" : "s"
                      } ride it (${calendar.usedByTriggers.join(", ")}) and will lose its exclusions.`
                }
                execute={() => asOutcome(client.deleteCalendar(fleet, calendar.name))}
                onDone={(result) => {
                  setOutcome(result);
                  onChanged();
                }}
                destructive
                quiet
              />
            ),
          },
        ]}
        rows={calendars}
        rowKey={(calendar) => calendar.name}
        emptyMessage="This fleet has no calendar."
      />

      {/* While the form dialog is open, ITS copy speaks — one voice per outcome. */}
      {outcome && !adding && (
        <Banner tone={outcome.kind === "ok" ? "success" : "danger"} live={outcome.kind === "ok" ? "status" : "alert"} dense>
          {outcome.kind === "error" ? "the verb did not reach the service — check the service logs" : outcome.message}
        </Banner>
      )}

      {/* The form opens in the centred dialog (§9.5) — never at the page's bottom. */}
      <Dialog
        open={adding}
        onOpenChange={setAdding}
        title="Add a calendar"
        description={`A holiday or cron calendar for ${fleet} — triggers exclude the days it names.`}
      >
        {outcome && outcome.kind !== "ok" && (
          // Quiet form verb + modal: the refusal must speak IN the dialog (review R1).
          <div className="mb-3">
            <Banner tone="danger" live="alert" dense>
              {outcome.kind === "error" ? "the verb did not reach the service — check the service logs" : outcome.message}
            </Banner>
          </div>
        )}
        <AddCalendar
          client={client}
          fleet={fleet}
          onDone={(result) => {
            setOutcome(result);
            if (result.kind === "ok") setAdding(false);
            onChanged();
          }}
        />
      </Dialog>
    </div>
  );
}

function AddCalendar({ client, fleet, onDone }: { client: AdminClient; fleet: string; onDone: (outcome: VerbOutcome) => void }) {
  const [name, setName] = useState("");
  const [type, setType] = useState<(typeof TYPES)[number]["id"]>("holiday");
  const [description, setDescription] = useState("");
  const [dates, setDates] = useState("");
  const [days, setDays] = useState<number[]>([]);
  const [cron, setCron] = useState("");

  const parsedDates = dates.split(",").map((entry) => entry.trim()).filter(Boolean);
  const spec: CalendarSpec = {
    type,
    description: description || null,
    excludedDates: type === "holiday" || type === "annual" ? parsedDates : null,
    excludedDays: type === "weekly" ? days : null,
    cronExpression: type === "cron" ? cron : null,
  };
  const ready =
    name.trim().length > 0 &&
    ((type === "holiday" || type === "annual") ? parsedDates.length > 0 : type === "weekly" ? days.length > 0 : cron.trim().length > 0);

  return (
    <div className="row-card space-y-2 text-xs">
      <div className="flex flex-wrap items-end gap-2">
        <label className="flex flex-col gap-1">
          Name
          <input className="control" value={name} onChange={(event) => setName(event.target.value)} />
        </label>
        <span className="flex flex-col gap-1">
          <label htmlFor="calendar-type">Type</label>
          <select
            id="calendar-type"
            className="control"
            value={type}
            onChange={(event) => setType(event.target.value as typeof type)}
          >
            {TYPES.map((entry) => (
              <option key={entry.id} value={entry.id}>{entry.label}</option>
            ))}
          </select>
        </span>
        <label className="flex flex-col gap-1">
          Description
          <input className="control" value={description} onChange={(event) => setDescription(event.target.value)} />
        </label>
      </div>

      <p className="text-faint">{TYPES.find((entry) => entry.id === type)!.hint}</p>

      {type === "holiday" && (
        <label className="flex flex-col gap-1">
          Excluded dates (comma separated, ISO)
          <input className="control font-mono" value={dates} onChange={(event) => setDates(event.target.value)} placeholder="2026-01-01, 2026-04-23" />
        </label>
      )}

      {type === "annual" && (
        <label className="flex flex-col gap-1">
          {/*
            An annual calendar excludes a DAY AND MONTH every year, so the year in these
            dates is ignored by the engine. The field still takes a full ISO date because
            the contract's payload is a date list — saying so beats letting an operator
            believe they excluded one particular new year (review R5).
          */}
          Excluded dates — the year is ignored, only the day and month recur
          <input className="control font-mono" value={dates} onChange={(event) => setDates(event.target.value)} placeholder="2026-01-01, 2026-04-23" />
        </label>
      )}

      {type === "weekly" && (
        <fieldset className="flex flex-wrap gap-2">
          <legend className="sr-only">Excluded days</legend>
          {DAYS.map((day, index) => (
            <label key={day} className="flex items-center gap-1">
              <input
                type="checkbox"
                checked={days.includes(index)}
                onChange={(event) => setDays(event.target.checked ? [...days, index] : days.filter((entry) => entry !== index))}
              />
              {day}
            </label>
          ))}
        </fieldset>
      )}

      {type === "cron" && (
        <label className="flex flex-col gap-1">
          Excluded times (cron)
          <input className="control font-mono" value={cron} onChange={(event) => setCron(event.target.value)} placeholder="0 0 0-6 * * ?" />
        </label>
      )}

      {ready && (
        <VerbButton
          label="create it"
          confirm={`Create calendar ${name} in ${fleet}?`}
          execute={() => asOutcome(client.putCalendar(fleet, name, spec))}
          onDone={onDone}
          quiet
        />
      )}
    </div>
  );
}
