# Finance card — Corporate Payments & End-of-Day Reconciliation

## The story (the sector's bread and butter)

A bank's corporate payment platform: corporate customers submit payment instructions
(single, and as batch files), the platform validates, executes and reports them; every night
an end-of-day (EOD) job reconciles executed payments against the core-banking ledger and
produces the regulator-ready day report. This is the most recognizable workload in the
sector — money moves, retries happen, auditors come.

## Tenancy & actors

- **Tenant = corporate customer** (each corporate sees only its own accounts, instructions,
  reports). Fail-closed isolation is not a feature here, it is table stakes.
- Actors: corporate treasurer (submits/approves), bank operations (monitors, repairs),
  auditor (reads immutable history), the EOD scheduler (nobody — and that is the point).

## Vertical slices of the sample

1. Submit payment instruction (single) → validate → execute → `PaymentExecuted` event
2. Upload batch payment file → parse → per-item instructions (**bulk trigger fires here**)
3. Approve/reject flow (four-eyes on high amounts)
4. Instruction list & day report (keyset-paginated, tenant-scoped)
5. EOD reconciliation job → mismatch report → operations queue

## Feature coverage (every capability earns its place)

| Capability | Concrete use | Proven by |
|---|---|---|
| multiTenancy | corporate-scoped accounts/instructions | cross-tenant read attempt = 404, missing header = 400 (smoke) |
| auth openid | corporate SSO; ops vs treasurer roles | 401/403 paths in smoke |
| auditTrail | every instruction state change + approver identity | audit rows in the same transaction as the state change |
| softDelete | beneficiary/template removal (payments referencing them stay intact) | deleted beneficiary invisible in lists, joins still resolve |
| idempotency | resubmitted instruction (client retry, gateway timeout) must NOT double-pay | same Idempotency-Key → same instruction id, one execution |
| dataProtection | IBAN + national id classified once, masked in audit rows/logs | audit row shows masked IBAN |
| distributedCaching | FX rates & fee schedule (hot read on every instruction) | [Cacheable] rate query + [InvalidatesCache] on rate update |
| distributedLocking | account-level posting order; EOD runs as a singleton | two concurrent postings serialize; second EOD start refuses |
| outbox + broker | `PaymentExecuted` → ledger feed + customer notification | event round-trips the broker in smoke |
| worker (queue) | payment instruction consumer (inbox-guarded) | duplicate MessageId processed once |
| jobs (schedule) | EOD reconciliation, banking-calendar aware | see job inventory below |
| archival | payments + audit 10 years (regulatory), hot→cold | archived instruction retrievable, out of hot queries |

## Job inventory → requirements loaded onto the Jobs module

**EOD reconciliation** — the module's hardest customer:
- **Calendar-aware scheduling**: banking days, not calendar days (holiday table; timezone-safe).
- **Singleton with takeover**: exactly one run per business day across N instances
  (distributed lock composition); a crashed run must be RESUMABLE from its last checkpoint,
  not restarted blind.
- **Chunked + checkpointed**: N-thousand items per day; progress persisted; partial failure
  isolates the failing item into a repair queue, the run continues.
- **Deadline semantics**: must complete before 07:00; the module surfaces *predicted* overrun
  (progress rate), not just failure after the fact.
- **Manual verbs**: rerun (idempotent by design), replay-single-item, dry-run.

## Archival & retention demands

- Instructions + audit rows: 10-year retention, immutable, tenant-scoped export for audits.
- Hot store keeps the active period only; archive is queryable (not a backup) with p95 < 5s
  for a single-instruction recall.

## NFR block (module RFCs answer this line by line)

**Performance** — instruction submit p95 < 300ms at 50 rps sustained; batch file of 10k items
ingested < 5 min; EOD 100k reconciliations < 45 min on the reference profile.
**Observability** — per-instruction correlation from HTTP entry through consumer to ledger
event; job metrics: progress %, items/s, error count, checkpoint age, predicted-finish;
dashboards: payments-flow + EOD-run panels; alerts: EOD overrun prediction, DLQ depth > 0,
duplicate-payment attempt rate.
**Operational** — runbooks: stuck EOD (takeover), poisoned instruction (repair queue),
replay after core-banking outage; graceful shutdown drains consumers; DLQ with redrive verb;
zero-downtime deploy during business hours proven in the sample.

## Trigger check

- Fires **`bulk`** (batch payment file — inseparable from the popular scenario).
- Does NOT fire yarpGateway/rules/saml/ldap (single app, plain domain code, openid SSO).
