# Module RFC: Goldpath.Analyzers

> Status: v1.0 accepted — scope approved by Ömer (2026-07-04): 7 rules in v1, deferrals written;
> analyzers are configurable guardrails, not constraints (severity via `.editorconfig`,
> suppression always possible WITH justification — ADR-0005). Phase 1 item 6.

## 1. Scope / Non-Goals

**Scope — the executable-standards package (ADR-0005):** the rule backlog accumulated by the
four core RFCs, shipped as Roslyn analyzers. v1 = the seven rules that are implementable with
precise syntax/semantic analysis:

| ID | Rule | Severity | Detection |
|---|---|---|---|
| GP0102 | `new HttpClient()` — bypasses resilience + discovery defaults | warn | object-creation operation |
| GP0202 | `Skip().Take()` offset pagination on a query | warn | Queryable call-chain analysis |
| GP0301 | `DateTime` property on a Goldpath-marked entity (UTC policy → `DateTimeOffset`) | warn | marker-implementing types |
| GP0302 | `Migrate()`/`EnsureCreated()` without a Development guard | warn | syntactic `IsDevelopment` ancestor heuristic (documented) |
| GP0303 | Interpolated/concatenated SQL in `*SqlRaw` APIs | **error** | argument constancy analysis; interpolation-safe `FromSql` untouched |
| GP0401 | `Publish`/`Send` of a type not marked `IIntegrationEvent` | **error** | semantic type check (moves the runtime guard to compile time) |
| GP0402 | Mediant `INotification` also marked `IIntegrationEvent` | **error** | dual-interface check; Mediant types matched by name (no hard dependency) |

**Deferred (written, not silent):** GP0101/0103 (registration-flow analysis — high cost/low yield),
GP0203 (kebab routes — belongs to Spec Engine naming rules V8), GP0403 (needs manifest
knowledge — arrives with the manifest-as-AdditionalFile design alongside Spec Engine).

**Non-Goals:** no style rules (`.editorconfig`+`dotnet format` own that), no duplication of
CA/QM rules, no code fixes in v1 (diagnostics first; fixes are a fast-follow).

