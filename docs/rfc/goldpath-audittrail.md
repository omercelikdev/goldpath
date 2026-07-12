# Module RFC: Goldpath.AuditTrail

> Status: v1.0 accepted & implemented — D1-D4 approved by Ömer (2026-07-05):
> same-transaction entity audit · IAuditedEntity/IAuditLogged split · full values default
> with namesOnly switch (superseded as recommendation by DataProtection per-property masking) · IUserContext added to Abstractions · Ring B (Phase 2, item 9) · Effort M
> Dependencies: Goldpath.Abstractions, Goldpath.Data (save-contributor seam), Mediant.Behaviors
> (composed: `[Auditable]` + `EfAuditStore` + buffering from mediant#117 — ADR-0003)

## 1. Scope / Non-Goals

**Scope — two audit levels, one correlated story:**

| Level | What | How |
|---|---|---|
| **Command audit** | "Who ran which command with what payload" | **Mediant `[Auditable]`** + `IAuditStore` (EF store + #117 buffering) — composed, not rewritten; Goldpath wires registration + options |
| **Entity audit** | "Which fields of which row changed, old → new" | **Goldpath save-contributor** (the Data seam built for exactly this): stamps `IAuditedEntity` fields AND writes change-log rows in the SAME transaction as the change |
| **The correlated story** | One id walks both levels | Both records carry correlationId/traceId + user — command audit says *why*, entity audit says *what exactly* |

**Non-Goals:** no eventstream store in v1 (manifest option reserved, written deferral);
no PII masking here (DataProtection's job — the interplay is specified: audit stores what
DataProtection lets through); no UI (Operate surface: audit is queryable data + the portal's
Audit page arrives with the portal RFC).

## 2. Seam Map
Data seam consumer: two contributors on `IEntitySaveContributor` (stamping + change log).
Command seam: Mediant behavior (no Goldpath code). No HTTP/message seam.

## 3. Manifest Surface
```yaml
features:
  auditTrail:
    store: database        # database (v1) | eventstream (reserved)
    entityValues: full     # full | namesOnly  (D3)
```

## 4. API Surface
```csharp
builder.AddGoldpathAuditTrail<OrdersDbContext>(o => ...);   // contributors + Mediant audit wiring
modelBuilder.AddGoldpathAuditLog();                          // change-log entity into the app's model

public class Order : IAuditedEntity, IAuditLogged { }   // stamps + change rows (D2)

public interface IUserContext { string? UserId { get; } }   // NEW in Abstractions (D4)
```

## 5. Analyzer Rules (SHIPPED — analyzer batch 2, 2026-07-05)
| ID | Rule | Severity |
|---|---|---|
| GP0501 | `IAuditLogged` entity in a context where `AddGoldpathAuditLog()` was not called | error |
| GP0502 | Manual writes to `IAuditedEntity` stamp fields (CreatedAt/By…) from application code | warn |

## 6. Ops Package
Dashboard: audit write rate, buffer depth (via #117), change-log table growth. Runbook:
audit retention/archival guidance (retention is a POLICY knob — banking keeps years; the
Data Archival module in Ring C is the long-term answer, referenced not duplicated).

## 7. Test Plan
- Entity level (SQLite + Postgres): Added/Modified/Deleted produce correct change rows
  (old→new per property), same-transaction atomicity (rollback → no audit rows), stamps
  filled with user+clock+tenant, `namesOnly` mode redacts values
- Command level: `[Auditable]` command lands in the store with user context (compose test)
- Correlation: entity rows carry the correlation id from the ambient Activity

## 8. DoD
- [x] Decisions locked · package + tests green (5/5: change rows with who+old→new,
      stamps auto-fill + stamp-only isolation, rollback atomicity, NamesOnly redaction,
      Mediant command store composed) · PublicAPI locked
- [x] README (4 sections) + CHANGELOG · analyzer specs (GP0501/0502) SHIPPED in analyzer batch 2
- [x] Abstractions gains IUserContext; Data's GoldpathSaveContext gains User; the Data
      interceptor now snapshots entries (contributors may add rows while iterating)
- [ ] Ops package + GM wiring → with GM shape growth (same pattern as Idempotency, tracked)

## 9. Decision Points (Ömer)
- **D1 — Entity audit rows live in the app's own DbContext/transaction** (a change and its
  audit row commit or die together — the strongest guarantee an auditor can ask for), vs a
  separate store (async, lossy on crash). **Recommendation: same transaction.**
- **D2 — Two markers:** `IAuditedEntity` = stamp fields only (exists today);
  new `IAuditLogged` (Abstractions) = full change-log rows. Not every stamped entity needs
  row-level history. **Recommendation: yes.**
- **D3 — Value recording default:** `full` (old→new values — what makes audit useful) with a
  `namesOnly` switch; PII-safe masking arrives when DataProtection lands (values pass through
  its mask). **Recommendation: full default, interplay written.**
- **D4 — User identity:** add `IUserContext` to Abstractions + an HTTP claims-based
  implementation registered by this module; `GoldpathSaveContext` gains `User` (additive minor).
  **Recommendation: yes — audit without "who" is not audit.**
