# Goldpath.Abstractions

Contract layer of the Goldpath enterprise asset. Every other Goldpath package — and consumer code
that wants to stay implementation-free — references only this. Single micro-dependency:
`Microsoft.Extensions.Compliance.Abstractions`, so the data-classification attributes are
real Microsoft `DataClassificationAttribute`s and log redaction works natively (DataProtection RFC, D1).

## Getting started

```
dotnet add package Goldpath.Abstractions
```

There is nothing to configure and nothing to register: the package contains contracts only.

## Configuration

None. This package deliberately has no options, no DI, and no behavior
(see `docs/rfc/goldpath-abstractions.md`).

## Advanced

- `TenantId` / `ITenantContext` — tenant propagation contract; implemented by the
  MultiTenancy module, `null` means single-tenant.
- `IAuditedEntity`, `ISoftDeletable`, `IMultiTenant` — entity capability markers acted on
  by the data-path interceptors (AuditTrail, SoftDelete, MultiTenancy modules).
- `IIntegrationEvent` — marker for broker-bound events (MassTransit outbox); in-process
  domain events are Mediant notifications and must not carry it.
- `GoldpathHeaders` — canonical header names (`X-Goldpath-Tenant`, `Idempotency-Key`, `X-Correlation-Id`).

## Providers

Not applicable — no providers; the package is pure BCL.
