# Module RFC: Goldpath.SoftDelete

> Status: v1.0 accepted & implemented — D1-D3 approved by Ömer (2026-07-05):
> Suppress() hard-delete scope · contributor Order (Data, additive) · explicit filter call · Ring B (Phase 2, item 10) · Effort S
> Dependencies: Goldpath.Abstractions (`ISoftDeletable` marker exists since day one), Goldpath.Data
> (save-contributor seam + the new contributor ordering, D2)

## 1. Scope / Non-Goals

**Scope:** deletes of `ISoftDeletable` entities become updates (`IsDeleted=true` +
`DeletedAt`/`DeletedBy` stamped from clock/user), and a global query filter hides deleted
rows everywhere by default.

| Piece | How |
|---|---|
| Delete→update conversion | Save contributor (Data seam): `EntityState.Deleted` → `Modified` + stamp fields — runs FIRST so AuditTrail sees the truth (a soft delete audits as the `IsDeleted false→true` change, in the same transaction) |
| Default invisibility | `modelBuilder.ApplyGoldpathSoftDelete()` → global query filter `!IsDeleted` on every `ISoftDeletable` entity |
| Reading deleted rows | EF's own `IgnoreQueryFilters()` — no Goldpath wrapper (ADR-0003) |
| **Hard delete escape hatch** | `using (GoldpathSoftDelete.Suppress()) { db.Remove(x); … }` — explicit, scoped, async-safe (D1); pairs naturally with an audit trail of WHO hard-deleted |
| Undelete | Set `IsDeleted=false` manually — a plain update, audited like any change (documented) |

**Non-Goals:** no automatic cascade conversion (children follow their own markers; a
soft-deletable parent with hard-deletable children is a documented pitfall, not magic);
no retention/purge (Ring C Data Archival); no filtered-unique-index management (provider
guidance documented — soft-deleted rows still occupy unique keys).

## 2. Seam Map
Data seam only: one contributor (order −100, runs before audit/stamps) + one model extension.

## 3. Manifest Surface
`features.softDelete: true` (toggle — already in the schema).

## 4. API Surface
```csharp
builder.AddGoldpathSoftDelete();                       // registers the contributor
modelBuilder.ApplyGoldpathSoftDelete();                // global filters (template generates the call)
using (GoldpathSoftDelete.Suppress()) { ... }          // hard-delete scope (AsyncLocal)
```
Plus (D2, additive to Goldpath.Data): `IEntitySaveContributor.Order` default interface member
(`0` default); the interceptor sorts contributors by it.

## 5. Analyzer Rules (SHIPPED — analyzer batch 2, 2026-07-05)
| ID | Rule | Severity |
|---|---|---|
| GP0601 | `ISoftDeletable` entity in a context without `ApplyGoldpathSoftDelete()` | error |

## 6. Ops Package
Dashboard: soft-delete rate; deleted-row ratio per table (growth signal → archival need).
Runbook: undelete procedure; unique-index-with-deleted-rows guidance.

## 7. Test Plan
Remove → row survives with `IsDeleted/DeletedAt/DeletedBy` set · default queries hide it ·
`IgnoreQueryFilters` sees it · `Suppress()` scope really deletes (and only within the scope) ·
AuditTrail interplay: the soft delete appears as a Modified change row (`IsDeleted false→true`),
not as Deleted — the two modules tell one consistent story · contributor ordering proven.

## 8. DoD
- [x] Decisions locked · package + tests green (4/4: stamped-update conversion + default
      invisibility + IgnoreQueryFilters, Suppress() scope boundary, the AuditTrail interplay
      — exactly 3 Modified rows with IsDeleted False→True, never "Deleted" — and undelete)
      · PublicAPI locked (Data gains `Order`)
- [x] README (4 sections) + CHANGELOG · GP0601 SHIPPED in analyzer batch 2 · ops/GM wiring tracked (same pattern)

## 9. Decision Points (Ömer)
- **D1 — Hard-delete escape hatch = explicit `Suppress()` scope** (AsyncLocal): visible in
  code review, greppable, works across any DbContext instance in the flow. Alternatives
  (a magic `HardDelete()` method, or "no escape at all") are either wrapper-ish or unrealistic
  for GDPR/right-to-erasure flows. **Recommendation: Suppress scope.**
- **D2 — Contributor ordering:** add `int Order => 0` as a DEFAULT interface member on
  `IEntitySaveContributor` (additive, no existing contributor changes); the interceptor sorts.
  SoftDelete runs at −100 so audit/stamps observe the converted (Modified) state.
  **Recommendation: yes — registration-order coupling between modules would be fragile.**
- **D3 — Filter application stays explicit** (`ApplyGoldpathSoftDelete()` in OnModelCreating, like
  every other model call; the template generates it; GP0601 catches forgetting it).
  **Recommendation: yes.**
