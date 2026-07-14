# Phase D gap ledger

Every place the asset fell short while building the samples FOR REAL, with its
disposition. This file is the point of Phase D: findings become fix-PRs or
written-trigger deferrals — never silent workarounds.

| # | Found while | Finding | Disposition |
|---|---|---|---|
| G1 | `goldpath add worker` (CorPay) | **specdrift is not on nuget.org** — the CLI's own teaching line (`dotnet tool install -g specdrift`) is unfulfillable for adopters; every `add` verb dead-ends without the engine | OPEN — publish specdrift to nuget.org (its own repo/release); until then the README must say "built from source" honestly |
| G2 | `goldpath new -o <dir>` (CorPay) | `db init` broke whenever CWD ≠ appRoot: owner paths were CWD-relative while ef ran WITH appRoot as working directory (prefix doubled) — the post-step silently failed in the quickstart too | FIXED in this PR (`DbCommand` absolute-from-the-door + CWD regression test); ships next train |
| G3 | first AppHost smoke (CorPay) | `add worker` generated no `Properties/launchSettings.json` — Aspire infers endpoints from it, so `WithHttpHealthCheck` found no endpoint and the WHOLE app refused to start | FIXED in this PR (deterministic-port launchSettings emitted; asserted in AddWorkerTests); ships next train |
| G4 | G2's fallout | when `db init` defers (teaching message path), the FIRST-CONTRACT commit is skipped too and never happens later — `goldpath db init` run manually does not re-attempt it | OPEN — move/duplicate the contract commit into `db init` itself (it owns the build moment) |
| G5 | slice 1 (PaymentExecuted consumer) | No blessed home for cross-process event contracts between the api and ADDED workers — the walking skeleton consumes in-process; a worker consuming an api event has nowhere idiomatic to share the record | OPEN — decide the idiom (shared contracts project per app? source-linked file?) before slice 2 leans on the payments worker |
| G6 | `goldpath db add` (slice 1) | The verb runs for EVERY owner, so owners with no model change get EMPTY migrations (noise per api-entity add; the eod worker's copy even failed the CHARSET format gate) | OPEN — skip owners whose model diff is empty (`ef migrations has-pending-model-changes` is already used by `db status`) |

Format: new rows land as they are found; OPEN rows become issues when their slice
closes; FIXED rows name the PR that carried them.
