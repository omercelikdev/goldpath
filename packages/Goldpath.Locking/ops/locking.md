# Locking — Ops Runbook

## "The job didn't run anywhere" triage
1. Check `goldpath_lock_acquire_total{outcome="timeout"}` — everyone timing out means someone
   HOLDS the lock. Held or leaked?
2. Inspect the store directly:
   - Redis: `KEYS goldpath:*:lock:*` (the value carries the holder's lease)
   - Postgres: `SELECT * FROM pg_locks WHERE locktype = 'advisory';`
   - SQL Server: `SELECT * FROM sys.dm_tran_locks WHERE resource_type = 'APPLICATION';`
3. Leaked (holder crashed): Redis leases expire on their own; Postgres/SqlServer advisory
   locks die with the SESSION — a leak here means a zombie connection: kill it at the
   database, not in the app.

## The paused-holder hazard (read before designing a locked flow)
A GC pause / VM freeze can suspend a holder past its lease; a second instance then acquires
legitimately. Locks here are mutual exclusion for EFFICIENCY. Design the protected work
idempotent (the Idempotency module exists exactly for this) — never treat the lock as the
sole correctness barrier for money-moving operations.

## Contention signal
`goldpath_lock_wait_seconds` climbing + timeout rate rising = the protected section grew or the
schedule got denser. Shorten the critical section before reaching for longer timeouts —
longer timeouts convert contention into latency, silently.

## Provider notes
- Redis: lock lifetime is lease-based (auto-extend while the handle lives).
- Postgres/SqlServer: session-scoped — connection pool exhaustion can masquerade as
  "cannot acquire"; watch pool saturation alongside lock metrics.
