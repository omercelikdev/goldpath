# Goldpath admin API — the frozen contract (H8)

Status: **FROZEN** (2026-07-12). This document is the UI phase's input: the single run
console is written ONCE against this surface. Any change below this line is a breaking
change and needs this document updated in the same PR (and, after NuGet release, a
versioning note per H7).

## Conventions (all five surfaces)

- **Mount**: `Map<Module>Admin<TContext>(prefix, exposeUnsecured)` on the management head;
  default prefix `/goldpath/admin/<module>`. Fail-closed: every endpoint demands
  `GoldpathPolicies.Ops` unless `exposeUnsecured: true` (visible opt-out + startup warning).
- **Verbs are POSTs with kebab-case names** (`trigger`, `replay-items`, `lift-hold`,
  `pause-all`). No PATCH; `PUT`/`DELETE` only for true upserts/removals (calendars).
- **Verb envelope**: every mutating verb returns `GoldpathAdminResult { ok, message }` —
  `200` when `ok`, `400` with the same envelope when refused (the message is the reason;
  never a silent 200). Unexpected exceptions stay with the platform's problem+json.
  Missing entities on GET answer `404` with no body.
- **Actor**: taken from the authenticated identity (`anonymous` fallback); every mutating
  verb writes an audit row server-side — the UI never supplies the actor.
- **Paging**: list endpoints take `?take=` (defaults 50–200 per endpoint) and clamp to
  `[1, 500]` (`AdminPaging.Clamp`, compile-linked into all five modules). Large ordered
  sets offer keyset paging (`afterRow` on the bulk validation report).
- **Failure nouns**: execution failures answer to **`failures`** everywhere
  (campaign `/{id}/failures`, notification `/failures`; bulk execution failures live in
  the JOBS repair queue via the run detail). Bulk's `/batches/{id}/errors` is the
  **validation report** — a different concept, deliberately a different noun.
- **Audit reads are a capability, not a baseline**: jobs exposes a global `/audit`,
  campaign a per-entity `/{id}/audit`; the other modules' evidence lives in their domain
  rows (decisions on the batch, erasure receipts). The console shows an audit tab only
  where the capability exists.

## Route inventory (frozen)

### jobs — `/goldpath/admin/jobs`
| Method | Route | Returns |
|---|---|---|
| GET | `/fleets` | fleet list |
| GET | `/fleets/{fleet}/jobs` | `GoldpathJobInfo[]` |
| GET | `/fleets/{fleet}/runs?job=&take=` | run list |
| GET | `/runs/{runId}` | `GoldpathRunDetail` (chunks by status + open failures) |
| POST | `/fleets/{fleet}/jobs/{job}/trigger` · `/pause` · `/resume` · `/reschedule` | `GoldpathAdminResult` |
| POST | `/fleets/{fleet}/pause-all` · `/resume-all` | `GoldpathAdminResult` |
| POST | `/runs/{runId}/rerun` · `/replay-items` | `GoldpathAdminResult` |
| GET/PUT/DELETE | `/fleets/{fleet}/calendars[/{name}]` | calendar CRUD |
| GET | `/audit?take=` | admin audit trail |

### archival — `/goldpath/admin/archival`
| Method | Route | Returns |
|---|---|---|
| GET | `/definitions` | definition list |
| GET | `/entries/{definition}/{key}` | entry detail (natural key) |
| POST | `/entries/{definition}/{key}/hold` · `/lift-hold` · `/erase` | `GoldpathAdminResult` |
| GET | `/holds?includeLifted=&take=` · `/erasures?take=` | evidence lists |
| POST | `/definitions/{definition}/verify` | `GoldpathAdminResult` |

### bulk — `/goldpath/admin/bulk`
| Method | Route | Returns |
|---|---|---|
| GET | `/definitions` | intake numbers per definition |
| POST | `/batches/{definition}` (body = file) | `GoldpathBulkBatchInfo` |
| GET | `/batches?definition=&state=&take=` · `/batches/{batchId}` | batch list / detail |
| GET | `/batches/{batchId}/errors?afterRow=&take=` | VALIDATION report (keyset) |
| POST | `/batches/{batchId}/approve` · `/reject` | `GoldpathAdminResult` |

