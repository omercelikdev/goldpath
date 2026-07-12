# CLAUDE.md — GoldpathWorker

Goldpath worker (`kind: worker`). The manifest (`.goldpath/manifest.yaml`) is the single source of
truth; the trigger shape is compile-time composition — a schedule worker contains no
messaging code at all.

## Rules
- Constitution and rationale: the Goldpath repo (`docs/adr`).
- Queue workers: broker-bound contracts implement `IIntegrationEvent` (GP0401/0402);
  consumers are inbox-guarded — processing is exactly-once, write handlers idempotently anyway.
- Schedule workers: the tick body (`IntervalJob.RunTickAsync`) is the unit — keep the
  timer loop free of business logic; upgrade to `--trigger jobs` when you need clustering,
  checkpoints or the admin verbs.
- Jobs workers (Goldpath.Jobs): author chunk-shaped jobs (`IGoldpathJob`) — plan by COUNT, execute a
  chunk per call; the runner checkpoints after every chunk, so kills RESUME. The fleet's
  audited ops surface lives at `/goldpath/admin/jobs` (trigger/pause/reschedule/replay).
- Entities use `DateTimeOffset` (UTC policy); schema changes go through migrations
  (Development auto-creates; production applies the CI bundle).
- The deterministic engine is registered in `.mcp.json` (`specdrift mcp`); "done" without a
  clean `spec_validate` + `spec_drift` is not done.

## Run
`dotnet run --project src/GoldpathWorker.AppHost` → containers start, dashboard opens.
`dotnet test` → smoke: a published message is processed exactly once (queue) / the
interval job ticks against the real host (schedule) / the nightly job runs end to end
through the audited admin verbs (jobs).
