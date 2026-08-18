# Approvals — Ops Runbook

## "Why is this request still pending" triage
1. Read the request's TRAIL (`GetAsync` / the admin surface): every routing, escalation and
   delegation step is in it with its actor and timestamp — the answer is usually the last line.
2. Pending age counts from `PendingSince` (it RESETS on escalation — a rung's deadline is that
   rung's, not the request's lifetime). Compare against the ladder's `EscalateAfter` per rung.
3. If nothing escalates at all: the sweep is a scheduled job — check the Jobs console for the
   escalation job's last run before suspecting the engine.

## Decision refused — outcome decoding
- `FourEyesViolation` — the requester tried to decide their own request. Not a bug, the rule.
- `WrongRole` — decider holds neither the pending rung's role nor an active delegation.
  Check delegation expiry (`Until` is absolute UTC) before re-granting roles.
- `NotPending` — someone else decided first, or it expired. The trail says which.

## Escalation storms
A spike of `GoldpathApprovalEscalated` events means a rung stopped deciding (vacation,
role change, worklist not being watched). Delegate that rung's holder or shorten nothing —
fix the staffing; the ladder's deadlines are the SLA you declared.

## Expiries
`GoldpathApprovalExpired` at the top rung is a governance signal, never noise: the request
needed the highest authority and did not get it in time. Alarm on expiry count > 0.

## Store
The in-memory store loses state on restart — it is for tests and single-node demos.
Compose a database-backed `IGoldpathApprovalStore` before production.
