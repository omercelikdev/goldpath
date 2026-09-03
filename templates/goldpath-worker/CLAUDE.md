# CLAUDE.md — GoldpathWorker

Goldpath worker (`kind: worker`). The manifest (`.goldpath/manifest.yaml`) is the single source of
truth; the trigger shape AND every feature are compile-time composition — a schedule worker
contains no messaging code at all, a worker without `features.auditTrail` has no audit table.

## Rules
- Conventions: `.claude/conventions.md`. Constitution and rationale: the Goldpath repo (`docs/adr`).
- Queue workers: broker-bound contracts implement `IIntegrationEvent` (GP0401/0402);
  consumers are inbox-guarded — processing is exactly-once, write handlers idempotently anyway.
- Schedule workers: the tick body (`IntervalJob.RunTickAsync`) is the unit — keep the
  timer loop free of business logic; upgrade to `--trigger jobs` when you need clustering,
  checkpoints or the admin verbs.
- Jobs workers (Goldpath.Jobs): author chunk-shaped jobs (`IGoldpathJob`) — plan by COUNT, execute a
  chunk per call; the runner checkpoints after every chunk, so kills RESUME. The fleet's
  audited ops surface lives at `/goldpath/admin/jobs` (trigger/pause/reschedule/replay) and
  the console over it at `/goldpath/console/` — both behind the management head's auth
  floor (`providers.auth`), or carrying the VISIBLE `exposeUnsecured` opt-out when the
  floor is `none` (acceptable only behind an authenticating boundary).
- Features live behind the same manifest: multiTenancy, auditTrail, softDelete,
  dataProtection, distributedLocking, notification, fileExchange. The ones that own tables
  need the worker's database (queue or jobs trigger). Solution-shaped features (layout,
  idempotency, caching, archival, bulk, campaign, approvals) are not offered here on purpose.
- Entities use `DateTimeOffset` (UTC policy); schema changes go through migrations
  (Development auto-creates; production applies the CI bundle).
- The deterministic engine is registered in `.mcp.json` (`specdrift mcp`); "done" without a
  clean `spec_validate` + `spec_drift` is not done.

## Skills (agent workflows)
- `goldpath-manifest` — enable/disable capabilities; manifest + wiring change together, engine-checked.
- `breaker` agent — adversarial scenarios as executable tests against the worker's contracts
  (message contracts, admin verbs, idempotency); succeeds by finding failures.
The solution template's `goldpath-feature` and `goldpath-test-gen` skills are HTTP-slice and
OpenAPI-shaped; a worker has neither, so they are not shipped here.

## Guardrail hooks (`.claude/settings.json` — in-loop, unskippable)
- Post-edit: touched `.cs` files are whitespace-formatted automatically.
- Stop gate: the agent cannot end a turn with a red `dotnet build`; `specdrift drift`
  runs too and gates at WARN (`--fail-on warn`, install: `dotnet tool install -g specdrift`).
Hooks live in `.claude/hooks/` — delete `settings.json` to opt out (not recommended).

## Ops
`ops/grafana-worker-dashboard.json` is the day-one board: bus consume/faults, run progress,
checkpoint age, item failures, auth failures, tenancy guard, locks. Panels stay green-empty
until the matching feature is wired — an empty panel names a capability you have not enabled.

## Run
`dotnet run --project src/GoldpathWorker.AppHost` → containers start, dashboard opens.
`dotnet test` → smoke: a published message is processed exactly once (queue) / the
interval job ticks against the real host (schedule) / the nightly job runs end to end
through the audited admin verbs (jobs). Authed shapes prove probes green + the 401 floor
on the head, the admin surfaces and the console instead.
