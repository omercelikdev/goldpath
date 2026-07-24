# AI-assisted SDLC — coverage & status

Status: LIVING DOCUMENT (updated in the same PR whenever a row's status changes — the
same discipline as `coverage-matrix.md`). First cut 2026-07-23.

`foundation.md` §6–§8 defines the target: a command-driven lifecycle where design,
analysis, development, test, DB work, release and operations are all AI-assisted and
**machine-validated** — zero-trust toward AI output, every claim gated by a proof.
This document tracks where reality is against that target, honestly. Statuses:

- **SHIPPED-PROVEN** — exists and has been exercised end-to-end with recorded evidence.
- **SHIPPED-UNPROVEN** — ships today, but no recorded end-to-end run exists yet.
- **PARTIAL** — some of the target exists; the rest has a listed gap.
- **NOT BUILT** — nothing exists; phase/trigger noted where the deferral is written.

A row may only move to SHIPPED-PROVEN with a pointer to its evidence (transcript,
nightly job, or test). Claiming above the real status is the one failure mode this
document exists to prevent.

## 1. Lifecycle stages vs. reality

| Stage | Target (foundation) | Today | Deterministic gate today | Gap → next artifact |
|---|---|---|---|---|
| Design / analysis | `authoring` + `spec-review` skills: business language → manifest + spec (§6.1) | **NOT BUILT** — manifests are hand-authored | `specdrift validate` / `spec_drift` (schema + rules) | `authoring` skill — Phase 2 (§12) |
| Composition | manifest as the single source of truth | `goldpath-manifest` skill **SHIPPED-UNPROVEN**; CLI + schema + GM matrix **SHIPPED-PROVEN** (nightly, 8 shapes) | GM matrix (nightly) + specdrift | drive the skill in a recorded run |
| Development | `new-service` / `add-feature`: spec → code with tests (§7) | `goldpath new`/`add worker` deterministic scaffolding **SHIPPED-PROVEN** (GM nightly); `goldpath-feature` skill **SHIPPED-PROVEN** — it drove the five CorPay S2 slices (`coverage-matrix.md` tooling table); the `goldpath add feature` CLI verb itself is **SHIPPED-UNPROVEN** (never driven in a sample) | build + analyzers (39 rules) + PublicAPI ledger + `dotnet format` | field the CLI verb (coverage-matrix plans it for the Insurance sample) |
| DB | migrations discipline driven by CLI verbs | `goldpath db` verbs + D7 proofs (`validate-migrations.sh` on real pg) **SHIPPED-PROVEN** | migration bundle CI step + GP1801 | — |
| Test | `test-gen` (never sees the implementation, §8.2) + breaker + property-based + mutation | `goldpath-test-gen` skill **SHIPPED-PROVEN** — fielded on CorPay 2026-07-23: 8 spec-derived paging/tenancy/state tests + a 7-item gap report (`samples/corpay/tests/SPEC-GAPS.md`), diet held (no `Features/` reads; public seams via compile probes); `breaker` agent **SHIPPED-PROVEN** — same run, own context: 15 `Breaker_` tests, 14 attacks repelled, **1 genuine finding** (sub-cent amount accepted — fixed in the same PR; `samples/corpay/tests/BREAKER-VERDICT.md`), breaker eval 3/3 PASS; mutation gates **SHIPPED-PROVEN** (10 packages nightly, 6 heavy on dispatch); property-based **PARTIAL** (CsCheck present, not yet the §8.3 catalog-driven norm) | Stryker break=70 + test projects in CI | edge-case catalog is Phase 2 (domain memory) |
| Review / validation chain | §6.4: schema → build → analyzers → arch tests → contracts → tests → mutation → review agent → human | Chain **SHIPPED-PROVEN** up to mutation; review agent **SHIPPED-PROVEN as a manual script** (`scripts/review-agent.sh`, findings recorded on merged PRs); **in-loop mechanical gating (hooks) NOT BUILT** — nothing forces the chain while the AI is still in its turn | CI gates on PR + nightly | **hook set in the template** (post-edit format, stop-gate build + spec validate) — see §4 |
| Skill quality (evals) | eval set per skill; a skill that fails evals is not released (§6.2) | **PARTIAL** — as of 2026-07-24 the 4 acceptance runners are portable (published specdrift tool, no local-checkout assumption; failures print their output) and the deterministic half runs nightly (`evals-acceptance`: runner syntax + breaker vs CorPay). The LLM half — running the skills themselves per fixture — stays deferred until an agent-in-CI story exists (P2) | nightly `evals-acceptance` job | agent-in-CI story (P2) unlocks full skill-run evals |
| Model proficiency matrix | skill × model → pass-rate matrix (§6.2) | **NOT BUILT** — Phase 2 (needs the eval lane first) | — | after the eval lane |
| Release / DevOps | release train + delivery telemetry (§6.5) | Train **SHIPPED-PROVEN** (OIDC trusted publishing, license gate, roll script); delivery report **NOT BUILT** — Phase 2 | release workflow + license gate | — |
| Operations | admin APIs + ops packs + console (§7.1) | Admin APIs + dashboards + runbooks **SHIPPED-PROVEN** per module; console **PARTIAL** (U1 in progress, RFC accepted) | admin contract frozen + integration proofs | console phases U1–U4 |
| Pipeline (chained skills + human gates) | §6.1 level 3 | **NOT BUILT** — Phase 2 by written deferral (§12) | — | — |
| Domain memory | project knowledge that grows with every run (§6.3) | **NOT BUILT** — `domain-memory-v1.md` is a v0.1 draft | — | Phase 2/3 |

