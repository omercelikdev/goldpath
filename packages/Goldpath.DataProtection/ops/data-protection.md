# DataProtection — Ops Runbook

## HMAC key rotation
1. Generate a new 64-byte key, Base64-encode it (`openssl rand -base64 64`).
2. Set `Goldpath:DataProtection:HmacKey` to the new key and BUMP `Goldpath:DataProtection:HmacKeyId`.
3. Deploy. Old hashes stop correlating with new ones — by design; the KeyId embedded in the
   output tells you which key era a hash belongs to. Keep the rotation date in the change log
   so investigators can bound correlation windows.

## "Why is this value masked?" triage
Resolution order (first hit wins):
1. **Catalog** entry for `(Type, Property)` — declared in `AddGoldpathDataProtection(o => o.Catalog(...))`.
2. **Attribute** on the member: `[GoldpathPersonalData]`, `[GoldpathSensitiveData]`, any
   `DataClassificationAttribute`-derived annotation, or Mediant's `[SensitiveData]` (by name).
Unclassified members pass through untouched. `AuditValueMode.NamesOnly` nulls ALL values
regardless of classification — check `Goldpath:AuditTrail:EntityValues` before suspecting the catalog.

## Classification review checklist (new entities)
- Identity fields (national id, name, phone, email, address) → `[GoldpathPersonalData]`
- Financial/health/credentials → `[GoldpathSensitiveData]`
- Scaffolded/generated entities → catalog entry next to `AddGoldpathDataProtection`
- DTOs crossing the service boundary: GP0701 (next analyzer batch) will flag classified
  members on `IIntegrationEvent` types — until then, review manually.

## Signals
- A sudden DROP in masked values in audit rows = a mapping regression (renamed property
  orphaned a catalog entry — the catalog is type-safe, but reflection-era code paths and
  string-based projections are not).
