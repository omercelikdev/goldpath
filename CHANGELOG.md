# Changelog

All notable changes to the Goldpath packages are documented here.
Format: [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) · Versioning: SemVer.

## [0.1.0-preview.1] - 2026-07-13

Upgrade guide: `docs/upgrades/0.1.0-preview.1.md` (first release — nothing to upgrade from).

### Added
- **Release hardening (H1–H8) — the enterprise-ready pass before first publish**:
  - *Migrations story (H1)*: migrations live in the APP; Development auto-migrates,
    production is EF bundle-first; `goldpath db init|add|status|bundle` (owner-aware);
    one table set has ONE owner (GP1801); three end-to-end proofs on real PostgreSQL
    including data-preserving column adds; the migrations runbook.
  - *Fail-closed admin surfaces (H2)*: every `Map*Admin` demands the `goldpath-ops`
    policy out of the box; opting out is visible (`exposeUnsecured: true`) and warned.
  - *End-to-end trace correlation (H4)*: run/chunk/replay spans; the operator's request
    traceparent crosses the Quartz boundary via the job data map; bulk batches pin the
    upload trace and every later span links back — one trace id per instruction,
    through the repair path; `docs/ops/trace-correlation.md`.
  - *Frozen admin API contract (H8)*: `docs/rfc/goldpath-admin-contract.md` — envelope,
    paging (`take` clamped [1,500] everywhere), failure nouns, full route inventory,
    with a route-freeze test.
  - *Two-executor bulk kill-9 proof + CI reference benches (H6)*: the winning executor
    killed mid-batch recovers on the second node with NO double payment; all bench
    numbers re-measured on pinned CI hardware into the ops docs.
  - *Versioning & support contract (H7)*: `docs/rfc/goldpath-versioning.md` — lockstep
    train, pre-1.0 rules, support window, the D4 release gate this entry satisfies.
  - Heavy proof gates moved to GitHub Actions: PR gates + integration, nightly GM
    matrix (7 shapes) + migrations proofs + mutation matrix, dispatchable benches.
