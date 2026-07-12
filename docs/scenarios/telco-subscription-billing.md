# Telco card — Subscription Management & Billing Cycle

## The story

An operator's subscription & billing platform used by dealers: subscribers activate plans,
usage streams in continuously and gets rated, the monthly billing cycle produces invoices,
payment-gateway callbacks settle them, campaigns notify segments. Subscription, usage,
invoice — the telco heartbeat, and the highest-volume card of the three.

## Tenancy & actors

- **Tenant = dealer / MVNO account** (a dealer manages only its own subscriber base; MVNO is
  the same isolation story at larger grain — one platform, no fork).
- Actors: dealer agent (activate/change/port), billing operations (cycle owner), CRM
  (campaigns), the payment gateway (an unreliable robot that retries — by design).

## Vertical slices of the sample

1. Activate subscription (plan catalog from cache) → `SubscriptionActivated`
2. Port-in number (a state machine with hard concurrency rules)
3. Usage ingestion (high-volume queue consumer) → rating → rated-usage store
4. Monthly billing run → invoices → `InvoiceIssued` → notifications
5. Payment-gateway callback (retried, out of order, duplicated — welcome to production)
6. CDR/invoice archival by volume tier

## Feature coverage

| Capability | Concrete use | Proven by |
|---|---|---|
| multiTenancy | dealer-scoped subscribers/invoices | cross-dealer read = 404; smoke sends the tenant header |
| auth openid | dealer/CRM SSO; billing-ops role gates the cycle verbs | run-billing endpoint 403 for dealer role |
| auditTrail | tariff changes + subscriber state changes (regulator asks) | audit row with actor, same transaction |
| softDelete | deactivated subscriptions (win-back campaigns read them) | out of active lists, reachable by campaigns |
| idempotency | payment callback retries (same event id, N deliveries) | one settlement per callback id, N deliveries proven in test |
| dataProtection | traffic/location detail + national id | masked everywhere but the authorized projection |
| distributedCaching | plan/campaign catalog + balance reads (hottest path) | [Cacheable] catalog; campaign publish invalidates by tag |
| distributedLocking | port-in state transitions; billing-run singleton | concurrent port-in commands serialize; second run refuses |
| outbox + broker | `UsageRecorded` → rating; `InvoiceIssued` → notification | events round-trip in smoke |
| worker (queue) | usage/CDR consumer — THE throughput proof of the messaging floor | sustained-rate inbox-guarded ingestion test |
| jobs (schedule) | monthly billing cycle — THE scale proof of the Jobs module | see job inventory |
| archival | rated CDRs (huge volume) + invoices | tiering: hot 3 months, warm 12, archive 10y |

## Job inventory → requirements loaded onto the Jobs module

**Monthly billing run** — the module's scale customer:
- **Chunked by subscriber segment, parallel across instances** (competing chunks via the
  locking floor); checkpoint per chunk; resume continues, never re-bills (idempotent per
  subscriber-period — enforced by design, tested by the sample).
- **Long-running honesty**: hours, not minutes — live progress (subscribers/s, ETA), pause
  and drain verbs, throttling so the cycle never starves the interactive path (resource
  budget, not best wishes).
- **Late usage policy**: usage arriving after cutoff rolls to the next period —
  the module gives the cutoff/watermark primitive; policy stays in the sample.

**Campaign dispatch (CRM-triggered, not cron)**: on-demand jobs with the same progress/
chunking machinery — the Jobs module serves BOTH cron and ad-hoc triggers (**`notification`
demand reinforced here**: dispatch emits events, the notification module sends).

## Archival & retention demands

- CDR volume forces **tiered archival with rollup** (detail ages into summaries); invoice
  archive is legal-grade (immutable, verifiable). Archive queries must not touch hot-path
  performance budgets — separate read model, measured separately.

## NFR block

**Performance** — usage ingestion ≥ 500 msg/s sustained on the reference profile with inbox
dedup ON (the honest number, not the naked one); balance read p95 < 100ms cache-warm;
billing 500k subscribers < 4h with interactive p95 degradation < 10%.
**Observability** — consumer lag, ingest rate, dedup hit-rate as first-class metrics; billing
run: per-chunk progress, ETA, re-bill-prevented counter; the SAME job metric vocabulary as
Finance/Insurance (module-shipped); dashboards: ingestion + billing-cycle; alerts: consumer
lag growth, chunk stall, callback-dedup anomaly spike.
**Operational** — runbooks: mid-cycle instance loss (chunk takeover), gateway outage backlog
drain, tariff hotfix during a run (version pinning as in Insurance); billing rerun for one
subscriber (surgical, audited); capacity note per module: what saturates first and how it
shows on the dashboard BEFORE it hurts.

## Trigger check

- Reinforces **`notification`** (billing + campaign notifications).
- Does NOT fire `bulk` (usage is streaming, not file-batch; Finance already fired it), nor
  yarpGateway (single app fronting nothing).
