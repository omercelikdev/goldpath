# Module RFC: Goldpath.Caching

> Status: v1.0 accepted & implemented — D1–D4 approved by Ömer (2026-07-05): no wrapper over
> HybridCache · Mediant query path composed with gaps filed as issues (#130 L1, #131
> invalidator), not worked around · tenant-scoped keys from day one · Redis-only L2.
> Ring B (Phase 2, item 12) · Effort S
> Dependencies: Goldpath.Abstractions (tenant context for key scoping), Mediant.Behaviors
> (composed: `[Cacheable]` + `[InvalidatesCache]` — ADR-0003),
> Microsoft.Extensions.Caching.Hybrid + StackExchange Redis (composed; MIT)

## 1. Scope / Non-Goals

**Scope — two cache surfaces, one key convention, one invalidation story:**

- **App surface = Microsoft `HybridCache`** (composed, not wrapped): L1 in-process + L2
  distributed, built-in stampede protection and tag-based invalidation. `AddGoldpathCaching()`
  wires it; the manifest decides whether L2 (Redis) exists. Application code injects
  `HybridCache` directly — no Goldpath wrapper interface (ADR-0003).
- **Query surface = Mediant `[Cacheable]`** (composed): the pipeline behavior with its
  stampede lock pool is READY in Mediant 1.1.0 over `IDistributedCache`. Goldpath supplies the
  `IDistributedCache` (Redis when L2 is on) and the key convention.
- **Key convention (the actual Goldpath value-add):** `goldpath:{tenant}:{area}:{key}` — tenant scoping
  baked in NOW so MultiTenancy (item 13) does not have to retrofit cache isolation later.
  Cross-tenant cache bleed is a security bug, not a performance bug.
- **Invalidation convention:** commands mark `[InvalidatesCache("area")]` (Mediant behavior
  does the work); app-surface entries carry HybridCache tags with the same `area` names —
  one vocabulary across both surfaces, documented per module README template.

**Non-Goals (v1, written not silent):** output caching / HTTP response caching (ASP.NET has
it; different concern), cache-aside code generation (Spec Engine later), distributed
invalidation pub/sub beyond what Redis/HybridCache provide, providers other than Redis (D4).

## 2. Seam Map

| Seam | Touch |
|---|---|
| Mediant pipeline | `[Cacheable]`/`[InvalidatesCache]` behaviors registered; store + key prefix supplied by Goldpath |
| HTTP middleware | none |
| EF interceptor | none in v1 — save-contributor-driven auto-invalidation deferred (recorded: needs entity→area mapping, arrives with Spec Engine manifest knowledge) |
| MassTransit filter | none |

## 3. Manifest Surface
```yaml
distributedCaching:
  levels: [l1, l2]          # [l1] = in-process only (HybridCache without Redis); l2 adds Redis
  redis: { connectionName: "redis" }   # Aspire/config connection name when l2 present
  defaultTtl: 300           # seconds; per-entry overrides in code
```

## 4. API Surface
```csharp
builder.AddGoldpathCaching();                      // HybridCache (+ Redis L2 per manifest) + Mediant wiring

// App surface — Microsoft's own type, no wrapper:
public sealed class RateService(HybridCache cache, GoldpathCacheKeys keys)
{
    public async ValueTask<Rates> GetAsync(CancellationToken ct) =>
        await cache.GetOrCreateAsync(keys.For("rates", "current"),
            _ => FetchAsync(ct), tags: ["rates"], cancellationToken: ct);
}

// Query surface — Mediant's own attributes, no wrapper:
[Cacheable(300, CacheKeyPrefix = "rates")]
public sealed record GetRatesQuery : IQuery<Rates>;

[InvalidatesCache("rates")]
public sealed record UpdateRatesCommand : ICommand<Unit>;

// The convention helper (the only new public API besides the builder extension):
keys.For("rates", "current");                 // injected GoldpathCacheKeys: "goldpath:{tenant}:rates:current"
GoldpathCacheKeys.Compose("t1", "rates", "current");  // explicit tenant (background/message flows)
```

