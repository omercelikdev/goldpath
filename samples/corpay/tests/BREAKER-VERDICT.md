# Breaker verdict — payment-instructions surface (2026-07-23)

Adversarial run against `specs/CorPay.Api.json` + `.goldpath/manifest.yaml` + `tests/SPEC-GAPS.md`.
Attacked as a hostile caller through the public handler seams only; no `src/` source was read
(signatures learned via the compiler). Two new files:

- `tests/CorPay.Api.Tests/BreakerFourEyesStateMachineTests.cs`
- `tests/CorPay.Api.Tests/BreakerSubmitAndPagingTests.cs`

## Suite status after this run: RED

`Failed: 1, Passed: 51, Skipped: 0, Total: 52` (full `tests/CorPay.Api.Tests` run).
The single red is the breaker finding below and is left FAILING on purpose (breaker does
not fix product code or `Skip` — the orchestrator decides).

## Failure found (1) — genuine gap, left failing

### `Breaker_subcent_amount_scale_probe` — submit accepts sub-minor-unit amounts (G1)
`SubmitPaymentInstructionHandler.Validate` enforces amount **positivity**
(`SubmitPaymentInstructionRulesTests.Amount_must_be_positive`) but enforces **no scale /
minor-unit bound**: `Amount = 0.001m` passes validation with no `Amount` error. A hostile
consumer can submit `0.001 TRY` (and, by the same absence of an upper/scale rule,
arbitrarily-scaled or absurd magnitudes) into a treasury pipeline whose settlement works in
minor units — the amount is un-settleable / silently rounds downstream.

Contract basis: `SPEC-GAPS.md` **G1** — "the submit command's field constraints (amount
bounds …) are enforced by the app but invisible in the contract". This run makes the gap
concrete: the enforced bound is *positivity only*; scale is unconstrained. The spec neither
documents nor forbids sub-cent amounts, so this is surfaced for a product decision (add a
scale rule + document it in the contract, or explicitly declare fractional minor units legal).

## Targets attacked and REPELLED (kept as passing pins of subtle contract points)

All of the following passed — each pins a point the exported contract leaves implicit, so
they are kept (not noise) to stop the next run re-deriving them.

### Target 1 — four-eyes / state-machine abuse (G6)
- `Breaker_double_approve_never_pays_twice` — approve an already-`Executed` instruction with a
  *different* valid approver: refused, `ICoreBankingClient.ExecuteAsync` ran **exactly once**.
- `Breaker_reject_after_execute_is_refused_and_state_holds` — `Executed` is terminal for reject.
- `Breaker_approve_after_reject_is_refused_and_never_pays` — `Rejected` is terminal; no payment.

### Target 2 — tenant fence under hostility (`manifest multiTenancy: true`)
- `Breaker_foreign_tenant_cannot_approve_and_no_money_moves` — rival tenant approving acme's raw
  id fails; acme's instruction stays `PendingApproval`; zero executions.
- `Breaker_foreign_tenant_cannot_reject_my_instruction` — rival cannot reject acme's id.

### Target 3 — unknown / foreign id on approve & reject (G4)
- `Breaker_unknown_id_approve_fails_without_throwing` — id `999_999` = clean `Result` failure,
  not an unhandled exception / 500; zero executions.
- `Breaker_unknown_id_reject_fails_without_throwing` — same on the reject route.

### Target 4 — boundary / unicode abuse on submit validation (G1)
- `Breaker_unicode_digit_iban_is_rejected_on_both_sides` — Arabic-Indic `٣` and full-width `３`
  digit look-alikes in an IBAN fail the shape check on debtor and creditor sides (codepoint-strict).
- `Breaker_currency_homoglyph_is_not_the_whitelisted_currency` — Cyrillic `Т`/`У` homoglyphs of
  `TRY` are rejected (whitelist is exact-match, not fuzzy).
- `Breaker_currency_must_be_exact_three_uppercase` — leading/trailing space, 4-char, 2-char and
  mixed case all rejected.

### Target 5 — paging under hostile size + concurrent inserts (G2)
- `Breaker_nonpositive_size_never_returns_a_dangling_cursor` — `size = 0` and `size = -5` never
  return an empty page with a non-null cursor (no infinite-walk / DoS); applied size never negative.
- `Breaker_absurd_size_is_clamped_not_honored_verbatim` — `size = 10_000` is clamped to a
  defensive ceiling (`<= 1000`), not echoed back.
- `Breaker_concurrent_inserts_never_duplicate_or_skip_stable_rows` — inserting `INTRUDER-A/B`
  mid-walk leaves every pre-existing row appearing exactly once (keyset paging is stable).

## Not covered this run (for the next breaker)
- G3 idempotency-key replay (same key + different body): no idempotency-key seam is reachable
  from the discovered public submit signature, so it could not be attacked as a hostile caller;
  the manifest says `idempotency: true` but the surface exposes no key — worth a targeted probe
  once the key channel is identified.
- G7 auth floor (401/403): out of reach from the direct-handler harness (no HTTP pipeline);
  belongs to a WebApplicationFactory-style integration attack, not this unit-seam harness.
- Amount upper bound / `decimal.MaxValue` and reference max-length: same G1 root cause as the
  failing scale probe; not separately asserted to avoid piling redundant reds on one gap.

## Resolution (orchestrator, same PR)
`Breaker_subcent_amount_scale_probe` was a genuine finding: the amount guard enforced
positivity only. Fixed in `SubmitPaymentInstruction.cs` — amounts with more than 2
decimal places are now a validation error (every whitelisted currency settles in 2 minor
units). Suite after the fix: **52/52 green**. The G1 root cause (request constraints
invisible in the exported contract) remains open in `SPEC-GAPS.md`.
