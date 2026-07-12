# Goldpath.Messaging

Message-path floor of the Goldpath enterprise asset, composed on the **MassTransit 8.x OSS line**
(v9 went commercial — the golden path carries no commercial-license surprise; exit strategy
documented in `docs/rfc/goldpath-messaging.md` D1).

## Getting started

```csharp
builder.AddGoldpathMessaging(bus =>
{
    bus.AddConsumer<OrderConfirmedConsumer>();
    bus.AddGoldpathOutbox<OrdersDbContext>(o => o.UsePostgres());   // features.outbox — template wires the lock provider

    bus.UsingRabbitMq((context, cfg) =>                        // transport from manifest providers.broker
    {
        cfg.Host(connectionString);
        cfg.ConfigureGoldpathEndpoints(context);                    // guard + propagation + retry + conventions
    });
});
```

## Configuration

Bound from `Goldpath:Messaging`:

```json
{ "Goldpath": { "Messaging": { "Retry": { "ImmediateCount": 3 } } } }
```

Redelivery defaults: 5m/15m/30m, then the error queue — messages are never silently dropped.

## Advanced

**The event boundary (the rule everything hangs on):**

```csharp
public record OrderConfirmed(Guid OrderId) : IIntegrationEvent;   // broker-bound — marker REQUIRED
public record OrderTotalsChanged(...) : INotification;            // in-process — Mediant, never the bus
```

Publishing an unmarked type throws (runtime guard; analyzer GP0401 will catch it at build).
A Mediant notification must never carry `IIntegrationEvent` (GP0402) — one world only.

- **Outbox** (`AddGoldpathOutbox<TContext>`): publish commits atomically with business data;
  consumer-side inbox dedup included; 30m duplicate-detection window default.
- **Propagation**: `X-Goldpath-Tenant` and `X-Correlation-Id` flow through publish/consume
  automatically; on consume the tenant is restored into the message-scoped
  `GoldpathMessageTenantContext` (`ITenantContext` in golden-path services).
- Kebab-case endpoint naming; OTel tracing flows via MassTransit's built-in instrumentation.

## Providers

Transport chosen in the manifest (`providers.broker`): `rabbitmq`, `kafka`, `inmemory`
(tests/dev). Transport packages and the outbox lock provider are wired by the template;
this package references only MassTransit core + EF outbox.
