# Module RFC: Goldpath.Jobs (scheduled & long-running work)

> Status: v1.1 ACCEPTED — D1–D10 approved by Ömer (2026-07-06). v1.0 was revised after the
> a prior in-house scheduler scheduler analysis (clustered Quartz store IN — see D2), the UI standardization
> decisions (D8), the multi-worker topology question (D9), and the telco MDM bulk-operations case
> analysis (the L4 landing pad + six design constraints, §12). Requirements source: the
> three scenario cards' job inventories. First RFC under the module excellence bar:
> Performance / Observability / Operational sections are load-bearing. Effort L
> (slices S1–S3 + S2b dashboard).

## 0. The execution ladder (where this module sits)

One RUN MODEL, four rungs — each rung composes the previous, the ops console renders all:

| Level | Module | Work shape | Distinct trait |
|---|---|---|---|
| L1 | Consumers (Goldpath.Messaging — shipped) | continuous stream | inbox dedup, lag |
| **L2** | **Goldpath.Jobs (THIS RFC)** | scheduled/ad-hoc runs | chunk/checkpoint/resume, minutes–hours |
| L3 | Bulk (Ring C, fired by finance card) | file/set intake | validation + repair, ON the L2 run model |
| L4 | Campaign (Ring C, fired by a telco-scale device-management (MDM) case) | multi-DAY paced execution | pacing policy (windows/quotas/TPS), broker fan-out |

L4 = L2's run model + a policy engine + broker fan-out (L1 workers) + counter store
(Goldpath.Caching) — designed here as the landing pad (§12), built later in ladder order.

## 1. Scope / Non-Goals

**Scope:** recurring and ad-hoc long-running work: calendar-aware scheduling, clustered
execution with automatic failover, chunked/checkpointed/resumable runs, per-item failure
isolation (repair queue), completion chaining, live progress + deadline prediction, admin
API + embedded dashboard (§7.1: API is the contract, the screen is its skin), full ops
package.

**Non-goals (deferrals with triggers):** DAG/workflow orchestration (trigger: a sample needs
fan-in/fan-out — compare against Temporal-class tools then; never grow one accidentally);
cross-app orchestration (trigger: first multi-service transformation); the InfraOps portal
page (Phase 3 — the dashboard components are built portal-ready, D8); Hangfire (D1 — not a
deferral, a license impossibility); L3/L4 themselves (own RFCs, in ladder order).

## 2. Seam Map

- **Time + cluster seam — Quartz.NET (Apache-2.0), CLUSTERED persistent store (D2):** cron,
  holiday calendars, misfire policy, ad-hoc triggering, AND cluster-wide exactly-once
  firing + automatic failover (`RequestsRecovery`) from Quartz's battle-tested row-lock
  store. The `qrtz_` schema ships as a FIRST-CLASS EF MIGRATION (the a prior in-house scheduler-proven move —
  the store never escapes migration discipline). Postgres + SqlServer DDL both.
