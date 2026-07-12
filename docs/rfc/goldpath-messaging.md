# Module RFC: Goldpath.Messaging

> Status: v1.0 accepted — D1-D4 approved by Ömer (2026-07-04): MassTransit 8.x pin with exit strategy · analyzer-enforced event boundary · outbox template-default on · retry defaults · Phase 1 item 5 (heaviest) · Ring: golden-path core
> Dependencies: Goldpath.Abstractions, Goldpath.Data (outbox storage), MassTransit **v8** (composed — ADR-0003; see the licensing note in §9/D1)

## 1. Scope / Non-Goals

**Scope — the message-path floor:**

| Pillar | What it is |
|---|---|
| **The event boundary** (the defining decision) | In-process domain events = **Mediant notifications** (`IPublisher`, optionally through the Mediant outbox for reliability). Cross-service integration events = types marked **`IIntegrationEvent`** (Goldpath.Abstractions), published via MassTransit. The two worlds never mix — analyzer-enforced (GP0401/0402) |
| MassTransit composition | `AddGoldpathMessaging(...)`: consumer registration, corporate topology conventions, OTel integration on. Transport packages (RabbitMQ/Kafka) wired by the template from the manifest — this package stays transport-neutral (in-memory transport included for tests) |
| Topology conventions | Kebab-case endpoint naming (service-scoped), message type → topic/exchange naming without namespaces, error/dead-letter queue conventions |
| **Transactional outbox/inbox** | `features.outbox`: MassTransit's EF outbox composed with the Goldpath.Data DbContext — publish commits atomically with business data; consumer-side inbox dedup included. Requires a broker (manifest rule V1) |
| Header propagation seam | `GoldpathHeaders.TenantId` + `X-Correlation-Id` flow through publish/consume filters; consume side restores the ambient context (the message-seam counterpart of the Data seam — Ring B MultiTenancy plugs in here) |
| Consumer resilience defaults | Retry (immediate ×3) + delayed redelivery (5m/15m/30m) → error queue. Tunable via options; never silently dropped |

**Non-Goals:**
- No saga orchestration (Ring C, own RFC)
- No request/response over the bus (HTTP is the sync path; the bus is for events — opinionated)
- No transport abstraction beyond MassTransit's own (we compose, not wrap)
- No message versioning framework in v1 (convention documented: additive changes only;
  breaking = new message type — the AsyncAPI spec + Spec Engine `diff` govern this)

## 2. Seam Map
Message-seam owner: publish/consume filters are the plug-in points (tenant, correlation,
future idempotency inbox interplay). Consumes the Data seam (outbox tables via DbContext).

## 3. Manifest Surface
`providers.broker` (transport — template wires) · `features.outbox` (toggle, V1 rule).

## 4. API Surface

```csharp
builder.AddGoldpathMessaging(bus =>
{
    bus.AddConsumer<OrderConfirmedConsumer>();
}, options => { options.Retry.Immediate = 3; });                  // transport wired by template

public record OrderConfirmed(Guid OrderId) : IIntegrationEvent;    // broker-bound — marker required
await publishEndpoint.Publish(new OrderConfirmed(id), ct);         // outbox-atomic when enabled
```

Multi-target `net8.0;net10.0`. MassTransit pinned to the **8.x** line.

## 5. Analyzer Rules (defined here, shipped in Goldpath.Analyzers)
| ID | Rule | Severity |
|---|---|---|
| GP0401 | `Publish`/`Send` of a type not marked `IIntegrationEvent` | error |
| GP0402 | A Mediant notification (`INotification`) also marked `IIntegrationEvent` — one world only | error |
| GP0403 | Consumer registered while `features.outbox` disabled and no inbox — at-least-once without dedup | warn |

## 6. Ops Package
Dashboard additions: publish/consume rate, consumer lag (transport-specific), error-queue depth,
outbox backlog gauge, redelivery counts. Alerts: error-queue growth, outbox backlog sustained.
Runbook: poison-message triage (error queue → inspect → fix consumer or park), outbox backlog
causes (broker down vs consumer slow), redelivery storm response.

## 7. Test Plan
- Integration (MassTransit test harness, in-memory transport): publish→consume round trip;
  `IIntegrationEvent` marker respected; tenant+correlation headers propagate and the ambient
  context is restored on consume; retry policy kicks in on consumer failure then error queue;
  outbox path: save+publish atomic (rollback publishes nothing) — SQLite-backed
- Golden manifest impact: GM-5 (worker-heavy) exercises consumer+outbox; GM-2 outbox on
- Real-broker (Testcontainers RabbitMQ) arrives with item 7 — tracked, not silent

## 8. DoD
- [x] RFC decisions locked (§9 — D1-D4 approved 2026-07-04)
- [x] Package + tests green (4: round-trip with tenant stamp/restore, boundary-guard rejection
      with the GP0401 message, immediate-retry attempt counting, outbox registration
      composition). Outbox ATOMICITY proof deferred to item 7 (Testcontainers Postgres) — tracked
- [x] README (4 sections) + CHANGELOG · analyzer specs (GP0401-0403) to backlog
- Note: the boundary guard runs at RUNTIME until Goldpath.Analyzers ships GP0401 (belt before suspenders)

## 9. Decision Points (Ömer)

- **D1 — MassTransit version strategy (STRATEGIC):** **MassTransit v9 has gone commercial**
  (the MediatR story repeating); v8 (8.5.10) remains Apache-2.0 OSS with maintenance.
  **Recommendation: pin the 8.x line** and record an explicit exit strategy in this RFC:
  the Goldpath surface (`AddGoldpathMessaging`, filters, conventions) is our own thin layer, so a future
  move (v8 fork, Wolverine, or growing Mediant toward transport) changes the composition,
  not consumer code. Revisit at Phase 3 with real usage data. The enterprise pitch even
  benefits: "no commercial-license surprise in the golden path."
- **D2 — The event boundary as specified:** Mediant notifications in-process /
  `IIntegrationEvent`+MassTransit cross-service, analyzer-enforced, never mixed.
  **Recommendation: yes** — this is the boundary the whole Messaging design hangs on.
- **D3 — Outbox default:** `features.outbox` recommended-on in generated services with a broker
  (template default `true`), directly composable off. **Recommendation: yes** — at-least-once
  without atomicity is the classic silent-inconsistency source in banking/telco flows.
- **D4 — Retry defaults:** immediate ×3 + delayed redelivery 5m/15m/30m → error queue,
  options-tunable. **Recommendation: yes.**
