# Goldpath.Caching

Ring B caching: Microsoft `HybridCache` as the app surface (L1+L2, stampede-protected, tag
invalidation) and Mediant `[Cacheable]`/`[InvalidatesCache]` as the query surface — composed,
not wrapped. Goldpath adds the manifest-driven tier wiring and the tenant-scoped key convention.

## Getting started

```csharp
builder.AddGoldpathCaching();                 // L1 + Redis L2 (ConnectionStrings:redis)

// App surface — inject Microsoft's own type, no Goldpath wrapper:
public sealed class RateService(HybridCache cache, GoldpathCacheKeys keys)
{
    public async ValueTask<Rates> GetAsync(CancellationToken ct) =>
        await cache.GetOrCreateAsync(keys.For("rates", "current"),
            _ => FetchAsync(ct), tags: ["rates"], cancellationToken: ct);
}

// Query surface — Mediant's own attributes:
[Cacheable(300, CacheKeyPrefix = "rates")]
public sealed record GetRatesQuery : IQuery<Rates>;
```

## Configuration

```jsonc
"Goldpath": {
  "DistributedCaching": {
    "Levels": ["l1", "l2"],          // ["l1"] = in-process only, no Redis dependency
    "RedisConnectionName": "redis",  // resolved from ConnectionStrings
    "DefaultTtlSeconds": 300
  }
}
```

Manifest: `features.distributedCaching: { levels, redis.connectionName, defaultTtl }`.
In `["l1"]` mode both surfaces stay fully in-process (memory distributed cache backs the
query path) — unit tests and single-instance deployments need no Redis.

## Advanced

- **Key convention (`GoldpathCacheKeys`):** `goldpath:{tenant}:{area}:{key}` — tenant scoping is baked
  in BEFORE the MultiTenancy module lands, because retrofitting a key format means
  invalidating production caches, and cross-tenant cache bleed is a security bug. Inject
  `GoldpathCacheKeys` for ambient-tenant keys; `GoldpathCacheKeys.Compose(tenant, area, key)` for
  background/message flows.
- **Invalidation vocabulary:** one `area` name across both surfaces — HybridCache **tags**
  on the app surface (`RemoveByTagAsync("rates")` works TODAY), `CacheKeyPrefix` on the
  query surface.
- **The composition loop, closed:** the gaps this module filed upstream shipped in Mediant
  1.2.0 and are composed here — the query path rides the SAME HybridCache (L1+L2 on
  `[Cacheable]` hits, [mediant#130](https://github.com/omercelikdev/mediant/issues/130))
  and `[InvalidatesCache]` really invalidates, tag-based
  ([mediant#131](https://github.com/omercelikdev/mediant/issues/131)). The pinned no-op
  test broke on the version bump exactly as designed and now asserts the real semantics.
- **Stampede:** both surfaces protected (HybridCache built-in; Mediant lock pool).

## Providers

L2 = Redis (StackExchange; wire-compatible with Valkey/Garnet). Other L2 stores deferred
until a project needs one (RFC D4) — YAGNI over a provider zoo.
