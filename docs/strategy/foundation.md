# Goldpath Enterprise Asset — Foundation Analysis and Roadmap

> Purpose: the **crack-proof foundation** of an AI-native, spec-driven, enterprise .NET accelerator.
> This document is a living document; every major decision is recorded here as an ADR.
> Status: v0.1 — first comprehensive analysis (2026-07-03)

---

## 1. Product Definition — What It Is and What It Is Not

**What it is:** A golden path for enterprise .NET projects: composable NuGet libraries + templates
+ AI skills + guardrails + living documentation. An end-to-end delivery pipeline from design to
operations — AI-heavy but human-gated.

**What it is not:**
- Not a framework (it does not impose a project structure; the anti-ABP positioning is preserved)
- Not an abstraction over anything Microsoft already provides (Aspire, Extensions.*, OTel, Polly are used directly)
- Not a custom DSL (spec formats are industry standards: OpenAPI, AsyncAPI, JSON Schema)
- Not a fully autonomous AI factory (human approval at every gate; the banking/telco audit reality)

**Three adoption levels (the guarantee of non-imposition — immutable):**
| Level | Approach | Target customer |
|---|---|---|
| L1 | NuGet only — add packages to an existing project | Brownfield, cautious |
| L2 | `goldpath init` — attach manifest + skills + guardrails to an existing solution | Gradual transformation |
| L3 | `goldpath-solution` — full scaffold from scratch | Greenfield, transformation projects |

---

## 2. Constitution — 10 Decisions the Foundation Must Never Crack On (ADR-000x)

If these decisions change, the product has effectively been redesigned from scratch. Each will be a separate ADR file.

1. **The manifest is the single source of truth.** `.goldpath/manifest.yaml` (validated with JSON Schema).
   Wizard, CLI, skills, CI — all of them read from and write to the manifest. CLI flags are
   merely one way of producing a manifest.
2. **Spec formats are industry standards.** API = OpenAPI, events = AsyncAPI, data = JSON Schema.
   The enterprise manifest is only a thin binding layer (which specs, which modules, which profiles).
3. **The Microsoft layer is configured, not wrapped.** Infra (logging, health, resilience,
   rate limiting, OTel) is Microsoft packages; the template sets up best-practice configuration.
   Goldpath NuGets contain only the enterprise patterns Microsoft does not provide.
4. **Deterministic/agentic separation.** The boundary layer (DTOs, clients, contracts, skeletons) =
   deterministic codegen; business logic = skills (AI). Everything deterministic is 100% tested;
   AI output passes through guardrails and a human.
5. **Standards are executable.** A standard = schema + analyzer + architecture test +
   CI rule. A rule written in a document but not machine-verified does not count as a standard.
6. **AI lives in the development layer, not in the libraries.** There is no AI inside the NuGet
   packages (the stability + maintenance promise). AI = context files + skills + review agent + telemetry.
7. **Skills live in the same repo as the standards, on the same version, and are tested.**
   A skill without golden manifest → expected output evals does not get merged.
8. **"Runs on first click" is a proven property.** The golden manifest matrix goes through the
   generate→build→spin up→smoke test loop on every commit (Template CI). "latest" is forbidden; everything is pinned.
9. **Documentation is generated, not hand-written (wherever possible).** Documents that cannot
   be generated are subject to a freshness check in CI. "No doc = no merge."
10. **Human gates are fixed.** Spec approval (business), MR approval (dev), cutover approval (ops).
    No skill may bypass these gates.

---

## 3. The Spec Layer — The Backbone of Design

### 3.1 Spec taxonomy
| Spec | Format | Owner | Production method |
|---|---|---|---|
| Service manifest | YAML (enterprise, with JSON Schema) | Tech lead | Wizard / `goldpath init` / authoring skill |
| API contract | OpenAPI 3.1 | Analyst + dev | Authoring skill drafts, human approves |
| Event contract | AsyncAPI 3 | Analyst + dev | Authoring skill drafts, human approves |
| Data model | JSON Schema | Analyst + dev | Authoring skill drafts, human approves |
| Domain memory | Markdown (structured: ubiquitous language, BC map, rules) | Team | Skills update it on every operation |
| Architecture decisions | ADR (MADR format) | Tech lead | By hand + skill draft |
| Integration spec | `externalSystems` section in the manifest + mock definition | Dev | Reverse-engineer / authoring skill |

### 3.2 Spec lifecycle
`draft → review → approved → implemented → deprecated`
- Status is kept as frontmatter in the manifest/spec file.
- The `draft → review` transition depends on two validations:
  **spec-lint** (mechanical: schema, naming, reference integrity, are example tables filled in —
  a spec without examples cannot enter review) + the **`spec-review` skill** (semantic: ambiguity,
  contradiction, missing edge cases, ubiquitous language conformance, conflicts with existing specs).
  The analyst runs this loop themselves before submitting — they see the errors before anything leaves their desk.
