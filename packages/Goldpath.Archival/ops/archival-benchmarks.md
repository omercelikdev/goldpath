# Goldpath.Archival — performance proofs (archival RFC §6, module excellence bar)

Measured, not promised. Re-run with `scripts/bench-archival.sh` (real PostgreSQL via
Testcontainers); update this file whenever the engine changes.

## Reference profile

Apple Silicon dev machine · PostgreSQL 17 (container) · Debug build.

## Results (2026-07-07)

| Proof | Budget (§6) | Measured |
|---|---|---|
| Single-entry recall (definition + key) at **1,000,000 entries**, p95 | < 5 s | **0.5 ms** |
| Archive round-trip: extract → store → retrieve, 50-row graph | < 10 s | **0.2 s** |
| Row-retention purge, 100k aged rows (2k batches) | < 5 min | **3.0 s** |

Notes:
- Recall rides the unique `(Definition, AggregateKey)` index — the budget holds with four
  orders of magnitude of headroom; the alert watches `goldpath_archival_retrieval_seconds` (S2).
- The purge walks bounded, ordered batches by design (the MDM lesson: one giant DELETE is
  a lock storm; boring 2k-row batches are the point).
- The hot-path-protection proof (interactive p95 during a full-tilt archive run) reuses
  the jobs-module bench pattern and lands with S3's GM shape, where a hot workload exists
  next to the archive run.

## Reference profile (CI): ubuntu-latest, 4 vCPU / 16 GB — 2026-07-13

The PINNED profile adopters can rent and budget against (`bench.yml` dispatch,
run 29228081113; the dev-machine numbers above/below are the fast point, not the promise).

| Proof | Budget (§6) | CI measured |
|---|---|---|
| Single-entry recall at 1,000,000 entries, p95 | < 5 s | **4.3 ms** |
| Archive round-trip, 50-row graph | < 10 s | **0.46 s** |
| Row-retention purge, 100k aged rows | < 5 min | **16.0 s** |
