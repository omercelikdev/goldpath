# Module RFC: Goldpath.Bulk (file/set intake — L3 of the execution ladder)

> Status: v1.0 ACCEPTED — D1–D7 approved by Ömer (2026-07-07). SHIPPED: S1 (intake core +
> execution composition), S2 (ops surface: admin API + gauges/panel + runbooks +
> GOLDPATH15xx), S3 (`features.bulk` manifest word — module COMPLETE; the console rides the
> consolidated UI phase).
> Trigger: the finance card's
> "upload batch payment file → parse → per-item instructions" flow (inseparable from the
> popular scenario). Requirements source: finance NFR block (10k-item file ingested
> < 5 min; resubmitted work must NOT double-execute; four-eyes approval on high amounts).
> Module excellence bar applies: §6–§8 are load-bearing. Effort M (S1–S3) — the execution
> half already exists in Goldpath.Jobs; this module is the INTAKE half plus composition.

## 0. Position on the ladder

L3 = a file (or client-supplied set) becomes a validated, approved, resumable RUN:

| Half | What it is | Who provides it |
|---|---|---|
| Intake | upload → parse → per-row VALIDATION → report → approve/reject gate | **THIS module** |
| Execution | chunked/checkpointed/resumable run, repair queue, replay verbs, prediction, console | **Goldpath.Jobs, unchanged** |

The distinct L3 trait the ladder table promised — "validation + repair, ON the L2 run
model" — is exactly this split. Bulk ships NO scheduler, NO runner, NO new run tables:
a batch's execution IS a `GoldpathJobRun`, visible in the SAME console with the SAME verbs.
L4 (`campaign`) will later compose bulk-shaped intake with pacing + fan-out; the six MDM
constraints (goldpath-jobs.md §12) that touch row execution (claim-before-external-call,
batched state writes) are honored here so L4 inherits clean semantics.

## 1. Scope / Non-Goals

**Scope:** content-addressed file intake (same-database store), CSV parsing v1 behind a
format seam, typed per-row validation with a queryable report, an explicit approve/reject
gate (audited), execution of approved batches as Goldpath.Jobs runs with row-level failure
isolation into the EXISTING repair queue, batch/row status surfaces, admin API + ops
package, privacy-by-construction reporting (errors name row+field, never echo values).

**Non-goals (deferrals with triggers):**
- Other formats: fixed-width, XLSX, JSON-lines (trigger: first card/customer demanding
  one; `IGoldpathBulkFormat` is the seam from day one, D3).
- Object-store file backend (trigger: same as archival D1 — volume/compliance outgrowing
  the database; `IGoldpathBulkFileStore` is the seam).
- SFTP/scheduled pickup (trigger: first integration project; an app `IGoldpathJob` polling +
  the intake API covers it without module code).
- Transformation/mapping DSL (row → domain mapping stays code in the row handler; a DSL
  is Spec Engine territory if ever).
- Pacing, quotas, windows, broker fan-out — that is L4 (`campaign`), not this module.
- Four-eyes/maker-checker POLICY (who may approve what amount) — domain logic in the app;
  the module provides the GATE, the actor stamp and the audit row.

## 2. Seam Map

- **File seam — same database, content-addressed (D1):** the uploaded bytes land as
  chunked blob rows in the app's database via EF (`GoldpathBulkFiles` + `GoldpathBulkFileChunks`),
  SHA-256 stamped. The hash IS the idempotence key: re-uploading identical bytes returns
  the SAME batch instead of creating a double-payment risk (finance's resubmit story at
  file granularity). `IGoldpathBulkFileStore` is the provider seam for the deferred object
  store.
- **Format seam (D3):** `IGoldpathBulkFormat` turns a byte stream into typed rows + per-row
  raw excerpts. v1 ships CSV (header mapping, delimiter, culture, quoted fields) —
  deliberately boring and RFC-4180-shaped; anything cleverer waits for its trigger.
- **Validation seam (D3):** a batch DEFINITION binds a row type, a row validator
  (delegate — full domain power: format checks, lookups via a scoped provider), an
  optional in-file duplicate key (`RowKey`) and a mandatory row-count ceiling
  (`MaxRows`, D5/GP1501). Validation writes `GoldpathBulkRowError` rows: batch, row number,
  field, message — NEVER the offending value (classified data stays out of reports by
  construction; the raw file remains the evidence for those with access).
- **Lifecycle seam — an explicit state machine (D2):**
  `Received → Parsing → Validated → Approved|Rejected → Executing → Completed|CompletedWithFailures`.
  Approve/reject are audited admin verbs carrying the actor; `AutoApprove()` exists as an
  opt-in for non-gated flows (imports, reference data) and is analyzer-visible (GP1503).
