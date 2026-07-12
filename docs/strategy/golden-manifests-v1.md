# Goldpath Golden Manifest Matrix v1 (Working Draft)

> The concrete form of Foundation 8.4 / ADR-0008: the set of combinations that proves the
> "runs with one click" claim on every commit. Whenever the template or any core package
> changes, CI runs for every golden manifest: **generate → build → spin up → smoke → tear down**.
> Status: v0.1 draft (2026-07-03)

---

## 0. Selection Principles

1. **A full factorial is impossible and unnecessary** (3 architectures × 2 codeOrg × 3 db × …
   × 2^8 features). The approach: **pairwise coverage + realistic personas**. Every enum value
   appears in at least one GM; every Ring B feature is enabled in at least one GM; the
   combinations are not made up — they are target customer profiles (sales demo = golden
   manifest).
2. **GM count discipline:** 6 in v1. Adding a new GM requires an RFC (CI time is a budget).
3. **What is not covered is not hidden:** values not covered in v1 are explicitly listed in §4
   (no silent gaps).
4. **Every combination failure coming from the field** either updates an existing GM or is
   added as a regression manifest (a permanent lesson).

---

## 1. The Six Golden Manifests (personas)

| ID | Persona | Architecture | codeOrg | db / cache / broker / auth | Features | Extras |
|---|---|---|---|---|---|---|
| **GM-1** `minimal-default` | "The 5-line manifest" — the golden path itself | modular-monolith | vertical-slice | postgres / redis / rabbitmq / openid (all defaults) | — | Fastest smoke; demo opener |
| **GM-2** `banking-core` | Cheque/promissory-note style banking transformation | microservice | clean-architecture | oracle / redis / kafka / ldap | idempotency, outbox, auditTrail(db), softDelete, dataProtection(catalog), distributedLocking | 2 services + SOAP externalSystem (mock: record-replay) + full nfr |
| **GM-3** `telco-tenant` | Multi-tenant telco self-service | modular-monolith | vertical-slice | sqlserver / redis / rabbitmq / openid | multiTenancy(header, shared-db), distributedCaching(l1+l2), idempotency | worker: consumer-rabbitmq |
| **GM-4** `simple-internal` | Small internal app / SME — the narrowest footprint | monolith | clean-architecture | sqlserver / inmemory / **none** / apikey | — | Proof of rule V1 (outbox is rejected when there is no broker — negative test pair) |
| **GM-5** `worker-heavy` | Batch/processing-heavy back office | modular-monolith | vertical-slice | postgres / redis / kafka / openid | outbox, idempotency | workers: scheduler-quartz + consumer-kafka + batch |
| **GM-6** `gateway-front` | API productization / platform facing the outside world | microservice | vertical-slice | postgres / redis / inmemory-broker / saml | idempotency | gateway (autoRegisterServices) + 2 services + REST externalSystem (mock: stub) |

## 2. Coverage Check (every enum value ≥1 GM)

| Dimension | Value → GM |
|---|---|
| deploymentModel | monolith→4 · modular-monolith→1,3,5 · microservice→2,6 |
| codeOrg | clean→2,4 · vertical-slice→1,3,5,6 |
| db | postgres→1,5,6 · sqlserver→3,4 · oracle→2 |
| cache | redis→1,2,3,5,6 · inmemory→4 |
| broker | rabbitmq→1,3 · kafka→2,5 · none→4 · inmemory→6 |
| auth | openid→1,3,5 · ldap→2 · apikey→4 · saml→6 |
| features (Ring B) | idempotency→2,3,5,6 · outbox→2,5 · auditTrail→2 · softDelete→2 · multiTenancy→3 · dataProtection→2 · distributedLocking→2 · distributedCaching→3 |
| worker | consumer-rabbitmq→3 · scheduler-quartz→5 · consumer-kafka→5 · batch→5 |
| gateway / externalSystems / nfr | 6 / 2(soap,record-replay)+6(rest,stub) / 2 |

## 3. Execution Model

- **Every commit (template or core package change):** 6 GMs × [generate → build → `dotnet test`
  (smoke: live end-to-end flow + health) → tear down]. Containers come from the corporate
  registry mirror (the offline scenario is deliberately tested).
- **Nightly:** plus `goldpath validate` & `drift` for every GM (the Spec Engine also audits its own
  output), and a package version bump rehearsal (Renovate simulation: are the GMs still green
  with the latest core version).
- **Negative corpus:** invalid manifests (started with today's m3/m4) are part of the schema
  test suite; every new V-rule adds at least one negative example.

## 4. Deliberately Uncovered in v1 (no silent gaps)

- worker: `bulk` (its module comes with the execution ladder L3; added to GM-5 with its RFC).
  `scheduler-hangfire` was REMOVED from the schema entirely (2026-07-06, jobs RFC D1):
  Hangfire is LGPL-3.0 — the license gate rejects it, so it is an impossibility, not a gap
- db-per-tenant isolation (multiTenancy's heavyweight mode — a GM-3 variant in the module RFC)
- Ring C modules other than `modules: [yarpGateway]` (the catalog vision; every module RFC
  MUST state which GM it will enter — RFC template §7 "golden manifest impact")

## 5. Evolution Rules

1. A new module RFC → enters at least one GM (otherwise it is not merged).
2. A combination that blows up in the field → a GM update or a regression manifest (never deleted).
3. A GM change demands a review as serious as a schema/template change (these are the product's contract).
4. Every GM is also an example project usable in sales/demos — narrated by its persona name.
