# Mutation gates — what is scored, what is excluded, and why

One config per package (`Goldpath.<Package>.json`), all at the same thresholds: high 85,
low 75, **break 70** — `scripts/mutation-gate.sh <Package>` runs one, the nightly matrix
runs the hosted-fit set, `mutation-heavy.yml` the six long-running ones. Stryker JSON
cannot carry comments, so every `mutate` exclusion and every `ignore-methods` entry is
justified HERE (ADR-0005: suppression is visible and justified at every layer). An
exclusion without a row below is a finding.

## Excluded files

| Package | Excluded file | Why it is not scored |
|---|---|---|
| Jobs | `GoldpathJobsExtensions.cs` | DI composition — registrations and option binding; behavior lives in the registered types, which ARE scored. |
| Jobs | `QuartzStoreModel.cs` | EF model mapping for Quartz's own tables — declarative; a mutated column name fails the migration proofs, not a unit test. |
| Jobs | `GoldpathQuartzAdapter.cs` | The Quartz adapter — exercised only with a live scheduler (the integration suite and the console smoke), which Stryker's unit loop does not host. |
| Jobs | `GoldpathJobHistoryListener.cs` | Quartz listener callbacks — same live-scheduler dependency. |
| Jobs | `GoldpathJobsFleetRegistry.cs` | Cluster-member registry over Quartz metadata — live-scheduler dependency. |
| Jobs | `GoldpathJobsAdminEndpoints.cs` | Minimal-API route table over the admin service; the contract is pinned by the route-freeze test and the console smoke. |
| Jobs | `GoldpathJobsAdminService.cs` | The admin verbs (trigger/pause/reschedule/calendars/rerun/replay) run through the LIVE scheduler and the fleet registry — the same dependency that excludes the adapter and the registry. MEASURED 2026-09-03: scoring it drops the package below the break (the unit loop cannot host the scheduler, so its 738 lines survive by construction). It is proven where the scheduler exists: `JobsClusterTests`, `JobsRunListTests`, `BulkClusterTests` and the console smoke's 29 journeys drive every verb against real Postgres + Quartz. |
| FileExchange | `GoldpathFileExchangeMetrics.cs` | Meter declarations (see Campaign/Notification); joined 2026-09-03 with the admin surface. |
| Campaign | `GoldpathCampaignExtensions.cs` | DI composition (see Jobs). |
| Campaign | `GoldpathCampaignMetrics.cs` | Meter/counter declarations — names are pinned by the dashboard's queries, not by mutants. |
| Campaign | `GoldpathCampaignConsumers.cs` | MassTransit consumers — exercised against a real broker in the integration suite. |
| Campaign | `GoldpathCampaignAdminEndpoints.cs` | Route table (see Jobs). |
| Notification | `GoldpathNotificationExtensions.cs` | DI composition. |
| Notification | `GoldpathNotificationMetrics.cs` | Meter declarations. |
| Notification | `GoldpathNotificationChannels.cs` | SMTP/channel transports — exercised against a real Mailpit in the integration suite. |
| Notification | `GoldpathNotificationAdminEndpoints.cs` | Route table. |

Everything else in a scored package is mutated — including the admin services of the
modules whose engines are in-process (Approvals, FileExchange, Notification's reads); the
Jobs admin service is the exception above, and the reason is measured, not assumed.

## Ignored methods

| Package | Method pattern | Why |
|---|---|---|
| all | `Log*` | Logging calls carry no behavior a test can observe; mutating them only manufactures survivors. |
| Locking | `CreateRedisProvider` | Builds the Redis lock provider — needs a live Redis, which the unit loop does not host; the Redis path is proven by the integration suite. |

## Packages without a gate

`ApiDefaults`, `Locking.SqlServer`, `Sdk` — composition and adapter shells with little
branching logic; each has a unit suite. A gate joins the moment one of them grows an engine
path (the 2026-09-01 audit's rule). `Console`, `Messaging` and `ServiceDefaults` gained
gates on 2026-09-03 (the thin-suites PR): each carries real branching (asset resolution,
the publish/consume filters, correlation + concurrency guard) that a unit suite can score.
