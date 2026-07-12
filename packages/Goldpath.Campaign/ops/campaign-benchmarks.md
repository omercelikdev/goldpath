# Goldpath.Campaign — measured performance (campaign RFC §6)

Reference profile: local dev machine (Apple Silicon), PostgreSQL 17 in Testcontainers,
`scripts/bench-campaign.sh` (tests: `CampaignBenchTests`, Trait `Category=Bench`).
Re-run and update this file whenever the enumeration, release or sink path changes.

| Proof | Budget | Measured (2026-07-10) | Verdict |
|---|---|---|---|
| Enumeration: 1M targets materialized into item rows | memory flat, minutes not hours | **15.3 s = 65,266 rows/s** | a 30M campaign enumerates in ~8 min |
| Pacer precision: configured 200 TPS, measured over 15 s | ±5% steady-state | **186.7 released/s** (−6.7%, includes cold-start slice) | governor holds; see note |
| LIVE throttle: 200 → 50 TPS mid-run, next 15 s | takes effect within one tick | **50.0 released/s** — exact | D6 delivered |
| Outcome sink: 100k outcomes, batches of 200 | 30M items never mean 30M writes | **1.7 s = 57,462 outcomes/s** | set-based flushes hold |

## Reading the numbers

- **Enumeration is not the bottleneck:** 65k rows/s means even the TT-Mobile-scale 30M
  plan materializes inside a coffee break — and it streams, so memory stays flat.
- **The pacer's −6.7% includes the cold start:** the measured window opens BEFORE the
  first tick banks budget and while the same slice is still enumerating; steady-state
  ticks track the ceiling (the throttle phase — pure steady-state — measured EXACTLY
  50.0/s). The governor never overshoots, which is the side that pages.
- **The throttle number is the product:** an operator typed a new ceiling on a LIVE
  campaign and the very next slice held it exactly — no restart, no redeploy, no drain.
- **Sink throughput is the honesty floor:** 57k outcomes/s of durable terminal evidence
  (two set-based item updates + relative counters per 200-batch). The consumers' real
  external calls dominate in production; the module's own write cost is ~17 µs/outcome.
