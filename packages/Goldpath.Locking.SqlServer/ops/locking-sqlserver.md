# Locking.SqlServer — Ops Runbook

The SQL Server provider for `Goldpath.Locking` (Medallion `sp_getapplock` application locks
on the app database — zero new infrastructure). The metrics and the dashboard are the
Locking module's (`goldpath_lock_acquire_total{outcome}`, `goldpath_lock_wait_seconds`,
`Goldpath.Locking/ops/grafana-locking-dashboard.json`); this runbook is the provider's
half: what an applock looks like from the server and what goes wrong with it.

## Inspecting a held lock
```sql
SELECT resource_description, request_mode, request_status, request_session_id
FROM sys.dm_tran_locks
WHERE resource_type = 'APPLICATION';
```
The resource description carries the Goldpath lock name. `request_status = WAIT` rows are
the contention the timeout counter is about to record.

## "Job didn't run anywhere" (lock leaked vs held)
An applock is SESSION-owned by this provider: it dies with the connection, crash included —
a "leaked" lock is a session that is still alive. Find it (`sys.dm_exec_sessions` joined on
the session id above); if the holder is a hung process, kill the SESSION, never the lock.
A held lock with a live holder is not a leak — the paused-holder hazard applies: locks are
mutual exclusion, not correctness fences. Design the handler idempotent (the Idempotency
module) rather than shortening the timeout.

## Timeouts climbing
`goldpath_lock_acquire_total{outcome="timeout"}` rising on one lock name = contention on
that resource. Before scaling, check the holder's duration: a handler holding the lock
across a slow dependency call serializes the whole fleet behind that dependency.

## Which database holds the lock
The lock lives in the database named by `ConnectionName` (the app database by default) —
NOT in `master`, unlike the template's Development-migration gate, which locks on `master`
because the app database may not exist yet. A lock taken on the wrong database is invisible
to every other instance: two workers "both holding the lock" are holding two locks.

## Permissions
`sp_getapplock` needs no special grant beyond a connection to the database; a login that
cannot see `sys.dm_tran_locks` needs `VIEW SERVER STATE` for the inspection query only.
