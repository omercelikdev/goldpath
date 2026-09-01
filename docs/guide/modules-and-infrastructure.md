# Modules × infrastructure — what each capability actually costs

The adopter's (and the sales call's) one-page answer to "if we take X, what do we have
to run?" Every row is verified three ways against the repo: what the CLI recipes wire
(`FeatureRecipes`), what the template generates, and what the drift profile enforces —
never a promise, always the composition. The wizard (`goldpath new`, bare) derives from
exactly this table and says the reasons out loud ("no broker needed — removed").

| Module | App database | Broker | Redis | Jobs scheduler | Notes |
|---|---|---|---|---|---|
| multitenancy | ✔ (model filter) | — | — | — | header/subdomain resolution; admin surfaces tenant-scoped (contract R1) |
| audittrail | ✔ (same-transaction rows) | — | — | — | one row per changed property; retention points at archival |
| softdelete | ✔ (three columns) | — | — | — | filtered-index guidance in its runbook |
| idempotency | — | — | optional | — | keys live in the host's distributed cache: MEMORY fallback without caching, Redis with it |
| dataprotection | ✔ (classified columns) | — | — | — | classify once, every sink masks |
| caching | — | — | **✔ the ONLY Redis source** | — | HybridCache L1+L2 |
| locking | ✔ (the lock lives in the app db) | — | — | — | zero new infra on postgres/sqlserver |
| archival | ✔ | — | — | ✔ | chain-verified export, then delete |
| **bulk** | **✔ ONLY the app database** | — | — | ✔ | **the bulk-only customer runs Postgres and nothing else** — nightly `GmBulkOnly` proves it |
| notification | ✔ | — | — | ✔ | SMTP is config, not infrastructure |
| campaign | ✔ | **✔ REQUIRED** | — | ✔ | the release path IS broker fan-out (RFC D8) — said by the schema, the template, the CLI and the wizard |
| approvals | ✔ (requests + signatures) | — | — | ✔ | the escalation sweep rides the scheduler; decisions publish lifecycle events through the outbox when composed |
| fileexchange | ✔ (rails + rows) | — | — | ✔ | file-based rails; ingestion marks publish through the outbox when composed |
| outbox (integration events) | ✔ | ✔ | — | — | the outbox publishes THROUGH a broker (SPEC0101) |

Cross-cutting facts the table folds in:

- **One scheduler per app**: archival, bulk, notification, campaign, approvals and
  fileexchange ride ONE `AddGoldpathJobs` composition (and bring the operations console
  with it) — a second module never opens a second scheduler.
- **Auth is orthogonal**: openid/apikey/none changes no infrastructure; `none` makes the
  admin surfaces' opt-out VISIBLE and is acceptable only behind an authenticating
  boundary.
- **Layouts don't change the bill**: vertical-slice and clean-architecture compose the
  same modules onto the same infrastructure; microservice adds a database PER service
  (`goldpath new service`) and optionally the YARP gateway — still nothing a module
  didn't ask for.
- **Past local**: `goldpath export compose` generates the container story FROM the
  AppHost; environments stay CI-built manifests (foundation §10).

Proofs, not promises: every claim above has a nightly golden-manifest row or a CLI test
behind it (`docs/strategy/golden-manifests-v1.md`; the wizard's derivation table is
pinned in `WizardTests`).
