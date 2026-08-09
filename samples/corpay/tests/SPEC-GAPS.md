# Spec gaps — goldpath-test-gen run, payment-instructions surface (2026-07-23)

Produced by the `goldpath-test-gen` skill run (implementation-blind). Per the skill:
"if you cannot write a test without peeking, the SPEC is underspecified — report the
gap instead of peeking." These are the cases the exported contract
(`specs/CorPay.Api.json`) does not specify, so no spec-derived test can pin them.
This list feeds the `breaker` agent.

| # | Gap | Why it blocks a spec-derived test |
|---|---|---|
| ~~G1~~ | ~~no requestBody schema~~ — **CLOSED 2026-08-09**, verified in the committed export: `POST /api/v1/payment-instructions` carries `requestBody` → `#/components/schemas/SubmitPaymentInstructionCommand` | Closed by the preview.6 re-export (#139), exactly as the root-fix note below predicted |
| ~~G2~~ | ~~no query parameters~~ — **CLOSED 2026-08-09**, verified: `GET /api/v1/payment-instructions` documents `cursor` (string), `size` (integer), `status` (string) | Closed by the same re-export. Bounds/clamp SEMANTICS are still prose-only, which is G6's territory, not G2's |
| G3 | Manifest says `idempotency: true`; the spec **never mentions an idempotency key** | Replay semantics on submit (same key + same body, same key + different body) are contractually invisible |
| G4 | `404` is undocumented on the `/{id}` routes | Approve/reject of an unknown or foreign-tenant id has no specified outcome |
| G5 | Approve/reject respond `201 Created` | Semantically dubious for a state transition on an existing resource; if intentional, the contract should say what is "created" |
| G6 | State machine is implicit | `PaymentStatus` declares 5 states but no transition table (who may approve, whether Rejected is terminal, whether re-submit after reject is legal) |
| G7 | `securitySchemes.goldpath` exists but the payment operations carry **no security requirement, no 401/403 responses** | The auth floor is real in the app yet absent from the contract |

Spec-derived tests added by this run (all green, implementation-blind):
`PaymentListPagingContractTests` — cursor walk termination (`nextCursor: null` = end),
exactly-once page coverage across sizes, applied-size reporting, tenant fencing,
the closed 5-state status set.


## Reconciliation 2026-08-09 — checked against the export, not assumed

The note below said the gaps "stay listed until that re-export makes them false". The
re-export shipped with preview.6 (#139) and nobody re-read the file, so it sat three weeks
stale in BOTH directions: an audit assumed all of G1–G2 were now false, while the file
still listed them as open.

Verified against the committed `src/CorPay.Api/openapi/CorPay.Api.json` (openapi 3.1.1, 40
paths):

- **G1 CLOSED** — `requestBody` → `SubmitPaymentInstructionCommand`.
- **G2 CLOSED** — `cursor`, `size`, `status` are documented parameters.
- **G3 STILL OPEN** — `POST` carries no header parameters at all; the idempotency key
  remains contractually invisible.
- **G4 STILL OPEN** — responses are `201/400/500`; no `404` on the `{id}` routes.
- **G5 STILL OPEN** — approve/reject still answer `201`.
- **G6 STILL OPEN** — no transition table.
- **G7 STILL OPEN and the one that matters to a buyer** — `securitySchemes.goldpath` is
  declared, but no operation carries a `security` requirement and no `401`/`403` is
  documented. An API-gateway or security review reads the contract, not the app, and would
  conclude the payment surface is unauthenticated. **This is the first thing to fix in the
  next contract slice.**

## Root fix landed (2026-07-26)
G1/G2's root cause — the `[HttpEndpoint]` dispatcher hiding the request side from OpenAPI
inference — is FIXED at the seam: Mediant 1.4.0 stamps request metadata + `Accepts`, and
`Goldpath.ApiDefaults` projects query-bound properties into documented parameters (proof:
`ContractExportTests` in the goldpath repo). CorPay's own committed contract re-exports
when this sample moves to the next published train (preview.4) — the gaps above stay
listed until that re-export makes them false.
