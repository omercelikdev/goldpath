# Goldpath.Notification — measured performance (notification RFC §6)

Reference profile: local dev machine (Apple Silicon), PostgreSQL 17 in Testcontainers,
`scripts/bench-notification.sh` (tests: `NotificationBenchTests`, Trait `Category=Bench`).
Re-run and update this file whenever the render or send path changes.

| Proof | Budget | Measured (2026-07-08) | Verdict |
|---|---|---|---|
| Template render (tokens + culture fallback) | < 1 ms | **0.3 µs** | ~3000× headroom |
| The insurance night: 10k requests (render + evidence row each) | nightly window | **28 s** (~2.8 ms/request) | one durable roundtrip per request — see note |
| 10k send pass, no-op channel (chunk 500) | — (baseline) | **2.5 s ≈ 4,070 rows/s** | claim+stamp+counter overhead only |

## Reading the numbers

- **Render is free** — string work; the channel always dominates.
- **Request cost is one durable write per notification ON PURPOSE:** each request commits
  its evidence row (the same-transaction guarantee is the product). 10k in 28 s clears
  any nightly window; if a card ever demands bulk-request batching, a `RequestManyAsync`
  that shares one SaveChanges is a straightforward addition — deferred until asked.
- **Send throughput is the floor:** 4,070 rows/s is the engine's honesty cost (persisted
  claim before the wire, per-row stamps, one checkpoint per 500). A real SMTP/gateway
  call dominates in production; the module's own overhead is ~0.25 ms/row.

## Reference profile (CI): ubuntu-latest, 4 vCPU / 16 GB — 2026-07-13

The PINNED profile adopters can rent and budget against (`bench.yml` dispatch,
run 29228081113; the dev-machine numbers above/below are the fast point, not the promise).

| Proof | Budget | CI measured | Verdict |
|---|---|---|---|
| Template render | < 1 ms | **0.7 µs** | ~1400× headroom |
| The insurance night: 10k requests | nightly window | **142.3 s** (~14 ms/request) | one durable roundtrip per request; a 100k night ≈ 24 min on 4 vCPU |
| 10k send pass, no-op channel (chunk 500) | — (baseline) | **7.4 s ≈ 1,343 rows/s** | claim+stamp overhead on 4 vCPU |
