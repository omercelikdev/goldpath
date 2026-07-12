# Runbook — Goldpath.ServiceDefaults (Ring A floor)

## Probe semantics
- `/health/live` — self check only; failing = restart the pod (liveness).
- `/health/ready` — all registered checks; failing = remove from load balancing (readiness).
  Flapping readiness usually means a dependency check (DB/broker) is unstable — check the
  dependency before restarting the service.

## "Service unhealthy" triage
1. `/health/ready` body is masked by design — check details via logs (scope `CorrelationId`)
   or the health metrics on the baseline dashboard.
2. Correlate: take the `X-Correlation-Id` from the failing caller, search logs; the same id
   is on the trace (`goldpath.correlation_id` tag) for the full picture.

## 429 spikes (concurrency guard)
- The guard is collapse protection: a 429 spike = the service is saturated, not "clients are
  too chatty". Check downstream latency first (slow dependency backs up requests).
- Tuning: `Goldpath:ServiceDefaults:RateLimiting:{ConcurrencyLimit,QueueLimit}` — raise only with
  a capacity measurement, never as a reflex.

## Telemetry gaps
- No traces? `OTEL_EXPORTER_OTLP_ENDPOINT` unset or collector unreachable; Development uses
  the console exporter instead. Sampling: profile-driven (Minimal 1% / Standard 10% / Full 100%).
