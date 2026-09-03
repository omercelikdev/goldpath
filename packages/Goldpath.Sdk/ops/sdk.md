# Sdk — Ops Runbook

`Goldpath.Sdk` is the platform seam product modules and the admin surfaces ride: the admin
guard (`AdminSurfaceGuard`), tenant scoping for admin reads (`AdminTenantScope`), paging
clamps, and the frozen result envelope. It emits no meter; its operational signal is two
log lines, both deliberate.

## `{Prefix} is mapped WITHOUT the ops policy (exposeUnsecured: true)`
Logged ONCE at startup per admin prefix mapped with the opt-out. It is not an error — it is
the inventory. Acceptable only behind an authenticating boundary (mTLS, a gateway, the
cluster). In a central log search, `"mapped WITHOUT the ops policy"` lists every head whose
admin surface is open; the answer to "why is this here" must be a boundary you can name.
The templates write the opt-out only for `--auth none` shapes and say so in a comment next
to the line; the console smoke's secured host proves the guarded branch answers 401.

## `Cross-tenant admin access by {Actor}: requested tenant {Requested} (ambient {Ambient})`
Logged on every admin read where a holder of the all-tenants role (`goldpath-ops-all-tenants`)
scoped a call to a tenant other than the ambient one (`?tenant=`), or to all tenants. This
is the audit line for privileged reads: alert on its RATE per actor (a script walking every
tenant looks exactly like an exfiltration), and reconcile actors against the role's
membership periodically.

## Refusals you will see in the envelope
- `400` with a teaching body on a multi-tenant app whose call carried no tenant — the
  contract's R1 rule; the console renders it as "refused", never "absent".
- `403` for a principal without `goldpath-ops` — grant the role; the guard is uniform
  across every admin surface, so a per-surface exemption is not a thing.

## Paging
Every list endpoint clamps `take` to `[1, 500]` (`AdminPaging`). A client asking for more
gets 500 rows and no error — by design; large reads are a reporting concern, not an admin
one. If a triage view needs more, that is a contract revision, not a clamp change.

## Dashboard
None of its own: the console board (`Goldpath.Console/ops`) shows the admin traffic and
refusals the guard produces; the two log lines above are log-search alerts, not panels.
