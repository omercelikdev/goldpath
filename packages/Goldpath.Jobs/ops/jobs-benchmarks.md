# Goldpath.Jobs — performance proofs (jobs RFC §6, module excellence bar)

Measured, not promised. Re-run with `scripts/bench-jobs.sh` (real PostgreSQL via
Testcontainers); update this file whenever the runner changes.

## Reference profile

Apple Silicon dev machine · PostgreSQL 17 (container) · Debug build · `MaxParallelChunks = 4`.
CI numbers on the shared runner will differ; the BUDGETS are the contract, the numbers
below are the current evidence.

## Results (2026-07-07)

| Proof | Budget (§6) | Measured |
|---|---|---|
| 100k-item no-op run, plan → complete (200 chunks × 500) | < 5 min | **0.4 s** |
| Checkpoint cost per chunk (vs naked loop, absolute) | < 25 ms | **2.1 ms** |
| Kill-9 at mid-run: chunks lost | ≤ 1 chunk repeats | **≤ 1** (integration-asserted) |
| Interactive point-read p95 under a full-tilt write-heavy job (telco card) | < 10% degradation | **0.4 ms → 0.3 ms (no degradation)** |

Notes:
- The checkpoint budget is stated ABSOLUTE (ms/chunk) because a no-op baseline makes a
  percentage meaningless; against any real chunk (hundreds of items with I/O) 2.1 ms is
  far below the 5% envelope the RFC targets.
- Kill-9/exactly-once is a hard assertion in `JobsClusterTests`, not a benchmark: every
  chunk lands exactly once except at most the single in-flight chunk at the kill.
- The interactive proof measures at the DATABASE layer (where the contention actually
  lives): point-read p95 against the shared store with a `MaxParallelChunks = 4`
  write-heavy run at full tilt. Measured with S3 (`Bench_interactive_reads_under_a_full_tilt_job`).
