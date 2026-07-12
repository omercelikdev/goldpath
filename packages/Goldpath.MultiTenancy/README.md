# Goldpath.MultiTenancy

Ring B multi-tenancy: the tenant becomes ambient truth on every seam. Resolved once per
request (fail-closed), stamped on writes, filtered on reads, guarded against cross-tenant
writes, and propagated through messages (the messaging seam ships in Goldpath.Messaging).

## Getting started

```csharp
builder.AddGoldpathMultiTenancy();
app.UseGoldpathMultiTenancy();                    // before anything tenant-dependent

// OnModelCreating — note the `this`, it is what keeps the filter live:
modelBuilder.ApplyGoldpathMultiTenancy(this);

public class Loan : IMultiTenant { public TenantId TenantId { get; set; } }
```

That's the whole programming model: entities marked `IMultiTenant` are stamped on add,
invisible to other tenants on read, and protected against cross-tenant writes — no tenant
parameter threading, ever.

## Configuration

```jsonc
"Goldpath": {
  "MultiTenancy": {
    "Strategy": "Header",                    // Header (X-Goldpath-Tenant) | Subdomain
    "Strict": true,                          // unresolvable tenant → 400 (fail-closed)
    "ExemptPaths": ["/health", "/alive", "/openapi"]
  }
}
```

## Advanced

- **Escape hatches are explicit scopes** (greppable, reviewable):
  ```csharp
  using (GoldpathTenant.Bypass())      { /* admin/reporting: reads widen, writes STAY guarded */ }
  using (GoldpathTenant.Use("acme"))   { /* background job/seeding writes AS acme */ }
  ```
- **The write guard throws** (`GoldpathCrossTenantWriteException`) and counts
  `goldpath_tenant_write_guard_trips_total` — a cross-tenant write is a security event; any
  non-zero value deserves an alert.
- **Fail-closed everywhere:** no ambient tenant → strict 400 on HTTP, zero rows on queries,
  exception on writes. Single-tenant deployments simply don't enable the module.
- **Filters compose:** an entity can be `IMultiTenant` + `ISoftDeletable`; both filters
  AND-combine regardless of `Apply…` call order (`GoldpathQueryFilters`).
- **Why `ApplyGoldpathMultiTenancy(this)`:** EF only re-evaluates filter state per query when it
  is rooted at the context instance — closure/static state gets constant-folded into the
  cached query plan, freezing `Bypass()`/`Use()` (verified empirically on EF 8 and EF 10).
- **The full square is proven on real infrastructure:** HTTP header → middleware → EF stamp
  → outbox → RabbitMQ → consume restore → consumer-side EF stamp, all owned by the origin
  tenant (`TenantSquareTests`).
- **Strategic deferrals** (tracked in the RFC + module plan, built on first project demand):
  path-prefix resolution, db-per-tenant isolation. The manifest schema deliberately rejects
  them until they exist — no silent gaps.

## Providers

Isolation v1 = shared-db with query filters. The tenant column converts via `TenantId` ↔
string (max 64), provider-neutral.
