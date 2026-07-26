import { StateBadge } from "./StateBadge";

/** The run row of the frozen contract, as the console reads it. */
export interface RunProgressData {
  status: string;
  startedAt: string;
  finishedAt?: string | null;
  deadlineAt?: string | null;
  predictedFinishAt?: string | null;
  totalChunks: number;
  completedChunks: number;
  failedChunks: number;
  totalItems?: number | null;
  itemFailures: number;
}

export interface RunProgressProps {
  run: RunProgressData;
  /** Injected for tests and for clock-skew-free rendering; defaults to now. */
  now?: Date;
}

/** Items per second from the chunk rate — null while nothing has completed yet. */
export function itemsPerSecond(run: RunProgressData, now: Date): number | null {
  if (run.completedChunks <= 0 || run.totalChunks <= 0 || !run.totalItems) return null;
  const end = run.finishedAt ? new Date(run.finishedAt) : now;
  const seconds = (end.getTime() - new Date(run.startedAt).getTime()) / 1000;
  if (!Number.isFinite(seconds) || seconds <= 0) return null;
  const done = (run.completedChunks / run.totalChunks) * run.totalItems;
  return done / seconds;
}

export type DeadlineVerdict = "none" | "on-track" | "overrun-predicted" | "overrun";

/**
 * The deadline verdict: a FINISHED run is judged on when it actually ended; a live run
 * on its prediction. The console warns BEFORE the deadline passes — that is the whole
 * point of carrying a prediction in the run row.
 */
export function deadlineVerdict(run: RunProgressData): DeadlineVerdict {
  if (!run.deadlineAt) return "none";
  const deadline = new Date(run.deadlineAt).getTime();
  if (run.finishedAt) return new Date(run.finishedAt).getTime() > deadline ? "overrun" : "on-track";
  if (!run.predictedFinishAt) return "on-track";
  return new Date(run.predictedFinishAt).getTime() > deadline ? "overrun-predicted" : "on-track";
}

const VERDICT_TEXT: Record<Exclude<DeadlineVerdict, "none">, string> = {
  "on-track": "on track",
  "overrun-predicted": "predicted to overrun",
  overrun: "overran the deadline",
};

function formatRate(rate: number): string {
  return rate >= 10 ? `${Math.round(rate).toLocaleString()} items/s` : `${rate.toFixed(1)} items/s`;
}

/**
 * The run progress composite of ui-standard-v1 §4: chunk completion, live rate, and the
 * prediction judged against the deadline. Percentages come from CHUNKS (the honest
 * denominator the engine actually plans); item counts are shown, never guessed.
 */
export function RunProgress({ run, now = new Date() }: RunProgressProps) {
  const pct = run.totalChunks > 0 ? Math.round((run.completedChunks / run.totalChunks) * 100) : 0;
  const rate = itemsPerSecond(run, now);
  const verdict = deadlineVerdict(run);
  const barTone =
    verdict === "overrun" || run.failedChunks > 0
      ? "bg-danger"
      : verdict === "overrun-predicted"
        ? "bg-warning"
        : "bg-primary";

  return (
    <div data-testid="run-progress" className="space-y-2">
      <div className="flex items-center gap-3">
        <StateBadge state={run.status} />
        <span className="text-sm text-muted-foreground">
          {run.completedChunks}/{run.totalChunks} chunks
        </span>
        {run.failedChunks > 0 && (
          <span className="text-xs text-danger">{run.failedChunks} failed</span>
        )}
        {run.itemFailures > 0 && (
          <span className="text-xs text-danger">{run.itemFailures} items in repair</span>
        )}
      </div>

      <div
        role="progressbar"
        aria-valuenow={pct}
        aria-valuemin={0}
        aria-valuemax={100}
        className="h-2 w-full overflow-hidden rounded-full bg-muted"
      >
        <div className={`h-full ${barTone}`} style={{ width: `${pct}%` }} />
      </div>

      <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
        <span>{pct}%</span>
        {rate !== null && <span>{formatRate(rate)}</span>}
        {run.totalItems != null && <span>{run.totalItems.toLocaleString()} items planned</span>}
        {verdict !== "none" && (
          <span
            data-verdict={verdict}
            className={
              verdict === "overrun"
                ? "text-danger"
                : verdict === "overrun-predicted"
                  ? "text-warning"
                  : "text-success"
            }
          >
            {VERDICT_TEXT[verdict]}
          </span>
        )}
      </div>
    </div>
  );
}
