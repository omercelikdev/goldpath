# Module RFC: Goldpath.Abstractions

> Status: v0.1 accepted (2026-07-03) · Ring: foundational (Phase 1, item 1) · Dependencies: Microsoft.Extensions.Compliance.Abstractions ONLY (added 2026-07-05 by the DataProtection RFC, D1 — classification attributes are native DataClassificationAttributes)

## 1. Scope / Non-Goals

**Scope:** The thin contract layer every other Goldpath package (and consumer code) can reference
without pulling in any implementation. Contents — and nothing more:
- `TenantId` (validated value type) + `ITenantContext` — tenant propagation contract consumed
  by all seams; implemented by the MultiTenancy module, safely inert without it.
- Entity capability markers the data-path interceptors act on: `IAuditedEntity`,
  `ISoftDeletable`, `IMultiTenant`.
- `IIntegrationEvent` — marker for broker-bound events (the Mediant-domain-event vs
  MassTransit-integration-event boundary defined by the Messaging RFC).
- `GoldpathHeaders` — canonical HTTP header names used across seams.

**Non-Goals (deliberate, constitution-driven):**
- No `Result<T>`/`Error` types — Mediant provides them (ADR-0003: compose, don't rewrite).
- No `IClock` — the BCL `TimeProvider` already exists (ADR-0003).
- No entity/aggregate base classes — capability markers over inheritance; base-class
  hierarchies are the framework trap (foundation: golden path, not framework).
- No DI helpers, no configuration, no behavior of any kind.

## 2. Seam Map
Referenced BY every seam; touches none itself. Zero runtime dependencies (BCL only).

## 3. Manifest Surface
None — always present (every Goldpath package references it); not a toggle.

## 4. API Surface
Namespace `Goldpath`, multi-targeted `net8.0;net10.0` (LTS policy). Public API tracked with
Microsoft.CodeAnalysis.PublicApiAnalyzers (`PublicAPI.Shipped/Unshipped.txt`).

## 5. Analyzer Rules
None shipped by this package (it is what other packages' analyzers reference).

## 6. Ops Package
N/A — no runtime behavior, nothing to observe.

## 7. Test Plan
Unit tests for `TenantId` validation/equality semantics and `GoldpathHeaders` constants;
public API surface locked by PublicApiAnalyzers (a change = a visible diff in the API file).
Golden manifest impact: none directly (implicitly exercised by every GM).

## 8. DoD
- [x] RFC accepted
- [x] Package builds warning-free with XML docs on all public members
- [x] PublicAPI files populated; API baseline locked
- [x] Unit tests green (15/15)
- [x] README (4 sections) + CHANGELOG entry
