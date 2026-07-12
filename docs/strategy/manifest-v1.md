# Goldpath Manifest v1 — Field-by-Field Definition (Working Draft)

> Detail of Foundation section 3.3. Status: v0.2 — §6 decisions locked, JSON Schema written
> (`goldpath-manifest.schema.json`) (2026-07-03)
> Constitution ties: ADR-0001 (manifest is the single source of truth), ADR-0002 (business
> logic/flows do NOT go into the manifest — this is a binding layer, not a DSL).

---

## 0. Design Principles

1. **Every field has a default** — a minimal manifest is ~10 lines; advanced options unfold as
   needed (progressive disclosure). The defaults are the golden path itself.
2. **`schemaVersion` is mandatory** — the schema is SemVer'd; the Spec Engine declares its
   compatibility range.
3. **Inheritance model:** the solution manifest carries the shared decisions; service/module
   manifests inherit them and override only in permitted fields (the demo rule: a module may
   not override any provider; a service may override db/cache).
4. **The manifest contains no behavior:** no endpoints, flows, business rules, or mappings.
   Those belong to the specs (OpenAPI/AsyncAPI) and the code. The manifest only says
   "what exists, which profile, connected to what."
5. **Convention > configuration:** path/folder fields are next to nonexistent; the `.goldpath/`
   and `specs/` locations are fixed by convention.

---

## 1. Common Envelope (in every manifest)

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `schemaVersion` | int | ✔ | — | Manifest schema version (v1 = `1`) |
| `kind` | enum | ✔ | — | `solution` \| `service` \| `module` \| `worker` \| `gateway` |
| `name` | string | ✔ | — | PascalCase, subject to corporate naming rules (validated by the Spec Engine) |
| `description` | string | ✔ | — | Single sentence — CLAUDE.md and the catalog feed off it |
| `owner` | string | ✔ | — | Team/person (audit + CODEOWNERS generation) |

---

## 2. Solution Manifest (`kind: solution`)

### 2.1 `architecture`
| Field | Type | Default | Values |
|---|---|---|---|
| `deploymentModel` | enum | `modular-monolith` | `monolith` \| `modular-monolith` \| `microservice` |
| `codeOrg` | enum | `vertical-slice` | `clean-architecture` \| `vertical-slice` |

### 2.2 `providers` (a module may NOT override; a service may, partially)
| Field | Default | Values | Service override? |
|---|---|---|---|
| `db` | `postgresql` | `postgresql` \| `sqlserver` \| `oracle` | ✔ |
| `cache` | `redis` | `redis` \| `inmemory` | ✔ |
| `broker` | `rabbitmq` | `rabbitmq` \| `kafka` \| `inmemory` \| `none` | ✘ (solution-wide) |
| `auth` | `openid` | `openid` \| `saml` \| `ldap` \| `apikey` | ✘ |

### 2.3 `features` — Ring B cross-cutting concerns (bool OR options object)
```yaml
features:
  idempotency: true                    # short form: default options
  outbox: true                         # requires a broker (V1); includes inbox as well
  auditTrail:
    store: database                    # database | eventstream
  softDelete: true
  multiTenancy:
    strategy: header                   # header | subdomain | path-prefix
    isolation: shared-db               # shared-db | db-per-tenant
  dataProtection:
    piiFields: annotate                # annotate (attribute-driven) | catalog (central list)
  distributedLocking: false
  distributedCaching:
    levels: [l1, l2]
```
Rule: `true` = default options; object = fine-tuning. Whatever is off does not exist AT ALL
(compile-time composition, foundation 5.0). Option schemas are defined in the module RFCs;
the manifest schema includes them via `$ref`.

### 2.4 `modules` — Ring C advanced (list; each entry is an RFC-backed module)
```yaml
modules: [yarpGateway]                 # what exists in v1; the catalog is the vision
```
Decision (locked): outbox → under `features` (a broker-backed option); pagination → does NOT
go into the manifest (an ApiDefaults out-of-the-box default — not an option, but the floor).

### 2.5 `observability`
| Field | Default | Values |
|---|---|---|
| `profile` | `standard` | `minimal` \| `standard` \| `full` (bundles of sampling, metric set, log level) |
| `exporters` | `[otlp]` | `otlp` \| `prometheus` \| `console` |

### 2.6 `nfr` — targets for the release gate (foundation 8.4)
```yaml
nfr:
  p95LatencyMs: 200
  throughputRps: 500
  availability: "99.9"
  errorBudgetPct: 0.1
```
Defaults at the solution level; a service overrides with its own `nfr`.
Enforcement: for `kind: service`, the release pipeline WARNS when nfr is absent and BLOCKS
at the RC gate.

