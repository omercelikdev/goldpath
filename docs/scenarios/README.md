# Sector scenario cards — the requirements input for Phase C/D

Three cards, three sectors, one rule set. These are DOCUMENTS, not code: they exist so the
remaining core modules (Jobs, Archival, dashboards — and whatever the cards trigger) are
designed against real, popular workloads instead of guesses, and so Phase D's samples start
with their requirements already written.

## The two standing rules (Ömer, 2026-07-06 — non-negotiable)

1. **Popular scenarios, FULL feature coverage.** Every card is its sector's most recognizable
   workload, and EVERY platform capability appears in EVERY card with a concrete,
   non-decorative use: the 7 Ring B features + auth + outbox/broker + worker kinds + jobs +
   archival. The coverage matrix below tolerates no empty cell. A feature that cannot earn a
   real place in a popular scenario is a feature to question — that is the point of the rule.
2. **Module excellence bar.** Every module RFC from here on carries three MANDATORY sections
   — **Performance** (targets + a measured proof), **Observability** (metrics, correlation,
   a shipped Grafana panel), **Operational** (rerun/resume semantics, failure playbook,
   runbook file). A module without its perf proof, its panel and its runbook is NOT done,
   whatever else works.

## Coverage matrix (capability × sector — all cells filled by design)

| Capability | Finance — Payments & EOD | Insurance — Policy & Claims | Telco — Subscription & Billing |
|---|---|---|---|
| multiTenancy | corporate customer portals | agency/broker portals | dealer & MVNO accounts |
| auth (openid) | corporate SSO | agent SSO | dealer/CRM SSO |
| auditTrail | payment state changes (regulator) | endorsements & claim decisions | tariff/subscriber changes (regulator) |
| softDelete | beneficiary/template removal | cancelled policies stay queryable | deactivated subscriptions |
| idempotency | duplicate payment submission | duplicate claim/disbursement | payment-gateway callback retries |
| dataProtection | IBAN/national-id masking | health data (special category) | traffic/location data, national-id |
| distributedCaching | FX rates & fee schedules | tariff/product catalog | tariff & campaign catalog, balance reads |
| distributedLocking | account-level posting, EOD singleton | claim-file edits, renewal-run singleton | port-in state machine, billing-run singleton |
| outbox + broker | PaymentExecuted → ledger/notify | ClaimApproved → disbursement | UsageRecorded → rating; InvoiceIssued |
| worker (queue) | payment instruction consumer | FNOL (claim intake) consumer | usage/CDR ingestion consumer |
| jobs (schedule) | EOD reconciliation (calendar-aware) | nightly renewals + payment run | monthly billing cycle (chunked, resumable) |
| archival | 10y payment/audit retention | claim files, decade-scale | CDR/invoice volume tiering |

## What the cards TRIGGER (deferral discipline: demand fires the trigger)

- **`bulk` (Ring C)** — fired by Finance: corporate batch payment file upload (toplu ödeme)
  is inseparable from the popular scenario. Enters the Phase C queue after Archival.
- **`notification` (Ring C)** — fired by Insurance (renewal notices) and Telco (billing &
  campaign notifications). Enters the Phase C queue after `bulk`.
- **`rules`** — NOT fired: insurance underwriting stays plain domain code in the sample;
  revisit only if rule churn proves painful there. **`yarpGateway`** — NOT fired: single-app
  samples front nothing. Both keep their written triggers.

## How to read a card

Each card: the story → tenancy & actors → the sample's vertical slices → the coverage table
(feature → concrete use → how the sample proves it) → worker/job inventory with the
requirements it loads onto the Jobs module → archival/retention demands → the NFR block
(performance targets, observability, operational) that the module RFCs must answer line by
line.
