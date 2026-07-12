# Module RFC: Goldpath.MultiTenancy

> Status: v1.0 accepted — D1–D6 approved by Ömer (2026-07-05) WITH THE EXPLICIT CONDITION
> that deferrals are strategic backlog items with defined triggers, never silent gaps:
> path-prefix resolution and db-per-tenant isolation are tracked in the module plan's open
> ledger and get built when the first project demands them. Ring B (Phase 2, item 13) · Effort M
> (down from L: the messaging seam already propagates tenants — Goldpath.Messaging publishes
> `X-Goldpath-Tenant` and restores `ITenantContext` on consume since Phase 1)
> Dependencies: Goldpath.Abstractions (TenantId, ITenantContext, IMultiTenant, GoldpathHeaders —
> all shipped), Goldpath.Data (save-contributor seam + model conventions)

## 1. Scope / Non-Goals

**Scope — the tenant becomes ambient truth on every seam:**

- **Resolution (HTTP middleware):** header `X-Goldpath-Tenant` (default; gateway-friendly) or
  subdomain (`acme.api.bank.com`) per manifest. Resolved once per request into a scoped
  `ITenantContext` — every module that already consumes it (Data stamps, AuditTrail rows,
  cache keys, message headers) lights up without changes. **Fail-closed:** unresolvable
  tenant → 400 (D2), with an exempt-path list (health/openapi/metrics).
- **EF isolation (shared-db):** `ApplyGoldpathMultiTenancy()` puts a global query filter on every
  `IMultiTenant` entity (rows of other tenants do not exist for queries); a save contributor
  stamps `TenantId` on Added entities; a **write guard** turns any save touching another
  tenant's row into an exception (cross-tenant write is a security event, not a bug to log).
