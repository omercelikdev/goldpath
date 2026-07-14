# Goldpath guide — the adopter's path

Start here; go deep via the links. The deep layers (RFCs, runbooks, ops packs) already
exist per module — this guide is the map, not a copy.

1. **[Getting started](getting-started.md)** — install to running app in five minutes.
2. **[Concepts](concepts.md)** — the six ideas everything else hangs on: manifest,
   composition, the run model, migrations discipline, the frozen admin contract, proofs.
3. **[The CorPay tour](corpay-tour.md)** — a real finance app built ONLY with the public
   verbs, feature by feature, with the bugs the build itself found.
4. **Proof stories** — why the claims hold: [No double payment under kill -9](../stories/kill9-no-double-payment.md).

Operating depth (already written, linked from the concepts): the
[migrations runbook](../ops/migrations-runbook.md), the
[trace-correlation guide](../ops/trace-correlation.md), the
[frozen admin contract](../rfc/goldpath-admin-contract.md), the
[versioning & support contract](../rfc/goldpath-versioning.md), and every module's
`ops/` pack (Grafana board + runbooks + measured performance).
