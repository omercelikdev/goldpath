# Goldpath.Auth

Ring B auth composition: your IdP composed, never rewritten. OIDC/JWT bearer (default) or a
minimal API-key handler, a secure-by-default fallback policy, token–tenant binding with
MultiTenancy, and Mediant's `[Authorize]` fed from the same principal — fail-closed at both
the endpoint and the command level.

## Getting started

```csharp
builder.AddGoldpathAuth();          // strategy per manifest; every endpoint now demands a principal
app.UseGoldpathAuth();              // after UseGoldpathMultiTenancy(): authN → tenant binding → authZ

app.MapGet("/public/rates", ...).AllowAnonymous();   // the explicit, greppable escape

[Authorize(Roles = "loan-officer")]                  // Mediant command level, same principal
public sealed record ApproveLoanCommand : ICommand<Result>;
```

## Configuration

```jsonc
"Goldpath": {
  "Auth": {
    "Strategy": "OpenId",                  // OpenId | ApiKey | None
    "Authority": "https://idp.bank.com/realms/prod",
    "Audience": "orders-api",              // unset skips audience validation
    "TenantClaim": "goldpath_tenant",           // token–tenant binding claim
    "BindTenant": true,
    "ApiKeys": { "batch-runner": "<from secret store>" }
  }
}
```

## Advanced

- **Secure by default:** the fallback policy denies anonymous everywhere; health probes stay
  exempt (`MapGoldpathDefaultEndpoints` marks them `AllowAnonymous` explicitly). Fail-open auth is
  how internal APIs end up on the internet.
- **Token–tenant binding:** an authenticated token carrying `goldpath_tenant=acme` on a request
  resolved to `globex` → 403 + `goldpath_auth_tenant_binding_rejects_total` (alert on any
  non-zero — the write-guard pattern). Claim absent → binding not enforced, so
  gateway-owns-tenancy topologies stay valid.
- **ApiKey is deliberately minimal** (internal/legacy callers): named clients, constant-time
  comparison, principal = client name so audit rows say WHO. No key-management UI — Ring C.
- **Claim-mapping tolerance:** the Mediant authorization context reads both mapped
  (`ClaimTypes.Role`) and raw (`role`/`roles`, `sub`) claim types — behavior never depends
  on a handler mapping flag.
- **OpenAPI:** the security scheme (bearer/apiKey) is added to generated documents on
  net10.0 targets (document transformers arrived after net8's OpenApi package).
- **Strategic deferrals** (tracked, schema-rejected until built): `saml` (SSO/portal-layer
  concern; APIs speak JWT via token exchange), `ldap` (sits behind the IdP).

## Providers

None of ours — that's the point. `openid` composes ASP.NET JwtBearer against ANY compliant
IdP; `apikey` is self-contained; `none` wires nothing (mTLS/gateway topologies).
