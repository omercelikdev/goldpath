# RFC: Template Completion — features at generation, worker kind, the `goldpath` CLI

> Status: v1.0 accepted — D1–D5 approved by Ömer (2026-07-06), together with the REVISED
> program order: samples move AFTER the remaining core modules (scenario cards first as
> requirements input) — see docs/strategy/program-ledger.md. Closes the library-completion ledger
> (2026-07-06 review with Ömer) so the sector-sample phase starts on a finished floor.
> Effort L (three shippable slices, sequential).

## 1. The three gaps, honestly stated

1. **Ring B features are not generation choices.** The manifest knows seven features; the
   template generates wiring for ONE (outbox) plus auth. A team wanting multi-tenancy or
   audit today must hand-wire what the drift profile describes. The golden path's core
   promise ("manifest'te aç, uygulama ona dönüşsün") is only half-delivered at `dotnet new`
   time.
2. **Only one kind exists.** The manifest speaks `solution|service|module|worker|gateway`;
   only the solution shape has a template. A worker (queue consumer / scheduled batch host)
   is the most common second head an enterprise adds — and today it means hand-rolling.
3. **No `goldpath` CLI.** Foundation L2 promises `goldpath init`/add ergonomics; today the entry
   point is raw `dotnet new` + hand edits. Fine for us, wrong for a pilot team.

## 2. Slices (sequential MRs)

### Slice A — feature flags at generation
`dotnet new goldpath-solution --features multitenancy,audittrail,softdelete,...` (multi-choice):
each selected feature generates its manifest line, its `PackageReference`, its
`AddGoldpath*` registration and its model call (`ApplyGoldpath*`/`AddGoldpathAuditLog`) — exactly the rows
the drift profile declares, so **specdrift is the acceptance test**: any generated
combination must be validate+drift clean. GM matrix gains one "everything-on" shape.
Idempotency composes with Redis when caching l2 is on; DataProtection adds the classified
sample field to Order (a living example, not dead config).

### Slice B — the worker kind
`dotnet new goldpath-worker -n Billing.Nightly` → a host with: ServiceDefaults floor, MassTransit
consumer wiring (inbox-guarded) OR a scheduled `BackgroundService` skeleton (choice:
`--trigger queue|schedule`), `kind: worker` manifest validating against the schema, its own
`.specdrift` profile, smoke test (container-backed for queue; time-abstracted for schedule).
NOT the Jobs/Quartz module (Ring C, needs its RFC) — this is the plain hosting shape; the
schedule trigger is BCL `PeriodicTimer`, honest and dependency-free, and it becomes the
Jobs module's landing pad later.

### Slice C — the `goldpath` CLI (thin, deterministic)
A dotnet tool (`goldpath`) that WRAPS what exists — no new semantics:
- `goldpath new solution|worker ...` → `dotnet new` passthrough with manifest-aware defaults
- `goldpath add feature <name>` → applies the drift profile's row (package + registration +
  model call + manifest line) to an EXISTING app — the textual, documented-boundary
  transform; ends with specdrift validate+drift (fail = rollback, loudly)
- `goldpath check` → specdrift validate+drift + build in one verb
- `goldpath init` (L2 attach) is OUT of v1 — attaching to arbitrary brownfield needs the
  transformation pack's analysis; recorded, not silent.
Home: the goldpath repo (`tools/Goldpath.Cli`), packaged like the rest; same gates.

## 3. Decision Points (Ömer)

- **D1 — Slice order A→B→C** (A unblocks the most; C depends on A's rows being real).
  **Recommendation: this order.**
- **D2 — Feature flags as ONE multi-choice `--features` parameter** (comma list) rather
  than seven booleans — mirrors the manifest, keeps `dotnet new --help` readable.
  **Recommendation: multi-choice.**
- **D3 — Worker's schedule trigger = BCL PeriodicTimer skeleton** (no Quartz yet; Ring C
  Jobs module will slot into the same shape). **Recommendation: yes, recorded.**
- **D4 — `goldpath add feature` edits are textual** (anchor-comment driven: the template ships
  `// goldpath:features` anchors so the CLI inserts deterministically), same documented boundary
  as the drift engine; Roslyn-grade rewriting is explicitly out.
  **Recommendation: anchors + textual.**
- **D5 — `goldpath init` deferred to the transformation pack** with its trigger written.
  **Recommendation: defer.**

## 4. DoD (per slice; sequential MRs)
- [x] A: all 7 features generate wired (multi-choice `--features`, repeated-flag syntax:
  `--features multitenancy --features audittrail ...`); feature parts ship as conditional
  PARTIAL files (Order.MultiTenancy.cs etc. — clean composition, no interface-list splicing);
  matrix proven: 5 combinations generate+validate+build+drift GREEN, and the everything-on
  shape passed the FULL GM pipeline (GmEverything: redis resource, tenant-header smoke,
  postgres locking, 27s) — added to the CI gm-matrix. Idempotency without caching gets a
  documented in-memory store fallback. goldpath:features ANCHOR comments ship in Program.cs,
  the csproj and OnModelCreating for Slice C's textual `goldpath add`.
- [x] B: both worker triggers generate, build, smoke; manifest kind validates (lean by
  schema design: workerType + trigger, providers stay in the owning solution); its own
  specdrift profile (workerType-gated wiring, SPEC0111/0112 teaching rules, no openapi —
  probes are not business contracts); GM gains GmWorkerQueue + GmWorkerSchedule (validate-gm.sh
  is template-parameterized via GOLDPATH_GM_TEMPLATE). The queue smoke proves exactly-once with a
  duplicated MessageId against the real broker; the schedule tick is time-abstracted AND
  observed live (AppHost pins Worker:Interval=1s for the dev loop).
- [x] C: `goldpath new/add/check` against a generated app end to end; `add feature` proven by
  specdrift before/after (engine-red → byte-identical rollback, tested); packaged with the
  standard gates (tools/Goldpath.Cli, PackAsTool `goldpath`, mutation gate ≥70 with the IO shells
  Program.cs/ConsoleProcessRunner excluded as untestable process plumbing — inline in
  stryker/Goldpath.Cli.json). The templates additionally ship middleware/resources/references
  anchors so `add` lands feature extras (tenant middleware order, the AppHost redis
  resource) exactly where generation would. The CLI embeds the manifest schema it was built
  from — tool and contract cannot drift apart. `goldpath add` refuses non-solution manifests
  (features live in the owning solution).
- [x] Ledger updated; sector-samples RFC unblocked and referenced
