# RFC Index

Every module/significant component is born through an RFC before implementation begins.
Template = the eight fixed sections of [goldpath-idempotency.md](goldpath-idempotency.md):

1. Scope / Non-Goals · 2. Seam Map · 3. Manifest Surface · 4. API Surface ·
5. Analyzer Rules · 6. Ops Package · 7. Test Plan (including golden manifest impact) · 8. DoD

This table is COMPLETE by rule: every file in this folder has a row, and the row tells the
truth (the freshness gate keeps links alive; the review agent's R1 class polices claims).
All "implemented" modules are published on nuget.org at `0.1.0-preview.2`.

| RFC | Module / Topic | Status |
|---|---|---|
| [goldpath-abstractions](goldpath-abstractions.md) | Goldpath.Abstractions (foundational) | implemented |
| [goldpath-servicedefaults](goldpath-servicedefaults.md) | Goldpath.ServiceDefaults (Ring A) | implemented |
| [goldpath-apidefaults](goldpath-apidefaults.md) | Goldpath.ApiDefaults (golden-path core) | implemented |
| [goldpath-data](goldpath-data.md) | Goldpath.Data (golden-path core) | implemented |
| [goldpath-messaging](goldpath-messaging.md) | Goldpath.Messaging (golden-path core) | implemented |
| [goldpath-analyzers](goldpath-analyzers.md) | Goldpath.Analyzers (executable standards) | implemented |
| [goldpath-idempotency](goldpath-idempotency.md) | Goldpath.Idempotency (Ring B) | implemented (ops package = written Phase 2 deferral) |
| [goldpath-audittrail](goldpath-audittrail.md) | Goldpath.AuditTrail (Ring B) | implemented (ops package = written deferral) |
| [goldpath-softdelete](goldpath-softdelete.md) | Goldpath.SoftDelete (Ring B) | implemented |
| [goldpath-auth](goldpath-auth.md) | Goldpath.Auth (Ring B) | implemented |
| [goldpath-multitenancy](goldpath-multitenancy.md) | Goldpath.MultiTenancy (Ring B) | implemented |
| [goldpath-caching](goldpath-caching.md) | Goldpath.Caching (Ring B) | implemented |
| [goldpath-locking](goldpath-locking.md) | Goldpath.Locking (Ring B) | implemented |
| [goldpath-dataprotection](goldpath-dataprotection.md) | Goldpath.DataProtection (Ring B) | implemented |
| [goldpath-jobs](goldpath-jobs.md) | Goldpath.Jobs (the run engine) | implemented |
| [goldpath-archival](goldpath-archival.md) | Goldpath.Archival | implemented |
| [goldpath-bulk](goldpath-bulk.md) | Goldpath.Bulk (ladder L3) | implemented |
| [goldpath-notification](goldpath-notification.md) | Goldpath.Notification | implemented |
| [goldpath-campaign](goldpath-campaign.md) | Goldpath.Campaign (ladder L4) | implemented |
| [goldpath-template](goldpath-template.md) | `dotnet new` solution pack | implemented |
| [goldpath-template-completion](goldpath-template-completion.md) | template completion set | implemented |
| [goldpath-migrations](goldpath-migrations.md) | migrations discipline + `goldpath db` + bundle | implemented (H1) |
| [goldpath-admin-contract](goldpath-admin-contract.md) | the admin API contract | **FROZEN** (2026-07-12) — revision pending for tenant scoping (audit A1, preview.3) |
| [goldpath-versioning](goldpath-versioning.md) | SemVer & support promise (H7) | accepted (binding) |
| [goldpath-event-contracts](goldpath-event-contracts.md) | event contracts idiom (per-app `<Name>.Contracts`) | accepted (2026-07-14) |
| [goldpath-skills-v1](goldpath-skills-v1.md) | the AI skill layer v1 | implemented — ships inside the template; field status: `../strategy/ai-sdlc-status.md` §2 |
| [goldpath-console](goldpath-console.md) | Goldpath.Console (the UI phase) | accepted — U1 in flight |
| [spec-engine-v1](spec-engine-v1.md) | specdrift (separate repo) | implemented — 0.4.1 published (NuGet tool + MCP + Docker + Action) |
