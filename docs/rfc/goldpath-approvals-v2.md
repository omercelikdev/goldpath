# RFC: Approvals v2 — the product-proven rules the ladder engine is missing

Status: **Proposed** (owner decision pending — per the constitution, code follows approval).
Source of truth for the deltas: the api-portal product's approval engine, where every rule
below shipped, survived contract tests and ran live (multi-stage chains proven 2026-08-24;
the whole engine exercised daily by three exams).

## 1. Problem

`Goldpath.Approvals` models the FINANCIAL ladder well: amount-routed rungs, four-eyes,
bounded delegation, deadline escalation, a trail. The api-portal product needed a sibling
shape — GOVERNED CHANGES (a payload awaiting decision, applied transactionally on the
final yes) — and building it surfaced rules that apply to BOTH shapes but exist in
neither the package nor its RFC. A bank adopting the package hits them in week one.

## 2. The proven deltas

| Rule | What the product proved | Ladder engine today |
| --- | --- | --- |
| **Withdraw** | The requester may take back a PENDING request; only the requester; lands in the trail. | Missing — a mistaken request can only be rejected by someone else. |
| **Quorum rungs** | A rung may demand N DISTINCT approvals ("two managers"), not one. | One decision total. |
| **Distinct-eyes across the chain** | One identity signs at most ONCE per request, across rungs/quorum — even holding every role. Without it a two-step chain is one person clicking twice. | N/A today (single decision) — becomes load-bearing the moment quorum lands. |
| **Mandatory reason on rejection** | A bare "no" teaches the requester nothing; the engine refuses empty rejection reasons. | `reason` accepted but not required. |
| **Reject → resubmit chain** | A rejected request is resubmittable with a fresh payload; the new request carries a `Supersedes` link — the audit trail reads draft → reject → rework as ONE story. | Terminal reject only. |
| **Decision events carry the reason** | Notification templates need it; the portal's evidence-backed notices consumed it. | `GoldpathApprovalRejected` carries reason ✓ — keep. |

Deliberately NOT proposed: the governed-change facet itself (payloads, materializers,
red diff, stale-baseline). That is a different aggregate shape; the portal implements it
as product code, and hoisting it is a separate RFC once a second product needs it.
This RFC only closes the rules that make the EXISTING ladder bank-grade.

## 3. Design sketch (additive, non-breaking)

- `WithdrawAsync(id, requestedBy)` → `Withdrawn` status (new enum member, additive).
- `GoldpathApprovalRung` gains `RequiredApprovals` (default 1). `DecideAsync` records a
  signature row per grant; the rung completes at quorum; `Signatures` joins the trail.
- Distinct-eyes: a signature by an identity already on the request's signature list →
  new outcome `AlreadySigned`.
- Rejection with a blank reason → new outcome `ReasonRequired`.
- `ResubmitAsync(rejectedId, requestedBy)` → new request, `SupersedesId` set, trail linked.
- Store seam grows `AddSignatureAsync`/`GetSignaturesAsync` (in-memory + EF store).
- PublicAPI: all additions land in `PublicAPI.Unshipped.txt`; no shipped surface changes.

## 4. Proof plan

Engine unit tests mirror the portal's (the seven MultiStage facts, translated to rungs);
the CorPay sample's approval story exercises withdraw + quorum; mutation gate holds ≥70.

## 5. Decision requested

Approve → implementation lands as one PR with tests and ledger updates. Reject/amend →
this file records why, per the ledger discipline.
