# Goldpath.ApiDefaults

API surface conventions of the Goldpath golden path: URL-segment versioning, the keyset
cursor-pagination primitive, JSON wire defaults, and deterministic OpenAPI export (net10).

## Getting started

```csharp
builder.AddGoldpathApiDefaults();

var app = builder.Build();
var api = app.MapGoldpathApi();            // /api/v1/... — versioned root group
api.MapGet("/orders", ...);           // → GET /api/v1/orders
api.MapMediantEndpoints();            // Mediant [HttpEndpoint] commands/queries attach here
```

## Configuration

Deliberately minimal — the conventions ARE the configuration. Page size bounds are
constants of the wire contract (`PageRequest.DefaultSize` = 50, `MaxSize` = 200).

## Advanced

**Cursor pagination** (executor ships with Goldpath.Data; the contract lives here):

```csharp
api.MapGet("/orders", async (string? cursor, int size, OrdersDb db) =>
{
    var request = new PageRequest(cursor, size);
    // Goldpath.Data: keyset ToPageAsync reads/writes GoldpathCursor — no Skip/Take, ever.
    Page<OrderDto> page = await db.Orders.OrderBy(...).ToPageAsync(request);
    return page;                       // { "items": [...], "nextCursor": "…", "size": 50 }
});
```

- Cursors are **opaque**: base64url keyset payloads; a tampered cursor fails
  `GoldpathCursor.TryDecode` → return 400. No total count by design (RFC D2) — expose an
  explicit aggregate endpoint if a consumer truly needs one.
- JSON wire: camelCase, enums as strings, nulls not written.
- OpenAPI: interactive endpoint in Development only; CI consumes the build-time export
  as the Spec Engine `drift` input (net10 targets only — net8 consumers keep their setup).

## Providers

Not applicable.
