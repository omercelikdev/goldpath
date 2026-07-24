# Goldpath.Analyzers

The executable standards of the Goldpath enterprise asset (ADR-0005): rules defined by the core
module RFCs, enforced at compile time. Guardrails, not constraints — severities are
configurable per repo, suppression is always possible **with justification**.

## Getting started

Wired automatically by the golden-path template (`Directory.Build.props`). Manual:

```
dotnet add package Goldpath.Analyzers
```

Rules are inert when the target types are absent (e.g. GP0401 never fires in a project that
doesn't reference MassTransit) — safe for L1 à-la-carte adoption.

## Configuration

Standard Roslyn mechanics — `.editorconfig`:

```ini
dotnet_diagnostic.GP0202.severity = suggestion   # tune a rule down for this repo
```

In-code suppression requires a justification (reviewed like any deviation):

```csharp
#pragma warning disable GP0302 // Justification: one-shot ops tool, runs supervised
```

## Advanced

| ID | Rule | Default |
|---|---|---|
| GP0102 | `new HttpClient()` — use IHttpClientFactory | warn |
| GP0202 | `Skip().Take()` — use the keyset primitive | warn |
| GP0301 | `DateTime` on a Goldpath-marked entity — use `DateTimeOffset` | warn |
| GP0302 | `Migrate`/`EnsureCreated` without a Development guard | warn |
| GP0303 | Interpolated/concatenated raw SQL | **error** |
| GP0401 | Publishing a type without `IIntegrationEvent` | **error** |
| GP0402 | Mediant notification cross-marked as integration event | **error** |
| GP0501 | `IAuditLogged` entity but the model never calls `AddGoldpathAuditLog()` | **error** |
| GP0502 | Manual write to audit stamp fields from application code | warn |
| GP0601 | `ISoftDeletable` entity but the model never calls `ApplyGoldpathSoftDelete()` | **error** |
| GP1003 | `[Idempotent]` on a Mediant query (no effect) | info |
| GP0701 | Classified property on an integration event | warn |
| GP0702 | Classified property without the DataProtection module | info |
| GP0801 | `[Cacheable]` on a command | **error** |
| GP0802 | `[InvalidatesCache]` on a query | info |
| GP0803 | Raw string cache key (tenant-scoping bypass) | warn |
| GP0901 | `IMultiTenant` entity but no `ApplyGoldpathMultiTenancy(this)` | **error** |
| GP0902 | Manual write to `TenantId` from application code | warn |
| GP0903 | `IgnoreQueryFilters` on a tenant entity without a visible `Bypass()` | warn |
| GP0904 | Admin endpoint takes a `tenant` parameter but never consults `AdminTenantScope` (contract R1) | warn |
| GP1101 | Raw string lock name (tenant-collision risk) | warn |
| GP1102 | Lock handle discarded / stored without `using` | warn |
| GP1201 | Anonymous endpoint inventory | info |
| GP1202 | Auth secret as a string literal | **error** |

GP0501/0601 only fire in assemblies that contain a `DbContext`; entity-only assemblies are
exempt. Deferred rules (GP0101/0103/0203/0403/1001/1002/1004) are tracked in
`docs/rfc/goldpath-analyzers.md`.

## Providers

Not applicable.
