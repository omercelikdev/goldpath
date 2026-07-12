# Goldpath.Bulk — measured performance (bulk RFC §6)

Reference profile: local dev machine (Apple Silicon), PostgreSQL 17 in Testcontainers,
`scripts/bench-bulk.sh` (tests: `BulkBenchTests`, Trait `Category=Bench`). Re-run and
update this file whenever the intake or execution path changes.

| Proof | Budget | Measured (2026-07-07) | Verdict |
|---|---|---|---|
| 10k-row CSV: upload → parse → validate → report | < 5 min (the finance card) | **0.39 s** (upload 0.04 s) | ~770× headroom |
| 100k-row execute, no-op handler (chunk 500) | — (baseline) | **17.1 s ≈ 5,840 rows/s** | claim+stamp+counter overhead only |

## Reading the numbers

- **Intake is not the bottleneck and never will be** — the card's 5-minute budget was
  written for end-to-end operator experience; the engine spends it 770× under. Real
  intakes add domain-validator lookups; budget accordingly, the pipeline itself is free.
- **Execute throughput is the FLOOR, not the ceiling:** 5,840 rows/s is what the
  engine's honesty costs — the persisted CLAIM before any side effect, the per-row stamp,
  the batched counter updates, one checkpoint per 500 rows. A real handler's external
  call will dominate; the module's own overhead is ~0.17 ms/row.
- Upload streams and hashes in 256 KB chunks — the file never materializes as one array;
  memory stays flat regardless of file size.
