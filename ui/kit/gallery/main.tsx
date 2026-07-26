// The kit gallery: every composite, both themes, real tokens — the screen the eye
// verifies BEFORE any slice ships (U1 exit gate; screenshots ride the PRs).
import { StrictMode, useState } from "react";
import { createRoot } from "react-dom/client";
import "../src/tokens/tokens.css";
import { StateBadge } from "../src/components/StateBadge";
import { KeysetTable, type KeysetPage } from "../src/components/KeysetTable";
import { VerbButton } from "../src/components/VerbButton";
import { RunProgress, type RunProgressData } from "../src/components/RunProgress";
import { AuditBlock, type AuditEntry } from "../src/components/AuditBlock";
import type { VerbOutcome } from "../src/adminResult";
import { KNOWN_STATES } from "../src/status";

// Derived from the §5 source of truth — the eyes-on gate cannot drift from the map.
const STATES = [...KNOWN_STATES, "SomethingUnknown"];

interface DemoRun { id: string; job: string; state: string; items: number }

const DEMO_RUNS: DemoRun[] = [
  { id: "run-9f21", job: "eod-reconciliation", state: "Completed", items: 41250 },
  { id: "run-9f20", job: "payment-fanout", state: "Running", items: 12007 },
  { id: "run-9f1f", job: "eod-reconciliation", state: "Failed", items: 388 },
  { id: "run-9f1e", job: "archival-sweep", state: "Recovering", items: 91_002 },
  { id: "run-9f1d", job: "payment-fanout", state: "Completed", items: 8_441 },
  { id: "run-9f1c", job: "bulk-import", state: "CompletedWithFailures", items: 5_003 },
  { id: "run-9f1b", job: "eod-reconciliation", state: "Completed", items: 40_118 },
  { id: "run-9f1a", job: "notification-send", state: "Suppressed", items: 12 },
  { id: "run-9f19", job: "bulk-import", state: "Validated", items: 77_100 },
];

async function loadDemoRuns(cursor: string | null, take: number): Promise<KeysetPage<DemoRun>> {
  await new Promise((resolve) => setTimeout(resolve, 350));   // the loading state is part of the eyes-on pass
  const start = cursor ? Number(cursor) : 0;
  const items = DEMO_RUNS.slice(start, start + take);
  const next = start + take < DEMO_RUNS.length ? String(start + take) : null;
  return { items, nextCursor: next };
}

const verbOk = async (): Promise<VerbOutcome> => {
  await new Promise((resolve) => setTimeout(resolve, 500));
  return { kind: "ok", message: "run 9f22 scheduled; audit row written" };
};
const verbRefused = async (): Promise<VerbOutcome> => {
  await new Promise((resolve) => setTimeout(resolve, 500));
  return { kind: "refused", message: "the batch is not Validated — approve requires the validation gate to have passed" };
};
const verbError = async (): Promise<VerbOutcome> => {
  await new Promise((resolve) => setTimeout(resolve, 500));
  return { kind: "error", status: 503 };
};

const DEMO_NOW = new Date("2026-07-26T10:01:40Z");
const DEMO_RUN_BASE: RunProgressData = {
  status: "Running",
  startedAt: "2026-07-26T10:00:00Z",
  totalChunks: 10,
  completedChunks: 4,
  failedChunks: 0,
  totalItems: 100_000,
  itemFailures: 0,
};

const DEMO_RUN_STATES: { label: string; run: RunProgressData }[] = [
  { label: "healthy · on track", run: { ...DEMO_RUN_BASE, deadlineAt: "2026-07-26T11:00:00Z", predictedFinishAt: "2026-07-26T10:14:00Z" } },
  { label: "predicted to overrun (the warning fires BEFORE the deadline)", run: { ...DEMO_RUN_BASE, deadlineAt: "2026-07-26T10:30:00Z", predictedFinishAt: "2026-07-26T10:45:00Z" } },
  { label: "failures + repair queue", run: { ...DEMO_RUN_BASE, completedChunks: 7, failedChunks: 2, itemFailures: 37 } },
  { label: "completed, overran", run: { ...DEMO_RUN_BASE, status: "Completed", completedChunks: 10, finishedAt: "2026-07-26T10:44:00Z", deadlineAt: "2026-07-26T10:30:00Z" } },
];

