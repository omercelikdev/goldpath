# Baseline Dashboard & Alerts — Goldpath.ServiceDefaults

Every Goldpath service is born observable; this defines the baseline every service gets.
(Grafana JSON is provisioned with the Template CI work — tracked there, not silently missing.)

## Dashboard panels (RED + saturation)
| Panel | Source |
|---|---|
| Request rate / error ratio / duration p50-p95-p99 | `http.server.request.duration` (ASP.NET OTel) |
| 429 rejections | rate-limiter rejection count |
| Readiness status + flap count | `/health/ready` probe results |
| Outbound dependency latency/errors | `http.client.request.duration` |
| Runtime saturation | GC, thread pool queue, CPU (runtime instrumentation) |

## Default alert rules
| Alert | Condition (suggested) | Severity |
|---|---|---|
| Error ratio high | 5xx / total > 2% for 5m | critical |
| Readiness flapping | ready↔unready ≥ 3 in 10m | critical |
| Saturation | 429 rate > 0 sustained 5m | warning |
| Latency regression | p95 > NFR target (manifest `nfr.p95LatencyMs`) for 10m | warning |
