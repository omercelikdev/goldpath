# Spec gaps — goldpath-test-gen run, payment-instructions surface (2026-07-23)

Produced by the `goldpath-test-gen` skill run (implementation-blind). Per the skill:
"if you cannot write a test without peeking, the SPEC is underspecified — report the
gap instead of peeking." These are the cases the exported contract
(`specs/CorPay.Api.json`) does not specify, so no spec-derived test can pin them.
This list feeds the `breaker` agent.

| # | Gap | Why it blocks a spec-derived test |
|---|---|---|
| G1 | `POST /payment-instructions`, `/{id}/approve`, `/{id}/reject` document **no requestBody schema** | The submit command's field constraints (amount bounds, currency whitelist, IBAN shape, reference rules) are enforced by the app but invisible in the contract — a hostile consumer cannot know a 400 from the spec alone |
| G2 | `GET /payment-instructions` documents **no query parameters** | `cursor`/`size` exist (the response schema references an applied size) but their names, bounds and clamp behavior are unspecified — size=0, negative, or 10_000 behavior cannot be derived |
| G3 | Manifest says `idempotency: true`; the spec **never mentions an idempotency key** | Replay semantics on submit (same key + same body, same key + different body) are contractually invisible |
| G4 | `404` is undocumented on the `/{id}` routes | Approve/reject of an unknown or foreign-tenant id has no specified outcome |
| G5 | Approve/reject respond `201 Created` | Semantically dubious for a state transition on an existing resource; if intentional, the contract should say what is "created" |
| G6 | State machine is implicit | `PaymentStatus` declares 5 states but no transition table (who may approve, whether Rejected is terminal, whether re-submit after reject is legal) |
| G7 | `securitySchemes.goldpath` exists but the payment operations carry **no security requirement, no 401/403 responses** | The auth floor is real in the app yet absent from the contract |

Spec-derived tests added by this run (all green, implementation-blind):
`PaymentListPagingContractTests` — cursor walk termination (`nextCursor: null` = end),
exactly-once page coverage across sizes, applied-size reporting, tenant fencing,
the closed 5-state status set.