const DEMO_AUDIT: AuditEntry[] = [
  { id: 1, timestamp: "2026-07-26T10:04:11Z", user: "ops-chief", correlationId: "corr-7f21", entityType: "PaymentInstruction", entityKey: "8814", action: "Modified", propertyName: "Status", oldValue: "PendingApproval", newValue: "Executed" },
  { id: 2, timestamp: "2026-07-26T10:04:11Z", user: "ops-chief", correlationId: "corr-7f21", entityType: "PaymentInstruction", entityKey: "8814", action: "Modified", propertyName: "ApprovedBy", oldValue: null, newValue: "ops-chief" },
  { id: 3, timestamp: "2026-07-26T09:58:02Z", user: "treasurer", correlationId: "corr-7f1e", entityType: "Counterparty", entityKey: "CP-233", action: "Modified", propertyName: "nationalId", oldValue: "12345678901", newValue: "10987654321" },
  { id: 4, timestamp: "2026-07-26T09:58:02Z", user: "treasurer", correlationId: "corr-7f1e", entityType: "Counterparty", entityKey: "CP-233", action: "Modified", propertyName: "iban", oldValue: "TR330006100519786457841326", newValue: "DE89370400440532013000" },
  { id: 5, timestamp: "2026-07-26T02:00:00Z", user: null, correlationId: "corr-nightly", entityType: "ArchiveEntry", entityKey: "ORD-77", action: "Added", propertyName: "ErasedAt", oldValue: null, newValue: "2026-07-26T02:00:00Z" },
];

function Gallery() {
  const [dark, setDark] = useState(false);
  return (
    <div className={dark ? "dark" : ""}>
      <div className="min-h-screen bg-app text-foreground p-8" style={{ minHeight: "100vh" }}>
        <div className="bg-surface rounded-2xl p-6">
          <div className="flex items-center justify-between mb-6">
            <h1 className="text-lg font-semibold">@goldpath/kit — gallery</h1>
            <button
              className="rounded-md border border-border bg-background px-3 py-1.5 text-sm hover:bg-accent"
              onClick={() => {
                document.documentElement.classList.toggle("dark", !dark);
                setDark(!dark);
              }}
            >
              {dark ? "light" : "dark"} theme
            </button>
          </div>
          <section className="bg-background rounded-lg border border-border p-5" style={{ boxShadow: "var(--shadow-surface)" }}>
            <h2 className="text-sm font-medium text-muted-foreground mb-4">StateBadge — the §5 ramp, every domain state</h2>
            <div className="flex flex-wrap gap-2">
              {STATES.map((s) => <StateBadge key={s} state={s} />)}
            </div>
          </section>

          <section className="bg-background rounded-lg border border-border p-5 mt-6" style={{ boxShadow: "var(--shadow-surface)" }}>
            <h2 className="text-sm font-medium text-muted-foreground mb-4">AuditBlock — old→new rows, classified fields masked</h2>
            <AuditBlock entries={DEMO_AUDIT} classified={["nationalId", "iban"]} />
          </section>

          <section className="bg-background rounded-lg border border-border p-5 mt-6" style={{ boxShadow: "var(--shadow-surface)" }}>
            <h2 className="text-sm font-medium text-muted-foreground mb-4">RunProgress — chunks, live rate, prediction vs deadline</h2>
            <div className="space-y-6">
              {DEMO_RUN_STATES.map((demo) => (
                <div key={demo.label}>
                  <p className="mb-2 text-xs text-faint">{demo.label}</p>
                  <RunProgress run={demo.run} now={DEMO_NOW} />
                </div>
              ))}
            </div>
          </section>

          <section className="bg-background rounded-lg border border-border p-5 mt-6" style={{ boxShadow: "var(--shadow-surface)" }}>
            <h2 className="text-sm font-medium text-muted-foreground mb-4">VerbButton — confirm-before-verb, verbatim refusals, audit hint</h2>
            <div className="flex flex-wrap items-center gap-4">
              <VerbButton label="trigger" confirm="Trigger the eod-reconciliation run now?" execute={verbOk} />
              <VerbButton label="approve" confirm="Approve batch b-1231?" execute={verbRefused} />
              <VerbButton label="erase" confirm="Erase the classified fields of ORD-77?" execute={verbOk} destructive />
              <VerbButton label="pause-all" confirm="Pause EVERY job in the fleet?" execute={verbError} destructive />
            </div>
          </section>

          <section className="bg-background rounded-lg border border-border p-5 mt-6" style={{ boxShadow: "var(--shadow-surface)" }}>
            <h2 className="text-sm font-medium text-muted-foreground mb-4">KeysetTable — cursor pager, honest footer, never a total</h2>
            <KeysetTable
              columns={[
                { header: "Run", cell: (r: DemoRun) => <span className="font-mono text-xs">{r.id}</span> },
                { header: "Job", cell: (r: DemoRun) => r.job },
                { header: "State", cell: (r: DemoRun) => <StateBadge state={r.state} /> },
                { header: "Items", cell: (r: DemoRun) => r.items.toLocaleString(), align: "right" as const },
              ]}
              loadPage={loadDemoRuns}
              rowKey={(r) => r.id}
              take={4}
              emptyMessage="No runs today."
            />
          </section>
        </div>
      </div>
    </div>
  );
}

createRoot(document.getElementById("root")!).render(<StrictMode><Gallery /></StrictMode>);
