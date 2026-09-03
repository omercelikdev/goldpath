# ApiDefaults — Ops Runbook

The floor's HTTP posture: URL-segment API versioning (`/api/v{n}/…`, Asp.Versioning) with
the `api-supported-versions` / `api-deprecated-versions` response headers, RFC 9457 problem
details, the build-time OpenAPI export, and the wire conventions (camelCase, enums as
strings, cursor pagination). The signals below are ASP.NET Core's own request meter, which
`Goldpath.ServiceDefaults` exports; nothing here invents a metric.

## "Who still calls v1" — deprecated-version traffic
The version is IN the route template, so the request meter carries it as `http_route`:
```promql
sum(rate(http_server_request_duration_seconds_count{http_route=~"/api/v1/.*"}[1h])) by (http_route)
```
Zero for a week across every route of a version is the evidence the sunset step needs.

## Version deprecation procedure
1. **Headers first** — mark the version deprecated in the versioning setup; every response
   now carries `api-deprecated-versions: 1`. Clients that read headers see it immediately.
2. **Communicate** — the deprecation date and the replacement in the API portal / release
   notes; the traffic panel above is the list of who has not moved.
3. **Sunset** — remove the version's endpoints in a train boundary (a MAJOR for the API
   contract, even when the packages are not); the OpenAPI export shrinks in the same PR,
   which is how the spec-drift gate proves nothing else changed.
Never remove a version whose panel is not flat: an unmapped route answers 404 with no
problem-details body, and the client's first sign is a production incident.

## Cursor-invalid (400) spike
A cursor is opaque and signed by shape, not by secret: a client that persists a cursor
across a deploy that changed the page's ORDER BY gets `400` with the problem type
`cursor-invalid`. A spike right after a deploy is that; a steady trickle is a client
building cursors by hand. Neither is data loss — the first page still answers.
```promql
sum(rate(http_server_request_duration_seconds_count{http_response_status_code="400"}[5m])) by (http_route)
```

## Problem details discipline
Every non-2xx carries a problem-details body with `type`, `title`, `status`, `traceId`.
An operator triages by `traceId` (one search in the trace store); a body WITHOUT one is a
middleware ordering bug — `AddGoldpathApiDefaults` must run before the app's own handlers.

## Dashboard
No board of its own on purpose: the RED panels live in the ServiceDefaults baseline
(`dashboard-and-alerts.md`), and the two queries above are the only version-shaped additions —
paste them into that board rather than maintaining a second one.