- **Goldpath.Campaign S3 — campaign is a manifest capability (module COMPLETE — the execution
  ladder L1–L4 closes)** — `features.campaign` joins the manifest schema WITH the first
  cross-field rule: a solution enabling campaign cannot pick `broker: none` (the release
  path IS broker fan-out; the invalid corpus proves the rejection). `dotnet new
  goldpath-solution --features campaign` generates the order-winback sample (type keys as CODE
  constants; a `#error` guard says the broker rule at build time) wired into the SHARED
  jobs block (four riders, still ONE scheduler) AND the app's bus; `goldpath add feature
  campaign` mirrors it — the new `BusLines` seam registers consumers on THE bus, never a
  second one, and refuses a messaging-less app with the D8 teaching text. Drift guards a
  TRIANGLE (campaign + jobs + messaging); GmEverything grew to ELEVEN features.
- **Goldpath.Campaign S2 — the ops surface (audited verbs, live throttle, governor board)** —
  create/pause/resume/abort/throttle on `/goldpath/admin/campaign`, EVERY mutating verb
  audited (`GoldpathCampaignAudit`); the LIVE throttle patches the row and the pacer obeys
  within one tick (bench: exact); abort requires a reason and drains claimed items
  gracefully; GP1701–1703; Grafana governor board + 7 runbooks + measured performance
  (1M enumeration 65k rows/s, sink 57k outcomes/s).
- **Goldpath.Campaign S1 — governed mass-execution, L4 of the ladder** — durable target plan
  with watermarks + LIVE policy on the row, a LONG-LIVED single-leader pacer on Goldpath.Jobs
  (takeover from durable watermarks), publish-then-mark release + state-guarded consumer
  claim (double-send structurally impossible), batching outcome sink (30M items never
  mean 30M writes), stale-claim sweep + completion-flip repair filing, replay heals.
- **Goldpath.Notification S3 — notification is a manifest capability (module COMPLETE)** —
  `features.notification` joins the manifest schema; `dotnet new goldpath-solution --features
  notification` generates the order-confirmed sample (template keys and dedup keys as
  CODE constants — template keys are wire contracts, name them like APIs) wired into the
  SHARED jobs block (now composing three riders: archival, bulk, notification — still
  ONE scheduler); `goldpath add feature notification` mirrors it composition-aware through
  the `JobsOptionsLines` seam; the drift guard pair; GmEverything grew to TEN features.
- **Goldpath.Notification S2 — the read-only ops surface (queue health, evidence views)** —
  the admin API (`MapGoldpathNotificationAdmin`, `/goldpath/admin/notification`) is READ-ONLY BY
  DESIGN: requesting belongs to the app (the notifier), re-sending to the jobs console
  (`replay-items`) — an admin verb that could inject messages would be an evidence hole.
  Recipients are MASKED on every surface (`o***@e***`). Templates view with live state
  counts, template hash, retention window and the oldest-requested age; filtered
  notification queries (tenant fail-closed); suppression and failure reports. Queue
  gauges (`goldpath_notification_queue`, `goldpath_notification_oldest_requested_age_seconds` —
  a stuck channel pages BEFORE customers call) publish from both the templates view and
  the ~30-second send run; Grafana panel + six runbooks. Analyzers **GP1601** (a direct
  `SmtpClient` while Goldpath.Notification is referenced is an evidence hole — warning) and
  **GP1602** (a template without `DeleteBodyAfter` keeps personal data forever — info).
- **Goldpath.Notification S1 — one event becomes one message, with evidence** — the
  transactional-notification core: requests are EVIDENCE ROWS in the app's own database
  (a REQUIRED unique dedup key makes a retry storm land once — proven on the pg unique
  index; rendering happens AT REQUEST TIME so a missing token throws into the app's
  transaction and a bad notice never persists; the TEMPLATE HASH stamped into every row
  proves what was sent even after body retention nulls the content). `Sent` means
  **accepted by the channel** — named honestly; delivery-status feedback is deferred
  with a written trigger. Claim-before-send with a stale-claim sweep: an interrupted
  send repairs, NEVER silently re-sends; bounded in-attempt retry, exhausted → the jobs
  repair queue, `replay-items` is the human-confirmed re-send. `MaySend` suppression is
  evidence too; `NotBefore` is the quiet-hours field. Channels: email (MailKit, MIT)
  and webhook ship with ATTACHMENTS in the contract from day one; SMS is a documented
  seam. Proven on PostgreSQL with a REAL SMTP server (smtp4dev): the renewal mail
  actually lands — subject asserted through the server's API — and a dead webhook
  exhausts, repairs and replays into a live listener. The FOURTH jobs-riding feature.
- **Goldpath.Bulk S3 — bulk is a manifest capability (module COMPLETE)** — `features.bulk`
  joins the manifest schema; `dotnet new goldpath-solution --features bulk` generates the full
  composition (an OrderImport sample — row type + handler as a conditional file — wired
  into a SHARED `AddGoldpathJobs` block with archival: one scheduler however many jobs-riding
  features are enabled); `goldpath add feature bulk` mirrors it with a composition-aware
  recipe — on a jobs-wired app it inserts `AddGoldpathBulkJobs` INTO the existing scheduler
  configuration instead of opening a second one (a new `JobsOptionsLines` insertion seam
  every future jobs-riding feature reuses); TWO drift rows guard the pair; GmEverything
  grew to NINE features.
- **Goldpath.Bulk S2 — the intake ops surface (upload, report, gate)** — the admin API
  (`MapGoldpathBulkAdmin`, `/goldpath/admin/bulk`): octet-stream upload (`curl --data-binary
  @payments.csv` is the whole client story — no multipart ceremony) that fires the
  validate run IMMEDIATELY with the cron as safety net, a definitions view with live
  state counts and the awaiting-approval age, tenant-fail-closed batch queries, the
  value-free error report paged by row number, and approve/reject verbs stamping
  actor + note — the state machine's rows ARE the audit, no separate audit table.
  Intake gauges (`goldpath_bulk_batches` by state, `goldpath_bulk_awaiting_approval_age_seconds` —
  the human-in-the-loop alert) publish from both the definitions view and the
  minute-cron validate run; the Grafana panel pages past the gate SLA; six runbooks.
  Analyzers **GP1501–1503**: a ceiling-less definition is an error (an unbounded
  intake is a decision nobody made), `SaveChanges` inside a row handler is a warning
  (the engine batches per chunk), `AutoApprove` is visible as info.
- **Goldpath.Bulk S1 — file intake becomes a validated, approved, resumable run (L3)** — the
  intake half of the execution ladder's third rung: a content-addressed, streamed file
  store in the app's own database (identical bytes return the SAME batch — a client retry
  storm cannot double-pay; a REJECTED file may be resubmitted deliberately), RFC-4180 CSV
  v1 behind the `IGoldpathBulkFormat` seam (columns map by HEADER NAME — a reordered export
  cannot shift money between fields), typed per-row validation with in-file duplicate
  keys and VALUE-FREE error reports (row+field+message, never the data), a mandatory row
  ceiling refused whole, and an audited approve/reject gate (invalid rows block by
  default; a rejection needs a reason). Execution composes Goldpath.Jobs UNCHANGED: chunks
  over row-number space, the persisted CLAIM lands before any side effect (a row
  interrupted mid-flight goes to the repair queue instead of being silently re-sent —
  MDM constraint 2, proven), row failures ride THE repair queue and the jobs
  `replay-items` verb heals them (the last cleared failure flips the batch to
  Completed). Measured: 10k-row intake 0.39 s against the finance card's 5-minute
  budget; engine overhead ~0.17 ms/row. Proven on PostgreSQL with the real runner:
  trigger → validate run → gate → execute run → poisoned rows repaired → exactly-once
  sink reconciliation.
- **Goldpath.Archival S3 — archival is a manifest capability (module COMPLETE)** —
  `features.archival` joins the manifest schema (the schema-rejects-unimplemented rule
  held to the end: the key lands WITH the capability); `dotnet new goldpath-solution --features
  archival` generates the full composition (Goldpath.Archival + Goldpath.Jobs, an Order sample
  lifecycle, archive/jobs model contributions, both admin APIs mounted on the new
  `goldpath:features endpoints` anchor); `goldpath add feature archival` mirrors it into existing
  apps (provider- and connection-name-aware); TWO drift rows guard the pair — a manifest
  claiming archival without the jobs runtime is a finding. Admin-endpoint service
  parameters are now explicit `[FromServices]` (jobs + archival): build-time OpenAPI
  export runs with no connection string, and inference must never read a GET's service
  as a request body.
- **Goldpath.Archival S2 — the lifecycle ops surface (hold, erasure, verify)** — the admin API
  (`MapGoldpathArchivalAdmin`, `/goldpath/admin/archival`): definitions view with live numbers,
  tenant-scoped retrieval (fail-closed), legal hold place/lift (the hold row IS the audit —
  who/when/case; an active hold exempts purge AND erasure), and the KVKK/GDPR verb:
  **erasure redacts every `[GoldpathPersonalData]`-classified field INSIDE the stored document
  through the real DataProtection catalog**, re-stamps the content hash, marks the entry
  and writes the evidence row — the chain hash never changes, so verification reads the
  divergence WITH the mark as lawful and WITHOUT it as tamper (proven on PostgreSQL with
  the real module wired). Erasure is idempotent evidence: only CHANGED values count, a
  second request reports "nothing left". Plus the `Goldpath.Archival` meter (backlog, appended/
  purged, erasures, verify failures — the tamper alert pages on ANY, retrieval p95 against
  the 5s budget), the Grafana panel, six runbooks, and analyzers **GP1401–1403** (an
  archive that cannot honor erasure is a liability — error; guardless row retention —
  warning; lifecycle-less archive — info).
- **Goldpath.Archival S1 — the data lifecycle after "hot" (L2 composes itself)** — declarative
  GRAPH archives (EF include-paths; whole claim files as one tamper-evident document) and
  guarded ROW retention, executed as `IGoldpathJob`s on the Jobs module: chunked, checkpointed,
  resumable, visible in the same console. Integrity model: every entry seals a **ChainHash**
  at append (immutable; the chain links through it) plus a **ContentHash** of the current
  document — the audited erasure path (S2) will re-stamp content WITHOUT breaking the
  chain, and a divergence with no erasure record is tamper. The VERIFY job files findings
  straight into the jobs repair queue — tamper alarms ride the existing metric. Retention
  purges remove only a contiguous chain PREFIX; an active legal hold stops the purge at
  itself (and the chain still verifies via the purged-head anchor). Proven on PostgreSQL:
  archive-moves-graph, indexed retrieval, a raw-SQL tamper caught, hold blocking the purge,
  guarded row purge; CsCheck properties: any single-character corruption detected, graph
  round-trip is identity.
- **Goldpath.Jobs S3 — the jobs worker is a template choice (module COMPLETE)** — `dotnet new
  goldpath-worker --trigger jobs`: a chunked `NightlyReportJob` sample (plan by count, checkpoint
  per chunk, deadline set — GP1302 enforces the SLA in generated code), `workerType:
  scheduler-quartz` manifest with its cron, the SPEC0113 invariant (a quartz scheduler
  declares its cron), scheduler-quartz-gated drift rows, and the audited admin API mounted
  on the worker. GmWorkerJobs joined the GM matrix and runs the whole story: admin trigger →
  6/6 chunks → audit trail. The GM proof caught two REAL bugs before merge: the sample's
  int day-key silently became an identity column (EF convention; `ValueGeneratedNever` now,
  and the trap is documented), and a poisoned CHECKPOINT save could wedge a chunk as
  Claimed forever — the runner now retreats through a fresh scope (regression-tested).
  Interactive proof recorded: point-read p95 0.4→0.3ms under a full-tilt run (budget <10%).
- **Goldpath.Jobs S2 — the ops surface (§7.1's first full exemplar)** — the admin API
  (`MapGoldpathJobsAdmin`, `/goldpath/admin/jobs`): fleets DISCOVERED from the store (deploying a new
  worker kind just appears — D9), jobs with live trigger state, runs/run detail with the
  repair queue, and the verb set: trigger (+dry-run), pause/resume, fleet-wide
  pause-all/resume-all, reschedule (runtime cron override, D7), rerun (double-run-guarded),
  replay-items (through the job's `IGoldpathItemReplay` hook ON an executor — the type lives
  there), calendar CRUD (holiday/weekly/cron + which-triggers-ride-it). EVERY mutating verb
  writes an audit row (iron rule 2). Management heads now run NO scheduler at all —
  on-demand, never-started schedulers per fleet (zero cluster noise). Plus: the `Goldpath.Jobs`
  meter (§7 vocabulary — progress, items/s, checkpoint age, predicted-overrun-BEFORE-the-
  deadline, repair depth, duration), the Grafana dashboard template and seven runbooks in
  `ops/`, and analyzers GP1301–1303 (renumbered from 05xx: ids are wire contracts). Proven
  by the two-process integration suite driving every verb and asserting the audit trail.
- **Goldpath.Jobs S1 — L2 of the execution ladder lands (clustered)** — scheduled and
  long-running work on a CLUSTERED Quartz persistent store (exactly-once firing, misfire,
  `RequestsRecovery` failover) with the Goldpath run model on top: chunked/checkpointed/
  RESUMABLE runs, per-item repair queue, live progress + deadline prediction, completion
  chaining (`StartAfter<T>`), input version pinning, business-day calendars. The `qrtz_`
  schema ships as EF model contributions (`modelBuilder.AddGoldpathJobs()`) — the store never
  escapes migration discipline. Two modes: `AddGoldpathJobs` (executor; one cluster per worker
  kind) and `AddGoldpathJobsManagement` (API head, thread pool 0 — verbs everything, executes
  nothing). PROOF: real kill-9 (child processes) — node A killed mid-run, node B recovers
  the fire and RESUMES from the checkpoint, every chunk exactly once except at most the
  in-flight one; management member triggers a stored job it cannot execute; bench 100k
  items in 0.4s with 2.1ms/chunk checkpoint cost (budgets: 5min/25ms). Mutation 73.8%.
- **The `goldpath` CLI exists (Slice C — template completion DONE, Phase A closes)** — a dotnet
  tool (`Goldpath.Cli`, command `goldpath`) that WRAPS what exists, no new semantics: `goldpath new
  solution|worker` (template passthrough), `goldpath add feature <name>` (the drift profile's
  row applied to an existing app: manifest line + package + registration + model call +
  feature extras, landing on the `goldpath:features` anchors exactly as generation would), and
  `goldpath check` (specdrift validate + drift + build, one verb). Every `add` ends in an engine
  round-trip; a red engine restores every touched file byte-identical and fails loudly.
  Context-aware where the template was: audit registration infers the DbContext type,
  locking detects provider + connection name, idempotency composes with caching (and
  caching retires the memory fallback). The CLI embeds the manifest schema it was built
  from, so tool and contract cannot drift apart. Templates gained middleware/resources/
  references anchors for the same reason. Proof: a plain generated app transformed
  feature-by-feature into the everything-on shape by `goldpath add` alone — engine clean at
  every step, then the REAL smoke suite green on the CLI-composed app.
- **Template: the WORKER kind exists (Slice B of template completion)** — `dotnet new
  goldpath-worker --trigger queue|schedule`: a probe-carrying host (readiness/liveness are the
  worker's deployment contract too). `queue` generates an inbox-guarded MassTransit
  consumer (exactly-once: dedup rides the same DbContext transaction as the work) over
  postgres/sqlserver; `schedule` generates a BCL PeriodicTimer batch skeleton (RFC D3 — the
  Ring C Jobs module's landing pad), zero infrastructure. `kind: worker` manifests are lean
  by schema design (workerType + trigger; providers live in the owning solution) and get
  their own specdrift profile: workerType-gated wiring rows plus two teaching rules
  (SPEC0111 consumer-names-its-queue, SPEC0112 batch-consumes-nothing). GM matrix gains
  GmWorkerQueue + GmWorkerSchedule; the queue smoke publishes into the real broker twice
  with one MessageId and proves exactly-once end to end.
- **Template: Ring B features are now GENERATION choices (Slice A of template completion)** —
  `dotnet new goldpath-solution --features multitenancy --features audittrail ...` (7 choices):
  each selection generates its manifest line, package reference, `AddGoldpath*` registration and
  model call — exactly the drift profile's rows, so specdrift is the acceptance test.
  Feature deltas ship as conditional PARTIAL entity files; caching provisions a Redis
  resource in the AppHost; locking lives in the app database (postgres/sqlserver aware);
  multi-tenant smoke sends the tenant header. Proven: a 5-combination matrix
  (generate+validate+build+drift) and the everything-on shape through the full GM pipeline
  (GmEverything, real containers, 27s — now in the CI gm-matrix). `goldpath:features` anchors
  ship for the upcoming CLI's textual `add`.

### Changed
- **Mediant 1.1.0 → 1.2.0 (the composition loop closes)** — all three issues this asset
  filed shipped: #129 (GET positional-record binding), #130 (HybridCache-backed query
  caching — [Cacheable] hits now get L1+L2), #131 (a real default ICacheInvalidator —
  [InvalidatesCache] works, tag-based). Goldpath.Caching now registers AddMediantHybridCaching:
  both cache surfaces ride ONE HybridCache with one tag vocabulary. The deliberately-pinned
  no-op test broke on the version bump exactly as designed and flipped to the real
  semantics. Template pins bumped alongside.

### Added
- **Goldpath Skills v1 — the Phase 2 gate is MET** — the template now ships the agentic layer:
  `goldpath-feature` (business sentence → merge-ready slice; contract-first, engine-checked),
  `goldpath-manifest` (toggles with named consequences; manifest+wiring change together),
  `goldpath-test-gen` (spec-derived tests with a written context DIET — never reads
  implementations, foundation §8.2), the `breaker` agent (falsification as executable
  tests), and `.mcp.json` registering specdrift — "done" without clean
  spec_validate+spec_drift is not done (ADR-0004 made procedural). Every skill carries an
  outcome-only eval (`evals/skills/`, foundation §5's hard rule); the goldpath-feature eval ran
  end to end: 9/9 acceptance on a generated app. Its first run also caught a
  doc-vs-reality drift (folder-per-feature vs file-per-feature) — docs aligned.

### Added
- **Spec Engine v1 COMPLETE (M4 — goldpath integration)** — the template now ships the goldpath
  specdrift profile: 5 cross-field invariant rules (outbox⇒broker, l2⇒cache provider,
  redis-locking⇒cache provider, subdomain+apikey warning, hmac secret-store discipline) and
  the full Ring B wiring table (feature ⇄ package ⇄ registration call, value-gated where
  strategies differ) plus the committed-vs-built OpenAPI pair. validate-gm.sh runs
  spec-lint (validate + drift) on every generated shape; the CI pipeline gains the
  spec-lint job (valid corpus passes WITH rules, invalid corpus must fail). The deferred
  analyzer ledger was revisited against the engine: GP0403 moves up a layer (drift/AsyncAPI,
  engine v2); the rest recorded per-rule. Engine pinned at v0.4.0.

### Fixed
- **Two rots caught by specdrift's first run against this repo** (Spec Engine M1 proof):
  the corpus manifest `m2-service-full.json` still used `dataProtection.piiFields`, an
  option the schema dropped when the module shipped — refreshed to the current options;
  and the template's source manifest hardcoded `auth: openid` instead of carrying the
  `--auth` choice — now a `GOLDPATH-AUTH` replacement token, so the generated manifest tells
  the truth about the generated app.

### Added
- **CI gates authored end-to-end** — every
  local gate is now a pipeline job: build (PublicAPI/audit), format, full test suite, the
  Testcontainers integration suite over DinD, the mutation gate fanned out one job per
  package, the license gate (self-provisioning python3), the GM matrix with the three
  locally-proven shapes (authed default / open flow / sqlserver+no-broker), benchmarks and
  pack. Pipelines stay manual-only until the runner exists; the runner runbook makes the fix
  one step (requirements, registration commands, sizing, DinD alternatives) and the ENABLE
  edit at the top of the file is the single switch that turns local discipline into a
  machine gate (ADR-0005/0010). Definition is server-lint clean.

### Added
- **Goldpath.Analyzers batch 3 — the full Ring B rule backlog (12 rules, 23 total)** —
  GP0701/0702 (classified PII on integration events; classification without the
  DataProtection module), GP0801/0802/0803 (cache attribute misuse; raw cache keys),
  GP0901/0902/0903 (multi-tenancy wiring, manual tenant writes, filter-dodging without a
  visible Bypass), GP1101/1102 (raw lock names; leaked lock handles), GP1201/1202
  (anonymous-surface inventory; literal secrets on the Goldpath auth surfaces). All rules stay
  severity-configurable and suppressible with justification (ADR-0005); heuristic rules say
  so in their docs. The analyzer backlog is now EMPTY except the recorded manifest-dependent
  deferrals (GP0101/0103/0203/0403/1001/1002/1004).

### Added
- **Test hardening (foundation §8 alignment)** — the mutation gate now covers EVERY package
  (`scripts/mutation-gate.sh`: per-package Stryker configs under `stryker/`, break-at 70 —
  below the threshold, no merge), closing the drift where only Goldpath.Data was mutation-scored.
  Property-based coverage (CsCheck) added to the algorithmic surfaces that shipped
  example-based: the TenantId grammar (any invalid character anywhere rejects; boundary
  exact), the cache-key convention (distinct tenants never collide for any area/key), and
  the lock-name convention (distinct tenants never contend; global/tenant namespaces
  disjoint). Goldpath.Locking's metered decorator gained direct unit coverage (all four acquire
  shapes, contended and not). Gate scores at introduction: Abstractions 95.2 · SoftDelete 91.7
  · Locking 91.7 · AuditTrail 80.4 · DataProtection 79.0 · Data 76.9 · Auth 76.4 ·
  Idempotency 76.0 · Caching 73.9 · MultiTenancy 72.6 — every package ≥ break 70. The gate
  run itself drove 30+ new tests (metric emission per acquire path, guard messages, option
  branches, the audit schema contract, claim fallback chains) and one justified named ignore
  (Redis's eager-connect factory — container-tested, unit-unreachable).

### Added
- **Goldpath.Auth (Ring B companion — the last cross-cutting gap)** — the manifest's
  `providers.auth` gets its implementation: OIDC/JWT bearer composed against any IdP
  (openid, default) or a minimal named-client API-key handler; secure-by-default fallback
  policy (anonymous is an explicit `[AllowAnonymous]`; probes exempted in
  MapGoldpathDefaultEndpoints); token–tenant binding with MultiTenancy (mismatch = 403 +
  `goldpath_auth_tenant_binding_rejects_total`); Mediant `[Authorize]` composed on the same
  principal for command-level checks. Template gains `--auth openid|apikey|none`; the
  authed golden-manifest shape is GREEN (secure-by-default 401 + green probes is the
  first-click contract). saml/ldap are schema-rejected strategic deferrals.

### Added
- **Goldpath.Locking (Phase 2, item 14 — Ring B COMPLETE)** — Medallion DistributedLock composed
  (MIT): Redis / Postgres advisory locks / SQL Server sp_getapplock behind manifest-driven
  provider selection; application code injects Medallion's own `IDistributedLockProvider`
  (no wrapper; metrics via a decorator over the same interface —
  `goldpath_lock_acquire_total{outcome}`, `goldpath_lock_wait_seconds`). Tenant-scoped lock names
  (`GoldpathLockNames`, fail-closed without an ambient tenant; explicit `Global()` escape).
  The same contract test proven on real Redis AND Postgres. Fencing tokens honestly out of
  scope: locks reduce duplicate work, idempotency guarantees correctness — the modules compose.
  The SQL Server provider ships as the OPTIONAL `Goldpath.Locking.SqlServer` package: the license
  gate caught that its chain carries Microsoft's proprietary-but-free SqlClient SNI runtime —
  scoped exception recorded in the gate itself; the core Goldpath.Locking graph stays fully OSS.

### Added
- **Goldpath.MultiTenancy (Phase 2, item 13)** — the tenant becomes ambient truth on every seam:
  header/subdomain resolution (fail-closed 400 with exempt paths), a live tenant query filter
  on every `IMultiTenant` entity (context-rooted — the only shape EF re-evaluates per query),
  automatic stamping, and a cross-tenant write guard that throws and counts a security metric
  (`goldpath_tenant_write_guard_trips_total`). Explicit `GoldpathTenant.Bypass()`/`Use()` scopes.
  THE FULL SQUARE proven on real Postgres+RabbitMQ: HTTP tenant survives middleware → EF
  stamp → outbox → broker → consume restore → consumer-side stamp. Supporting changes:
  Abstractions gains `GoldpathAmbientTenant` (flow carrier), Data gains `GoldpathQueryFilters`
  (AND-composition; SoftDelete migrated — two modules' filters no longer erase each other),
  Messaging's consume filter restores the ambient tenant. Strategic deferrals per approval
  condition (path-prefix, db-per-tenant): tracked in the module plan, REJECTED by the
  manifest schema until implemented.

### Added
- **Goldpath.Caching (Phase 2, item 12)** — Microsoft HybridCache as the app surface (L1 + Redis
  L2 per manifest, stampede-protected, tag invalidation) and Mediant
  `[Cacheable]`/`[InvalidatesCache]` as the query surface, composed without wrappers.
  Tenant-scoped key convention `goldpath:{tenant}:{area}:{key}` (`GoldpathCacheKeys`) baked in ahead of
  MultiTenancy. L2 proven on real Redis (Testcontainers): cross-host L2 read, tenant isolation.
  Known gap recorded: Mediant 1.1.0 ships no `ICacheInvalidator`, so query-path eviction is
  TTL-only — filed as mediant#131 (pinned by a deliberately-failing-when-fixed test);
  query-path L1 filed as mediant#130.

### Added
- **Goldpath.DataProtection (Phase 2, item 11)** — classify a property ONCE (`[GoldpathPersonalData]`/
  `[GoldpathSensitiveData]` attributes or the type-safe code catalog for untouchable entities) and
  every sink masks it consistently: audit change rows (per-property masking replaces the
  all-or-nothing `namesOnly` as the recommended posture), MEL log redaction (the Goldpath
  attributes are Microsoft `DataClassificationAttribute`s — `EnableRedaction()` +
  `[LogProperties]` works natively), and Mediant audit patterns (catalog names feed
  `SensitivePatterns`; Mediant `[SensitiveData]` recognized by name). Redaction composed from
  Microsoft.Extensions.Compliance: `***` erasure default, HMAC pseudonymization opt-in.
  Goldpath.Abstractions gains the classification contracts and its single micro-dependency
  (Microsoft.Extensions.Compliance.Abstractions).

### Added
- **Goldpath.Analyzers batch 2 — Ring B module guards** — GP0501 (`IAuditLogged` entity with a
  DbContext present but no `AddGoldpathAuditLog()` call, error), GP0502 (manual writes to audit
  stamp fields from application code, warn; save contributors exempt), GP0601
  (`ISoftDeletable` entity with a DbContext present but no `ApplyGoldpathSoftDelete()` call, error),
  GP1003 (`[Idempotent]` on a Mediant query — a no-op, info). Entity-only assemblies are
  exempt from the wiring checks; all rules remain severity-configurable and suppressible with
  justification (ADR-0005).

### Added
- **Goldpath.SoftDelete (Phase 2, item 10)** — deletes of `ISoftDeletable` entities become stamped
  updates touching exactly three columns; a global `!IsDeleted` filter hides them everywhere
  (`ApplyGoldpathSoftDelete()`); hard deletion is the explicit `GoldpathSoftDelete.Suppress()` scope
  (right-to-erasure flows). AuditTrail interplay proven: a soft delete audits as the
  `IsDeleted false→true` change — one story, same transaction. Data seam gained contributor
  ordering (`IEntitySaveContributor.Order`, additive default member; SoftDelete runs at −100).

### Added
- **Goldpath.AuditTrail (Phase 2, item 9)** — two audit levels, one correlated story: Mediant
  `[Auditable]` command audit (EF store composed) + Goldpath entity audit via the Data
  save-contributor seam — stamps auto-filled and old→new change rows written in the SAME
  transaction as the change (rollback leaves no audit rows — proven). New `IAuditLogged`
  marker (change rows) alongside `IAuditedEntity` (stamps). `IUserContext` added to
  Abstractions (HTTP-claims implementation included); `GoldpathSaveContext` carries `User`;
  the Data interceptor now snapshots entries so contributors can add rows safely.

### Added
- **Goldpath.Idempotency — Ring B opens (Phase 2, item 8)**: the HTTP `Idempotency-Key` middleware
  composed on Mediant 1.1.0's idempotent-operation coordinator (#114/#115 — one store, one
  semantics across HTTP and `[Idempotent]` commands). Behavior locked per the RFC: replay is
  byte-for-byte (`Goldpath-Idempotent-Replay: true`), concurrent duplicate → 409 (or Wait mode:
  serialize + replay), key reuse with a different payload → 422 (SHA-256 fingerprint),
  key scope `http:{tenant}:{method}:{path}:{key}`, only 2xx stored. Manifest schema gained
  the idempotency options object.

### Added
- **Phase 1 gate items (7c part 2)** — build-time OpenAPI export in the template (the
  deterministic Spec Engine `drift` input; config made docgen-tolerant), the GM matrix as a
  CI job (manual until the DinD spike passes on company runners), and **pipelines re-enabled**
  (MR/main). Architecture shapes proposed as 7d parallel to early Phase 2 (decision pending).

### Added
- **Ops decisions + gates (7c part 1)** — foundation §7.1: the three-surface ops rule
  (Observe=dashboards, Operate=InfraOps portal with "no screen without an Admin API /
  no action without audit", Configure=manifest) and the central-logging decision
  (MEL+OTel+Collector; deliberately not Serilog — sink choice is environment config, never
  code; reference collector config in the ServiceDefaults ops package). **License gate live:**
  scripts/license-gate.py — 217 packages (transitive included) all free/OSS; SPDX OR/AND
  semantics; PostgreSQL license allowlisted; verified legacy list. CI: license-gate (blocking),
  benchmark and DinD-spike jobs (manual until pipelines resume).

### Added
- **Template shape choices (7b part 2)** — the selection story begins, with the rule
  "a choice is only offered once its combination is proven": `--db postgresql|sqlserver`,
  `--broker rabbitmq|none`. Conditional composition throughout (packages, AppHost resources,
  Program.cs, outbox lock provider, manifest); `broker=none` generates zero messaging code.
  `scripts/validate-gm.sh` generalizes the local proof per shape (GM-1 and the GM-4 shape green).

### Added
- **Integration suite (7b)** — real-container proofs (Testcontainers, postgres:17 + rabbitmq:4):
  THE outbox atomicity proof (a rolled-back transaction publishes nothing; a committed one
  delivers exactly once) and Postgres keyset parity (composite DateTimeOffset walk with
  duplicate-key pressure, Guid descending walk, member-init projection). Designing the proof
  surfaced a real gap: `AddGoldpathOutbox` now auto-registers the consumer-side EF inbox on every
  receive endpoint (previously only the publish side was outboxed).

### Added
- **Goldpath.Templates (7a)** — `dotnet new goldpath-solution` scaffolds the GM-1 golden-path shape:
  Aspire AppHost (postgres+rabbitmq containers), Ring A floor, a Mediant vertical-slice
  walking skeleton (idempotency-ready POST, keyset-paginated GET, outboxed integration event
  + consumer), manifest, CLAUDE.md family, pinned everything, and a smoke suite that drives
  the REAL AppHost. `scripts/validate-gm1.sh` = the local ADR-0008 proof (pack → install →
  generate → build → smoke). The walking skeleton immediately earned its keep: it surfaced
  the Mediant positional-record GET-binding gap (mediant#129) and the Goldpath.Data projection
  rule (member-init projections only — now documented in the Data README).

### Added
- **Goldpath.Analyzers** — executable standards (ADR-0005) as a Roslyn analyzer package
  (netstandard2.0, `analyzers/dotnet/cs`): GP0102 (new HttpClient), GP0202 (Skip/Take
  offset pagination), GP0301 (DateTime on marked entities), GP0302 (unguarded runtime
  Migrate/EnsureCreated), GP0303 (interpolated raw SQL — error), GP0401 (publishing
  without IIntegrationEvent — error), GP0402 (notification cross-marked — error).
  Rules match types by metadata name (zero hard dependencies; inert when the seam is absent —
  L1 à-la-carte safe). Deferred rules (0101/0103/0203/0403) recorded in the RFC.

### Added
- **Goldpath.Messaging** — the message-path floor (`net8.0;net10.0`, transport-neutral, pinned to
  the **MassTransit 8.x OSS line** — v9 went commercial; exit strategy in the RFC): the
  Mediant/MassTransit event boundary with a runtime `IIntegrationEvent` guard (GP0401 until
  the analyzer ships), kebab-case topology, tenant/correlation header propagation with
  consume-side restoration (`GoldpathMessageTenantContext`), retry defaults (immediate ×3 +
  delayed redelivery 5m/15m/30m → error queue), and `AddGoldpathOutbox<TContext>` composing
  MassTransit's EF transactional outbox (30m dedup window).

### Added
- **Test hardening + repo CI**: repo CI pipeline (build/test/mutation/pack on every MR),
  Stryker.NET mutation testing on Goldpath.Data (score 65.67% → 81.16% after targeted hardening;
  break threshold ratcheted to 70), CsCheck property-based tests (cursor codec fuzz +
  keyset walk invariant over random datasets), BenchmarkDotNet baseline for the cursor codec.
  The hardening pass caught and fixed a real defect: the enum-to-string convention silently
  overrode explicit `HasConversion<int>()` provider-type conversions.

### Added
- **Goldpath.Data** — the data-path floor (`net8.0` EF8 / `net10.0` EF10, provider-neutral):
  keyset cursor-pagination executor (`ToPageAsync`, 1-2 keys with per-key direction, self-ordering,
  size+1 last-page detection, never OFFSET; invalid cursor → `GoldpathInvalidCursorException`),
  the save-contributor seam (`IEntitySaveContributor` + single interceptor with clock/tenant
  context — Ring B modules plug in here), and model conventions (string 256, decimal 18,4,
  enum-as-string, `TenantId` conversion). Migration policy per RFC D1: dev auto-migrate,
  production bundle-only. NuGet audit caught the vulnerable SQLitePCLRaw native lib
  (GHSA-2m69-gcr7-jv3q, test-only dep) — lifted to patched bundle 3.0.3. Third catch of the gate.
- **Goldpath.ApiDefaults** — API surface conventions (`net8.0;net10.0`): URL-segment versioning
  (default v1, versions reported), the keyset cursor-pagination wire contract
  (`PageRequest`/`Page<T>`/`GoldpathCursor` — opaque base64url cursors, no total count by design),
  JSON wire defaults (camelCase, enums as strings, null-writes ignored), and deterministic
  OpenAPI generation with a Development-only interactive endpoint (net10-only pillar).
  NuGet audit blocked the vulnerable transitive `Microsoft.OpenApi` 2.0.0 (GHSA-v5pm-xwqc-g5wc);
  lifted to patched 2.9.0 via a direct pinned reference.
- **Goldpath.ServiceDefaults** — the Ring A floor (`net8.0;net10.0`): OpenTelemetry
  (traces/metrics/logs, profile-driven sampling, OTLP + dev console exporter), health
  endpoints (`/health/live`, `/health/ready` — always mapped, masked), RFC 9457
  ProblemDetails with `correlationId`/`traceId` extensions, correlation middleware
  (`X-Correlation-Id`), HTTP resilience + service discovery client defaults, and a global
  concurrency guard (429 as ProblemDetails). One call: `AddGoldpathServiceDefaults()` +
  `MapGoldpathDefaultEndpoints()`. Ops baseline (dashboard/alerts/runbook) included.
  Note: NuGet audit caught known CVEs in OpenTelemetry 1.11.x during development —
  pinned to patched 1.16.0 (the supply-chain gate working as designed).
- **Goldpath.Abstractions** — zero-dependency contract layer (`net8.0;net10.0`):
  `TenantId` (validated value type) + `ITenantContext`; entity capability markers
  `IAuditedEntity`, `ISoftDeletable`, `IMultiTenant`; `IIntegrationEvent` marker
  (broker-bound events, per the Messaging boundary); `GoldpathHeaders` canonical header names.
  Public API locked with PublicApiAnalyzers.
- Solution infrastructure: pinned SDK (`global.json`), central package management
  (`Directory.Packages.props`, everything pinned), warnings-as-errors + XML docs enforced
  (`Directory.Build.props`), single explicit NuGet feed (`nuget.config`).
