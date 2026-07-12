# Auth — Ops Runbook

## "Everything is 401" triage (in this order)
1. **Authority reachability:** can the pod resolve/reach the IdP's discovery endpoint?
   (Air-gapped networks: is the IdP inside the segment?) `goldpath_auth_failures_total` reason
   tags tell you: signature failures = key mismatch/rotation; lifetime = clock skew.
2. **Clock skew:** container clock vs IdP clock; JWT validation tolerates 5 min by default.
3. **Audience:** the token's `aud` vs `Goldpath:Auth:Audience` — IdP client misconfig shows up
   here after every realm change.

## Token-binding rejects (`goldpath_auth_tenant_binding_rejects_total` > 0)
Treat as a stolen-token suspicion until proven otherwise:
1. The 403s carry no detail to the caller ON PURPOSE; correlate server-side via the
   correlation id → which principal, which tenant header, which token tenant.
2. One user, many tenants probed → credential theft pattern; kill the session at the IdP.
3. One integration, one wrong tenant constantly → client misconfiguration; fix the caller.

## API key rotation (dual-key overlap)
1. Add the NEW key as a second entry (`"client-name-v2": "<new>"`) — both valid.
2. Move the caller; watch that the old key's usage drops to zero (principal name is the
   client — filter logs by it).
3. Remove the old entry. Never rotate by overwriting in place — that's an outage.

## Fallback-policy surprises
A new endpoint returning 401 "for no reason" IS the system working: secure by default.
The fix is a conscious decision — a token, or an explicit `[AllowAnonymous]` (which GP1201
will flag for inventory once analyzer batch 3 ships).
