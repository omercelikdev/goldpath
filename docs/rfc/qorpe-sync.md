# RFC: qorpe.sync — the Migration & Co-existence Product Module

**Status:** proposed — owner acceptance pending
**Date:** 2026-08-18
**Constitution grounding:** ADR-0012 (product modules on the platform — Sync is one of the two
first-party examples the ADR itself names), ADR-0003 (compose, don't rewrite), foundation §9
step 5 (data migration reconciliation as a cutover-gate input), §5.1 (by-product timing rule).

---

## 1. Scope / Non-Goals

**Scope.** Productize the migration & co-existence machinery every legacy transformation needs,
as a first-party PRODUCT MODULE per ADR-0012: own (private) repo, binds to the published
Goldpath train like an adopter, declares under a namespaced `products.qorpe.sync` key, may be
closed on the open core. Four components:

1. **Capture** — CDC from the legacy store (Debezium-class) plus file/CSV extraction contracts
   for black-box sources; controlled staging.
2. **Stream** — ordered, replayable, idempotent change stream (Kafka-class transport).
3. **Transform** — schema + semantic mapping with an ADAPTER model: per-source and per-target
   adapters plug in; mapping definitions are versioned data, not code; golden-record dedup.
4. **Reconcile** — event-id–matched comparison between source and target (subledger ↔ GL
   class checks included), continuous difference lists, and the dashboard whose output is a
   cutover-gate input (§9 step 5). Composes db-compare for PK/sequence-independent data
   comparison rather than rewriting it.

**Non-goals.** Not an ETL/BI platform; not a message broker (transports are composed); not a
data-quality suite (profiling feeds it, lives with discovery tooling); no domain knowledge in
the module — mappings are customer data.

## 2. Seam Map

Product-module seams per ADR-0012: binds `Goldpath.Sdk` + published packages (never source);
its reconciliation reports feed the cutover evidence bundle (specanchor-composition RFC seam
map row "Reconciliation"); Mockifyr stubs stand in for unreachable sources in tests; the
transformation method (specanchor) treats Sync as the K5 layer of its coverage model.

## 3. Manifest Surface

`products.qorpe.sync` (namespaced per ADR-0012 D2). The HOST app's manifest is untouched;
Sync ships its own manifest and stands up standalone (product-module rule).

## 4. API Surface

Admin/ops surface follows the admin contract (R3) so the family console can federate it;
pipeline definitions (source, mapping version, reconciliation policy) are declarative
artifacts validated by schema — the specdrift profile pattern, third profile.

## 5. Analyzer Rules

The product repo imports the exported gate set (license allowlist, mutation on the mapping
and reconciliation cores, PublicAPI lock) — the specdrift/specanchor precedent. GP20xx
no-source-leak analyzer applies (product binds packages, never source).

## 6. Ops Package

Runbook + dashboard are the product's own deliverable (reconciliation lag, unmatched-event
count, replay depth, difference-list age). "No runbook = no module" applies unchanged.

## 7. Test Plan

- Rig: a two-store fixture (source with planted drift: a lost update, a duplicate, an
  out-of-order event, a semantic-mapping trap) — the answer-key discipline again; the
  reconcile component must catch every planted difference.
- The Kafka-transport compatibility spike (MassTransit rider vs the streaming leg) runs HERE,
  closing the verification item the factoring-class gap analysis recorded.
- Composition proof: the specanchor rehearsal (open-threads T20) uses Sync for its
  reconciliation leg once the module reaches demo grade — one rehearsal, two proofs.

## 8. DoD (v0 — engagement-shaped)

- [ ] RFC accepted by the owner (this row flips on the owner's word; the master-plan records
      the second-product-repo ordering decision alongside).
- [ ] Private repo stands with the exported gate set; binds the published train only.
- [ ] Capture→Stream→Transform→Reconcile runs end to end on the two-store rig; every planted
      difference caught; reconciliation report generated.
- [ ] db-compare composed for the data-level comparison leg; Kafka spike documented.
- [ ] Admin surface exposes pipeline status per the admin contract.

### Decisions

- **D1 — Product module, not Ring B:** migration machinery is horizontal but heavyweight and
  commercially separable — exactly the ADR-0012 shelf, and Sync is the ADR's own named example.
- **D2 — Adapters + versioned mapping data:** per-source/per-target adapters; mappings are
  DATA (versioned, schema-validated), so a new engagement is a new mapping set, not a fork.
- **D3 — Reconcile composes db-compare;** transports are composed (Kafka-class for the
  stream), never rewritten (ADR-0003).
- **D4 — Born as a by-product (§5.1):** v0's scope is exactly what the factoring-class
  engagement's migration needs; generalization beyond that waits for the second consumer.
- **D5 — Ordering recorded:** this opens a second product repo alongside the API Portal
  pilot. That is an explicit owner reordering decision, recorded here and in the master plan —
  not a silent third front. The API Portal keeps its pilot slot; Sync's v0 rides the
  engagement's own timeline.