---

## 3. Service Manifest (`kind: service`)

In addition to / overriding the solution fields:

| Field | Type | Required | Description |
|---|---|---|---|
| `boundedContext` | string | ✔ | Context name on the DDD map (matches the domain memory; validated by the Spec Engine) |
| `providers.db` / `providers.cache` | enum | ✘ | Overrides the solution default (justified by independent deployment) |
| `specs.openapi` | path[] | ✔* | Approved OpenAPI file(s) — under `specs/`, status lives in the spec file's frontmatter |
| `specs.asyncapi` | path[] | ✘ | Event contracts |
| `nfr` | object | ✔ at RC | Service-specific targets |

### 3.1 `externalSystems` — integration + mock contract (in one place)
```yaml
externalSystems:
  - name: payment-gateway
    kind: rest                         # rest | soap | queue | file
    spec: specs/external/payment.yaml  # its contract, if one exists
    direction: outbound                # outbound | inbound | both
    mock:
      strategy: stub                   # stub | record-replay | passthrough
      stubs: mocks/payment/            # WireMock JSON is canonical (Mockifyr/WireMock provider)
    criticality: high                  # selects the resilience defaults (timeout/retry/CB profile)
```

---

## 4. Module / Worker / Gateway Manifests

- `kind: module` — only `name/description/owner` + `codeOrg` override (demo rule:
  NO provider override; it is part of the solution).
- `kind: worker` — plus `workerType`: `scheduler-quartz` \|
  `consumer-kafka` \| `consumer-rabbitmq` \| `batch` \| `bulk` plus trigger/topic fields.
  (`scheduler-hangfire` removed 2026-07-06 — LGPL-3.0 fails the license gate; jobs RFC D1.)
- `kind: gateway` — plus a route auto-registration toggle (`autoRegisterServices: true`).

---

## 5. Validation Rules (Spec Engine `validate` — business rules above the schema)

| # | Rule | Level |
|---|---|---|
| V1 | `outbox`/consumer worker while `broker: none` → error | error |
| V2 | `strategy` is mandatory when `multiTenancy` is enabled | error |
| V3 | `kind: module` is valid only under `deploymentModel: modular-monolith` | error |
| V4 | `kind: service` + empty `specs.openapi` → error (spec-first) | error |
| V5 | Missing `nfr` → warning; block in the RC pipeline | warn→error |
| V6 | `externalSystems[].mock` undefined → warning (the test cycle wants mocks) | warn |
| V7 | Manifest ↔ csproj ↔ Program.cs consistency (toggle semantics) | error (drift) |
| V8 | Naming rules (name, boundedContext, topic names) | error |

---

## 6. Locked Decisions (2026-07-03, approved by Ömer)

1. **Naming:** Ring B = `features`, Ring C = `modules`. ✔
2. **Outbox → `features`** (requires a broker, rule V1); **pagination → does not go into the
   manifest** (an ApiDefaults default). ✔
3. **`environments` do NOT exist in the manifest** — environments/promotion are the job of the
   CI template + org-config; the manifest is environment-agnostic (the same manifest in every
   environment). ✔
4. **Placement:** `.goldpath/manifest.yaml` at the solution root, `.goldpath/manifest.yaml` in each
   service's own folder; the Spec Engine resolves the inheritance. ✔

---

## 7. Examples

### Minimal (greenfield, single-run target)
```yaml
schemaVersion: 1
kind: solution
name: OrderPlatform
description: Order management modernization
owner: team-orders
# EVERYTHING else is default: modular-monolith, vertical-slice, postgres, redis, rabbitmq, openid
```

### Fully populated service example
```yaml
schemaVersion: 1
kind: service
name: ChequeService
description: Cheque lifecycle (issue, clearing, protest)
owner: team-cheque
boundedContext: cheque-management
providers: { db: oracle }              # override justified by proximity to the bank's core
features:
  idempotency: true
  auditTrail: { store: database }
  dataProtection: { piiFields: catalog }
specs:
  openapi: [specs/cheque-api.yaml]
  asyncapi: [specs/cheque-events.yaml]
externalSystems:
  - name: core-banking
    kind: soap
    direction: outbound
    mock: { strategy: record-replay, stubs: mocks/core-banking/ }
    criticality: high
nfr: { p95LatencyMs: 300, throughputRps: 200, availability: "99.95" }
```
