# Insurance card — Policy Lifecycle & Claims

## The story

An insurer's policy & claims platform used through agencies: agents quote and issue
policies, policyholders (via agents) file claims (FNOL — first notice of loss), adjusters
decide, approved claims disburse. Every night renewals are produced and payment runs
execute; claim files live for a decade after closure. Issuance, renewal, claim — this IS
insurance.

## Tenancy & actors

- **Tenant = agency/broker** (an agency sees only its own book of business; the insurer's
  back office operates cross-tenant with an explicit elevated scope, never by accident).
- Actors: agent (quote/issue/FNOL), adjuster (assess/decide), back office (renewal & payment
  runs, repair queues), auditor.

## Vertical slices of the sample

1. Quote → issue policy (underwriting checks as PLAIN domain code — `rules` stays untriggered)
2. Endorse policy (mid-term change; every endorsement audited)
3. FNOL: claim intake (via API and via queue from a partner channel)
4. Adjuster decision → approved claim → `ClaimApproved` → disbursement
5. Nightly renewal job + disbursement payment run
6. Claim-file archive after closure (**the Archival module's primary customer**)

## Feature coverage

| Capability | Concrete use | Proven by |
|---|---|---|
| multiTenancy | agency-scoped policies/claims | cross-agency read = 404; back-office elevated scope is explicit and audited |
| auth openid | agent/adjuster SSO, role-gated decisions | adjuster-only decision endpoint 403 for agents |
| auditTrail | endorsements + claim decisions with actor identity | audit row committed with the decision, same transaction |
| softDelete | cancelled policies & withdrawn claims stay queryable | cancelled policy out of active lists, history intact |
| idempotency | duplicate FNOL submission; duplicate disbursement command | same key → one claim id; one disbursement |
| dataProtection | health data on claims (special category), national id | masked in audit rows, logs and adjuster-list projections |
| distributedCaching | tariff/product catalog on the quote path | [Cacheable] tariff query; product update invalidates by tag |
| distributedLocking | one adjuster finalizes a claim file; renewal run singleton | concurrent finalize → second gets a conflict; second renewal start refuses |
| outbox + broker | `PolicyIssued` → documents; `ClaimApproved` → disbursement | events round-trip in smoke |
| worker (queue) | FNOL consumer from the partner channel (inbox-guarded) | duplicate MessageId → one claim |
| jobs (schedule) | nightly renewal production + payment run | see job inventory |
| archival | claim files 10y after closure; policy documents | archived claim retrievable with its full audit trail |

## Job inventory → requirements loaded onto the Jobs module

**Nightly renewal run**:
- Set-based work discovery (policies expiring in the window), chunked, checkpointed.
- **Per-item isolation**: one uninsurable policy must not stop the night's renewals; failures
  land in a repair queue with the reason attached.
- Produces work for ANOTHER job (payment run) — the module needs first-class **job chaining**
  (renewal completes → payment run starts) without hand-rolled polling.
- **Notification handoff**: renewal notices go out per renewed policy (**`notification`
  trigger fires here**) — the job emits events; sending is not the job's business.

**Disbursement payment run**: money movement → idempotent per claim, singleton, resumable;
shares the deadline/progress semantics of Finance's EOD (one module, both satisfied).

## Archival & retention demands (the module's design driver)

- Claim file = entity graph (claim + decisions + documents + audit rows) — archival is
  **graph-scoped, not table-scoped**; restore returns the full file.
- Legal hold: a claim under litigation is EXEMPT from retention expiry until the hold lifts.
- Tenant-scoped erasure requests (KVKK/GDPR) must compose with retention rules — erasure
  masks personal data, never breaks the regulatory record.

## NFR block

**Performance** — quote p95 < 400ms with tariff cache warm; FNOL intake 20 rps sustained;
nightly renewal 50k policies < 60 min; claim-file archive/restore round-trip p95 < 10s.
**Observability** — claim correlation from FNOL through decision to disbursement event; job
progress/error/checkpoint metrics identical to Finance (ONE metric vocabulary — the module
ships it, samples inherit it); dashboards: claims-flow + renewal-run; alerts: repair-queue
depth, renewal overrun prediction, disbursement DLQ > 0.
**Operational** — runbooks: mid-run tariff deploy (run keeps old tariff version — jobs pin
their input version), poisoned claim repair, legal-hold placement; renewal rerun is a no-op
for already-renewed policies (idempotent by design, not by care).

## Trigger check

- Fires **`notification`** (renewal notices — inseparable from the popular scenario).
- Does NOT fire `rules` (underwriting stays plain code; written trigger kept: revisit if the
  sample's rule churn hurts), nor yarpGateway/saml/ldap.
