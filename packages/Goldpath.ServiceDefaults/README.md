# Goldpath.ServiceDefaults

The Ring A floor of the Goldpath enterprise asset: telemetry, health, RFC 9457 errors,
correlation, HTTP resilience + service discovery, and a global concurrency guard —
Microsoft packages configured with corporate opinion, in one call. Not optional in the
golden path; tunable, never disableable (see `docs/rfc/goldpath-servicedefaults.md`).

## Getting started

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddGoldpathServiceDefaults();

var app = builder.Build();
app.MapGoldpathDefaultEndpoints();   // /health/live + /health/ready
app.Run();
```

Every request now carries an `X-Correlation-Id`, unhandled errors return ProblemDetails
(without internals), HttpClients get retry/timeout/circuit-breaker + service discovery,
and OTLP telemetry flows when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.

## Configuration

Bound from `Goldpath:ServiceDefaults`, then the code callback applies:

```json
{ "Goldpath": { "ServiceDefaults": {
  "Observability": { "Profile": "Standard", "SamplingRatio": null },
  "RateLimiting":  { "ConcurrencyLimit": 1000, "QueueLimit": 100 },
  "Correlation":   { "AcceptInbound": true }
} } }
```

- `Observability.Profile`: `Minimal` (1%) · `Standard` (10%) · `Full` (always-on).
  Development always samples fully and adds a console trace exporter.
- `RateLimiting`: collapse protection, not throttling — generous by default; excess
  requests get a `429` ProblemDetails.

## Advanced

- Middleware order (ahead of user middleware): correlation → exception handler
  (non-Development) → concurrency guard.
- ProblemDetails responses carry `correlationId` and `traceId` extension members.
- Everything is standard Microsoft primitives: extend via normal DI
  (`builder.Services.AddHealthChecks().AddCheck(...)`, additional OTel sources, named
  rate-limit policies) — the floor composes, it does not wrap.

## Providers

Not applicable — exporters follow OTel environment conventions
(`OTEL_EXPORTER_OTLP_ENDPOINT`); no Goldpath-specific providers.
