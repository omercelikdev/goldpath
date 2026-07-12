# Module RFC: Goldpath.ServiceDefaults

> Status: v1.0 accepted — D1-D4 approved by Ömer (2026-07-03): NuGet package · health always mapped (masked) · global concurrency guard ON · dev console exporter+full sampling · Ring: A (always-on floor, Phase 1 item 2)
> Dependencies: Goldpath.Abstractions · Microsoft/OTel packages (configured, not wrapped — ADR-0003)

## 1. Scope / Non-Goals

**Scope — the Ring A floor, one call, non-removable.** Corporate-opinionated configuration of:

| Pillar | Built on | Corporate opinion |
|---|---|---|
| Telemetry | OpenTelemetry (traces/metrics/logs) | OTLP exporter default; resource attrs (service/version/env) auto-set; PII-safe log enrichment; console exporter in Development |
| Health | ASP.NET health checks | `/health/live` (self) + `/health/ready` (registered checks); K8s/OpenShift probe semantics; detail payload masked outside Development |
| Errors | `Microsoft.AspNetCore.Http.ProblemDetails` | RFC 9457 everywhere; correlation id as extension member; no stack traces outside Development |
| Correlation | Custom middleware (thin) | Accept or generate `X-Correlation-Id` (GoldpathHeaders), echo on response, enrich logs + current Activity; W3C traceparent remains the tracing truth |
| HTTP resilience | `Microsoft.Extensions.Http.Resilience` | Standard pipeline (retry+timeout+CB) as HttpClient default; criticality-based profiles arrive with the externalSystems wiring (Phase 2) |
| Rate limiting | `Microsoft.AspNetCore.RateLimiting` | Global concurrency guard on by default (generous, protects against collapse, not throttling); named policies opt-in per endpoint; 429 as ProblemDetails |
| Service discovery | `Microsoft.Extensions.ServiceDiscovery` | On for HttpClient (inert outside Aspire/K8s) |

**Non-Goals:**
- No auth (own module), no API versioning/pagination (ApiDefaults), no EF/messaging wiring (Data/Messaging)
- Not an Aspire-style copy-into-solution project (see decision D1)
- Pillars are not individually disableable (Ring A definition). Tuning ≠ disabling: options
  expose sampling, limits, header behavior — never an off switch. Escape hatch: everything is
  standard Microsoft primitives, so adding/overriding via normal DI remains possible (configure-not-wrap).

## 2. Seam Map
Registers the HTTP seam baseline (middleware order: correlation → rate limiting → exception/ProblemDetails)
and the process-wide telemetry/health plumbing. Consumed by every other module; touches no data/message seam itself.

## 3. Manifest Surface
Not a toggle (always present in generated apps). Reads the `observability` profile:
`minimal | standard | full` → sampling rate, metric set, log level defaults; `exporters` list.

## 4. API Surface
```csharp
builder.AddGoldpathServiceDefaults();                       // IHostApplicationBuilder — all pillars
builder.AddGoldpathServiceDefaults(o => { o.Observability.Profile = ObservabilityProfile.Full; });
app.MapGoldpathDefaultEndpoints();                          // /health/live + /health/ready
```
`GoldpathServiceDefaultsOptions`: `Observability` (profile, exporters, sampling override),
`RateLimiting` (global concurrency limit, queue length), `Correlation` (accept-inbound on/off).
Public API locked with PublicApiAnalyzers. Multi-target `net8.0;net10.0`.

## 5. Analyzer Rules (defined here, shipped in Goldpath.Analyzers — Phase 1 item 6)
| ID | Rule | Severity |
|---|---|---|
| GP0101 | Manual re-registration of a pillar (`AddOpenTelemetry`, `AddProblemDetails`, …) alongside `AddGoldpathServiceDefaults` — duplicate/conflicting config | warn |
| GP0102 | `new HttpClient()` instead of `IHttpClientFactory` — bypasses resilience + discovery defaults | warn |
| GP0103 | `MapGoldpathDefaultEndpoints` missing while `AddGoldpathServiceDefaults` present (no probes) | warn |

## 6. Ops Package
This module IS the ops foundation: baseline Grafana/Aspire dashboard template (RED metrics,
health, rate-limit rejections), default alert rules (readiness flapping, 5xx ratio, saturation),
runbook: probe semantics, correlation lookup flow, "service unhealthy" triage.

## 7. Test Plan
- Unit: options validation, correlation id generation/propagation logic
- Integration (WebApplicationFactory): correlation round-trip (inbound honored, absent → generated, response echoed, log scope enriched); ProblemDetails shape incl. correlation extension + no stack trace in Production env; `/health/live`+`/ready` status codes and masked payload; 429 returned as ProblemDetails under the concurrency guard
- Golden manifest impact: every GM exercises it implicitly; smoke asserts `/health/ready` green + one traced request visible

## 8. DoD
- [x] RFC decisions locked (§9 — D1-D4 approved 2026-07-03)
- [x] Package + options + integration tests green (7/7: correlation ×3, health, ProblemDetails leak-free, 429 guard, options binding)
- [x] PublicAPI baseline locked
- [x] Dashboard/alert/runbook templates committed (`ops/`)
- [x] README (4 sections) + CHANGELOG
- [x] Analyzer rule specs (GP0101-0103) defined in §5 — implementation lands with Goldpath.Analyzers (Phase 1 item 6)

## 9. Decision Points (Ömer)
- **D1 — Delivery form:** NuGet package (central updates via Renovate; extension via options/DI)
  vs Aspire-style copied project (user-editable, but drifts per solution and kills the update story).
  **Recommendation: NuGet package.** The copied-project pattern is the template-drift trap.
- **D2 — Health endpoints in production:** Always mapped (K8s/OpenShift probes need them) with
  detail masking + optional port restriction, vs dev-only (Aspire template default).
  **Recommendation: always mapped, masked.**
- **D3 — Rate limiting default:** Global concurrency guard ON with generous defaults
  (collapse protection) + named policies opt-in, vs registered-but-inactive.
  **Recommendation: ON.** An enterprise floor that ships with no back-pressure protection
  fails its own promise; generous limits keep it invisible until an incident.
- **D4 — Telemetry in Development:** console exporter + always-on sampling for local DX,
  OTLP in every other env. **Recommendation: yes.**
