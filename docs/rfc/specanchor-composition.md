# RFC: specanchor Composition — the Transformation Package's Deterministic Core Lives Outside

**Status:** proposed — owner decision pending
**Date:** 2026-08-16
**Constitution grounding:** ADR-0003 (compose, don't rewrite), ADR-0004 (deterministic/agentic split),
ADR-0005 (executable standards), ADR-0006 (AI lives in the dev layer), foundation §5.1 (the personal-OSS
policy and the third-front antidote), foundation §9 (the Transformation Package).

---

## 1. Scope / Non-Goals

**Scope.** Record how Goldpath relates to [specanchor](https://github.com/qorpe/specanchor) — a
framework-agnostic, spec-anchored legacy-modernization toolchain (Apache-2.0, the qorpe OSS line):
deterministic index (Roslyn + ScriptDom), source-referenced rule extraction with self-validating
skills, characterization testing, parity harness with a signed known-differences register, catalog
gates, CLI + MCP. It sits beside a build and never modifies application code.

The mapping this RFC fixes: **foundation §9's Transformation Package = specanchor (the deterministic
engines) + a Goldpath profile (the skills, formats and target wiring)**. §9 keeps the method and the
gates; the machinery §9 steps 1, 4 and 5 need is composed from specanchor, Mockifyr and db-compare —
not built inside this repo.

**Non-goals.** No Goldpath code in this RFC's scope — paperwork only (this file, the §9 mapping
paragraph, master-plan and open-threads records). No manifest surface, no package, no template change.
The Goldpath profile for specanchor (artefact → domain-memory translation, spec → Spec Engine feed)
is deliberately NOT designed here: it is an output of the rehearsal (D4), the same way the adapter
seam in specanchor's own toolset spec is an output of the first slice.

## 2. Seam Map (composition seams, not runtime seams)

specanchor is a dev-layer toolchain (ADR-0006 side), so its seams are compositional:

| Seam | Direction | What crosses it |
|---|---|---|
| **Discovery → domain memory** | specanchor → Goldpath | `reverse-engineer`-style output (rule cards, glossary, char tests) lands in the customer repo's `docs/domain/` per domain-memory-v1; evidence frontmatter = the rule card's `source_ref` |
| **Specs → Spec Engine** | specanchor → Goldpath | Approved rules become OpenAPI/AsyncAPI/JSON Schema/DMN artefacts the Phase-2 pipeline consumes; Goldpath is the default TARGET when the modern side is ours to choose — never a prerequisite |
| **Parity ← Mockifyr** | Goldpath line → specanchor | Mockifyr equalizes external dependencies for the parallel run (record & replay, email/SMS capture) — foundation §5.1 already declares the differential-testing infrastructure shared |
| **Reconciliation ← db-compare** | Goldpath line → specanchor | The §9 step-5 data reconciliation runner is db-compare, composed |
| **Lint ← specdrift** | shared | specanchor's artefact schemas are validated by its own gates today; specdrift remains the cross-artifact linter for manifest-driven repos, its D5 textual boundary intact — specanchor is a future second PROFILE, never a fork |

## 3. Manifest Surface

None. specanchor's binding unit is a `discovery/` folder plus `.specanchor/config`, both in the
customer's repo; `.goldpath/manifest.yaml` is untouched. A future `transformation` manifest hint is
explicitly deferred until the rehearsal shows one is needed.

## 4. API Surface

Consumed, not exposed: `specanchor index | gate | scaffold | mcp` (CLI, exit contract 0/1/2) and six
MCP tools (`index_summary`, `who_calls`, `table_access`, `dead_code`, `sql_object`, `gate`). Goldpath
skills MAY call these over MCP in the profile phase; nothing in Goldpath's published packages ever
references specanchor (the reverse is also true — specanchor's engine carries no Goldpath naming, the
specdrift D2 rule applied again).

## 5. Analyzer Rules

None shipped by Goldpath. specanchor ships its own executable standards (ADR-0005 discipline exported:
SA0xxx findings with teaching messages, mutation gate at break 75, license allowlist gate, CI parity
with the local command). Suppression/bypass in an engagement follows the same recorded-owned-expiring
rule as this repo.

## 6. Ops Package

Not applicable in the module sense (dev-layer toolchain, no runtime). The runbook equivalent is the
Discovery Zero playbook, which is written from the rehearsal's diary (D4) — not before.

## 7. Test Plan

- specanchor's own proof: the fake-legacy rig with a planted-trap answer key; 74 acceptance tests,
  mutation 94.8% (break 75), first LLM-half eval run 7/7 (2026-08-16, `evals/rule-extractor/`).
- **The composition's proof is the rehearsal:** the rig's legacy system is actually migrated to a
  Goldpath target — Discovery Zero → slices (dual-track) → approved specs → `goldpath new` → parity
  with Mockifyr equalization → db-compare reconciliation → cutover evidence bundle. Golden-manifest
  matrix impact: none until the profile phase; the rehearsal consumes the published train like an
  adopter (CorPay pattern).

## 8. DoD

- [ ] This RFC accepted by the owner (merge = the decision record).
- [ ] foundation §9 carries the mapping paragraph; master-plan records the early firing of the
      Phase 3 park as an owner decision; open-threads gains T20 with its trigger and proof.
- [ ] The rehearsal has run end to end and its diary produced the Discovery Zero playbook — only
      then does the Goldpath profile work (domain-memory translation, Spec Engine feed) get designed,
      and only then do ai-sdlc-status's `reverse-engineer`/`differential-test` rows move off NOT BUILT.

### Decisions

- **D1 — Separate repo, OSS line, already real:** `qorpe/specanchor`, Apache-2.0. Foundation §5.1
  governs it like any personal-OSS dependency: same evaluation criteria, written approval, and the
  Goldpath train never locks to its releases.
- **D2 — Bind via profile data and published artefacts, never source:** no submodules, no source
  copies, no `Goldpath.*` dependency inside specanchor's engine and no specanchor dependency inside
  Goldpath packages. The two products meet only through artefacts (files) and MCP.
- **D3 — §9 keeps the method, specanchor keeps the machinery:** the three-class parity contract, the
  human diff-triage decision, the three fixed gates (ADR-0010) stay Goldpath method; index, extraction
  validation, characterization, comparison and catalog gates live in specanchor.
- **D4 — Rehearsal before profile:** the fake-legacy → Goldpath migration is the third-front antidote
  made concrete (§5.1 timing rule: born as a by-product of a real deliverable — the factoring
  engagement — with Goldpath as first target). No profile code before the rehearsal's diary exists.
- **D5 — The third-front budget is capped:** until the rehearsal completes, Goldpath owes specanchor
  exactly this paperwork. The pilot product module and the samples keep their master-plan order.
- **D6 — Early firing of the Phase 3 park is an owner decision, recorded:** the parked entry said
  "the phase after the samples"; the factoring engagement moved it. Master-plan records the decision
  with this RFC as its artefact, so the ordered list stays the single truth.