### notification — `/goldpath/admin/notification` (read-only)
| Method | Route | Returns |
|---|---|---|
| GET | `/templates` | registered templates |
| GET | `/notifications?state=&template=&tenant=&take=` · `/notifications/{id}` | evidence list / detail |
| GET | `/suppressions?take=` · `/failures?take=` | filtered evidence views |

### campaign — `/goldpath/admin/campaign`
| Method | Route | Returns |
|---|---|---|
| GET | `/?state=&take=` · `/{id}` | campaign list / detail |
| GET | `/{id}/failures?take=` | failed items (item replay stays with jobs `replay-items`) |
| GET | `/{id}/audit?take=` | per-campaign audit |
| POST | `/` (create) · `/{id}/pause` · `/resume` · `/abort` · `/throttle` | `GoldpathAdminResult` |

### approvals — `/goldpath/admin/approvals` (T21 console federation, 2026-08-28)
| Method | Route | Returns |
|---|---|---|
| GET | `/requests?status=&ladder=&take=` · `/requests/{id}` | recent requests (R3 repeats) / detail with trail + signatures |
| POST | `/requests/{id}/approve` · `/reject` (body: role, reason) | `GoldpathAdminResult` — a refusal's message is the ENGINE rule's name (`FourEyesViolation`, `ReasonRequired`, …) |

Decisions run the engine unchanged: the caller's principal is the decider, so four-eyes,
distinct-eyes, the rung's role and the mandatory rejection reason all hold on this surface
exactly as anywhere else. The store is not tenant-scoped (the approvals model predates R1's
scoping and carries no tenant column), so no `?tenant=` parameter exists to lie with.

## Freeze mechanics

- The campaign surface carries a route-freeze test (`RouteContractTests`) — the pattern
  any surface adopts when the UI starts depending on it.
- Breaking fixes applied at freeze time (cheap now, expensive after the UI):
  campaign `/{id}/failed-items` → `/{id}/failures`; `take` clamp made uniform across all
  five modules (was jobs/archival-only).

## Revision R1 — tenant scoping (ACCEPTED 2026-07-24, lands at the preview.3 boundary)

**Finding (independent audit, A1 — CRITICAL):** the data layer is fail-closed, the admin
surfaces are not. No admin endpoint reads `ITenantContext`; where tenant filtering exists
at all (bulk, archival, notification) it is a CLIENT-SUPPLIED `?tenant=` query parameter,
and campaign has none. In a deployment with per-tenant operators, `goldpath-ops` alone
lets any operator read every tenant's batches, evidence and archives.

### The rule (all five surfaces, uniform)

1. **Ambient tenant is the scope.** When `multiTenancy` is on, every admin read and verb
   is scoped to the ambient `ITenantContext` — resolved exactly like the business
   endpoints (header/subdomain per the manifest). No ambient tenant on a multi-tenant
   app → `400` with a teaching envelope (fail-closed, never "all tenants").
2. **Cross-tenant is a second, explicit privilege.** The `?tenant=` override (and the
   implicit "all tenants" view) demands `GoldpathPolicies.OpsAllTenants`
   (`goldpath-ops-all` role) ON TOP of `GoldpathPolicies.Ops`. Without it: `403`.
   With it, `?tenant=` narrows and omitting it means "all" — today's semantics, now
   privilege-gated — and every crossing is logged with the actor, the requested tenant
   and the ambient one (structured warning on `Goldpath.AdminSurface`).
3. **Single-tenant apps are untouched.** `multiTenancy: false` keeps today's behavior
   byte-for-byte; the scoping layer compiles to a pass-through. The signal is the marker
   `AddGoldpathMultiTenancy` registers (`GoldpathMultiTenancyMarker`) — NOT the presence
   of an `ITenantContext`, which other modules register for their own flow (messaging
   propagates the tenant of a consumed message). Amended 2026-07-27 after the console
   gate caught a single-tenant app whose admin surfaces began refusing the moment a
   broker joined the composition.
4. **One shared seam.** The scoping lives in ONE shared-source file
   (`packages/shared/AdminTenantScope.cs`, compile-linked like the auth floor) that the
   endpoints call — not per-module reimplementations. The ADR-0005 companion analyzer
   rule (`GP0904`: an admin endpoint taking a tenant parameter without the seam — 0902 was taken) follows
   in the same preview.3 train.
