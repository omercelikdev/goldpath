# RFC: Goldpath.FileExchange — File-Based Integration Rails

**Status:** accepted (owner, 2026-08-18) — the owner pulled the Ring B trigger the same day ("nothing left incomplete"); recorded as an explicit owner ordering decision, mirroring qorpe-sync D5
**Date:** 2026-08-18
**Constitution grounding:** ADR-0003 (compose transports and parsers, don't rewrite),
foundation §5.1 (by-product timing rule), §6.2 Ring B criteria, open-threads **T22**
(this RFC is that thread's first DoD row).

---

## 1. Scope / Non-Goals

**Scope.** The file rail as a UNIT — today Jobs+Bulk cover pieces (a schedule, a large
ingestion) but an adopter still hand-writes the rail around them every time:

- **Pick-up / drop** — scheduled collection and delivery over composed adapters (SFTP,
  network share, object store); no transport code rewritten.
- **Validation** — declared per-rail format contracts (schema-validated artifacts); a file
  that fails contract never reaches ingestion.
- **Idempotent ingestion** — `(file, line)`-keyed dedup so a re-delivered or re-processed
  file applies zero duplicates (the adopter PoC's registry-file processor is the seed shape).
- **Quarantine** — a bad ROW quarantines, the BATCH continues; quarantined rows carry the
  reason and are individually reprocessable.
- **Reprocessing & replay** — re-run a file or a quarantine subset with the same zero-duplicate
  guarantee.
- **Archival** — processed files retained per rail policy, composing the Archival module.
- **Outbound** — generated files with delivery confirmation and the same archival discipline.

**Non-goals.** Not ETL/BI; not the CDC/streaming leg (that is qorpe.sync's Capture — Sync
moves STORES, FileExchange integrates COUNTERPARTIES); no format libraries rewritten (parsers
are composed); no domain knowledge — rail definitions are adopter data.

## 2. Seam Map

- **Jobs** — schedules drive pick-up/drop; no second scheduler.
- **Bulk** — large ingestions run through the existing batch machinery.
- **Idempotency** — the `(file, line)` dedup rides the module's store, not a bespoke table.
- **Archival** — retention/purge policy per rail.
- **AuditTrail** — every file's lifecycle (received → validated → ingested/quarantined →
  archived) is audited.
- **Messaging** — `FileReceived`/`FileIngested`/`RowsQuarantined` as `IIntegrationEvent`s;
  GP0401–0403 unchanged.
- **Mockifyr** — counterparty endpoints stubbed in tests; air-gapped runs stay first-class.

## 3. Manifest Surface

`features.fileExchange` — enabled or absent (compile-time composition). Rail definitions
(endpoint, schedule, format contract, quarantine and retention policy) are versioned
declarative artifacts beside the manifest, schema-validated at build.

## 4. API Surface

Admin-contract (R3) shaped: rail status, per-file lifecycle, quarantine list with repeatable
OR filters, `reprocess` verb (file or quarantine subset), all typed in OpenAPI so the family
console federates it.

## 5. Analyzer Rules

- A rail definition referencing an undeclared format contract fails the build (schema gate).
- Ingestion handlers bypassing the idempotency key surface are flagged — the standard ships
  its verifier.

## 6. Ops Package ("no runbook = no module")

Runbook + dashboard: rail lag (expected vs actual arrival), quarantine depth and age,
reprocess counts, outbound delivery confirmations outstanding. Alarm on a missed arrival
window — the incident file rails actually have.

## 7. Test Plan

- Planted-fault rig, answer-key discipline: a bad row, a duplicate file, a truncated file,
  an out-of-window arrival, a replay — every planted fault must be caught, quarantined, or
  deduplicated exactly as declared.
- Zero-duplicate replay proof: ingest, replay the same file, assert identical end state.
- Quarantine-continues proof: N bad rows quarantine while the batch completes.
- Adopter proof (the T22 proof column): one REAL bidirectional file rail runs with replay +
  idempotency + failure reprocessing + its ops runbook.

## 8. DoD

- [x] RFC accepted; Ring B criteria confirmed (file rails recur across banking
      registries/statements, insurance bordereaux, telco interconnect settlement).
- [x] Rails declared as data through the fluent surface with baked, compile-checked
      closures; declaration-time validation rejects a rail without ParseLine/Handle.
      (A standalone YAML schema for config-file rails is a follow-on, not shipped.)
- [x] The §7 planted-fault rig runs green (7 tests: clean file exactly-once, bad rows
      quarantine while the batch continues, duplicate-file replay applies zero duplicates,
      truncated file rejected whole, reprocess-after-fix retries only the quarantined row —
      `tests/Goldpath.FileExchange.Tests`).
- [x] `features.fileExchange` manifest key + `goldpath add feature fileexchange` CLI recipe
      wired (template flag lands with the module's template pass).
- [ ] Admin surface federates in the family console against a real app.
- [x] Runbook ships (`packages/Goldpath.FileExchange/ops/fileexchange.md`); dashboard JSON open.
- [ ] Database-backed `IGoldpathFileLedger` + transport adapters (SFTP/share/object store).
- [ ] The adopter proof runs (§7 last row) — the row that actually closes T22.

### Decisions

- **D1 — Ring B, born as a by-product (§5.1):** the trigger is the first adopter integration
  that is file-based and bidirectional entering an implementation backlog. Three of the
  factoring-class engagement's common processes are file rails; the pattern already has a
  private seed consumer (the adopter PoC's registry-file processor).
- **D2 — The rail is the unit,** not the pieces: Jobs+Bulk existing was the reason T22 waited;
  the recurring hand-written glue (validate → dedup → quarantine → archive → reprocess) is
  the module.
- **D3 — Rail definitions are data** — versioned, schema-validated; a new counterparty is a
  new definition, not new code.
- **D4 — Boundary with qorpe.sync:** Sync migrates and reconciles STORES during a
  transformation; FileExchange is the PERMANENT integration surface with counterparties.
  A transformation may use both; neither absorbs the other.
