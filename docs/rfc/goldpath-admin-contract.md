# Goldpath admin API — the frozen contract (H8)

Status: **FROZEN** (2026-07-12). This document is the UI phase's input: the single run
console is written ONCE against this surface. Any change below this line is a breaking
change and needs this document updated in the same PR (and, after NuGet release, a
versioning note per H7).

## Conventions (all five surfaces)

- **Mount**: `Map<Module>Admin<TContext>(prefix, exposeUnsecured)` on the management head;
  default prefix `/goldpath/admin/<module>`. Fail-closed: every endpoint demands
  `GoldpathPolicies.Ops` unless `exposeUnsecured: true` (visible opt-out + startup warning).
- **Verbs are POSTs with kebab-case names** (`trigger`, `replay-items`, `lift-hold`,
  `pause-all`). No PATCH; `PUT`/`DELETE` only for true upserts/removals (calendars).
- **Verb envelope**: every mutating verb returns `GoldpathAdminResult { ok, message }` —
  `200` when `ok`, `400` with the same envelope when refused (the message is the reason;
  never a silent 200). Unexpected exceptions stay with the platform's problem+json.
  Missing entities on GET answer `404` with no body.
- **Actor**: taken from the authenticated identity (`anonymous` fallback); every mutating
  verb writes an audit row server-side — the UI never supplies the actor.
- **Paging**: list endpoints take `?take=` (defaults 50–200 per endpoint) and clamp to
  `[1, 500]` (`AdminPaging.Clamp`, compile-linked into all five modules). Large ordered
  sets offer keyset paging (`afterRow` on the bulk validation report).
- **Failure nouns**: execution failures answer to **`failures`** everywhere
  (campaign `/{id}/failures`, notification `/failures`; bulk execution failures live in
  the JOBS repair queue via the run detail). Bulk's `/batches/{id}/errors` is the
  **validation report** — a different concept, deliberately a different noun.
- **Audit reads are a capability, not a baseline**: jobs exposes a global `/audit`,
  campaign a per-entity `/{id}/audit`; the other modules' evidence lives in their domain
  rows (decisions on the batch, erasure receipts). The console shows an audit tab only
  where the capability exists.

## Route inventory (frozen)

### jobs — `/goldpath/admin/jobs`
| Method | Route | Returns |
|---|---|---|
| GET | `/fleets` | fleet list |
| GET | `/fleets/{fleet}/jobs` | `GoldpathJobInfo[]` |
| GET | `/fleets/{fleet}/runs?job=&take=` | run list |
| GET | `/runs/{runId}` | `GoldpathRunDetail` (chunks by status + open failures) |
| POST | `/fleets/{fleet}/jobs/{job}/trigger` · `/pause` · `/resume` · `/reschedule` | `GoldpathAdminResult` |
| POST | `/fleets/{fleet}/pause-all` · `/resume-all` | `GoldpathAdminResult` |
| POST | `/runs/{runId}/rerun` · `/replay-items` | `GoldpathAdminResult` |
| GET/PUT/DELETE | `/fleets/{fleet}/calendars[/{name}]` | calendar CRUD |
| GET | `/audit?take=` | admin audit trail |

### archival — `/goldpath/admin/archival`
| Method | Route | Returns |
|---|---|---|
| GET | `/definitions` | definition list |
| GET | `/entries/{definition}/{key}` | entry detail (natural key) |
| POST | `/entries/{definition}/{key}/hold` · `/lift-hold` · `/erase` | `GoldpathAdminResult` |
| GET | `/holds?includeLifted=&take=` · `/erasures?take=` | evidence lists |
| POST | `/definitions/{definition}/verify` | `GoldpathAdminResult` |

### bulk — `/goldpath/admin/bulk`
| Method | Route | Returns |
|---|---|---|
| GET | `/definitions` | intake numbers per definition |
| POST | `/batches/{definition}` (body = file) | `GoldpathBulkBatchInfo` |
| GET | `/batches?definition=&state=&take=` · `/batches/{batchId}` | batch list / detail |
| GET | `/batches/{batchId}/errors?afterRow=&take=` | VALIDATION report (keyset) |
| POST | `/batches/{batchId}/approve` · `/reject` | `GoldpathAdminResult` |

### notification — `/goldpath/admin/notification` (read-only)
| Method | Route | Returns |
|---|---|---|
| GET | `/templates` | registered templates |
| GET | `/notifications?state=&template=&tenant=&take=` · `/notifications/{id}` | evidence list / detail |
| GET | `/suppressions?take=` · `/failures?take=` | filtered evidence views |

### campaign — `/goldpath/admin/campaign`
| Method | Route | Returns |
|---|---|---|
| GET | `/?state=&take=` · `/{id}` | campaign list / detail |
| GET | `/{id}/failures?take=` | failed items (item replay stays with jobs `replay-items`) |
| GET | `/{id}/audit?take=` | per-campaign audit |
| POST | `/` (create) · `/{id}/pause` · `/resume` · `/abort` · `/throttle` | `GoldpathAdminResult` |

## Freeze mechanics

- The campaign surface carries a route-freeze test (`RouteContractTests`) — the pattern
  any surface adopts when the UI starts depending on it.
- Breaking fixes applied at freeze time (cheap now, expensive after the UI):
  campaign `/{id}/failed-items` → `/{id}/failures`; `take` clamp made uniform across all
  five modules (was jobs/archival-only).