5. **Surfaces without tenant-stamped rows (campaign)** are inherently cross-tenant on a
   multi-tenant app: the WHOLE surface demands `GoldpathPolicies.OpsAllTenants` (an
   endpoint filter on the group). Single-tenant apps see no change.
6. **The same logic per-endpoint where a single surface mixes both kinds** (review-agent
   findings on the R1 PR, all accepted): archival's hold/lift-hold/erase verbs and its
   holds/erasures lists operate on rows keyed WITHOUT a tenant column — on a multi-tenant
   app they demand the privilege outright; bulk's `/errors` scopes through its batch's
   tenant (a foreign batch id answers like an absent one); notification's
   `/suppressions` + `/failures` scope like its other lists.

### Why this is a contract REVISION, not a break-and-hope

Routes, nouns, envelopes and paging are unchanged. What changes is authorization
semantics (`?tenant=` becomes privilege-gated) and default scope (ambient, not "all") —
a behavioral break permitted at a preview boundary per the H7 versioning promise, shipped
with an upgrade-guide entry. The UI phase (U2+) is written against THIS revision.

## Revision R2 — the scheduling surface (PROPOSED 2026-07-28)

**Finding.** The console covers what the five modules *do*, but only half of what the
fleet *is*. An operator can trigger a job and read its runs; they cannot see why a job
will fire at 03:00, which node ran the last one, whether a run was scheduled or triggered
by hand, or what a trigger's misfire policy is — and they cannot answer "show me
yesterday's failures" without walking a take-bounded list. A comparable Quartz management
screen carries all of it, and an adopter who has seen one will read the gaps as missing
capability rather than deliberate scope.

The gap splits three ways, and only the middle one is a contract change.

### 1. Already frozen, never put on screen (no contract change — U5 work)

`pause-all` / `resume-all`, `reschedule`, the calendar CRUD, and the global `/audit` are
all in the inventory above and none of them has a screen. `pause-all` is the one that
matters at 03:00: it is the single verb an operator reaches for during an incident, and
today the console cannot reach it. Booked as **open-threads T13**; the screens land in
U5 with the routes below.

### 2. Facts the store holds and the contract does not carry (ADDITIVE — this revision)

Additive only: no route is renamed, no envelope changes shape, and every field below is
*added* to a payload the console already reads. A client written against R1 keeps working.

| # | Addition | Why an operator needs it |
|---|---|---|
| R2.1 | `GET /fleets/{fleet}/status` → scheduler state: `runningSince`, `threadPoolSize`, `jobsExecuted`, `isShutdown`, plus the `nodes` already on `GoldpathFleetInfo` | "Is this fleet alive, and how big is it?" is the first question of an incident, and today it is answered by inference from whether runs appear |
| R2.2 | `GoldpathTriggerInfo` widens: `type` (cron\|simple), `priority`, `misfireInstruction`, `timeZoneId`, `startAt`, `endAt`, `timesTriggered`, `repeatInterval`, `repeatCount` | A cron string alone does not explain a fire time. Timezone and misfire policy are the two fields that make a "why did it not run?" answerable |
| R2.3 | `GoldpathJobRun` gains `triggeredBy` (`Scheduled`\|`Manual`\|`Rerun`\|`Replay`) | Who started this run. **Cost: a migration** (one nullable column) — the only schema change in R2. *Amended during implementation on both counts: the instance that ran it was already there as `StartedBy` and only needed showing, and `Replay` earned its own value — labelling a repair-queue redrive as a plain `Rerun` would have been the sort of small lie this column exists to prevent.* |
| R2.4 | `GET /fleets/{fleet}/runs` gains `?status=`, `?from=`, `?to=`, and keyset `?afterId=` | "Yesterday's failures" must not be a scroll. Keyset follows the bulk validation report's precedent (`afterRow`), not offset paging |
| R2.5 | `POST /fleets/{fleet}/jobs/{job}/triggers` (add) · `DELETE /fleets/{fleet}/jobs/{job}/triggers/{trigger}` (remove) | A declared job may legitimately need a second schedule (month-end as well as nightly). `reschedule` stays the frozen shorthand for the 90% case: changing the one cron a job has |
| R2.6 | Job detail exposes its **job data map, read-only** | Diagnosis needs to see the parameters a run was given. Editing them is drift (see §3) |

