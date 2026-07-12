# Goldpath.Idempotency

Ring B idempotency, composed on Mediant (one store, one semantics): the HTTP
`Idempotency-Key` middleware here; the command path via Mediant `[Idempotent]`.
The promise is **effectively-once processing** — not "exactly-once delivery" (that would be a lie).

## Getting started

```csharp
builder.Services.AddStackExchangeRedisCache(...);   // golden path; AddDistributedMemoryCache in dev
builder.AddGoldpathIdempotency();
```

```
POST /api/v1/orders
Idempotency-Key: 7f3c…            ← client-generated, stable across retries
```

First request executes and its response is stored; a retry replays it byte-for-byte
(`Goldpath-Idempotent-Replay: true`); a concurrent duplicate gets **409**; the same key with a
different payload gets **422** (fingerprint).

## Configuration

Bound from `Goldpath:Idempotency`:

```json
{ "Goldpath": { "Idempotency": { "TtlHours": 24, "OnConflict": "Reject", "Fingerprint": "Strict" } } }
```

- `OnConflict`: `Reject` (409, default) · `Wait` (serialize on the in-flight operation and replay).
- `Fingerprint`: `Strict` (SHA-256 of the body; mismatch → 422, default) · `None`.

## Advanced

- Key scope: `http:{tenant}:{method}:{path}:{key}` — tenants and endpoints never collide.
- Only mutating methods (POST/PUT/PATCH) participate; GET/DELETE pass through.
- Only 2xx responses are stored; a failed attempt leaves no trace, so a retry can run.
- Command path: mark Mediant commands `[Idempotent]` — same store, so HTTP and in-process
  semantics can never drift apart.
- Per-key serialization is process-local (Mediant coordinator); horizontally scaled replicas
  can race on a cold key — the store's last-write-wins keeps it safe (documented Mediant note).

## Providers

The store is Mediant's `IIdempotencyStore` over `IDistributedCache`: Redis in the golden path,
SQL Server distributed cache where mandated, in-memory for dev/tests — chosen by the host.
