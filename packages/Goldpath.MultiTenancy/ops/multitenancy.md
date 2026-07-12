# MultiTenancy — Ops Runbook

## "Tenant sees no data" triage (in this order)
1. **Resolution:** did the request reach the app with a tenant? Check `goldpath_tenant_unresolved_total`
   and the 400 rate. Behind a gateway, verify the `X-Goldpath-Tenant` header survives the hop
   (header allow-lists strip unknown headers more often than you think).
2. **Filter:** is the query running with the RIGHT ambient tenant? A background job without
   `GoldpathTenant.Use(...)` sees zero rows — by design, not a bug.
3. **Stamp:** were the rows written under the tenant you expect? Check the `TenantId` column
   raw (one `Bypass()` scope in a REPL/admin tool).

## Guard trips (`goldpath_tenant_write_guard_trips_total` > 0)
Treat as a security event until proven otherwise:
1. Find the `GoldpathCrossTenantWriteException` in logs — the message names the entity, the row's
   owner, and the acting ambient tenant.
2. Legitimate case (support tooling, migration script) → the fix is `GoldpathTenant.Use(tenant)`
   at the call site, making the acting tenant explicit and reviewable.
3. Anything else → involve security; correlate the request path/user via the audit trail
   (audit rows carry both tenant and user).

## Bypass audit
`Bypass()` is greppable by design. Periodically: `grep -rn "GoldpathTenant.Bypass" src/` — every
hit should be an admin/reporting flow with its own authorization. New hits in a diff are a
review flag, and GP0903 (analyzer backlog) will flag filter-dodging queries.

## Unresolved-rate spike
A sudden `goldpath_tenant_unresolved_total` climb is either a client misconfiguration (header
dropped after a gateway change) or probing. Split by path: probing hits many paths shallowly;
misconfiguration hammers few paths deeply.