- **Run seam — OURS (this is the module):** Quartz says "now, on this node"; `Goldpath.Jobs`
  owns what a RUN is: `JobRun` / `JobChunk` / `JobItemFailure` via EF in the APP database,
  checkpoint/resume, progress, prediction, repair, chaining, execution history (incl. which
  instance ran — a prior in-house scheduler's listener pattern, plus Vetoed/Recovered outcomes).
- **Leadership discipline (MDM fix #1):** NO per-second clustered tick — a fired job HOLDS
  its execution (one Quartz fire = one run); anything loop-shaped runs in-process on the
  node that owns the fire. Failover is Quartz recovery re-firing on a healthy node, where
  the runner RESUMES from the last checkpoint (never restarts blind).
- **Concurrency seam:** job-level singleton = `DisallowConcurrentExecution` on the clustered
  store (Quartz-native). Goldpath.Locking is NOT needed for the singleton anymore (D2 revision);
  it remains available to job BODIES needing app-level locks. Parallel chunk claims across
  instances use claim-updates on the run tables (`WHERE status='PENDING'` — MDM fix #2's
  same mechanism).
- **Messaging seam:** repair hand-offs and `JobRunCompleted` integration events ride the
  existing outbox/broker floor when present; in-proc Mediant notification otherwise. ONE
  messaging stack — MassTransit (Kafka rider at L4 scale; MDM fix #5).

## 3. Manifest Surface

```yaml
kind: worker
workerType: scheduler-quartz      # the enum value becomes real
trigger:
  cron: "0 30 1 * * ?"
```

**Schema change (deferral rule):** `scheduler-hangfire` REMOVED from the workerType enum —
Hangfire is LGPL-3.0; the license gate's allowlist rejects it. Not a strategic deferral: an
impossibility under the free/OSS constitution (D1).

**Two registration modes (D9):**
- `AddGoldpathJobs()` in a worker → EXECUTOR; scheduler name defaults to the worker's manifest
  name → **one Quartz cluster per worker KIND**, instances of that kind scale within it.
  (Never mix kinds in one cluster: any node can fire any trigger — a job class missing from
  a sibling's assembly is a TypeLoadException. a prior in-house scheduler fought the small version of this.)
- `AddGoldpathJobsManagement()` in the solution's API head → management-only member
  (thread pool 0 — can manage everything, can execute nothing; the a prior in-house scheduler topology). It
  DISCOVERS all scheduler names from the store: deploying a new worker kind changes
  nothing on the API/dashboard — it simply appears.

## 4. API Surface

```csharp
public interface IGoldpathJob
{
    /// Discover the work (set-based, keyset-paged; re-runs on resume).
    Task<GoldpathJobPlan> PlanAsync(GoldpathJobContext context, CancellationToken ct);
    /// Execute ONE chunk; the runner checkpoints after each successful return.
    Task ExecuteChunkAsync(GoldpathJobChunk chunk, GoldpathJobContext context, CancellationToken ct);
}

builder.AddGoldpathJobs(jobs =>
{
    jobs.AddJob<EodReconciliationJob>(j =>
    {
        j.Cron = "0 30 1 * * ?";
        j.Calendar = GoldpathCalendars.BankingDays("TR");
        j.Deadline = TimeSpan.FromHours(5.5);
        j.Chunk = c => { c.Size = 500; c.MaxParallelChunks = 4; };
        j.StartAfter<RenewalRunJob>();          // chaining v1 (D6)
        j.PinInput<TariffVersion>();            // version pinning
    });
});
```

**Definition vs schedule (D7):** the job DEFINITION lives in code (type-safe, analyzed,
manifest-disciplined — runtime create-by-classname is explicitly rejected); the SCHEDULE is
runtime-overridable through the admin API (reschedule cron, swap calendar, pause/resume —
audited). Ops reality: "run at 03:00 tonight" must not wait for an MR.

**Admin API** (`/goldpath/admin/jobs`, exported in OpenAPI → specdrift-visible): fleets (scheduler
names + cluster nodes with last check-in), jobs, runs (progress %, items/s, ETA, checkpoint
age, failures), verbs: `trigger` (+dry-run), `pause`/`drain`, `resume`, `rerun` (idempotent
by construction), `replay-item`, repair redrive, `reschedule`, calendar CRUD (holiday/
weekly/annual/cron — a prior in-house scheduler's set), scheduler standby/start. Every verb writes an audit
record; RBAC via the auth floor.

## 5. Dashboard (D8 — the UI standard is born here)

- **One RUN CONSOLE for the whole ladder.** The UI knows CAPABILITIES, not levels: every
  run renders in the same console (list → detail → chunks/failures/history/item drill-down);
  capability-gated panels appear per run — pacing panel (TPS gauge, quota, window) when the
  run carries policy (L4), intake/validation panel when it carries a file (L3), plain
  otherwise (L2). Consumers are streams, not runs → a "Queues" tab (lag/DLQ/redrive) in the
  same shell. A new level never changes the console — it adds a panel.
- **Mechanics:** TS+Vite+React lives in the goldpath monorepo (`ui/kit` design system +
  `ui/jobs-dashboard`); built at pack time; shipped as `Goldpath.Jobs.Dashboard` (RCL static
  assets). Consumers write ONE line: `app.MapGoldpathJobsDashboard()` — no Node anywhere near
  the generated app; air-gap safe. The UI talks ONLY through the admin API via a TS client
  GENERATED from the exported OpenAPI (spec-driven UI; §7.1 iron rule holds: the screen is
  the API's skin). Components are published portal-ready for Phase 3 composition.
- **Decision rule recorded:** PRODUCTS (Mockifyr; the future standalone gateway product)
  keep their own repo + UI, Goldpath composes them. MODULE SCREENS live in the goldpath monorepo,
  same version as the module. The `ui/kit` theme/token layer stays a thin blank canvas for
  Ömer's upcoming UX standard — one place to change, every screen follows.

## 6. Performance (measured, not promised)

- 100k-item no-op job on the GM profile: plan→complete **< 5 min**, checkpoint overhead
  **< 5%**, kill-9 at 50% loses **at most one chunk**.
- Full-tilt job + interactive load: interactive p95 degradation **< 10%** (default budget).
- Result/state writes are BATCHED per chunk, never per item (MDM fix #4 — item-rate row
  updates melt the hot table at 800+ TPS).
- Numbers recorded in `ops/jobs-benchmarks.md`; re-run via `scripts/bench-jobs.sh`.

## 7. Observability (shipped, not suggested)

- ONE metric vocabulary for the whole ladder: `goldpath_jobs_run_progress_ratio`,
  `goldpath_jobs_items_per_second`, `goldpath_jobs_checkpoint_age_seconds`,
  `goldpath_jobs_predicted_finish_seconds`, `goldpath_jobs_item_failures_total`,
  `goldpath_jobs_repair_queue_depth`, `goldpath_jobs_run_duration_seconds` (labels: scheduler, job,
  run, instance).
- Deadline prediction is a metric + alert rule that fires BEFORE the deadline does.
- Grafana panel template ships in the module's `ops/`; traces: run → chunk spans.

## 8. Operational (runbook or it didn't happen)

- Runbooks: stuck run (recovery/takeover semantics + verification), poisoned item (repair →
  redrive), mid-run deploy (input version pinning), missed window (misfire policy per
  option explained), counter/limit reconciliation after cache loss (MDM fix #3 pattern:
  fast counters are cache, truth is the durable run tables — recompute on warmup).
- Graceful shutdown = finish current chunk, checkpoint, let recovery hand over — proven by
  a SIGTERM-mid-run test asserting clean resume.
- Every admin verb audited; `rerun` on a completed run is a reasoned no-op, never a double.

## 9. Analyzer Rules

> Renumbered at implementation (S2): GP0501/0502 were already owned by the audit rules —
> ids are wire contracts, never recycled. The jobs rules live in the 13xx block.

- GP1301 (error): `ExecuteChunkAsync` opening its own transaction across chunks.
- GP1302 (warning): a registered job with no `Deadline` — silence is how 07:00 gets missed.
- GP1303 (info): `PlanAsync` materializing the full item list instead of keyset paging.

## 10. Test Plan

Unit (run state machine, prediction math, chaining) · integration (Testcontainers, TWO
executor instances: clustered exactly-once firing, kill-9 recovery resumes from checkpoint
on the OTHER node, repair redrive, calendar skips a holiday, cron+ad-hoc coexistence,
management-mode member can verb but never executes) · perf proofs of §6 · property-based
(CsCheck: checkpoint/resume never loses or duplicates an item across arbitrary failure
points) · breaker (kill during checkpoint write; clock skew) · GM `GmWorkerJobs` shape ·
spec (admin API in exported OpenAPI, drift wiring rows).

## 11. Providers (D10)

The module is provider-agnostic BY CONSTRUCTION: the run model is EF Core (any provider),
Quartz's store speaks Postgres/SqlServer/Oracle/MySQL natively (Apache-2.0). The golden
path ships **postgres + sqlserver** (both with EF-migration-managed qrtz DDL, both in the
GM matrix). **Oracle**: architecture-ready, but `Oracle.ManagedDataAccess`/EF provider is
FUTC-licensed — NOT in the license-gate allowlist. Trigger written: first real customer
demand (e.g. a telco-scale engagement materializing) → a scoped gate exception decision with
inline justification, exactly like the Microsoft SNI-runtime precedent. No silent adoption.

## 12. The L4 landing pad (from the telco-scale MDM analysis, 2026-07-06)

The MDM doc's architecture is sound (Scheduler-Agent-Supervisor + Competing Consumers +
CQRS; screen-trackable by construction). L4 (`campaign`) will compose THIS module's run
model + a policy engine + MassTransit fan-out + Goldpath.Caching counters. Six design
constraints from the analysis bind BOTH modules and are already reflected above:
1. Leadership loop, never per-second clustered ticks (§2).
2. Worker-side item claim before external calls — rebalance must not double-send
   (claim-update on target state; an SMS sent twice is a defect, "config is idempotent" is
   not a strategy).
3. Fast counters are cache; the durable run tables are truth; reconcile on warmup (§8).
4. Batched state writes, never per item (§6).
5. ONE messaging stack: MassTransit (+ Kafka rider at 30M scale) — no parallel raw clients.
6. Transport-agnostic: RabbitMQ mid-scale, Kafka rider when the inventory demands it.

## 13. Slices & DoD

- **S1** — clustered Quartz composition (EF-migrated store, executor+management modes) +
  run model + checkpoint/resume + recovery-resume proof (two instances, kill-9) + §6 perf.
- **S2** — admin API (full verb set, audit) + history + metrics + Grafana panel + runbooks.
- **S2b** — `ui/kit` first stones + jobs-dashboard (RCL, generated TS client, capability-
  gated panels) + Playwright smoke in CI.
- [x] **S3** — worker template `--trigger jobs` (BCL `schedule` skeleton stays, D4) +
  SPEC0113 + scheduler-quartz drift rows + `GmWorkerJobs` GREEN (admin-verb smoke) +
  interactive-degradation proof (0.4→0.3ms p95 under full tilt). `goldpath new worker --trigger
  jobs` works via passthrough.
- DoD: the three cards' job inventories answered line by line; excellence-bar artifacts
  present (bench doc, panel, runbooks); ledger updated.

## 14. Decisions (all approved 2026-07-06)

- **D1** Quartz.NET (Apache-2.0); Hangfire out BY LICENSE GATE; schema drops the enum value.
- **D2** (revised after a prior in-house scheduler) Clustered Quartz persistent store with EF-migration-managed
  schema; run model OURS via EF in the app DB; no per-second ticks; Goldpath.Locking no longer
  carries the job singleton.
- **D3** Admin-API-first, full verb set + calendar CRUD, every verb audited.
- **D4** Worker template gains `--trigger jobs`; BCL `schedule` skeleton stays.
- **D5** Prediction & throttling in the run model; the cards' numbers are the acceptance.
- **D6** Chaining v1 = `StartAfter<T>()`; DAG deferred with its trigger written.
- **D7** Definition in code; SCHEDULE runtime-overridable via audited admin verbs;
  create-by-classname rejected.
- **D8** Embedded dashboard standard: monorepo `ui/` (kit + per-module SPA), RCL packaging,
  generated TS client, capability-driven single console; products keep their own UI.
- **D9** Multi-worker topology: one scheduler (cluster) per worker kind; management-only
  member on the API head; store-driven discovery — zero-config fleet visibility.
- **D10** Provider-agnostic store; golden path pg+sqlserver; Oracle behind a written
  license-gate trigger.
