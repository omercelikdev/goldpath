# SoftDelete — Ops Runbook

## Undelete procedure
A soft delete is three columns, so undelete is their reversal — through code, with the
filter lifted:

```csharp
var row = await db.Orders.IgnoreQueryFilters().SingleAsync(o => o.Id == id);
row.IsDeleted = false; row.DeletedAt = null; row.DeletedBy = null;
await db.SaveChangesAsync();   // audits as the IsDeleted true→false change
```

Raw SQL works in an incident (`UPDATE ... SET "IsDeleted" = false ...`) but skips the audit
trail — prefer the code path whenever the app still runs.

## Unique indexes with deleted rows
A plain unique index counts deleted rows, so "delete then recreate" hits a violation. Use a
filtered index: `CREATE UNIQUE INDEX ... ON "Orders" ("Number") WHERE "IsDeleted" = false;`
(SQL Server: `WHERE [IsDeleted] = 0`). Symptom: unique violations on inserts whose live
duplicate does not exist in any screen — the collision is with a soft-deleted ghost.

## The deleted-row ratio (the archival signal)
`SELECT count(*) FILTER (WHERE "IsDeleted"), count(*) FROM "Orders";` per marked table.
A climbing ratio means every query drags dead rows through the `!IsDeleted` filter — that
is the Archival module's cue (age out, chain-verify, then really delete), not a reason to
scatter `Suppress()` calls.

## `Suppress()` — when a hard delete is legitimate
`GoldpathSoftDelete.Suppress()` is for right-to-erasure flows, greppable on purpose: audit
its use by reviewing every call site, and note a suppressed hard delete of an `IAuditLogged`
entity still writes its `Deleted` change rows in the same transaction — the erasure itself
leaves evidence. Never wrap it around convenience cleanup.

## Signals
No meter — the conversion is a SaveChanges rewrite (order −100, before audit, so the trail
records the converted truth: a soft delete audits as `Modified`, never `Deleted`). Missing
`ApplyGoldpathSoftDelete()` in `OnModelCreating` is analyzer rule GP0601 — deleted rows
appearing in screens means the filter is absent, not broken.