## 2. Packaging
`netstandard2.0` Roslyn analyzer package (`analyzers/dotnet/cs`); wired into consumer projects
by the template's `Directory.Build.props`. Matches types by metadata name — zero runtime
dependencies on Goldpath/Mediant/MassTransit/EFCore packages; rules no-op when the target types
are absent (L1 à-la-carte safe: the analyzer never fires on code that doesn't use the seam).

## 3. Test Plan
Microsoft.CodeAnalysis.Testing per rule: flagging + non-flagging cases (+ the guard variant for
GP0302); hermetic stub types in test sources (no package graph in test compilations).

## 4. DoD
- [x] 7 analyzers + descriptors with help links + release tracking (RS2008)
- [x] Analyzer tests green (14: flag/no-flag per rule, incl. the GP0302 guard variant;
      hermetic stub types — no package graph in test compilations)
- [x] README (4 sections) + CHANGELOG · deferred rules recorded in §1
- [x] Package verified: dll lands in analyzers/dotnet/cs

## 5. Batch 2 (2026-07-05) — Ring B module guards

The rules accumulated by the Idempotency/AuditTrail/SoftDelete RFCs:

| ID | Rule | Severity | Detection |
|---|---|---|---|
| GP0501 | `IAuditLogged` entity, DbContext present, no `AddGoldpathAuditLog()` call | **error** | compilation-end wiring check |
| GP0502 | Manual write to `IAuditedEntity` stamp fields from application code | warn | assignment target analysis; `IEntitySaveContributor` implementers exempt |
| GP0601 | `ISoftDeletable` entity, DbContext present, no `ApplyGoldpathSoftDelete()` call | **error** | compilation-end wiring check |
| GP1003 | `[Idempotent]` on a Mediant `IQuery` (no-op — queries are idempotent by contract) | info | interface + attribute name match |

Design notes: GP0501/0601 only fire when the compilation contains a `DbContext` — entity-only
assemblies are exempt (the wiring lives with the context). Wiring calls are matched by method
name across the compilation (`OnModelCreating` is not required as the callsite — any model
configuration path counts). Mediant types matched by name, consistent with GP0402 (no hard
dependency). Descriptors carry `CompilationEnd` custom tags (RS1037).

### Batch 2 DoD
- [x] 4 analyzers (ModuleGuardAnalyzers.cs) + descriptors with RFC help links + release rows
- [x] Tests green (8 new, 22 total): flag/no-flag per rule, DbContext-absent exemption,
      contributor exemption for GP0502
- [x] Module RFC backlog lines (audittrail/softdelete/idempotency) marked SHIPPED

**Deferred ledger — REVISITED against the Spec Engine (M4, 2026-07-06):**

| ID | Original blocker | Outcome |
|---|---|---|
| GP0403 | needed manifest knowledge (event not declared in the manifest specs) | **MOVES UP A LAYER**: cross-artifact truth is specdrift territory — lands as a drift-profile capability when AsyncAPI support ships (engine v2 scope), not as a Roslyn rule |
| GP1001 | write-command without [Idempotent] while idempotency is enabled | STAYS Roslyn: per-type attribute analysis needs semantics, but the "is idempotency enabled" input now has a cleaner path — the manifest-as-AdditionalFile design remains right, unblocked whenever we choose to invest |
| GP1002 | consumer registered without an inbox filter | STAYS deferred: registration-flow analysis; PARTIALLY covered today — the drift profile's outbox row catches the missing AddGoldpathOutbox wiring textually |
| GP1004 | no natural key detectable on a command | STAYS deferred (semantic, low yield) |
| GP0101/0103/0203 | registration-flow / Spec Engine naming rules | 0203 (kebab routes) now has its home: a future specdrift naming-rules pack; 0101/0103 stay deferred |

## 6. Batch 3 (2026-07-06) — the full Ring B rule backlog

Twelve rules accumulated by DataProtection/Caching/MultiTenancy/Locking/Auth:

| ID | Rule | Severity | Detection |
|---|---|---|---|
| GP0701 | Classified property on an `IIntegrationEvent` | warn | attribute base-chain match (Goldpath/Microsoft/Mediant classifications) |
| GP0702 | Classified property, DataProtection module absent | info | compilation-end; module presence by metadata name |
| GP0801 | `[Cacheable]` on a Mediant command | **error** | interface+attribute name match (GP1003 pattern) |
| GP0802 | `[InvalidatesCache]` on a Mediant query | info | same |
| GP0803 | Raw string cache key into `HybridCache` | warn | literal/interpolation as the `key` argument; variables stay silent (no guessing) |
| GP0901 | `IMultiTenant` entity, DbContext present, no `ApplyGoldpathMultiTenancy` | **error** | ModelWiringAnalyzer gains a third marker (0501/0601 pattern) |
| GP0902 | Manual `TenantId` write outside contributors | warn | 0502 pattern |
| GP0903 | `IgnoreQueryFilters` over `IMultiTenant` without a visible `Bypass()` | warn | SYNTACTIC heuristic (GP0302 precedent), documented |
| GP1101 | Raw string lock name into Medallion APIs | warn | literal/interpolation as the `name` argument |
| GP1102 | Acquired lock handle discarded or stored without `using` | warn | provable leaks only; returned/passed handles stay silent |
| GP1201 | `[AllowAnonymous]` / `.AllowAnonymous()` inventory | info | attribute + fluent invocation |
| GP1202 | Literal secret on the Goldpath surfaces (HmacKey/ApiKeys/UseHmacRedaction) | **error** | scoped shapes only — no generic secret guessing |

### Batch 3 DoD
- [x] 12 analyzers (Batch3Analyzers.cs + ModelWiringAnalyzer extension) + descriptors with
      RFC help links + release rows
- [x] Tests green (11 new, 33 total): flag/no-flag per rule incl. the variable-stays-silent
      cases, contributor exemption, visible-Bypass quiet path, provable-leak-only scoping
- [x] Module RFC backlog lines (5 RFCs) marked SHIPPED · analyzer count now 23