## 2. The §6.1 skill set, name by name

| Skill (foundation §6.1) | Status | Where |
|---|---|---|
| `authoring` | NOT BUILT (Phase 2) | — |
| `spec-review` | NOT BUILT (Phase 2) | — |
| `new-service` | covered deterministically | `goldpath new` (CLI — no LLM needed, by design) |
| `add-feature` | **SHIPPED-PROVEN** (drove CorPay S2) | `.claude/skills/goldpath-feature` |
| `test-gen` | **SHIPPED-PROVEN** (fielded on CorPay 2026-07-23 — `samples/corpay/tests/SPEC-GAPS.md`) | `.claude/skills/goldpath-test-gen` |
| `breaker` | **SHIPPED-PROVEN** (same run, 1 genuine finding fixed — `samples/corpay/tests/BREAKER-VERDICT.md`) | `.claude/agents/breaker.md` |
| `reverse-engineer` | NOT BUILT (transformation package, §9) | — |
| `differential-test` | NOT BUILT (transformation package, §9) | — |
| `docs-sync` | NOT BUILT | — |
| `upgrade` | NOT BUILT (first real consumer: a preview→preview migration) | — |
| *(extra, not in §6.1)* `goldpath-manifest` | SHIPPED-UNPROVEN | `.claude/skills/goldpath-manifest` |

3 skills + 1 agent ship in the template pack today. Three are now fielded in anger:
`goldpath-feature` drove the five CorPay S2 slices; `goldpath-test-gen` and `breaker`
ran on CorPay 2026-07-23 (spec-derived tests + gap report; adversarial run with one
genuine finding, fixed). `goldpath-manifest` remains unfielded (`coverage-matrix.md`
plans it for the Insurance sample).

## 3. Mechanism inventory — what carries the AI layer

| Mechanism | Role | Status |
|---|---|---|
| CLAUDE.md family + `conventions.md` | passive context | SHIPPED (template) |
| Skills | active recipes | PARTIAL — one fielded, three not (see §2) |
| MCP (`specdrift mcp`) | deterministic tools in the AI's hand | SHIPPED (`spec_validate`, `spec_drift`) |
| Hooks | unskippable in-loop gates | **SHIPPED** (template + CorPay, 2026-07-23): post-edit whitespace format on touched `.cs`; Stop gate blocks a red `dotnet build` and error-level `specdrift drift` findings. Honest limit: SPEC0203 is warn-level today, so drift blocks only on errors — a `--fail-on warn` flag is a specdrift 0.4.2 candidate |
| Evals | skill regression tests | PARTIAL — runners portable + nightly deterministic lane; full skill-run evals await agent-in-CI (P2) |
| Plugin packaging | install the layer into an *existing* app | NOT BUILT (template-only distribution today) |

## 4. The near-term path (ordered)

1. **Proof runs** — DONE for `goldpath-test-gen` + `breaker` (CorPay, 2026-07-23; see
   the Test row). Remaining: `goldpath-manifest` and the `add feature` CLI verb
   distribute to the next samples per `coverage-matrix.md`. Follow-ups the run itself
   surfaced: the exported OpenAPI misses request bodies/params (7 gaps in
   `SPEC-GAPS.md`), and `spec_drift` flags CorPay's undeclared jobs capability
   (SPEC0203) — both need owners.
2. **Hook set in the template** — DONE 2026-07-23 (`.claude/settings.json` +
   `.claude/hooks/` in the template and CorPay): post-edit whitespace format; stop-gate
   `dotnet build` + `specdrift drift`. The §6.4 chain now runs *inside* the AI's turn —
   an agent cannot end its turn on a red build. Follow-up: specdrift `--fail-on warn`
   so adopters can choose gate strictness (SPEC0203 is warn-level and passes today).
3. **Portable evals + nightly lane** — DONE 2026-07-24: `accept.sh` runners use the
   published specdrift tool (no `$HOME` checkout assumption) and print failing output;
   nightly `evals-acceptance` runs the deterministic half (syntax + breaker vs CorPay).
   Same PR closes the other half of the audit's Y10: `validate-gm.sh` now honors its
   pin (v0.4.1) — a local checkout is used only via an explicit
   `GOLDPATH_SPECDRIFT_SRC`, and announces itself when it is.
4. **CLI verbs as MCP tools** — typed `goldpath_*` tools next to specdrift's, so skills
   stop shelling out. Shaped by what the proof run shows is actually needed.
5. **Plugin packaging** — one installable unit (skills + MCP + hooks) for existing apps.
   Only after 1–3 mature; packaging unproven content distributes the wrong thing.

Phase 2 items (pipeline, authoring, delivery report, model matrix, domain memory) keep
their written gates in `foundation.md` §12 — this document does not re-plan them, it
only tracks when their triggers fire.
