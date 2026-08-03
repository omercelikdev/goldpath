# AuditTrail — Ops Runbook

## "The change has no audit row" triage
1. Is the entity marked? Change rows are written only for `IAuditLogged` entities (stamps
   only for `IAuditedEntity`). An unmarked entity audits nothing — by design, not by loss.
2. Is the module composed? `AddGoldpathAuditTrail<TContext>()` plus
   `modelBuilder.AddGoldpathAuditLog()` — a missing model call fails at startup, a missing
   registration audits nothing silently.
3. Values present but `NULL`? Either `EntityValues: NamesOnly` (the blunt global fallback)
   or the DataProtection module redacted that member — check the catalog before suspecting
   the writer. `Added` rows have no old value and `Deleted` rows no new value, correctly.
4. The row commits OR DIES with the change it describes (same DbContext, same transaction) —
   there is no buffer to drain and no lag to wait out. If the business row is there and the
   audit row is not, the entity was not marked at write time.

## The correlation walk
`GoldpathAuditLog.CorrelationId` carries `goldpath.correlation_id` (or the trace id): one
`SELECT * FROM "GoldpathAuditLog" WHERE "CorrelationId" = '…' ORDER BY "Timestamp"` lines
the entity changes up beside the HTTP request and the command-level audit that Mediant's
`[Auditable]` store wrote — two levels, one story.

## Growth and retention
One row PER PROPERTY per change: a hot table with wide updates grows this table fast.
Watch `SELECT pg_size_pretty(pg_total_relation_size('"GoldpathAuditLog"'));` and the oldest
`Timestamp`. Retention is a POLICY knob (banking keeps years) — when age-out is due, the
Archival module is the answer (chain-verified export, then delete); do not hand-roll a
DELETE job beside it. Reads ride `(EntityType, EntityKey)` and `(Timestamp)` indexes.

## Signals
No dedicated meter — the audit write is part of the business transaction, so its cost shows
up as SaveChanges latency. If p99 write latency climbs after marking a wide entity, that is
the per-property fan-out working as specified; mark narrower entities or mask members, don't
buffer the trail.
