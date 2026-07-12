# Goldpath.SoftDelete

Ring B soft delete: deletes of `ISoftDeletable` entities become stamped updates
(`IsDeleted`/`DeletedAt`/`DeletedBy`), and a global query filter hides them everywhere by
default. Hard deletion is an explicit, reviewable scope — not a habit.

## Getting started

```csharp
builder.AddGoldpathSoftDelete();

// OnModelCreating:
modelBuilder.ApplyGoldpathSoftDelete();      // !IsDeleted filter on every ISoftDeletable entity

public class Cheque : ISoftDeletable { /* IsDeleted, DeletedAt, DeletedBy */ }

db.Cheques.Remove(cheque);              // becomes an UPDATE touching exactly 3 columns
```

## Configuration

None — the manifest toggle (`features.softDelete: true`) wires the two calls above.

## Advanced

- **Reading deleted rows:** EF's own `IgnoreQueryFilters()` — no Goldpath wrapper.
- **Hard delete (right-to-erasure flows):**
  ```csharp
  using (GoldpathSoftDelete.Suppress())
  {
      db.Cheques.Remove(cheque);        // really deletes, only inside this scope
      await db.SaveChangesAsync();
  }
  ```
- **AuditTrail interplay:** a soft delete audits as the `IsDeleted false→true` change
  (Modified rows for exactly the 3 touched fields) — one consistent story, same transaction.
- **Undelete:** set `IsDeleted = false` — a plain update, audited like any change.
- **Pitfalls (documented, no magic):** children without the marker still hard-delete when
  cascaded; soft-deleted rows still occupy unique keys (use filtered indexes per provider).

## Providers

Storage is the application's DbContext — any provider Goldpath.Data supports.