Derived facts stay derived: a job's health rollup ("healthy / failing / mixed") and its
"last run 3 minutes ago" are computed by the console from runs it already reads. The
contract gains no aggregate endpoint it would then have to keep true.

### 3. Deliberately REFUSED — runtime authoring (the constitution decides this)

A comparable screen lets an operator pick a job class from a server-provided list, fill a
data map, and create a job; and delete one. **Goldpath will not**, at any layer:

- **Creating or deleting a JOB is a manifest change** (ADR-0001: the manifest is the
  single source of truth; composition is compile-time). A job created at runtime is a
  production behaviour that exists in no manifest, no review and no repository — the exact
  drift the constitution exists to prevent. There is no `available-classes` endpoint,
  because reflecting the assembly's job types into a picker is the first half of that
  feature.
- **Changing a job's data map at runtime is the same drift, quietly**: the job would then
  behave differently from the code that declares it. Read-only, per R2.6.
- **SCHEDULING is not authoring, and stays open**: triggers, calendars, pause/resume and
  reschedule are configuration about *when* a declared job runs. They are already audited
  server-side (iron rule 2), they survive restart in the Quartz store, and an operator
  who cannot move a run out of a maintenance window will move it by editing the database.

Owner decision, 2026-07-28: adopt this three-way split as written.

### Rejected, with the reason recorded

- **Quartz `standby`/`start` at the scheduler level.** It looks like the natural "stop
  everything" button and is a footgun in a cluster: standby applies to the node that
  received the call, so an operator who "stopped the fleet" has stopped one instance while
  the others keep firing — and it is lost on restart. `pause-all` is durable, cluster-wide
  and already frozen. One way to stop a fleet, and it is the one that works.
- **Offset paging on runs.** Ordered sets that grow while being read are exactly where
  offset paging skips and duplicates rows; keyset is the house rule.

### Test plan (the DoD for the implementation step)

1. **Unit**: the widened DTO carries every Quartz fact for both a cron and a simple
   trigger; `triggeredBy` is stamped `Manual` by the trigger verb, `Rerun` by rerun,
   `Replay` by the repair redrive and `Scheduled` by the scheduler path.
   *(The run list moved to integration: it orders by `DateTimeOffset`, which SQLite cannot
   translate at all — proving it on the package's own sqlite fixture would have proven it
   on a store Goldpath does not ship on.)*
2. **Integration (real Postgres + real Quartz)**: two-instance fleet — a run stamped with
   the instance that executed it; `?from`/`?to`/`?status` against a seeded window;
   `afterId` walks the whole set with no gap and no repeat; a second trigger added to a
   declared job fires and is removed again; the migration applies to a database written by
   the previous train.
3. **Refusal proofs**: there is no route that creates a job (the absence is asserted, so
   nobody adds one quietly); the scheduling verbs stay behind the ops floor and write
   audit rows; on a multi-tenant app they obey R1's scoping.
4. **Console (U5)**: every route above is driven in `scripts/console-smoke.sh` against a
   real fleet, and the axe gate stays green.

### Why this is a revision, not a break

Additive fields on existing payloads, new routes alongside the frozen ones, and one
migration that adds two nullable columns. R1 clients keep working unchanged. Shipped in
preview.5 (the scheduling train), with an upgrade-guide entry for the migration.

## Revision R3 (2026-08-02) — repeatable filters

Additive; nothing an R2 client sends changes meaning.

- The list filters `?status=` (jobs runs), `?state=` (bulk batches, campaigns,
  notifications), `?template=` (notifications) and `?definition=` (bulk batches —
  joined 2026-08-03 with T8/#72, before the revision ships) may now REPEAT:
  `?state=Failed&state=Suppressed`. Several values of one filter are OR'd; different
  filters still AND together. A single value behaves exactly as R2 did; an absent
  filter still means "all". Unknown values are ignored rather than refused — a
  filter that matches nothing returns an empty list, the same claim R2 made.
- Motivation: the console's facet filters are multi-select (ui-standard v1.2 §8.13),
  and the console refuses to fake OR by merging take-bounded pages client-side —
  the server is the only honest place to widen a filter.