- **Execution seam — the ladder eats itself again (D4):** approving a batch triggers the
  definition's `GoldpathBulkExecuteJob<TContext>` (an `IGoldpathJob`) with the batch id in the data
  map; `PlanAsync` chunks over validated row ranges; `ExecuteChunkAsync` feeds each row to
  the app's `IGoldpathBulkRowHandler<TRow>` and writes row statuses BATCHED per chunk (MDM
  constraint 4). A row failure files a `GoldpathJobItemFailure` — the EXISTING repair queue,
  `replay-items` verb and console views apply verbatim. Handlers that call external
  systems (payments!) get the claim-before-call contract in the handler context (MDM
  constraint 2): mark-executing is persisted at the chunk checkpoint BEFORE side effects,
  so a kill-9 rebalance never double-pays silently — the at-most-once-or-repair semantics
  are documented, tested, and the same as jobs.
- **Tenancy/privacy seams:** batches carry the tenant (fail-closed queries); row errors
  are value-free (above); the stored raw file is retrievable only through the admin API.

## 3. Manifest Surface

```yaml
features:
  bulk: true            # toggle form; object form reserved for the store provider
```

Schema key joins `$defs/features` WITH the final slice (the schema-rejects-unimplemented
rule, as always). Drift rows: `features.bulk` → package `Goldpath.Bulk`, call `AddGoldpathBulk`;
plus the composition guard pair `features.bulk` → package `Goldpath.Jobs`, call `AddGoldpathJobs`
(same pattern the archival S3 rows proved: claiming L3 without the L2 runtime is a
finding).

## 4. API Surface

```csharp
builder.AddGoldpathBulk<WebApplicationBuilder, OrdersDbContext>(bulk =>
{
    bulk.AddBatch<PaymentRow>("payments", b =>
    {
        b.Csv(c => { c.Delimiter = ';'; c.Culture = "tr-TR"; });   // format seam, v1: CSV
        b.MaxRows(50_000);                                          // ceiling is MANDATORY (GP1501)
        b.RowKey(r => r.EndToEndId);                                // in-file duplicate = validation error
        b.Validate((row, ctx) =>                                    // domain validation, scoped services via ctx
        {
            if (row.Amount <= 0) ctx.Fail(nameof(row.Amount), "amount must be positive");
            if (!Iban.IsValid(row.Iban)) ctx.Fail(nameof(row.Iban), "invalid IBAN");
        });
        // Approval gate is the DEFAULT; b.AutoApprove() opts out (GP1503 makes it visible).
    });
});

// The app's execution hook — one row, scoped services, claim-aware context:
public sealed class PaymentRowHandler : IGoldpathBulkRowHandler<PaymentRow>
{
    public Task ExecuteAsync(PaymentRow row, GoldpathBulkRowContext ctx, CancellationToken ct)
        => /* create instruction / call core banking; throw → repair queue */;
}

// DbContext: modelBuilder.AddGoldpathBulk();   // files, batches, rows-status, row-errors
// Jobs wiring: jobs.AddGoldpathBulkJobs<OrdersDbContext>();   // the execute job per definition
```

**Admin API** (`/goldpath/admin/bulk`, every verb audited):
- `POST /batches/{definition}` — multipart upload → returns the batch (or the EXISTING
  batch on an identical file: content-hash idempotence).
- `GET /batches` / `GET /batches/{id}` — states, counts (total/valid/invalid/executed/
  failed), timings, the run id once executing.
- `GET /batches/{id}/errors` — the validation report (row, field, message; pageable).
- `POST /batches/{id}/approve` / `POST /batches/{id}/reject` — the gate; actor stamped;
  approve refuses when invalid rows exceed the definition's tolerance (default: ANY
  invalid row blocks; `b.TolerateInvalidRows()` opts into partial execution of the valid
  subset — the report is the evidence of what was skipped).
- Run views delegate to the JOBS console (`/goldpath/admin/jobs`) — same run model, no
  duplicate screens; `replay-items` there IS bulk's row-retry verb.

## 5. Analyzer Rules (GOLDPATH15xx — new block)

- **GP1501 (error):** a batch definition without `MaxRows` — unbounded intake is a
  denial-of-service invitation; the ceiling is a decision, not a default.
- **GP1502 (warning):** a row handler that calls `SaveChanges` directly — row state
  writes are batched per chunk by the engine (MDM constraint 4); per-row saves wreck the
  10k budget and fight the checkpoint semantics.
- **GP1503 (info):** a definition using `AutoApprove()` — legitimate for imports, but
  the gate's absence should be visible in review.

## 6. Performance (measured, not promised)

Reference profile, `scripts/bench-bulk.sh` → `packages/Goldpath.Bulk/ops/bulk-benchmarks.md`:
- **The card's number:** 10k-row CSV upload → parse → validate → report **< 5 min**
  budget; expectation: seconds — measure and publish the real number.
- 100k-row execute with a no-op handler: rows/s throughput + checkpoint overhead per
  chunk (the jobs bench methodology, applied to the bulk planner).
