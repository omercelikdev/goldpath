# Module RFC: Goldpath.DataProtection

> Status: v1.0 accepted & implemented — D1–D5 approved by Ömer (2026-07-05): own attributes
> on the Microsoft compliance model · composed redactors (erase default, HMAC opt-in) ·
> per-property audit masking recommended over namesOnly · code-based catalog · encryption
> deferred to Ring C. Ring B (Phase 2, item 11) · Effort M
> Dependencies: Goldpath.Abstractions (classification marker), Goldpath.Data (save-contributor seam),
> Goldpath.AuditTrail (mask integration — replaces the NamesOnly stopgap),
> Microsoft.Extensions.Compliance.Classification + Redaction (composed — ADR-0003; MIT)

## 1. Scope / Non-Goals

**Scope — one classification, every sink masked.** A property is declared personal/sensitive
data ONCE; every place a value could leak — audit change rows, logs, Mediant audit entries —
masks it consistently:

- **Classification:** `[GoldpathPersonalData]` / `[GoldpathSensitiveData]` attributes (annotate mode)
  and a fluent code catalog (catalog mode — for scaffolded/legacy entities that must not be
  touched). Both produce the same classification set; both can coexist.
- **Redaction:** composed `Microsoft.Extensions.Compliance.Redaction` — erasure (default) and
  HMAC pseudonymization (opt-in: correlation survives, the value doesn't).
- **AuditTrail integration:** classified properties are masked per-property in change rows
  (old/new → redacted). This REPLACES the all-or-nothing `namesOnly` switch as the
  recommended posture; `namesOnly` stays as the global fallback.
- **Logging integration:** classification flows into MEL log redaction (compliance
  classification on `[LogProperties]` paths — Ring A logging stays MEL+OTel, foundation §7.1).
- **Mediant alignment:** properties carrying Mediant's `[SensitiveData]` are recognized by
  NAME (no hard dependency, same approach as the analyzers); classified property names are fed
  into `AuditBehaviorOptions.SensitivePatterns` when the Mediant audit path is enabled.

**Non-Goals (v1, written not silent):** field-level encryption at rest (D5 — Ring C),
API response shaping/authorization (a different concern), data retention/erasure workflows
(GDPR right-to-erasure is served by SoftDelete's `Suppress()`; retention is the Ring C
archival module), key management (ASP.NET DataProtection key rings are Ring A infra, not
this module).

## 2. Seam Map

| Seam | Touch |
|---|---|
| EF interceptor | AuditTrail's change-row contributor consults the classification set before writing old/new values |
| Mediant pipeline | classified names → `SensitivePatterns`; `[SensitiveData]` recognized on request/response types |
| HTTP middleware | none in v1 (response shaping is out of scope) |
| MassTransit filter | none in v1 — GP0701 (analyzer) covers the boundary instead |

## 3. Manifest Surface
```yaml
dataProtection:
  mode: annotate            # annotate | catalog | both (D4)
  redactor: erase           # erase | hmac (D2)
  auditMasking: true        # classified properties masked in audit change rows (D3)
```

## 4. API Surface
```csharp
// Abstractions (single micro-dependency: Microsoft.Extensions.Compliance.Abstractions — D1)
[GoldpathPersonalData]                    // GDPR-relevant identity data
[GoldpathSensitiveData]                   // broader secrecy (financial, health…)
public string NationalId { get; set; }

// DataProtection package
builder.AddGoldpathDataProtection(o =>
{
    o.UseHmacRedaction(keyFromConfig);            // opt-in pseudonymization (D2)
    o.Catalog(c =>                                 // catalog mode (D4)
    {
        c.Classify<LegacyCustomer>(x => x.TaxNumber, GoldpathDataClass.Personal);
    });
});
// AuditTrail picks the classification set up automatically when both modules are enabled.
```

## 5. Analyzer Rules (SHIPPED — analyzer batch 3, 2026-07-06)
| ID | Rule | Severity |
|---|---|---|
| GP0701 | Classified property on an `IIntegrationEvent` — PII crossing the service boundary | warn |
| GP0702 | Classified property on an entity while the DataProtection module is absent from the compilation | info |

## 6. Ops Package ("no runbook = no module")
- **Metrics:** `goldpath_dataprotection_redactions_total` (by sink: audit/log), catalog size
- **Runbook:** HMAC key rotation procedure (old hashes stop correlating — by design);
  "why is this value masked" triage (annotate vs catalog precedence); classification review
  checklist for new entities
- **Dashboard:** redaction rate per sink (a sudden drop = a mapping regression signal)

## 7. Test Plan
- Classification set: annotate + catalog + both produce identical sets; catalog wins ties
- AuditTrail interplay (SQLite + Postgres): classified property change → change row exists,
  old/new masked, unclassified properties unaffected; `namesOnly` still works as fallback
- Redactors: erase produces the fixed token; HMAC is deterministic per key + differs across keys
- Mediant alignment: classified names appear in `SensitivePatterns`; a type with Mediant
  `[SensitiveData]` (stubbed by name) is picked up without a Mediant reference
- Logging: a classified property logged via the MEL path arrives redacted
- License gate stays GREEN (Microsoft.Extensions.Compliance.* is MIT)

## 8. DoD
- [x] Decisions locked (D1–D5) · package + tests green (11: annotate/catalog/Mediant-by-name
      classification with catalog tie-win, erase token + passthrough, HMAC determinism across
      keys + loud no-key failure, audit interplay masked/unmasked/namesOnly on SQLite,
      SensitivePatterns feed, MEL [LogProperties] redaction end-to-end) · PublicAPI locked
- [x] AuditTrail masking integration replaces the NamesOnly recommendation (docs updated)
- [x] README (4 sections) + ops runbook + CHANGELOG · GP0701/0702 to backlog · GM wiring tracked
- Note (recorded, not silent): Abstractions gained its single micro-dependency
  (Microsoft.Extensions.Compliance.Abstractions) per D1 — the csproj description now says so.
  Postgres interplay coverage rides the existing integration-suite pattern; the entity-level
  masking path is provider-agnostic (contributor runs before the provider sees values).

## 9. Decision Points (Ömer)

- **D1 — Own classification attributes in Abstractions** (`[GoldpathPersonalData]`/`[GoldpathSensitiveData]`
  built on `Microsoft.Extensions.Compliance.Classification.DataClassification`), Mediant's
  `[SensitiveData]` recognized by name. Alternative: depend on Mediant core for the attribute —
  rejected: Abstractions must stay dependency-light and L1-safe. **Recommendation: own attributes.**
- **D2 — Redaction composed from Microsoft.Extensions.Compliance.Redaction** (ErasingRedactor
  default; HmacRedactor opt-in for correlation-preserving pseudonymization). Writing our own
  redactors would violate ADR-0003. **Recommendation: compose.**
- **D3 — AuditTrail: per-property masking becomes the recommended posture**, `namesOnly`
  remains as the blunt global fallback. **Recommendation: yes.**
- **D4 — Catalog mode is code-based fluent registration** (type-safe, refactoring survives),
  NOT config-file mappings (stringly-typed, drifts silently). The manifest only toggles the
  module and defaults. **Recommendation: code-based.**
- **D5 — Field-level encryption at rest is deferred to Ring C** (key management + search/index
  implications are a module of their own; masking ≠ encryption and conflating them breeds
  false security). **Recommendation: defer, keep written here.**
