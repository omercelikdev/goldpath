# Goldpath.AuditTrail

Ring B audit, two levels, one correlated story: Mediant `[Auditable]` records **why**
(who ran which command); the Goldpath entity contributors record **exactly what** (old → new per
property) — in the **same transaction** as the change, so an audit row commits or dies with
the data it describes.

## Getting started

```csharp
builder.AddGoldpathAuditTrail<WebApplicationBuilder, OrdersDbContext>();

// OnModelCreating:
modelBuilder.AddGoldpathAuditLog();          // Goldpath change-log + Mediant command-audit entities

[Auditable]                             // command level (Mediant)
public record ApproveLoanCommand(...) : ICommand<Result>;

public class Loan : IAuditedEntity, IAuditLogged { }   // stamps + full change rows
```

## Configuration

```json
{ "Goldpath": { "AuditTrail": { "EntityValues": "Full" } } }
```

- `EntityValues`: `Full` (old→new values, default) · `NamesOnly` (property names without
  values — by policy, or until DataProtection masking lands).

## Advanced

- `IAuditedEntity` alone = stamps only (CreatedAt/By, ModifiedAt/By — filled automatically,
  never by application code: GP0502). Add `IAuditLogged` for row-level history.
- Every entity row carries user (`IUserContext` — HTTP claims by default), tenant,
  and the correlation id: one id walks HTTP → command audit → entity rows.
- Only 'what changed' is written on Modified (unmodified properties are skipped).
- Retention/archival is a policy knob — see the runbook; the Ring C Data Archival module
  is the long-term answer.

## Providers

Storage is the application's own DbContext (any provider Goldpath.Data supports). The
`eventstream` store option in the manifest is reserved (written deferral in the RFC).
