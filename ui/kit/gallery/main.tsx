// The kit gallery: every composite, both themes, real tokens — the screen the eye
// verifies BEFORE any slice ships (U1 exit gate; screenshots ride the PRs).
import { StrictMode, useState } from "react";
import { createRoot } from "react-dom/client";
import "../src/tokens/tokens.css";
import { StateBadge } from "../src/components/StateBadge";
import { KeysetTable, type KeysetPage } from "../src/components/KeysetTable";
import { VerbButton } from "../src/components/VerbButton";
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
