# Central Logging Architecture — Reference (Goldpath.ServiceDefaults)

Decision (foundation §7.1): **MEL + OpenTelemetry, deliberately not Serilog.**
Apps never know the log store — centralization is collector configuration, not code.

```
service (MEL → OTel logs, traceId+correlationId stamped)
   │  OTLP  (OTEL_EXPORTER_OTLP_ENDPOINT)
   ▼
OpenTelemetry Collector (agent/daemonset per environment)
   ├── exporter → the environment's store: Elasticsearch / Loki / Splunk / …
   └── processors: batch, k8s attributes, (optional) redaction as a second PII net
```

## Reference collector config (skeleton)

```yaml
receivers:
  otlp:
    protocols: { grpc: {}, http: {} }

processors:
  batch: {}
  k8sattributes: {}          # pod/namespace enrichment on OpenShift/K8s

exporters:
  # Pick per environment — the SERVICE never changes:
  elasticsearch:
    endpoints: ["https://elastic.internal:9200"]
    logs_index: "logs-%{service.name}"
  loki:
    endpoint: "https://loki.internal/loki/api/v1/push"

service:
  pipelines:
    logs:
      receivers: [otlp]
      processors: [k8sattributes, batch]
      exporters: [elasticsearch]      # or loki — environment's choice
    traces:
      receivers: [otlp]
      processors: [batch]
      exporters: [elasticsearch]
```

## Rules
- Sink choice lives HERE (per environment), never in application code or Goldpath packages.
- Search flow: `X-Correlation-Id` from the caller → logs (CorrelationId scope) → the same id
  is on the trace (`goldpath.correlation_id` tag) — one id walks the whole story.
- PII: masked at the source (Phase 2 DataProtection, aligned with Mediant `[SensitiveData]`);
  collector redaction is a second net, not the primary control.
- Brownfield L1 with Serilog: fine — MEL providers coexist; the golden path stays MEL+OTel.
