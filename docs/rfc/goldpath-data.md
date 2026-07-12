# Module RFC: Goldpath.Data

> Status: v1.0 accepted — D1-D5 approved by Ömer (2026-07-03, recommendations as-is):
> dev auto-migrate + prod bundle-only · seam-first contributor contract · EF default naming ·
> DateTimeOffset/UTC policy · 1-2 key keyset scope · Phase 1 item 4 · Ring: golden-path core
> Dependencies: Goldpath.Abstractions, Goldpath.ApiDefaults (pagination contract), EF Core (composed — ADR-0003)

## 1. Scope / Non-Goals

**Scope — the data-path floor:**

| Pillar | What it is |
|---|---|
| **Keyset pagination executor** | `ToPageAsync` on `IQueryable<T>`: reads `PageRequest`, emits `Page<T>` with a `GoldpathCursor` — the other half of the ApiDefaults contract. Provider-neutral keyset predicate (`k1 > v1 OR (k1 = v1 AND k2 > v2)`); 1–2 keys, each asc/desc. `Skip/Take` in API queries is the analyzer-flagged anti-pattern (GP0202) |
| **Save-contributor seam** | `IEntitySaveContributor` + a single Goldpath `SaveChangesInterceptor` that runs registered contributors over tracked entries. Ring B modules (AuditTrail, SoftDelete, MultiTenancy) plug in as contributors in Phase 2 — compile-time composition preserved (disabled module = no contributor) |
| **Model conventions** | UTC-only temporal policy (`DateTimeOffset`), sane string-length default (require explicit `MaxLength` or fall back 256), decimal precision default (18,4 — money-safe), enum-to-string column default |
| **Migration standard** | Migrations in source, reviewed like code; Development auto-migrates on startup, production applies a CI-produced **migration bundle** artifact — runtime `Migrate()` outside Development is forbidden (and analyzer-flagged) |
| **Streaming guidance** | `IAsyncEnumerable` passthrough conventions; unbounded `ToList` on API paths flagged (GP0201 already) |

**Non-Goals:**
- No repository/unit-of-work abstraction over EF (`DbContext` IS the unit of work — wrapping it
  is the framework trap; Mediant `[Transactional]` covers the command-path transaction)
- No provider packages referenced here (Npgsql/SqlServer/Oracle are wired by the template from
  the manifest — this package stays provider-neutral on `Microsoft.EntityFrameworkCore.Relational`)
- No audit/soft-delete/tenant BEHAVIOR (Ring B modules; only their seam ships here)
- No second-level cache, no bulk-write engine (Ring C candidates, RFC-gated)

## 2. Seam Map
Data seam owner: defines the save-contributor pipeline every data-touching Ring B module uses.
Consumes the HTTP-seam pagination contract (ApiDefaults). No HTTP/message registration itself.

## 3. Manifest Surface
Reads `providers.db` only indirectly (template wires the provider). No toggle — golden-path core.

## 4. API Surface

```csharp
builder.AddGoldpathData<OrdersDbContext>(efOptions => ...);     // conventions + interceptor + options passthrough

// Keyset pagination (ascending default; 1 or 2 keys):
Page<OrderDto> page = await query
    .Select(o => new OrderDto(...))
    .ToPageAsync(request, o => o.CreatedAt, o => o.Id, ct);

// Ring B seam (implemented by Phase 2 modules, not by app code):
public interface IEntitySaveContributor
{
    void OnSaving(EntityEntry entry, GoldpathSaveContext context);   // context: clock, tenant, user
}
```

Multi-target: `net8.0` (EF Core 8 LTS) + `net10.0` (EF Core 10 LTS) — per-TFM pinned EF versions.

## 5. Analyzer Rules (defined here, shipped in Goldpath.Analyzers)
| ID | Rule | Severity |
|---|---|---|
| GP0301 | `DateTime` property on an entity — use `DateTimeOffset` (UTC policy) | warn |
| GP0302 | `Database.Migrate()`/`EnsureCreated()` call reachable outside Development guard | error |
| GP0303 | Raw SQL composed from interpolated/concatenated strings (`FromSqlRaw` with non-constant) | error |

## 6. Ops Package
Adds to baseline dashboard: EF query duration histogram, save-changes duration, migration
version gauge (deployed vs latest). Runbook: pending-migration detection, keyset-pagination
troubleshooting (wrong order-by = skipped rows), connection-pool exhaustion triage.

## 7. Test Plan
- Unit: keyset predicate construction (asc/desc, 1-2 keys, boundary equality), cursor
  round-trip through executor, contributor pipeline ordering
- Integration (SQLite in-memory for CI speed; Testcontainers-Postgres arrives with Template CI):
  ToPageAsync end-to-end page walk (no skipped/duplicated rows across pages), conventions
  applied (string length, decimal precision, enum-as-string), contributor invoked on save
- Golden manifest impact: GM smoke's paginated endpoint goes through the real executor

## 8. DoD
- [x] RFC decisions locked (§9 — D1-D5 approved 2026-07-03)
- [x] Package + tests green (9/9: page walks single/composite with duplicate first keys,
      descending, last-page null cursor, invalid-cursor throw, projection support,
      conventions incl. TenantId round-trip, contributor pipeline with context), PublicAPI locked
- [x] README (4 sections) + CHANGELOG · analyzer specs (GP0301-0303) to backlog
- Note: API shape adjusted for RS0026/27 (optionals live on the longest overload only);
  SQLite tests map DateTimeOffset→ticks (documented provider limitation, test-only)

## 9. Decision Points (Ömer)

- **D1 — Migration execution policy:** Development = auto-migrate on startup (F5 experience);
  everything else = CI-produced migration **bundle** (`dotnet ef migrations bundle`) applied as
  an explicit pipeline step before deploy; runtime `Migrate()` outside Development forbidden +
  analyzer-flagged (GP0302). **Recommendation: yes** — auto-migrate in production is the
  classic enterprise outage; the bundle is auditable and rollback-plannable.
- **D2 — Save-contributor seam ships now:** The `IEntitySaveContributor` contract + interceptor
  land in Phase 1 (empty pipeline until Ring B modules arrive). **Recommendation: yes** —
  seam-first keeps Phase 2 modules from ever touching Goldpath.Data internals.
- **D3 — Naming convention:** v1 keeps EF provider defaults (PascalCase→provider casing);
  a `snake_case` profile (PostgreSQL community standard) is a fast-follow option, not v1.
  **Recommendation: defaults now** — provider-neutral, least surprise across
  postgres/sqlserver/oracle DBAs; convention profiles need their own mini-RFC.
- **D4 — Temporal policy:** `DateTimeOffset` everywhere, stored UTC; `DateTime` on entities
  analyzer-flagged. **Recommendation: yes** — timezone bugs are the quietest data corruption.
- **D5 — Keyset scope for v1:** 1–2 keys with per-key direction (covers the canonical
  `(timestamp, id)` pattern); 3+ composite keys deferred until a real consumer needs them.
  **Recommendation: yes** — YAGNI with a documented boundary.
