# CorPay — where each NFR's proof lives

The finance card's NFR block, line by line (docs/scenarios/finance-payments.md):

| NFR | Budget | Proof |
|---|---|---|
| Batch file: 10k rows ingested | < 5 min | Platform floor: **0.85 s** on the CI reference profile (`Goldpath.Bulk` ops benchmarks) — CorPay adds only the validation lambda per row |
| EOD: 100k reconciliations | < 45 min | Rule pass: 100k refs in **milliseconds** (`EodReconciliationVolumeTests`, nightly); run machinery: `Goldpath.Jobs` CI benchmarks (chunk overhead ~ms); the budget belongs to the two day-window queries — indexed on `(Day, TenantId)` |
| Submit p95 < 300 ms @ 50 rps | staging concern | The platform floor is measured (ServiceDefaults/Data benches); a sustained-load p95 is an ADOPTER-STAGING measurement by nature — the sample deliberately does not fake one on shared CI hardware |
| Per-instruction correlation | end to end | `Goldpath.Jobs/Bulk` H4 span chain (upload trace pinned on the batch, linked from every later span) + `docs/ops/trace-correlation.md` |
| EOD overrun predicted | before 07:00 | 23:30 cron + 7.5 h `Deadline` → the run model's `PredictedFinishAt` metric and its alert (jobs ops pack) |

Honesty note: numbers quoted from the pinned CI profile (ubuntu-latest, 4 vCPU); the
sample's own nightly job keeps its contracts and smoke green against the PUBLISHED train.