- **Explicit escape hatches (SoftDelete's proven pattern):**
  `GoldpathTenant.Bypass()` — filter-free scope for admin/reporting flows (greppable, reviewable);
  `GoldpathTenant.Use("acme")` — pins the ambient tenant for background jobs and seeding.
- **Message flows:** ALREADY DONE in Goldpath.Messaging (publish filter sets `X-Goldpath-Tenant`,
  consume filter restores the scoped context). This module only adds the interplay proof:
  HTTP tenant → outbox → broker → consumer → stamped entity, end to end on real containers.

**Non-Goals (v1, written not silent):** db-per-tenant isolation (D4 — schema keeps the enum
value as reserved; connection-per-tenant + migration orchestration is its own module),
path-prefix resolution (D1 — touches routing; deferred until a project needs it), tenant
onboarding/provisioning workflows (product concern), per-tenant configuration/feature flags
(a later module), cross-tenant reporting products (use `Bypass()` + your own authorization).

## 2. Seam Map (all four — the constitution's full square)

| Seam | Touch |
|---|---|
| HTTP middleware | resolution (header/subdomain) → scoped `ITenantContext`; strict 400; exempt paths |
| EF interceptor | global filter + Added-stamp contributor + cross-tenant write guard |
| Mediant pipeline | nothing new — handlers see the scoped `ITenantContext` DI-natively |
| MassTransit filter | shipped in Phase 1 (Goldpath.Messaging); interplay proven here |

## 3. Manifest Surface
```yaml
multiTenancy:
  strategy: header          # header | subdomain (path-prefix reserved, D1)
  isolation: shared-db      # db-per-tenant reserved (D4)
  strict: true              # unresolvable tenant → 400 (D2)
  exemptPaths: ["/health", "/alive", "/openapi"]
```

## 4. API Surface
```csharp
builder.AddGoldpathMultiTenancy();                  // resolution + contributors + guard
app.UseGoldpathMultiTenancy();                      // middleware (before auth-dependent stages)

// OnModelCreating:
modelBuilder.ApplyGoldpathMultiTenancy(this);       // filter on every IMultiTenant entity (context-rooted: EF only re-evaluates filter state per query when rooted at the context instance)

public class Loan : IMultiTenant { public TenantId TenantId { get; set; } }  // stamped on add

using (GoldpathTenant.Bypass())   { /* admin/reporting: filter off, writes still guarded */ }
using (GoldpathTenant.Use("acme")) { /* background job runs AS acme */ }
```

## 5. Analyzer Rules (SHIPPED — analyzer batch 3, 2026-07-06)
| ID | Rule | Severity |
|---|---|---|
| GP0901 | `IMultiTenant` entity, DbContext present, no `ApplyGoldpathMultiTenancy()` call (0501/0601 pattern) | error |
| GP0902 | Manual write to `TenantId` on an `IMultiTenant` entity outside contributors (0502 pattern) | warn |
| GP0903 | `IgnoreQueryFilters()` on a query over an `IMultiTenant` entity without a `Bypass()` scope in the enclosing method | warn |

## 6. Ops Package ("no runbook = no module")
- **Metrics:** requests per tenant (dimension on Ring A HTTP metrics), `goldpath_tenant_unresolved_total`
  (spike = client misconfiguration or probing), `goldpath_tenant_write_guard_trips_total`
  (**any** non-zero value is a security signal — alert immediately)
- **Runbook:** "tenant sees no data" triage (resolution → filter → stamp order); guard-trip
  investigation procedure; `Bypass()` usage audit (grep + audit rows carry the acting user)
- **Dashboard:** per-tenant traffic/error split; unresolved-tenant rate

## 7. Test Plan
- Resolution: header hit/miss, subdomain parse, strict 400, exempt path passes, malformed
  tenant id (TenantId.TryCreate rules) → 400 not 500
- EF isolation (SQLite): two tenants — queries see only their rows; Added rows stamped with
  the ambient tenant; update/delete of the other tenant's row → guard exception;
  `Bypass()` sees both, and only inside the scope; `Use()` pins for background flows
- Interplay: AuditTrail rows carry the tenant; GoldpathCacheKeys already tenant-scoped (smoke)
- End-to-end (Testcontainers Postgres+RabbitMQ): HTTP tenant → outbox publish → consume →
  handler writes an IMultiTenant entity → stamped with the ORIGIN tenant (the full square)
- License gate GREEN (no new dependencies at all)

## 8. DoD
- [x] Decisions locked (D1–D6) · package + tests green (15: header/subdomain resolution with
      strict-400/exempt/malformed/no-leak, stamp + fail-closed isolation, Bypass scope
      boundaries, guard on foreign-write/bypass-write/orphan-add with transaction abort,
      SoftDelete+tenant filter composition — plus THE FULL SQUARE on real Postgres+RabbitMQ:
      HTTP header → middleware → EF stamp → outbox → broker → consume restore → consumer
      stamp, origin-tenant-owned end to end) · PublicAPI locked
- [x] README (4 sections) + ops runbook + CHANGELOG · GP0901/0902/0903 to backlog
- [x] GM wiring tracked · deferrals recorded per the approval condition: the manifest schema
      REJECTS path-prefix/db-per-tenant until implemented (no silent gaps); both tracked in
      the module plan with their trigger (first project demand)
- Implementation notes (recorded, not silent): `ApplyGoldpathMultiTenancy(this)` takes the context
  because EF constant-folds closure/static state in query filters into the cached plan
  (verified empirically on EF 8 AND EF 10 — a static-rooted filter froze Bypass/Use at
  first-seen values); context-rooted member chains are re-parameterized per execution.
  Goldpath.Data gained `GoldpathQueryFilters` (AND-composition — SetQueryFilter REPLACES, so SoftDelete
  and MultiTenancy filters would silently erase each other; SoftDelete migrated to it).
  Goldpath.Messaging's consume filter now also restores the AMBIENT tenant (GoldpathAmbientTenant),
  so consumer-side EF filters/guards see the origin tenant.

## 9. Decision Points (Ömer)

- **D1 — Resolution v1 = header + subdomain; path-prefix deferred.** Header is the standard
  behind a gateway (and what Goldpath.Messaging already speaks); subdomain is cheap to add.
  Path-prefix rewrites routes — real cost, no current demand. **Recommendation: defer path-prefix.**
- **D2 — Strict by default (fail-closed):** unresolvable tenant → 400, exempt list for
  infra endpoints. Fail-open (null tenant → unfiltered queries!) is exactly the silent
  cross-tenant leak this module exists to kill. Single-tenant deployments simply don't
  enable the module. **Recommendation: strict.**
- **D3 — Write guard is non-negotiable and exception-throwing** (not a log line): a save
  touching another tenant's row aborts the transaction. Guard stays active even inside
  `Bypass()` — bypass widens READS for reporting; cross-tenant WRITES need `Use(tenant)`,
  which makes the acting tenant explicit. **Recommendation: yes, guard always on.**
- **D4 — db-per-tenant deferred** (schema value reserved): connection factory + migration
  orchestration + per-tenant ops is a Ring C module of its own; shared-db-with-filter is the
  right default for the target segment's scale. **Recommendation: defer.**
- **D5 — Ambient scopes via AsyncLocal (`GoldpathTenant.Bypass`/`Use`)** — same mechanics as
  `GoldpathSoftDelete.Suppress()`, already proven across scopes and contexts. `Use()` composes
  with the scoped `ITenantContext` (ambient wins when set — background flows have no HTTP scope).
  **Recommendation: yes.**
- **D6 — Effort re-rated L→M** because the messaging seam shipped in Phase 1. The end-to-end
  container proof (the full square) stays IN scope — that's the claim that sells the module.
  **Recommendation: accept M with the e2e proof kept.**
