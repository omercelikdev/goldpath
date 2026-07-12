# Caching — Ops Runbook

## Safe flush
- App-surface entries: `HybridCache.RemoveByTagAsync("{area}")` — targeted, O(tag).
- Query-surface entries ride HybridCache since Mediant 1.2.0: `[InvalidatesCache]` and
  `RemoveByTagAsync` share the same tag vocabulary — targeted, O(tag). (Never `FLUSHALL`
  on a shared instance — it takes every service's cache down with yours.)

## TTL tuning
- `DefaultTtlSeconds` covers both surfaces; per-entry overrides beat it
  (`HybridCacheEntryOptions` / `[Cacheable(seconds)]`).
- L1 and L2 TTLs are set equal by default. If a value must converge faster across
  instances than it expires, shorten the LOCAL expiration in code — do not shorten the
  global default to fix one hot key.

## "Why is this stale" triage
1. Which surface? App (HybridCache) or query (`[Cacheable]`)?
2. Query surface: did the command carry `[InvalidatesCache]` with the RIGHT prefix?
   A prefix typo invalidates nothing, silently — check the area vocabulary first.
3. App surface: was the write path supposed to `RemoveByTagAsync`? Check the area
   vocabulary — a tag typo ("rate" vs "rates") invalidates nothing, silently.
4. Two instances disagreeing → L1 skew inside the local TTL window; expected within
   `LocalCacheExpiration`.

## Signals
- Hit-ratio collapse on one area → mass eviction or a key-convention regression
  (raw string keys bypassing `GoldpathCacheKeys` — GP0803 will flag these at compile time).
- L2 latency spike → Redis pressure; check maxmemory policy (`allkeys-lru` recommended
  for pure-cache instances) before scaling.
