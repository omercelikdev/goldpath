# Module RFC: Goldpath.Idempotency

> This document is also the **reference example of the module RFC template** (foundation 13.8):
> every module is born by filling in these eight sections. The template sections are fixed;
> the content is module-specific.
> Status: v0.1 draft (2026-07-03) · Ring: B · Dependencies: Data (store), Mediant (behavior), Messaging (inbox)

---

## 1. Scope / Non-Goals

**Scope:** In an at-least-once world (HTTP clients with retries, message re-delivery),
prevent the same operation from being applied twice. Two entry points: HTTP requests
(`Idempotency-Key` header) and message consumers (inbox/dedup). The first response is stored,
and on a retry **the same response is replayed** (not reprocessed).

**Non-goals:**
- Does not promise an "exactly-once delivery" guarantee (that would be a lie in a distributed system; the promise: *effectively-once processing*)
- Saga/compensation flows (the saga module's job)
- GET/HEAD dedup (already idempotent — an analyzer warns on unnecessary usage)

## 2. Seam Map (foundation 5.0 — touches three of the four seams)

| Seam | Component | Behavior |
|---|---|---|
| HTTP | `IdempotencyMiddleware` (**written by Goldpath**) | Captures the `Idempotency-Key` header; scope = tenant + route + key; first request → process + store response; retry → replay stored response; same key currently being processed → `409 Conflict`; same key with a **different body** → `422` (fingerprint) |
| Command path | **Mediant `[Idempotent]` + `DistributedCacheIdempotencyStore` (READY-MADE — not rewritten)** | SHA256 key (KeyProperty or full JSON), per-key lock (race-free, v1.0.0 hardened), response replay, TTL. Goldpath only does the manifest→options wiring |
| Message path | `InboxFilter` (MassTransit) (**written by Goldpath**) | `MessageId` dedup — processed message IDs go to the store; re-delivery is a no-op |
| Data | — | Store: the Mediant `IIdempotencyStore` contract is shared; the database provider ships with the Data module's migration pipeline |

### 2.1 Mediant Alignment (following the 2026-07-03 analysis)

- **The command path is composed from Mediant** — the implementation is mature (per-key
  locking, reference-counted eviction, `Result<T>` JSON round-trip fix). Goldpath does NOT
  REWRITE this layer; its added value is the HTTP and message seams + manifest wiring +
  shared store configuration. **Gate: MET — Mediant v1.0.0 stable shipped 2026-07-03**
  (foundation rule 5.1); implementation unblocked, scheduled per the module plan (Phase 2, item 8).
- **The semantic difference is deliberate:** the HTTP layer returns `409` for a concurrent
  same key (fast signal to the client); the command layer SERIALIZES via the Mediant per-key
  lock (`wait` semantics). These are two different layers, not a contradiction — the manifest
  `onConflict` selects only the HTTP behavior.
- **Analyzer alignment:** Mediant ships with its own QM1001-1004 rules (behavior attribute
  checks); Goldpath GP1001-1004 do NOT REPEAT them, they build on top (e.g. GP1002 a consumer
  without an inbox — outside Mediant's scope).

## 3. Manifest Surface

```yaml
features:
  idempotency:
    store: database        # database | redis   (default: database — audit demands persistence)
    ttlHours: 24           # lifetime of stored responses/keys
    onConflict: reject     # reject (409) | wait (wait for the first request's result)
    fingerprint: strict    # strict (same key + different body = 422) | none
```
The short form `idempotency: true` = the defaults above. (Schema: goldpath-manifest.schema.json
`$defs/features/idempotency` — the object form will be extended with this RFC.)

## 4. API Surface

```csharp
services.AddGoldpathIdempotency(opt => { ... });        // single entry point; generated from the manifest
[Idempotent(KeyExpression = "request.ChequeNo")]    // command-level marker (Mediant behavior)
public record PresentChequeCommand(...) : ICommand<Result<PresentmentDto>>;

public interface IIdempotencyStore                   // provider seam (database/redis impl)
{
    Task<IdempotencyEntry?> TryBeginAsync(IdempotencyScope scope, string fingerprint, CancellationToken ct);
    Task CompleteAsync(IdempotencyScope scope, StoredResponse response, CancellationToken ct);
}
```
The public API diff is tracked with PublicApiAnalyzers (foundation section 5).

## 5. Analyzer Rules (guardrails shipped with the module)

| ID | Rule | Severity |
|---|---|---|
| GP1001 | A write-performing command is not marked `[Idempotent]` while idempotency is enabled | warn |
| GP1002 | A broker consumer is registered without an inbox filter | error |
| GP1003 | `[Idempotent]` on a GET-semantics query (unnecessary) — SHIPPED (batch 2) | info |
| GP1004 | No `KeyExpression` and no natural key field can be detected on the command | warn |

## 6. Ops Package ("no runbook = no module")

- **Metrics:** `goldpath_idempotency_replay_total`, `conflict_total`, `fingerprint_mismatch_total`,
  `store_latency` (OTel; flows into the Ring A infrastructure)
- **Dashboard panel:** replay/conflict rate, store latency (Aspire dashboard + Grafana template)
- **Alerts:** a sudden spike in the replay rate = a client retry storm (signal of an upstream
  problem); a rise in fingerprint mismatches = a client integration bug
- **Runbook:** store growth/TTL tuning; diagnosing lock-ups in `wait` mode; key collision analysis

## 7. Test Plan (aligned with foundation 8)

- **Unit:** key scope normalization (tenant+route+key), fingerprint computation, TTL logic
- **Integration (Testcontainers):** two concurrent POSTs → single write + 409 for the second;
  replay of a completed request → byte-identical response; same key after TTL → new operation;
  consumer re-delivery → no-op; both the redis and database stores pass the same contract test
- **Property-based:** N parallel requests for the same key → always a single `Completed` record in the store
- **Benchmark (BenchmarkDotNet):** middleware overhead target — < 0.1ms excluding the store
  path, database store p95 < 5ms (regression tracked in CI)
- **Golden manifest impact:** the `idempotency: true` combination is added to the matrix
  (smoke: duplicate request scenario)

## 8. DoD (status as shipped in 0.1.0-preview.1 — MR !17)

- [x] Package + options + HTTP seam (middleware on the Mediant #114/#115 coordinator);
      command seam = Mediant `[Idempotent]` (same store); message seam = consumer EF inbox
      (shipped with AddGoldpathOutbox — supersedes the separate InboxFilter idea)
- [x] Store: Mediant `IIdempotencyStore` over `IDistributedCache` — Redis/SQL-cache/memory is
      the HOST's choice (recorded deviation from the two-provider plan: one contract, host-picked backing)
- [x] Tests 6/6: byte-for-byte replay, 422 fingerprint, 409 in-flight, tenant isolation,
      Wait-mode serialize+replay, no-header/GET passthrough
- [x] README (4 sections) + CHANGELOG · manifest schema gained the options object
- [ ] Analyzers (GP1001-1004) + their tests → next Goldpath.Analyzers batch (tracked)
- [ ] Ops package (replay/conflict metrics + dashboard/alert/runbook) + benchmark baseline →
      with the module's template wiring (GM-3/5 growth), tracked
- [ ] Golden manifest impact: idempotency joins GM-2/3/5/6 when those shapes ship (7d)
