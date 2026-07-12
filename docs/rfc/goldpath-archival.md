# Module RFC: Goldpath.Archival (retention, archive, legal hold, erasure)

> Status: v1.0 ACCEPTED — D1–D7 approved by Ömer (2026-07-07). SHIPPED: S1 (store + chain +
> jobs composition), S2 (admin surface: hold/erasure/verify + ops pack + GOLDPATH14xx), S3
> (`features.archival` schema key + template/CLI composition — module COMPLETE; the run
> console rides the consolidated UI phase). Requirements source: the three
> scenario cards' §archival demands (finance: 10y immutable payment/audit retention with
> p95 < 5s single recall; insurance: GRAPH-scoped claim files, legal hold, erasure ×
> retention; telco: CDR volume tiering + rollup with hot-path budgets untouched). Module
> excellence bar applies: §6–§8 are load-bearing. Effort L (S1–S3).

## 1. Scope / Non-Goals

**Scope:** the data lifecycle after "hot": declarative retention per aggregate, archive
runs that move whole GRAPHS into a tamper-evident store, row-retention purges for
high-volume detail, legal hold, erasure that composes with retention (masks, never breaks
the record), retrieval with measured budgets, admin API + ops package.

**Non-goals (deferrals with triggers):** object-store/offsite archive backends (S3/blob —
trigger: first customer whose volume or compliance regime outgrows the database; the store
is a seam from day one, D1); REHYDRATION back into hot tables (trigger: first workflow that
must resume on archived data — retrieval serves audits/inquiries, which is what the cards
ask); WORM/immutable-storage integration (trigger: a regulator demands hardware-level
immutability; the hash chain gives tamper-EVIDENCE now, D1); cross-database archival.

## 2. Seam Map

- **Store seam — same database, zero new infra (D1):** archived aggregates live as
  serialized JSON documents in `GoldpathArchiveEntries` (+ index table), written through EF in
  the app's own database — the locking/jobs decision applied again. Every entry carries a
  SHA-256 content hash AND the previous entry's hash (a per-definition hash CHAIN): any
  tampering breaks the chain and the `verify` job says so. `IGoldpathArchiveStore` is the
  provider seam the deferred object-store backend will implement.
- **Graph seam — EF metadata, no reflection guesswork (D2):** an archive DEFINITION names
  the aggregate root and its owned graph as Include-paths; extraction walks EF's model, so
  the graph the archive captures is exactly the graph the app maps. Serialization is
  System.Text.Json with a stable, version-stamped envelope (`schemaVersion`, definition,
  key, tenant, timestamps, hash links).
- **Execution seam — the ladder eats itself (D3):** archive/purge/verify runs ARE
  `IGoldpathJob`s composed on Goldpath.Jobs — chunked, checkpointed, resumable, deadline-predicted,
  visible in the SAME run console and admin API, for free. Archival ships job classes; the
  app schedules them like any job (cron + calendar).
- **Privacy seam — DataProtection catalog (D4):** erasure walks a stored document and
  REDACTS exactly the `[GoldpathPersonalData]`-classified properties via the existing
  `IGoldpathDataProtector.Redact` — one classification, every sink masks, now including the
  archive. Erasure never deletes an entry: the regulatory record survives, the personal
  data does not.
- **Tenancy seam:** entries carry the tenant; retrieval and erasure are tenant-scoped
  through the same fail-closed context as everything else.

## 3. Manifest Surface

```yaml
features:
  archival: true          # or the object form when the store provider arrives
```

**Schema change:** `archival` joins `$defs/features` WITH the implementation (the schema
rejects what does not exist — same discipline as always). Drift rows: `features.archival`
→ package `Goldpath.Archival`, calls `AddGoldpathArchival` / `AddGoldpathArchiveModel`.

## 4. API Surface

```csharp
builder.AddGoldpathArchival<WebApplicationBuilder, OrdersDbContext>(archival =>
{
    // GRAPH archive (insurance's claim file): root + owned graph + when it becomes due.
    archival.AddArchive<Claim>(a =>
    {
        a.Graph(c => c.Decisions, c => c.Documents);       // Include-paths, EF-checked
        a.Key(c => c.Id);
        a.DueWhen(c => c.ClosedAt != null, c => c.ClosedAt!.Value);
        a.ArchiveAfter(TimeSpan.FromDays(365));            // hot period after the due event
        a.RetainFor(years: 10);                            // then purge — unless held
        a.DeleteHotRowsAfterArchive();                      // move, not copy (opt-in)
    });

    // ROW retention (telco's CDR detail): bulk age-out, no document (rollup output stays).
    archival.AddRowRetention<RatedUsageDetail>(r =>
    {
        r.After(TimeSpan.FromDays(90), u => u.RecordedAt);
        r.Where(u => u.RolledUp);                           // never purge unsummarized detail
    });
});

// DbContext: modelBuilder.AddGoldpathArchiveModel();  — entries/index/holds/erasures/audit
```

Rollup itself (detail → summaries) stays DOMAIN LOGIC in an app `IGoldpathJob` (D5) — the module
guarantees the purge never outruns it (`Where(u => u.RolledUp)` is the contract).

