# Master plan — 2026-08-03 (owner-approved order)

The single ordered list of everything open, set with the owner on 2026-08-03 after a
repo-wide sweep (code, RFCs, ledgers, schema-vs-generator, samples, CI). Nothing is
open outside this list and the trigger-parked set at the bottom. Each step lands with
the standing discipline: tests → PR → review findings answered BEFORE merge → CI
green → merge → ledgers updated in the same PR.

## The order

1. **Campaign R1 implementation** — ExcludedDays, EndDate→ExpiredIncomplete, the retry
   ladder (30s → 2m → repair queue), GlobalTps; schema + template + CLI recipe +
   console fields + tests. The one accepted RFC with zero code; blocks S-FLEET-01/03.
2. **Housekeeping slice** (one group) — **DONE 2026-08-03**: T14 four `.Produces<T>` (+
   the response-side export test; CorPay's re-export proof rides preview.6) · T8 bulk
   `?definition=` filter (server + facet + smoke past the page bound) · ops runbooks for
   Idempotency/AuditTrail/SoftDelete · analyzers GP1001/1002/1004 (batch 4) · T4 review
   agent as a CI step + output-contract hardening (waits only on the `ANTHROPIC_API_KEY`
   secret — owner decision) · the monthly bench cadence (bench.yml, 1st of the month) ·
   the GM coverage-table reality note and the stale doc lines (2026-08-03, PR #131).
3. **preview.6 train** — **DONE 2026-08-05**: `v0.1.0-preview.6` on nuget.org (R3 +
   console U6–U9 + Campaign R1 + the housekeeping set; GM matrix + Campaign mutation
   green before the tag, ADR-0008). T12 closed (every CorPay head reaches ready — and
   fixing it surfaced the payments worker's D3-violating migration). The pre-flight
   also found and fixed: five silently red nightlies (the console build never installed
   the kit), mutation-heavy's missing tool restore, GP1001 catching the TEMPLATE's own
   unmarked command (the Mediant attribute surface is `KeyProperty`, not the RFC's
   `KeyExpression` sketch), and console-smoke's pg probe racing initdb. CorPay pins on
   preview.6 with its four commands `[Idempotent]`-marked; its re-export regained the
   four T14 schemas and `goldpath check` stays green — T14 closed. T1 unblocked.
4. **ADR-0012 + Platform/Module SDK RFC** — **DONE 2026-08-05 (ACCEPTED)**: Goldpath is
   a PLATFORM; product modules live in their OWN repos, bind to the published train
   like adopters (never source), declare under namespaced `products` keys, ship their
   own kit-composed consoles federated by the registry, and may be closed on the open
   core. D1 `Goldpath.Sdk` + D2 manifest surface + D6 `export compose` ride step 5;
   D5 `@qorpe/ui` extraction fires at consumer #3 (step 6). The acceptance carries the
   §2b condition: every risk has a written antidote AND its enforcing mechanism; the
   two honest GAPs (product-repo train-freshness step, GP20xx no-source-leak analyzer)
   are the pilot's DoD.
5. **Shapes + CLI package** (the RFC's implementation) — clean-architecture template ·
   microservice/multi-service layout + gateway · plain-monolith variant ·
   `goldpath new module|service` · the INTERACTIVE wizard (asks which modules, then
   derives the infrastructure itself: "no broker needed — removed") · `goldpath init`
   (L2 attach) · GM matrix 6/6 + a bulk-only GM row + the module×infrastructure
   matrix as an adopter/sales document.
6. **Pilot product module** — the API Portal or Sync skeleton: the module path's
   CorPay. Its console composes from the kit — the third consumer that fires the
   @qorpe/ui decision for real.
7. **Scenario campaign** — starts after step 3 and runs alongside: S-FIN-01 first,
   the S-FLEET set once R1 ships, the 30M rig spec co-written before any scale claim.
   All seven seeded scenarios get evidence.
8. **The samples — LAST, as the full-set exam** — Insurance and Telco from scratch
   (with the step-5 wizard, proving it in anger) plus CorPay's open cells (SoftDelete,
   Caching, a real Contracts-idiom worker consumer, the Archival slice, SPEC-GAPS
   G3–G7, GAP-LEDGER #32/#33/#34). `add feature`, `db bundle`, the manifest skill —
   all proven in real use; no empty core cell remains in the coverage matrix.

## Parked on written triggers (deliberately open)

Oracle provider (T16 — first committed Oracle-mandating adopter) · `upgrade` skill
(first real preview→preview migration) · console URL routes / OIDC login / tenant
picker (T9/T10/T11) · ⌘K entity-id search (needs a cross-module lookup endpoint) ·
stat-card trends (no time series in the contract) · TS mutation for the ui kit (T3) ·
saga + rules engine (Ring C — decided inside the step-4 RFC) · Phase 3 transformation
package (reverse-engineer, differential-test, strangler guide — the phase after the
samples).
