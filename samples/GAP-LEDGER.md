# Phase D gap ledger

Every place the asset fell short while building the samples FOR REAL, with its
disposition. This file is the point of Phase D: findings become fix-PRs or
written-trigger deferrals — never silent workarounds.

| # | Found while | Finding | Disposition |
|---|---|---|---|
| G1 | `goldpath add worker` (CorPay) | the CLI's teaching line (`dotnet tool install -g specdrift`) was unfulfillable — specdrift was not on nuget.org at the time | RESOLVED — specdrift 0.4.1 published (NuGet + MCP + Docker + Action); issue [#31](https://github.com/omercelikdev/goldpath/issues/31) closed |
| G2 | `goldpath new -o <dir>` (CorPay) | `db init` broke whenever CWD ≠ appRoot: owner paths were CWD-relative while ef ran WITH appRoot as working directory (prefix doubled) — the post-step silently failed in the quickstart too | FIXED — PR #29; shipped in 0.1.0-preview.3 |
| G3 | first AppHost smoke (CorPay) | `add worker` generated no `Properties/launchSettings.json` — Aspire infers endpoints from it, so `WithHttpHealthCheck` found no endpoint and the WHOLE app refused to start | FIXED — PR #29; shipped in 0.1.0-preview.3 |
| G4 | G2's fallout | when `db init` defers (teaching message path), the FIRST-CONTRACT commit is skipped too and never happens later — `goldpath db init` run manually does not re-attempt it | RESOLVED — [#32](https://github.com/omercelikdev/goldpath/issues/32) closed 2026-07-14 (`db init` owns the first-contract commit) |
| G5 | slice 1 (PaymentExecuted consumer) | No blessed home for cross-process event contracts between the api and ADDED workers — the walking skeleton consumes in-process; a worker consuming an api event has nowhere idiomatic to share the record | RESOLVED — event half [#33](https://github.com/omercelikdev/goldpath/issues/33) closed 2026-07-14 (the `<Name>.Contracts` idiom, RFC goldpath-event-contracts); read half by G5b |
| G5b | slice 5 (EOD read side) | RESOLVED IDIOM for the read case of G5: the worker MAPS api-owned tables read-only with `ExcludeFromMigrations` (the D3 pattern generalized from jobs tables to app tables) — no shared project, no duplicate ownership; G5's remaining open half is the EVENT-contract case (a worker CONSUMING an api event type) | RESOLVED for reads (PR #30, EOD worker); event half closed by [#33](https://github.com/omercelikdev/goldpath/issues/33) — the `<Name>.Contracts` idiom |
| G6 | `goldpath db add` (slice 1) | The verb runs for EVERY owner, so owners with no model change get EMPTY migrations (noise per api-entity add; the eod worker's copy even failed the CHARSET format gate) | RESOLVED — [#34](https://github.com/omercelikdev/goldpath/issues/34) closed 2026-07-14 (empty migrations are refused, not minted) |

Format: new rows land as they are found; OPEN rows become issues when their slice
closes; FIXED rows name the PR that carried them.

## Reconciliation rule (added 2026-08-09 after an audit found this file three weeks stale)

A row here is a claim about a GitHub issue, and the two drifted in BOTH directions:
this file said #32/#33/#34 were open while all three closed 2026-07-14, and
`open-threads.md` said #98/#72 were closed while both were still open. Neither ledger
could be trusted without a cross-check, which is exactly what a ledger exists to prevent.

**The rule:** a row's state is whatever `gh issue view <n>` says. `scripts/ledger-check.sh`
asserts it in CI, so the drift cannot come back silently.
