# RFC: Goldpath.Approvals — Human Approval Workflows

**Status:** proposed
**Date:** 2026-08-18
**Constitution grounding:** ADR-0003 (compose, don't rewrite — this module orchestrates PEOPLE'S
decisions, not systems; process orchestration remains the decided non-goal the closed T19 thread
proved composable), foundation §5.1 (by-product timing rule), §6.2 Ring B criteria,
open-threads **T21** (this RFC is that thread's first DoD row).

---

## 1. Scope / Non-Goals

**Scope.** The approval mechanics that today live in e-mail threads at every enterprise adopter,
as one composable Ring B module:

- **Approval definitions as data** — maker-checker, four-eyes, and amount-laddered authority
  chains (e.g. expert → deputy → manager → GM) declared as schema-validated artifacts, never
  code. A new ladder is a new definition, not a fork.
- **Decision lifecycle** — request → pending → granted/rejected/expired, with every transition
  audited; a guarded action proceeds only on a granted decision.
- **Delegation and escalation** — bounded delegation (no cycles, windowed), deadline-driven
  escalation up the ladder.
- **Worklist** — the pending-approvals inbox surface (per-approver, per-ladder) that feeds
  operator UIs and the family console.

**Non-goals.** Not a BPM/workflow engine (no arbitrary process graphs); not process
orchestration across services (that composes as a state machine per the T19 proof — this module
is what such a flow CALLS when a step needs a human); not an identity/org-chart system —
authority levels map onto the app's existing Auth roles/claims.

## 2. Seam Map

- **Auth** — who MAY decide at each rung; authority mapping is claims-based.
- **AuditTrail** — every request, decision, delegation, and escalation is an audited event;
  the module's value proposition IS the audit trail e-mail never had.
- **Notification** — pending/escalation nudges replace the e-mail thread, not augment it.
- **Jobs** — escalation and expiry timers (Quartz-backed, no bespoke scheduler).
- **Messaging** — `ApprovalGranted`/`ApprovalRejected`/`ApprovalExpired` as
  `IIntegrationEvent`s; GP0401–0403 hold the boundary unchanged.

## 3. Manifest Surface

`features.approvals` — enabled or absent (compile-time composition; a manifest without it has
NO approvals code). Ladder definitions live beside the manifest as versioned declarative
artifacts validated at build by the standard schema gate.

## 4. API Surface

Admin-contract (R3) shaped, so the family console federates it without bespoke work:
worklist (repeatable OR filters), decide, delegate, history per subject; all responses typed
in OpenAPI. The application-facing surface is one interface: request an approval for a subject
under a ladder, and observe/await its outcome.

## 5. Analyzer Rules

- A guarded operation (one declaring it requires approval) reachable without an approval-gate
  check fails the build — the same "the standard ships its verifier" rule every module obeys.
- Approval events not marked `IIntegrationEvent` are already caught by GP0401.

## 6. Ops Package ("no runbook = no module")

Runbook + dashboard: pending-decision age (the number the e-mail world cannot produce),
escalations fired, per-ladder throughput, expiry rate. Alarm on oldest-pending breaching the
ladder's own deadline.

## 7. Test Plan

- Ladder boundary values (the adopter PoC's amount ladder is the seed shape: each rung's
  inclusive edge, above-top-rung routing).
- Delegation cycle guard and window expiry; escalation timers on a virtual clock.
- Audit completeness: every lifecycle transition appears exactly once in the trail.
- Adopter proof (the T21 proof column): one REAL amount-laddered flow from the first adopter
  runs end to end on the module, audit trail inspected.

## 8. DoD

- [ ] RFC accepted; the four Ring B entry criteria confirmed in review (≥2 industries named:
      banking-class approvals, insurance underwriting sign-off, telco credit overrides).
- [ ] Ladder/definition schema published; validator wired into the standard gate.
- [ ] Lifecycle + delegation + escalation proven by the §7 deterministic tests.
- [ ] Admin surface federates in the family console against a real app.
- [ ] Ops pack ships (runbook + dashboard JSON).
- [ ] The adopter proof runs (§7 last row) — the row that actually closes T21.

### Decisions

- **D1 — Ring B, born as a by-product (§5.1):** built when the first adopter's implementation
  phase needs its first systematized approval flow — this RFC prepares the shelf; it does not
  open a front. Six of the factoring-class engagement's twelve common processes run approvals
  over e-mail today; the ladder shape is domain-agnostic.
- **D2 — Definitions are data:** ladders, quorums, deadlines, delegation windows are versioned
  declarative artifacts. Code changes are module changes; ladder changes are config reviews.
- **D3 — The human/saga boundary:** a compensating flow (T19) orchestrates SYSTEMS and may
  request an approval as one of its steps; Approvals never drives system steps itself. One
  sentence each side, so neither module grows into the other.
- **D4 — Worklist is part of the module,** not left to each adopter's UI team: without the
  inbox, adopters fall back to e-mail and the audit value evaporates.