**Admin API** (`/goldpath/admin/archival`, §7.1, every verb audited): definitions + due/held
counts, entry retrieval by (definition, key) — the p95 < 5s budget lives here, hash-chain
`verify` (on demand + scheduled), legal hold place/lift (scope: definition + key; audited
with the case reference), erasure request by subject key (+ status/history), run views
delegate to the jobs console (same run model).

## 5. Analyzer Rules (GOLDPATH14xx — new block)

- GP1401 (error): an archive definition whose graph carries `[GoldpathPersonalData]` in a
  compilation without the DataProtection module — erasure would be impossible; the archive
  would become a liability.
- GP1402 (warning): `AddRowRetention` without a `Where` guard — age is rarely the only
  truth; make the "safe to purge" predicate explicit.
- GP1403 (info): an archive definition on an aggregate with no `DueWhen` event — archiving
  by insert-age alone usually means the lifecycle was never modeled.

## 6. Performance (measured, not promised)

- Single-entry recall (definition + key) p95 **< 5s** at 1M entries — finance's number;
  proven by a seeded bench (`scripts/bench-archival.sh`, recorded in `ops/`).
- Archive round-trip (extract → store → retrieve, claim-sized graph ≈ 50 rows) p95
  **< 10s** — insurance's number.
- Row retention purges **100k rows < 5 min**, chunked via Jobs, batched deletes.
- Hot-path protection: interactive p95 during a full-tilt archive run degrades **< 10%**
  (same bench pattern the jobs module proved).

## 7. Observability (shipped)

- Metrics: `goldpath_archival_entries_total`, `goldpath_archival_due_backlog`,
  `goldpath_archival_held_total`, `goldpath_archival_erasures_total`,
  `goldpath_archival_verify_failures_total` (tamper alarm — page on ANY),
  `goldpath_archival_retrieval_seconds` (histogram; the <5s budget watches this), plus the run
  metrics inherited from Jobs.
- Grafana panel template in `ops/` (backlog, held, retrieval p95, verify status).
- Alert rules: verify failure > 0 (tamper), due backlog growing across N runs, retrieval
  p95 over budget.

## 8. Operational (runbook or it didn't happen)

Runbooks shipped with the module: auditor recall (retrieve + verify chain for a range),
legal hold placement/lift with case tracking, erasure request end-to-end (request → mask →
evidence), tamper alarm response (chain break localization), purge misfire (what
DueWhen/Where saved you from and how to dry-run), backlog catch-up after downtime (Jobs
resume semantics apply). Every admin verb audited; erasure leaves an evidence row (who,
when, subject, affected entries) — the KVKK answer is a query, not a search.

## 9. Test Plan

Unit (definition builder, envelope round-trip, hash chain math, due/retention windows,
redaction application) · property-based (CsCheck: serialize→store→retrieve→deserialize is
IDENTITY for arbitrary graphs; chain verification detects any single-bit mutation) ·
integration (pg: archive run moves a claim graph and deletes hot rows atomically per chunk;
retrieval under tenancy; hold blocks purge AND hard erasure; erasure masks classified
fields in stored documents; verify catches a manual UPDATE) · perf proofs of §6 · GM: the
solution template's `--features archival` shape through the full pipeline · spec: schema +
drift rows + CLI recipe.

## 10. Slices & DoD

- **S1** — store + hash chain + graph extraction + archive/purge/verify jobs + row
  retention (+ §6 proofs)
- **S2** — admin API (retrieval/hold/erase/verify, audited) + DataProtection-composed
  erasure + metrics + Grafana + runbooks + GP1401–1403
- **S3** — manifest schema key + template `--features archival` + drift/CLI recipe +
  GM shape + ledger/docs
- DoD: all three cards' §archival lines answered line by line; excellence-bar artifacts
  present; UI panels ride the consolidated UI phase (run views already free via Jobs).

## 11. Decision Points (Ömer)

- **D1 — Same-database archive store with a tamper-evident hash chain**; object-store
  backend is a written trigger behind the `IGoldpathArchiveStore` seam.
  **Recommendation: yes — zero new infra, evidence-grade integrity now.**
- **D2 — Graph via EF Include-paths + versioned JSON envelope**; retrieval is the v1
  restore (rehydration deferred with its trigger). **Recommendation: yes.**
- **D3 — Archival executes ON Goldpath.Jobs** (chunk/checkpoint/resume/console for free).
  **Recommendation: yes — the ladder composes itself.**
- **D4 — Erasure = classified-field redaction via the DataProtection catalog**, never
  entry deletion; legal hold exempts BOTH purge and erasure hard-paths.
  **Recommendation: yes — KVKK compliance without breaking the regulatory record.**
- **D5 — Two shapes: graph archive + row retention**; rollup stays app domain logic with
  the `Where` guard as the purge contract. **Recommendation: yes.**
- **D6 — Analyzer block GP1401–1403** as listed. **Recommendation: yes.**
- **D7 — `features.archival` joins the schema WITH S3** (not before), keeping the
  schema-rejects-unimplemented rule intact. **Recommendation: yes.**
