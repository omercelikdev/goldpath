# Goldpath.DataProtection

Ring B data protection: classify a property as personal/sensitive ONCE and every sink masks
it consistently — audit change rows, redacted logging, Mediant audit patterns. Masking, not
encryption: nothing here changes what is stored in YOUR columns, only what leaks into
observability and audit surfaces.

## Getting started

```csharp
builder.AddGoldpathDataProtection();

public class Customer
{
    [GoldpathPersonalData]                    // GDPR-relevant identity data
    public string? NationalId { get; set; }

    [GoldpathSensitiveData]                   // financial / health / confidential
    public string? Salary { get; set; }
}
```

With `Goldpath.AuditTrail` enabled, change rows for classified properties arrive masked (`***`)
while every other property keeps full old→new values — no configuration, the modules find
each other through the `IGoldpathDataProtector` seam.

## Configuration

```csharp
builder.AddGoldpathDataProtection(o =>
{
    // Legacy/scaffolded entities whose source must not be touched:
    o.Catalog(c => c.Classify<LegacyCustomer>(x => x.TaxNumber, GoldpathDataClass.Personal));

    // Pseudonymization instead of erasure — correlation survives, the value doesn't:
    o.UseHmacRedaction(builder.Configuration["Goldpath:DataProtection:HmacKey"]!);
});
```

Manifest: `features.dataProtection: { mode: annotate|catalog|both, redactor: erase|hmac,
auditMasking: true }`. Catalog entries win over annotations on the same member.

## Advanced

- **Redaction is composed** from `Microsoft.Extensions.Compliance.Redaction` (ADR-0003):
  the default `GoldpathErasingRedactor` yields a fixed `***` token (visible in audit rows,
  reveals nothing — not even length); HMAC mode uses the built-in `HmacRedactor`.
- **Log redaction:** the Goldpath attributes ARE Microsoft `DataClassificationAttribute`s, so the
  MEL enrichment path (`builder.Logging.EnableRedaction()` + `[LogProperties]`) redacts them
  natively — one annotation, both sinks.
- **Mediant alignment:** properties carrying Mediant's `[SensitiveData]` are recognized by
  name (no hard dependency); catalog-declared names are fed into Mediant's
  `SensitivePatterns` so command-level audit masks the same members.
- **AuditTrail `NamesOnly`** remains the blunt global fallback; per-property masking is the
  recommended posture (values stay useful, PII stays out).
- **Key rotation (HMAC):** bump `HmacKeyId` with the new key; old hashes stop correlating
  with new ones — by design. Runbook in `ops/`.

## Providers

Not applicable — sinks consume the `IGoldpathDataProtector` seam; absent module, absent service,
unmasked values (compile-time composition).
