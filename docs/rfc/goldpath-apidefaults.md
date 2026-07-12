# Module RFC: Goldpath.ApiDefaults

> Status: v1.0 accepted — D1-D4 approved by Ömer (2026-07-03): URL-segment versioning ·
> opaque cursor without total count · kebab/camel/enum-as-string · build-time OpenAPI export
> + dev-only endpoint. Sector-neutral by design (banking is one persona, not the target).
> Phase 1 item 3 · Ring: golden-path core
> Dependencies: Goldpath.Abstractions, Goldpath.ServiceDefaults, Mediant.AspNetCore (composed — ADR-0003)

## 1. Scope / Non-Goals

**Scope — the API surface conventions every Goldpath service is born with:**

| Pillar | Built on | Corporate opinion |
|---|---|---|
| Versioning | `Asp.Versioning.Http` | URL segment (`/api/v1/...`), default v1, deprecated-version response headers |
| **Cursor pagination** | Own primitive (wire contract + codec here; the `IQueryable` executor lands in Goldpath.Data) | The out-of-the-box performance primitive: keyset-based, opaque cursor — offset pagination is the analyzer-flagged anti-pattern |
| Endpoint conventions | Mediant `[HttpEndpoint]` + minimal APIs | Versioned route groups, kebab-case routes, standard endpoint filters |
| Validation errors | ProblemDetails (RFC 9457) | `errors` extension dictionary; same shape whether raised by FluentValidation (Mediant behavior) or model binding |
| JSON defaults | System.Text.Json | camelCase, enums as strings, ignore-null writes, strict number handling |
| OpenAPI export | `Microsoft.AspNetCore.OpenApi` (net10) | Deterministic document per version; build-time export artifact feeds Spec Engine `drift` (the "single source of truth" CI proof) |

**Non-Goals:** No auth/authorization (own module); no rate limiting (ServiceDefaults);
no HATEOAS; no OData/GraphQL (deliberate — golden path is REST+events); no response
envelope beyond pagination (plain resources, ProblemDetails for errors).

## 2. Seam Map
HTTP seam only. `MapGoldpathApi()` creates the versioned route group and applies conventions;
Mediant `[HttpEndpoint]` commands/queries map inside it. Pagination executor integration
arrives with Goldpath.Data (keyset `ToPageAsync` reading the contract defined here).

## 3. Manifest Surface
Not a toggle (golden-path core). Future: `api.versioning.deprecated: [v1]` metadata (v2 of this RFC).

## 4. API Surface

```csharp
builder.AddGoldpathApiDefaults();                       // versioning + JSON + validation shape + OpenAPI
var api = app.MapGoldpathApi();                         // versioned root group: /api/v{version}
api.MapMediantEndpoints();                          // Mediant commands/queries inside the group

// Pagination wire contract (executor in Goldpath.Data):
public sealed record PageRequest(string? Cursor, int Size = 50);        // Size clamped: 1..200
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor, int Size);
public static class GoldpathCursor                                            // opaque codec
{
    public static string Encode<TKey>(TKey lastKey, ...);
    public static bool TryDecode<TKey>(string cursor, out TKey key, ...);
}
```

Multi-target `net8.0;net10.0`; the OpenAPI pillar is `net10.0`-only (net8 L1 consumers keep
their existing Swashbuckle setup — documented limitation, not silent).

## 5. Analyzer Rules (defined here, shipped in Goldpath.Analyzers)
| ID | Rule | Severity |
|---|---|---|
| GP0201 | Endpoint returns an unbounded collection (`IEnumerable<T>`/`List<T>` from a query without `Page<T>`) | warn |
| GP0202 | `Skip()/Take()` offset pagination in an API-serving query — use the keyset primitive | warn |
| GP0203 | Route literal violates kebab-case convention | info |

## 6. Ops Package
Adds to the ServiceDefaults baseline dashboard: request rate by API version (deprecated-version
traffic panel — "who still calls v1"), page-size distribution. Runbook: version deprecation
procedure (headers → comms → sunset), cursor-invalid (400) spike triage.

## 7. Test Plan
- Unit: cursor codec round-trip + tamper → `TryDecode=false`; `PageRequest` clamping
- Integration: versioned route group resolves (`/api/v1/...`); validation error shape
  (`errors` dictionary, RFC 9457, correlationId present); JSON conventions (camelCase,
  enum-as-string) asserted on the wire; OpenAPI doc generated & deterministic (two builds → identical)
- Golden manifest impact: GM smoke asserts one versioned endpoint + one paginated endpoint
  end-to-end (arrives with the template)

## 8. DoD
- [x] RFC decisions locked (§9 — D1-D4 approved 2026-07-03)
- [x] Package + tests green (17/17: cursor codec round-trip/tamper/arity/url-safety, clamping,
      versioned group, JSON wire assertions, dev-only OpenAPI + determinism), PublicAPI locked
- [x] OpenAPI drift input: dev endpoint + deterministic doc verified; build-time export artifact
      wiring lands with the CI templates (Phase 1 item 7) — tracked, not silent
- [x] README (4 sections) + CHANGELOG · analyzer specs (GP0201-0203) to Goldpath.Analyzers backlog

## 9. Decision Points (Ömer)

- **D1 — Versioning strategy:** URL segment (`/api/v1/orders`) vs header vs query string.
  **Recommendation: URL segment** — cache/proxy-friendly, visible in logs and specs, the
  enterprise default; banking API gateways route on paths. Header versioning stays possible
  per-service later; the golden path picks one.
- **D2 — Pagination contract:** Opaque base64url cursor (server-controlled keyset payload;
  tampering → 400) + response `{ items, nextCursor, size }` **without a total count** —
  `COUNT(*)` on large tables is the offset trap reborn; consumers needing totals get an
  explicit separate aggregate endpoint. **Recommendation: opaque cursor, no total.**
- **D3 — Wire casing conventions:** kebab-case routes, camelCase JSON, enums as strings.
  **Recommendation: yes** (industry default; Spec Engine naming rules will enforce).
- **D4 — OpenAPI export mode:** Build-time export artifact (deterministic, feeds `drift` in CI)
  + interactive endpoint in Development only — not exposed in production.
  **Recommendation: build-time + dev-only endpoint.**