- The generate skill does not run without `approved` (gate #1).
- A spec change = an MR; contract tests surface old/new incompatibility in CI.
- A breaking spec change is a SemVer major; consumers are notified automatically (CI).

### 3.3 Manifest v1 scope (headings for the schema draft)
`name, boundedContext, deploymentModel (monolith|modular-monolith|microservice),
codeOrg (clean|vertical-slice), providers (db/cache/broker/auth), features (cross-cutting toggles),
modules (Goldpath advanced modules), specs (openapi/asyncapi refs), externalSystems (+mock strategy),
observability profile, tenancy profile, nfr (p95 latency, TPS, error budget — the gate targets
for load/stress tests; see 8.4)`

---

## 4. Living Documentation Architecture (the heart of the maintenance promise)

Three document classes, three different freshness mechanisms:

| Class | Example | Freshness mechanism |
|---|---|---|
| **Generated** | API reference (from OpenAPI), module configuration reference (from options classes), solution topology (from the manifest), CHANGELOG (from conventional commits) | Regenerated on every build — drift is impossible |
| **Living context** | CLAUDE.md, .claude/conventions.md, architecture.md, domain memory | Skill DoD: no skill may open an MR without updating the relevant context file as it finishes its work |
| **Curated** | ADRs, onboarding, runbooks | CI freshness check: if the code a document points to has changed, a "doc-review-needed" label is applied; ADRs are immutable anyway (a new one supersedes the old) |

Rules:
- Hand-written documentation is **kept to a minimum**; if a piece of information can be generated from code/spec, it is generated.
- Every module's README has 4 fixed sections: Getting Started / Configuration / Advanced / Providers (existing decision preserved).
- Docs drift CI check: code references inside documents (file paths, class names) are matched against real code; broken ones downgrade the build to a warning.
- CLAUDE.md is the single project summary read by both humans and AI — no separate document is kept for the two.

---

## 5. Library Design Constitution (the NuGet layer)

- **Package independence:** Every package works on its own, without the others. Inter-package dependencies may only point to `Goldpath.Abstractions` (a thin contracts package; its single micro-dependency is Microsoft.Extensions.Compliance.Abstractions, carried for native data-classification — DataProtection RFC D1).
- **DI-first, options pattern:** `services.AddGoldpathOutbox(opt => ...)`. Static state is forbidden. Every module has an `IOptions` class + validation (the config reference document is generated from it).
- **TFM policy:** Only .NET LTS is targeted; when a new LTS ships, migration happens within one release train, and the previous LTS is supported for 12 months.
- **Public API discipline:** Tracked with PublicApiAnalyzers; every public API change is a visible diff. SemVer is enforced strictly; breaking changes only in majors, deprecation announced with `[Obsolete]` at least one minor in advance.
- **Quality baseline (per package):** Unit + integration tests, mutation testing threshold, benchmarks (performance regression in CI), nullable enabled, zero analyzer warnings.
- **Security:** SBOM generation, dependency scanning, signed packages, license auditing (only dependencies with approved licenses) — the ready-made answer to the banking customer's supply-chain questions.
- **Performance-correct defaults ("out-of-the-box primitives"):** The easy path = the fast path.
  Performance-critical patterns are not left to the developer; they come as optimized, ready-made
  building blocks: **cursor/keyset pagination** (the default, against offset collapsing on large
  tables), compiled queries, projection-based queries (`AsNoTracking` + select), streaming (`IAsyncEnumerable`),
  bulk read/write, the cache-aside pattern. Wrong patterns are caught by analyzers (e.g. unbounded
  `ToList`, offset pagination warning). This primitive set grows over time — each one ships with
  its benchmark; the "optimized" claim is measured.
- **Code style standard — the single-handwriting rule:** Whether a human writes it or a skill
  generates it, the code must be indistinguishable. Single source: `.editorconfig` + analyzers +
  `dotnet format` (mandatory in CI). XML doc summaries are mandatory on public APIs (config/API
  reference documents are generated from them), the comment/region/naming policy is written down
  and machine-enforced; code produced by skills passes through the same rules — style is enforced
  by guardrails, not by prompts. The clean-code threshold is measured by analyzers + the SonarQube
  quality gate (complexity, duplication, method length).
- **Core set (Phase 1 commitment):** ServiceDefaults, Data (EF+Outbox/Inbox), Messaging (MassTransit), ApiDefaults (versioning, ProblemDetails, pagination), Analyzers. **The remaining ~45 modules in the catalog are vision/roadmap, not commitments.**

### 5.0 Cross-cutting strategy — three rings + four seams

**The rings:**
- **A — Always-on base** (foundation, not a feature; cannot be turned off, requires no configuration): structured
  logging, OTel, health, ProblemDetails (RFC 9457), correlation, resilience, rate limiting.
  Microsoft packages + ServiceDefaults configuration.
- **B — Opt-in Goldpath cross-cutting concerns** (value-add): idempotency, audit trail, soft delete,
  multi-tenancy, data protection/PII, distributed locking, caching L1+L2, outbox/inbox.
  **Entry criteria (all four required):** Microsoft doesn't provide it + needed in ≥2 industries + definable
  domain-independently + implementable without leaking into domain code.
- **C — Advanced modules (NOT cross-cutting):** saga, rules engine, bulk, notification hub —
  semi-products; a separate shelf, via RFC, vision category. Not presented alongside B in the catalog.

**The four seams (integration mechanics):** HTTP path = middleware; command/query path = **Mediant
pipeline behavior**; data path = EF interceptor; message path = MassTransit filter. A module is
the implantation of the same concern into the relevant seams (e.g. idempotency = HTTP middleware + consumer
inbox); the manifest toggle wires all of them in from the right place, and domain code sees none of it.

**Toggle semantics — composition is compile-time, behavior is runtime:** `enabled/disabled` in the
manifest is NOT a runtime if; it is a generation/wiring decision: a disabled module has no
csproj reference, no Program.cs line, no DLL (SBOM/CVE surface, startup, trimming,
audit clarity). Runtime feature flags (InfraOps) only change the behavior of an EXISTING
capability; they never add capability. Assembly-scanning/auto-registration magic is forbidden — Program.cs
is the application's honest inventory (the single reading point for human + AI). Enabling later =
`goldpath add <module>` (csproj + Program.cs + manifest together, as an MR); manifest ↔ code
consistency is verified by Spec Engine `validate/drift` in CI.

### 5.0.1 Template sustainability rule: "the template stays dumb"

Behavior lives in packages; the template is only folder layout + wiring (`Add...` calls,
driven by the manifest). Consequences: (1) updating generated solutions = a NuGet bump
(Renovate) — no re-scaffold; the template becomes irrelevant after day one; (2) the combination matrix
does not explode — a template difference is a wiring difference, not a behavior difference; golden manifest CI proves
the selected combinations; (3) customer customization is parameters/extensions, not a fork.
Carrying structural template changes into generated solutions is the job of the `upgrade` skill.

### 5.1 First-party components (Mockifyr, Mediant) and the personal OSS policy

- **Mockifyr (mock engine, github.com/omercelikdev/mockifyr, free):** Goldpath's mock module is
  designed **provider-pluggable**: the mock contract in the manifest + stub format = WireMock
  JSON canonical (portability). Providers: WireMock (today, mature) → Mockifyr (becomes the
  default once its parity roadmap reaches Goldpath's needs: HTTP facade + admin API + scenarios).
  Synergy: Mockifyr multi-tenancy = shared mock environments; the InfraOps "Mocks" page
  sits on the Mockifyr admin API; the differential testing infrastructure is shared with the transformation package.
- **Mediant (MediatR alternative, free) — status: v1.0.0 STABLE, GATE MET (released 2026-07-03;
  all 8 packages on nuget.org, GitHub release tagged):** MediatR's move to a commercial license
  is real pain in the enterprise — a free mediator is the right call for the golden path.
  Code analysis (2026-07-03): 256 tests (unit+integration+load), 11 pipeline behaviors
  (idempotency, audit, caching, transaction, retry, authorization, validation…), its own
  analyzers (QM1001-1004), a source generator (AOT), EF Core stores, benchmarked against
  MediatR. 1.0 is stable but **treated as a living product, not final** — needs arising from
  Goldpath flow back as GitHub issues (improvements/features/defects) and are resolved in parallel.
  **Composition strategy: Goldpath does not rewrite what Mediant provides** — feature modules
  compose command-path behavior from Mediant and layer the HTTP/message seams and manifest
  wiring on top. **Outbox boundary:** Mediant outbox = in-process domain events; integration
  events going to the broker = MassTransit transactional outbox — the Messaging RFC defines this
  boundary. Goldpath.Abstractions does not hard-couple to Mediant.
- **Policy — your own OSS goes through the RFC too:** First-party components are evaluated with
  the SAME criteria as third-party OSS (license, maintenance, version maturity, SBOM, bus factor).
  A dependency on a personal repo happens only with management approval and in writing (the conflict-of-interest/sustainability
  question is answered up front). Needs that emerge from Mockifyr/Mediant while building Goldpath assets
  feed back in both directions (dogfooding), but the Goldpath release train does not lock to their releases.
- **Standards engine — a versioned OSS product (the Spectral/Roslyn model):** Two separately
  versioned products: (1) **Engine** — personal GitHub, OSS; `dotnet tool` + NuGet library +
  container image; three modes: CLI (local/CI), library, **MCP server** (AI agents validate
  live). It loads a ruleset package, validates artifacts (manifest, OpenAPI, repo structure, naming),
  and reports: CLI (human), **SARIF** (CI code-quality surfaces), JSON (AI).
  (2) **Rulesets** — separately SemVer'd packages; the enterprise Goldpath ruleset is private in the
  the package registry. Rule IDs are stable (GP0001 style); the engine↔ruleset schema compatibility range is declared.
  The original value is not writing validators but **orchestration**: wrapping mature engines (JSON Schema,
  OpenAPI lint) under a single CLI/report/MCP. Timing: it is not launched as a separate project —
  the Phase 2 spec-lint is written from the start as the core of this engine, with Goldpath as its first customer;
  the OSS release is a by-product of the Phase 2 deliverable (the antidote to the third-front risk).

  **Spec Engine — mental model: a capability matrix** (*spec type × capability*; spec types are
  plugins: OpenAPI, AsyncAPI, JSON Schema, manifest — a new type = a new plugin):
  | Capability | Job |
  |---|---|
  | `validate` | schema + ruleset conformance (manifest business rules, example tables, naming, repo structure) |
  | `drift` | semantic diff of OpenAPI exported from code ↔ the approved spec — the CI proof of the "single source of truth" |
  | `diff` | spec v1↔v2, breaking-change classification → the machine makes the SemVer decision |
  | `generate` | the contract layer (DTO/client/server stubs) — Kiota/NSwag orchestration, 100% deterministic |
  | `test` | contract tests from the spec + executable scenarios from the approved example tables |
  | `mock` | spec examples → Mockifyr/WireMock JSON stubs — the mock environment is ready the moment the spec is approved (consumer teams don't wait) |
  | `docs` | spec → human-readable render (feeds the "generated" class of living documentation) |
  | `mcp` | all capabilities as agent tools — the CLI and the AI see the same rules/engine |
  Rulesets: declarative (YAML selector+assertion) + a C# plugin escape hatch; severity; justified
  suppression. Outputs: CLI (human), SARIF (CI code quality), JSON (AI); fixed exit codes.
  **Discipline:** (1) The engine is 100% deterministic — it never calls an LLM; AI (the skills) call the engine
  via MCP. (2) Capability phasing: v1 = validate+drift+mcp (the Goldpath Phase 2 need) →
  v1.5 = diff → v2 = generate/test/mock → docs. The matrix is designed up front; the cells fill in order.
  Technology: core is .NET (`dotnet tool`; Kiota/NSwag native, single runtime, npm-free CI —
  the differentiation against Spectral); the plugin architecture stays open to wrapping tools in other languages via adapters.
  Non-goals: it does not lint C# code (Roslyn's job), it does not generate business logic (the skill's job), and it is not a CI.

---

## 6. AI Layer Architecture

### 6.1 Three levels
1. **Passive (context):** CLAUDE.md, conventions, architecture, domain memory. Born inside the template from Phase 1 onward. Cost ~zero, works with every AI tool.
2. **Active (skills):** Executable recipes. Input is manifest + spec + domain memory; output is **always an MR**. The skill set: `authoring` (business language→spec), `spec-review` (validates the analyst's output — see 3.2), `new-service`, `add-feature`, `test-gen` (never sees the implementation — see 8.2), `breaker` (adversarial), `reverse-engineer`, `differential-test`, `docs-sync`, `upgrade` (version migration).
3. **Pipeline:** Skills chained through human gates: design→development→test→validation→report.

### 6.2 Skill anatomy (mandatory for every skill)
- Definition: input schema, output definition (Definition of Done: code + tests + documentation update + telemetry record)
- Eval set: golden manifests → expected-output tests; a skill version that fails evals is not released
- Versioning: same monorepo as the standards, same release train
- **Model independence:** Skill logic lives in a single canonical format (markdown instructions + MCP tools —
  MCP is the vendor-neutral standard); it binds to harnesses (Claude Code, Codex, Copilot, Cursor, local) via thin
  adapters. Whatever model sits behind the customer's enterprise LLM gateway is what gets used
  (including on-prem DeepSeek/Llama/Qwen — the bank reality).
- **Model proficiency matrix:** The eval harness runs per model → a *skill × model → pass rate*
  matrix. The promise is not "every model works" but "models are pluggable; proficiency is proven by evals."
  The quality floor is guaranteed by the guardrail chain, not the model — a weak model reduces speed, not quality.

### 6.3 Knowledge layers — generic core / industry pack / project knowledge

Everything is generic; domain knowledge belongs to the project, not the product:
1. **Generic core:** packages, templates, Spec Engine, skills — zero domain knowledge;
   a skill knows the *how*, not the *what*. Versioned, identical in every project.
2. **Industry pack (optional):** banking/telco/insurance rulesets + edge-case catalog
   seeds — portable across customers (industry knowledge, not domain knowledge); accumulates across projects.
3. **Project knowledge (in the customer's repo):** manifest, specs, domain memory, ubiquitous
   language, ADRs, parity contract, the filled-in edge-case catalog. Born as an empty skeleton
   from the template, grows with every skill run (DoD).

**"Learning" = artifact accumulation, not model training.** Knowledge lives in the repo: versioned,
auditable, customer-owned (the bank's IP stays at the bank), model-independent (if the LLM changes, the knowledge remains).
**Cold start** is treated as natural: in transformations `reverse-engineer`, in greenfield `authoring`
fills the memory as the project's first task. **The leakage ban (red line):** customer A's
domain knowledge never flows into the asset or to customer B; a pattern that turns out to be generic is promoted
to the industry pack/asset only by human decision.

### 6.4 Validation chain (for AI output)
`schema validation → build → analyzers → architecture tests → contract tests → unit/integration →
mutation score threshold → review agent (flags standard violations + suspicious logic) → human review → merge`

### 6.5 Delivery telemetry
Skills and CI record: spec→prod lead time, time per feature, human findings per MR,
guardrail catch count, escaped defects. Every project automatically produces a **Delivery Report** —
the evidence machine of the sales narrative.

---

## 7. End-to-End SDLC — Roles and AI

| Phase | Human | AI | Gate |
|---|---|---|---|
| Design | Analyst narrates, approves | `authoring`: natural language→manifest+OpenAPI draft | Spec approval (business) |
| Development | Dev steers, reviews | `new-service`/`add-feature`: spec→code, tests included | MR approval (dev) |
| Test | QA validates scenarios | `test-gen`: contract+scenarios from the spec; `differential-test` in transformations | CI green + QA approval |
| Release | Ops approves | Delivery report, changelog, runbook updates automatic | Deploy approval (ops) |
| Operations | Ops monitors, intervenes | InfraOps portal (YARP admin, mock toggle, job dashboard, feature flags) + runbooks | Cutover approval (in transformations) |

Ops integration is first-class: the InfraOps portal (the existing demo idea) is preserved; every module
brings its own dashboard, alert rules, and runbook template with it
("no runbook = no module" — the ops counterpart of the documentation rule).

---

### 7.1 Ops Surface Principles (decided 2026-07-04)

Three surfaces, one tool each — the antidote to screen sprawl:

| Surface | What | Tool | Rule |
|---|---|---|---|
| **Observe** | Metrics, traces, logs, health | Grafana/Aspire dashboards — NO custom UIs | Every module ships dashboard templates (ops package); building monitoring UIs violates configure-not-wrap |
| **Operate** | Runtime interventions: job trigger/pause, DLQ retry, gateway routes/canary, mock toggles, feature flags | **InfraOps Portal** — one shell, module-contributed pages | Screens are justified ONLY here (dashboards cannot act) |
| **Configure** | Composition: modules on/off, providers | Manifest + MR — never a runtime screen | Toggle semantics: composition is compile-time |

**Two iron rules:** (1) *No screen without an Admin API* — the API is the contract; the page
is its skin. Everything stays scriptable, GitOps-able, and AI-skill-drivable (agentic ops uses
the same API). (2) *No admin action without an audit record* (who/when/what — the portal
shell provides auth/RBAC/audit once, every module page inherits it).

**Phasing:** ops packages (dashboards/runbooks) ship with every module now → Admin APIs
arrive WITH the modules that own operable state (worker→jobs API, gateway→routes API,
Mockifyr keeps its own admin API — the portal embeds, never rebuilds) → the InfraOps Portal
shell gets its own RFC in Phase 3, born only when there are APIs to skin (no empty portals).

**Central logging architecture (decided: MEL + OTel, deliberately NOT Serilog):**
apps log via Microsoft.Extensions.Logging → OTLP → **OpenTelemetry Collector** → the
environment's store (Elastic/Loki/Splunk — the app never knows the sink; centralization is
collector config, not code). Correlation (traceId + correlationId) is stamped at the floor;
PII masking lands at the source with the Phase 2 DataProtection module. Serilog adds a second
pipeline concept with no remaining advantage on the OTLP path (its license is fine — the
reason is architectural); brownfield L1 consumers may keep it alongside MEL freely.
Reference collector config ships in the ServiceDefaults ops package.

## 8. Test Strategy — Pyramid + Oracle + Independence

### 8.1 The pyramid (produced entirely automatically)

| Level | Derived from | Production method | Tool/Note |
|---|---|---|---|
| Unit | Spec + domain rules | `test-gen` skill + property-based | xUnit + CsCheck/FsCheck |
| Integration | Manifest (provider selections) | Ships with the template + skill extends | Testcontainers (real DB/broker) |
| Contract | OpenAPI/AsyncAPI | **Deterministic codegen** (not AI) | Provider + consumer side |
| E2E | Analyst's acceptance scenarios | From the example tables in the spec | Runs on the AppHost |
| Smoke | Template | Part of the template | The "runs on first click" proof |
| Differential (oracle) | The legacy system's behavior | Traffic replay / recorded data | Transformation-specific — see section 9 |

### 8.2 "AI cannot grade its own homework" — test independence principles

A test that validates the code it itself wrote converges to zero value. The antidote is four mechanisms:

1. **Tests are derived from the spec, not the implementation.** The `test-gen` skill **never sees**
   the implementation code — its input is only the spec + domain memory + the edge-case catalog.
   Implementer and tester are separate agents, separate contexts.
2. **Adversarial breaker agent.** A third agent's sole job is to break the implementation:
   it looks at the spec and generates breaking scenarios asking "where could this code be wrong?"
   It succeeds on the day it finds something, not on the day it doesn't.
3. **Mutation testing (Stryker.NET) — an ungameable metric.** Even at 100% coverage the tests may
   be useless; the mutation score is the proof that the tests *actually catch defects*.
   The code is mutated; if the tests don't catch it, the tests are weak. A CI threshold: below it, no merge.
   This is the objective answer to the "AI validating itself" concern.
4. **Property-based testing.** Edge cases are not left to human/AI imagination; the machine generates them:
   empty, null, boundary values, negatives, unicode, overflow, rounding, time zones, concurrency.

### 8.3 The domain edge-case catalog

Part of domain memory: a **mandatory** boundary list per type. E.g. money → rounding,
negatives, cent precision, currency; dates → business days, year-end, statutory deadlines (protest day, etc.).
`test-gen` generates from this catalog as a checklist — the model is "if it's on the list", not "if it comes to mind".
The catalog is enriched with `reverse-engineer` output in every transformation.

### 8.4 The Test Cycle — an unskippable loop (trigger × test class matrix)

Systematic rigor is embedded in the infrastructure, not in people: the cycle is codified in the central CI template (include),
and every service born from the template is born with this loop. **Deleting a stage is forbidden**; skipping is possible only
with justified suppression and appears in the Delivery Report ("3 stages skipped, justifications: ...").

| Trigger | What runs | Time target |
|---|---|---|
| Every MR/commit | unit + contract + analyzers + arch tests + spec validate/drift | < 10 min (fast feedback) |
| Merge to main | + integration (Testcontainers, real DB/broker) + smoke (AppHost) + mutation (on changed areas) | < 30 min |
| **Nightly (daily)** | full regression: the whole pyramid + E2E + full mutation + dependency/security scan + **perf smoke** (short load, trend tracking) | night window |
| Release candidate | + **load/stress** (against the NFR targets in the manifest) + **soak** (long run: leaks/degradation) + **resilience/chaos** (broker kill, DB failover, external system timeout/fault — Mockifyr fault injection) + full differential in transformations | release gate |
| Production (continuous) | synthetic monitoring (live smoke: critical flows periodically) + SLO/error-budget monitoring | 24/7 |

Rules:
- **NFRs are part of the spec:** performance targets (p95 latency, TPS, error budget,
  resource ceiling) are defined in the manifest; load/stress runs against these targets and is a gate.
  A load test without targets is forbidden — "green" relative to what must be written down up front.
- **The load tool is .NET-native:** NBomber (or Microsoft crank) — consistent with the npm-free CI stance;
  scenarios can be derived from the spec (endpoints + example payloads are already there).
- **Perf at two levels:** package-level BenchmarkDotNet (section 5, regression in CI) +
  service-level load (nightly trend + release gate). The nightly perf smoke measures relative trend
  in an ephemeral environment; absolute target validation happens in a dedicated perf environment.
- **Test data:** seed + synthetic generation is the standard; anonymized prod data for
  differential (section 9). The nightly result is published as a morning summary — a red nightly
  is the first item on the agenda; there is no such category as a red you get used to (the broken-windows rule).

### 8.5 Analyst output → executable tests

Specification by example: the analyst provides acceptance criteria as structured example tables
(the authoring skill translates from natural language, the analyst approves). Every approved example is both documentation
and an automated E2E test. The output the business provides and the scenario the test runs are **the same artifact**
— there is no translation loss in between.

---

## 9. The Transformation Package (the peak of the value-add)

Strangler fig + differential testing, productized:
1. The `reverse-engineer` skill: legacy code/SPs/batches → manifest + spec + domain memory draft
2. Business approval (implicit rules are caught here — the most critical gate)
3. Production on the golden path (the Phase 1-2 deliverables come into play here)
4. `differential-test`: the old system as oracle — the same input into both systems, outputs compared.
   Input source: production traffic replay + recorded real data (anonymized).
   The WireMock module equalizes external dependencies (the mock infrastructure is half of this scenario).

   **Parity contract (naive equality is forbidden):** The comparison is rule-based; behaviors
   are classified per capability into three classes, and this classification is a versioned + business-approved
   artifact:
   - **Parity:** the business essence (amounts, balances, statuses, statutory deadlines) → strict equality
   - **Approved deviation:** a deliberate difference (a bug fix, tightened validation, a new
     design) → a recorded deviation (justification + business approval); validated by a **spec-based test**,
     not the oracle. Deliberately preserving a legacy bug (a downstream-system dependency) is likewise
     a recorded decision.
   - **Normalize:** technical differences (timestamps, ID formats, ordering, message text) → normalized
     before comparison.

   **The diff triage loop:** Every unexpected difference is either a bug in the new system (fixed) or
   an implicit behavior discovered in legacy (to business: keep/change decision → recorded in the parity
   contract). AI pre-classifies the differences; the decision belongs to the human. This is the real value of
   differential testing: it turns hidden business rules into explicit decisions instead of silent loss.
   The suppression list (suppressing the expected) is also subject to review — unjustified suppression is forbidden.
5. **Data migration reconciliation:** row counts + field-level comparison, PK/sequence-independent
   business-data comparison (the db-compare approach) — a mandatory input to the cutover gate.
6. Capability-based cutover: shadow/parallel run → metrics green → traffic switch → the old path closes.

Note: the differential testing (oracle) experience from the Mock Engine work carries over here directly.

---

## 10. Repo, Distribution, Governance

- **Monorepo:** packages/ + templates/ + skills/ + analyzers/ + docs/ +
  samples/. A single release train — skills and standards can never diverge.
- **Registry:** nuget.org (or an internal NuGet registry) + container image mirror (the air-gapped scenario is first-class:
  tested under the assumption that a bank/telco developer machine has no internet).
- **OSS ↔ Goldpath flow (producer topology):** The personal GitHub OSS projects (Mockifyr, Mediant,
  Spec Engine) publish to nuget.org with their own CI → the the package registry proxies nuget.org →
  Goldpath consumes with pinned versions. **Bind to the package, not the source code** — submodules/source
  copies are forbidden; from Goldpath's perspective there is no difference between Mediant and MassTransit. The needs flow:
  GitHub issue → OSS release → version bump from the mirror.
- **The consumer is repo-agnostic:** The unit Goldpath binds to is the MANIFEST, not the repo.
  `.goldpath/manifest.yaml` sits at the solution root; the solution may live in a single repo (polyrepo) or in a
  monorepo subfolder. The Spec Engine/skills/CI operate manifest-scoped; `goldpath discover`
  finds all manifests in a monorepo, and the CI matrix runs per manifest. Mono/poly support
  is not special work; it is the consequence of blindness to repo shape.
- **Execution-model blindness:** Packages are blind to hosting (plain DI extensions — IIS/systemd/
  compose/OpenShift makes no difference); the Aspire AppHost is the local golden path COMFORT, not a runtime
  dependency. Three tiers: local = AppHost (F5+dashboard) · alternative = `goldpath export
  compose` (GENERATED from the AppHost definition, never hand-written — the two definitions cannot diverge) · environments =
  K8s/OpenShift manifests from CI. In L1/L2 adoption the existing project's own execution
  setup is preserved as-is.
- **CI templates:** `include`d from the central repo; consumer projects are auto-updated via Renovate.
- **The pipeline is part of the product:** A solution produced from the template is born with a working pipeline —
  build → test cycle (8.4) → packaging → environment promotion (dev→test→staging→prod, gated)
  comes ready; there is no separate "pipeline setup" work item.
- **Local/CI parity:** The checks the pipeline runs run locally with the same commands
  (`goldpath check` = the exact counterpart of the CI validate/drift/test stages; AppHost = the local environment).
  The "worked on my machine" category is structurally absent; fast feedback does not wait for the pipeline.
- **Reference application (dogfood):** In the order-management domain, a continuously running sample system
  that uses the entire golden path in every release. Test bed, demo, and training material at once.
- **Ownership:** A platform team (dedicated). Intake: the RFC process — module requests come in as RFCs
  and pass a conformance check against the constitution (section 2).
- **Inner source:** Teams on customer projects can contribute via MRs; merge authority stays with the platform team.
- **Customer customization model (fork-free):** options + extension points + partial template
  overrides + the ability to add their own skills. A customer that forks cannot receive updates — which is why
  customization needs flow back into the product as extension points.
- **Commercial model:** "Included in project fee" (the decision from the demo) — positioned not as a licensed
  product but as the accelerator of the transformation project.

---

## 11. Risks and Red Lines

| Risk | Antidote |
|---|---|
| Scope creep (the lure of 54 modules) | Core/vision separation; the RFC gate; no phase starts before the previous one is validated in real use |
| The custom DSL trap | ADR-0002; business logic/flow definitions **never** enter the manifest |
| Microsoft steamrolling the path | ADR-0003; for every module, the "what if Microsoft ships this" exit plan is written in the ADR |
| Skill drift (standard changed, skill stale) | Monorepo + single release train + mandatory evals |
| Docs drift | Generated-docs-first + CI freshness checks + skill DoD |
| Template combination explosion | Module independence (constitution) + the golden manifest matrix is chosen deliberately, not every combination |
| AI vendor lock | Skills are model-independent definitions; the enterprise LLM gateway assumption |
| First demo collapsing on an air-gapped network | The offline/proxy scenario is part of Template CI |
| Orphaning | Dedicated platform team + the delivery report's visibility to management |

---

## 12. Phasing (dateless — gate-based)

Each phase's exit is the next phase's entry gate; gates govern, not the calendar.

- **Phase 0 — Constitution:** ADR-0001..0010 are written, the manifest v1 schema, the reference architecture diagram.
  *Gate: constitution approved, the manifest schema validates.*
- **Phase 1 — Golden Path Core:** 5 core packages + template (AppHost, seed, smoke) +
  Template CI (the golden manifest matrix) + the passive AI layer (the CLAUDE.md family) + CI templates.
  *Gate: our own team writes a service with it; the F5 experience is proven.*
- **Phase 2 — Spec-Driven + First Skills:** the manifest→OpenAPI pipeline, contract test generation,
  the `authoring` + `new-service` + `add-feature` + `docs-sync` skills + the eval harness + telemetry v1.
  *Gate: a feature flows from a business sentence to an MR in a single session (live demo ready).*
- **Phase 3 — Transformation Package:** `reverse-engineer` + `differential-test` + the strangler guide +
  WireMock integration. *Gate: an end-to-end pilot on a small legacy module + the first Delivery Report.*
- **Phase 4 — Productization:** InfraOps portal expansion, advanced modules (via RFC), delivery
  report automation, training/onboarding material. *Gate: the first external customer project.*

---

## 13. Open Questions and Next Artifacts (to be settled together)

1. ✔ Manifest v1 — `Goldpath_Manifest_v1.md` + `goldpath-manifest.schema.json` (ajv-validated, 2026-07-03)
2. ✔ Domain memory — `Goldpath_Domain_Memory_v1.md` (location/ID/language decisions locked, 2026-07-03)
3. ✔ Review agent — `Goldpath_Review_Agent_v1.md` (R1-R6 finding classes, hard stops,
   what it will not flag, the calibration loop, 2026-07-03)
4. ✔ Golden manifest matrix — `Goldpath_Golden_Manifests_v1.md` (6 persona GMs + coverage table
   + execution model + evolution rules, 2026-07-03)
5. ✔ Telemetry — `Goldpath_Telemetry_v1.md` (event model, KPI set, Delivery Report template,
   baseline strategy, 2026-07-03)
6. ✔ Reference application — `Goldpath_Module_Plan_v1.md` §4: OrderPlatform dogfood (a narrow order
   vertical; the live incarnation of GM-1; the single source for demo/training, 2026-07-03)
7. ✔ **Module Implementation Plan** — `Goldpath_Module_Plan_v1.md` §1-3 (revised with the Mediant v1.0.0
   analysis: Idempotency/Audit/Caching efforts shrank — the compose strategy; capacity rule:
   at most 2 active tracks at a time, 2026-07-03)
8. ✔ **Module RFC template** — reference example: `Goldpath_Module_RFC_Idempotency.md` (eight fixed
   sections: scope/non-goals, seam map, manifest+API surface, analyzer, ops package, test
   plan, DoD). Spec Engine/Mockifyr integrations/skills follow the same format. (2026-07-03)
