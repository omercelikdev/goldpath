# Data — Ops Runbook

The floor's persistence: EF Core on the app's own DbContext with the Goldpath conventions
(UTC `DateTimeOffset`, model defaults), keyset pagination (`ToPageAsync`), and the migration
discipline (Development walks the SAME migrations production applies from the CI bundle —
migrations RFC D2; `EnsureCreated` is analyzer-flagged, GP0302).

## Pending-migration detection
- A host that starts against a database BEHIND its model fails loudly on the first query
  that touches the missing column — never silently. Before that: `goldpath db status`
  (the CLI verb) lists pending migrations against the app's connection; run it in the
  deploy pipeline as a gate.
- `__EFMigrationsHistory` is the truth: `SELECT "MigrationId" FROM "__EFMigrationsHistory"
  ORDER BY 1 DESC LIMIT 3;` — compare with the newest file under `Migrations/`.
- Two replicas migrating at once race into "relation already exists": the template
  serializes Development migrations behind a database lock (`pg_advisory_lock` /
  `sp_getapplock`). Production applies the bundle ONCE, from CI, never from the app.

## Keyset pagination troubleshooting ("rows are skipped / repeated")
`ToPageAsync` walks a cursor over an ORDER BY that must be total and stable:
1. The order-by must end in a UNIQUE key (the id); ordering by a non-unique column alone
   skips ties across pages.
2. The cursor encodes the LAST row's key values — a client that mutates the cursor gets
   `400` (cursor-invalid); a spike there is a client bug, not data loss.
3. `Skip/Take` is analyzer-flagged (GP0301): if a list drifts under concurrent inserts,
   someone bypassed the page helper.

## Connection-pool exhaustion
Symptoms: p95 climbs, then `TimeoutException` acquiring a connection. Causes in order of
likelihood: a DbContext captured in a singleton (a scope leak — the EF meter's
`active_dbcontexts` never returns to baseline), long transactions holding connections
(the outbox commits WITH the business row on purpose — keep handlers short), or a pool
sized below replica count × concurrency. Raise `Maximum Pool Size` only after the leak is
ruled out.

## Signals
EF Core's meter (`Microsoft.EntityFrameworkCore`: queries, saved changes, active contexts,
compiled-query cache hits/misses, execution-strategy failures, optimistic-concurrency
failures) reaches the collector through `Goldpath.ServiceDefaults` since 2026-09-03; the
Npgsql meter (`Npgsql`: connection pool usage, open/idle/busy) rides with it. SQL Server's
client exposes EventCounters, not a Meter — pool signals there come from the server
(`sys.dm_exec_connections`) until the client ships one.

## Dashboard
`grafana-data-dashboard.json` — query and save-changes rates, active contexts (the leak
line), compiled-query cache misses (a climbing line is dynamic LINQ defeating the cache),
concurrency and execution-strategy failures, Npgsql pool usage.