- Validation-report query (largest error page) p95 measured at 50k-error batch.
- Upload memory ceiling: streamed hashing + chunked persistence — the 50 MB file never
  materializes as one array (assert allocation profile in the bench).

## 7. Observability (shipped)

`Goldpath.Bulk` meter: batches by state (gauge), `rows_validated_total` / `rows_invalid_total`
(the invalid RATIO is the data-quality signal), validate + execute durations, rows/s
during execution (rides the jobs ActiveRun registry), **oldest-awaiting-approval age**
(the human-in-the-loop alert: a batch stuck at the gate past its SLA pages someone),
upload dedup hits (a spike = a client retry storm). Grafana panel ships in `ops/`
(intake funnel: received→validated→approved→completed; invalid-ratio trend; awaiting-
approval age; execution progress via the jobs panels). Every metric lands in the panel or
it does not ship.

## 8. Operational (runbook or it didn't happen)

Runbooks (`packages/Goldpath.Bulk/ops/runbooks.md`):
1. Batch stuck in Parsing/Validating (run views, takeover — it is a jobs run).
2. High invalid ratio (report triage; the file is the evidence; who re-uploads and how
   dedup interacts with a CORRECTED file — different bytes, new batch, by design).
3. Batch awaiting approval past SLA (the alert, the gate verbs, the audit trail).
4. Poisoned row mid-execution (repair queue → fix → `replay-items` — the jobs runbook,
   linked not duplicated).
5. Replay after downstream outage (core banking down: failures pile into repair; redrive
   in bounded replays; claim semantics guarantee no double-send).
6. Erasing a batch's personal data (the raw file is the evidence store: retention for
   bulk files — `DeleteFileAfter(days)` on the definition; the archival module owns
   long-term evidence if the app archives instructions, not files).

## 9. Test Plan

- **Unit:** state machine transitions (every illegal transition refused with a teaching
  message); CSV parser (quoted fields, delimiter/culture, header mapping, broken lines →
  row errors not exceptions); content-hash dedup; RowKey in-file duplicates; validation
  context (Fail accumulates, value never stored); planner chunking math (golden table);
  approve tolerance semantics.
- **Mutation:** ≥ 75 break, golden tables for recipe/plan literals (the S2/S3 playbook).
- **Integration (pg, real containers):** the finance story end to end — upload 10k CSV →
  validated report with seeded errors → approve → execution as a REAL jobs run (visible
  via the jobs admin service) → kill-9 mid-execution → the other node resumes from the
  checkpoint → poisoned rows in the repair queue → replay-items completes them →
  counts reconcile exactly (no row lost, no row double-executed — the sink asserts it).
  Re-upload the same file → same batch id. Tenant scoping fail-closed.
- **Bench (Trait Category=Bench):** §6 numbers.

## 10. Slices & DoD

- **S1 — the intake core + execution composition:** file store + CSV format + batch state
  machine + validation engine + `GoldpathBulkExecuteJob` on Goldpath.Jobs + repair-queue
  composition + the kill-9 resume proof + §6 bench baseline.
- **S2 — the ops surface:** admin API (upload/report/gate verbs, audited) + metrics +
  Grafana panel + runbooks + GP1501–1503 analyzers.
- **S3 — the manifest word:** `features.bulk` schema key + solution template
  `--features bulk` (sample: order import batch) + `goldpath add feature bulk` recipe +
  drift row pair + GmEverything grows the shape → module closes.
- DoD: the finance card's bulk lines answered line by line; excellence-bar artifacts
  present (bench doc, panel, runbooks); ledger updated; console rides the UI phase.

## 11. Decision Points (Ömer)

- **D1 — File store: same database, content-addressed, chunked (zero new infra);**
  object store deferred behind `IGoldpathBulkFileStore` with the written trigger. Accept?
- **D2 — Two-phase lifecycle with an EXPLICIT approve/reject gate as the default;**
  `AutoApprove()` opt-out (analyzer-visible). The four-eyes POLICY stays app domain;
  the module ships the gate + actor + audit. Accept?
- **D3 — CSV-only v1 behind `IGoldpathBulkFormat`;** fixed-width/XLSX/JSON-lines deferred
  with triggers. Accept?
- **D4 — Execution IS a Goldpath.Jobs run:** no new runner/tables/console; repair queue and
  `replay-items` are THE row-retry story; batched row-state writes + claim-before-
  external-call per the MDM constraints. Accept?
- **D5 — Approve blocks on ANY invalid row by default;** `TolerateInvalidRows()` opts
  into executing the valid subset with the report as evidence. Accept?
- **D6 — Privacy-by-construction reports:** row errors carry row+field+message, never
  the value; the raw file is the evidence, admin-API-gated, with `DeleteFileAfter`
  retention. Accept?
- **D7 — GOLDPATH15xx analyzer block** (1501 error: no MaxRows; 1502 warning: SaveChanges in
  a row handler; 1503 info: AutoApprove). Accept?