## 5. Analyzer Rules (SHIPPED — analyzer batch 3, 2026-07-06)
| ID | Rule | Severity |
|---|---|---|
| GP0801 | `[Cacheable]` on a command (side-effecting request served from cache) | error |
| GP0802 | `[InvalidatesCache]` on a query (no-op) | info |
| GP0803 | Raw string cache key not built through `GoldpathCacheKeys` (tenant-scoping bypass) | warn |

## 6. Ops Package ("no runbook = no module")
- **Metrics:** HybridCache emits its own (hit ratio, latency) via MEL metrics — composed,
  flows into Ring A OTel. Dashboard panel: hit ratio per area, L2 latency, key count.
- **Alerts:** hit ratio collapse (mass invalidation or key-convention regression); L2 latency
  spike (Redis pressure).
- **Runbook:** safe flush procedure (area-tag invalidation, NEVER `FLUSHALL` on shared Redis);
  TTL tuning guidance; "why is this stale" triage (L1 TTL vs L2 TTL vs invalidation miss).

## 7. Test Plan
- Key convention: tenant present/absent, area+key composition, GP0803-friendly shape
- L1-only mode: `levels: [l1]` → no Redis dependency, HybridCache still serves + stampedes once
- Interplay (Testcontainers Redis): `[Cacheable]` query cached; `[InvalidatesCache]` command
  evicts; two concurrent misses → single upstream call (stampede, both surfaces)
- Tenant isolation: same key, two tenants → two entries; invalidation of one leaves the other
- License gate stays GREEN (HybridCache, StackExchange.Redis — MIT)

## 8. DoD
- [x] Decisions locked (D1–D4) · package + tests green (10: key convention with tenant
      isolation + loud empty-part failure, L1-only factory-once + 16-way stampede-once,
      Mediant [Cacheable] served-from-cache, real-Redis L2 cross-host read + tenant
      isolation on shared Redis via Testcontainers) · PublicAPI locked
- [x] README (4 sections) + ops runbook + CHANGELOG · GP0801/0802/0803 to backlog
- [x] Mediant issues filed: #130 (HybridCache-backed query store, D2) and #131
      ([InvalidatesCache] no-ops — no ICacheInvalidator ships in 1.1.0; found during
      implementation, PINNED by a test that fails on purpose when the fix lands) · GM wiring tracked
- Note — RESOLVED 2026-07-06: mediant#130 and #131 shipped in Mediant 1.2.0 and are
  composed (AddMediantHybridCaching: query path on the same HybridCache, L1+L2, tag-based
  invalidation). The pinned no-op test broke on the version bump exactly as designed and
  flipped to real invalidation semantics.

## 9. Decision Points (Ömer)

- **D1 — App surface is `HybridCache` itself, no Goldpath wrapper interface.** Consumers inject
  the Microsoft type; Goldpath only wires L1/L2 per manifest and ships the key helper. A wrapper
  would be exactly the framework-ism the constitution forbids. **Recommendation: no wrapper.**
- **D2 — Query path stays Mediant `[Cacheable]`; L1 for it comes from Mediant, not Goldpath.**
  Mediant's behavior is `IDistributedCache`-based today, so query-path hits go to L2 (Redis) —
  correct but misses the L1 win. Filing a Mediant issue ("optional HybridCache-backed
  CachingBehavior store") continues the issue→parallel-fix→compose loop; Goldpath composes it when
  it ships. Until then: honest note in README, L2-only on the query path.
  **Recommendation: file the issue, don't work around it in Goldpath.**
- **D3 — Tenant scoping is baked into the key convention NOW** (`goldpath:{tenant}:{area}:{key}`,
  tenant from `ITenantContext`, `_` when absent) even though MultiTenancy lands later —
  retrofit would mean invalidating every key format in production. GP0803 nudges raw keys
  toward the helper. **Recommendation: yes.**
- **D4 — L2 provider v1 = Redis only** (StackExchange, MIT; also Valkey/Garnet-compatible on
  the wire). Other L2s (SQL, Cosmos) deferred until a project actually needs one — YAGNI over
  provider zoo. **Recommendation: Redis only.**
