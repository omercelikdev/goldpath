# RFC Index

Every module/significant component is born through an RFC before implementation begins.
Template = the eight fixed sections of [goldpath-idempotency.md](goldpath-idempotency.md):

1. Scope / Non-Goals · 2. Seam Map · 3. Manifest Surface · 4. API Surface ·
5. Analyzer Rules · 6. Ops Package · 7. Test Plan (including golden manifest impact) · 8. DoD

| RFC | Module | Status |
|---|---|---|
| [goldpath-abstractions](goldpath-abstractions.md) | Goldpath.Abstractions (foundational) | implemented (0.1.0-preview.1) |
| [goldpath-servicedefaults](goldpath-servicedefaults.md) | Goldpath.ServiceDefaults (Ring A) | implemented (0.1.0-preview.1) |
| [goldpath-apidefaults](goldpath-apidefaults.md) | Goldpath.ApiDefaults (golden-path core) | implemented (0.1.0-preview.1) |
| [goldpath-data](goldpath-data.md) | Goldpath.Data (golden-path core) | implemented (0.1.0-preview.1) |
| [goldpath-messaging](goldpath-messaging.md) | Goldpath.Messaging (golden-path core) | implemented (0.1.0-preview.1) |
| [goldpath-analyzers](goldpath-analyzers.md) | Goldpath.Analyzers (executable standards) | implemented (0.1.0-preview.1) |
| [goldpath-idempotency](goldpath-idempotency.md) | Goldpath.Idempotency (Ring B) | implemented (0.1.0-preview.1 — Phase 2 opens) |
| [goldpath-audittrail](goldpath-audittrail.md) | Goldpath.AuditTrail (Ring B) | implemented (0.1.0-preview.1) |
| [goldpath-softdelete](goldpath-softdelete.md) | Goldpath.SoftDelete (Ring B) | implemented (0.1.0-preview.1) |
