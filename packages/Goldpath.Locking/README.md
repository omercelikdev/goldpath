# Goldpath.Locking

Ring B distributed locking: Medallion `DistributedLock` composed behind manifest-driven
provider selection — Redis (RedLock), Postgres (advisory locks) or SQL Server
(`sp_getapplock`) — with tenant-scoped lock names and acquire metrics.

**Honest scope:** locks reduce duplicate work; they are NOT correctness fences (no fencing
tokens — a paused holder can outlive its lock). Correctness comes from the Idempotency
module; the two compose.

## Getting started

```csharp
builder.AddGoldpathLocking();                       // provider per manifest

public sealed class SettlementJob(IDistributedLockProvider locks, GoldpathLockNames names)
{
    public async Task RunAsync(CancellationToken ct)
    {
        await using var handle = await locks.TryAcquireLockAsync(
            names.For("settlement"), TimeSpan.FromSeconds(5), ct);
        if (handle is null) return;            // another instance holds it — that's the point
        // ... exactly one instance runs this ...
    }
}
```

`IDistributedLockProvider` is Medallion's own interface — no Goldpath wrapper; the full acquire
API (wait, try-with-timeout, sync/async) is the library's, documented not rewritten.

## Configuration

```jsonc
"Goldpath": {
  "DistributedLocking": {
    "Provider": "Redis",                       // Redis | Postgres | SqlServer
    "ConnectionName": "redis"                  // db providers: point at the app db → ZERO new infra
  }
}
```

## Advanced

- **Names are tenant-scoped and fail-closed:** `names.For("nightly-report")` →
  `goldpath:{tenant}:lock:nightly-report`; with NO ambient tenant it THROWS (a silent
  cross-tenant lock collision is a correctness bug). Background flows either pin a tenant
  with `GoldpathTenant.Use(...)` or declare `GoldpathLockNames.Global("name")` — explicit and greppable.
- **Metrics via a decorator over the same interface** (composition, not a wrapper
  abstraction): `goldpath_lock_acquire_total{outcome}` and `goldpath_lock_wait_seconds` flow into
  Ring A OTel.
- **Provider swap is behavior-neutral** — the same contract test runs against real Redis
  AND real Postgres in CI (`LockContractTests`).
- **Timeout semantics:** `TryAcquire*` returns `null` on contention (no exception);
  `Acquire*` waits and throws `TimeoutException` — pick by intent.

## Providers

Redis (reuses the caching L2 connection by default) · Postgres advisory locks · SQL Server
`sp_getapplock`. ZooKeeper/etcd deferred until a project demands them (RFC).
