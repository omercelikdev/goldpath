# RFC: Spec Engine v1 — validate + drift + mcp

> Status: v1.0 accepted — D1–D6 approved by Ömer (2026-07-06): home = github.com/omercelikdev/specdrift
> (MIT) · the ENGINE stays generic — no product naming or knowledge inside it; schema +
> invariant rules + drift tables are PROFILE DATA that each golden path ships in its own repo
> (goldpath's profile lives here, in this repo, at M4). Parallel OSS track (module plan §2 "+"),
> the second half of the AI-native claim. Effort L (v1 cut below is M; the L covers the track).
> Constitution grounding: ADR-0001 (manifest is truth), ADR-0004 (deterministic/agentic
> split — the engine NEVER calls an LLM; AI skills call IT via MCP), ADR-0005 (executable
> standards).

## 1. What it is (and is not)

**Spec Engine is the deterministic half of AI-native.** Today the manifest is validated by
JSON Schema and enforced by analyzers INSIDE the code; nothing checks the space BETWEEN
artifacts — "the manifest says X, the repo says Y". That gap is exactly where AI-generated
changes rot silently. v1 is the spec-LINT layer:

- **`validate`** — the manifest beyond schema: cross-field invariants
  (l2 caching → a redis connection name; sqlserver locking → the optional package;
  subdomain tenancy + apikey auth = no tenant claim to bind → warn; every enabled feature's
  package referenced). Machine-readable output (JSON + human text), exit-code contract.
- **`drift`** — manifest ↔ repo reality:
  (a) feature enabled but the package/wiring call absent (and the reverse: package present,
  feature off — dead weight);
  (b) committed OpenAPI vs the build-exported document (the artifact GM already produces);
  (c) manifest schema version vs engine version skew.
  Output: a drift REPORT (json+md), never auto-fix in v1 — the fix is a human/skill decision.
- **`mcp`** — the same two verbs exposed as MCP tools over stdio, so AI skills ask the
  ENGINE instead of guessing: `spec_validate(manifestPath)`, `spec_drift(repoRoot)`.
  This is the ADR-0004 boundary made physical: the LLM never re-derives what the engine
  can answer deterministically.

**Non-goals (v1, written not silent):** deterministic CODEGEN (DTOs/clients/skeletons —
that is Spec Engine v2, after the lint layer proves the plumbing; the template remains the
generator until then), AsyncAPI validation (arrives with the event-contract authoring work),
auto-fix/`--write` modes, a hosted service (CLI+MCP only), Goldpath-internal analyzers'
territory (in-code rules stay Roslyn; the engine owns CROSS-ARTIFACT truth).

## 2. Shape

Single .NET tool (`dotnet tool install -g <name>`), three verbs:

```bash
<name> validate .goldpath/manifest.yaml            # exit 0/1 + report
<name> drift    --repo .                      # exit 0/1 + report (json + md)
<name> mcp                                    # stdio MCP server exposing both
```

- Manifest schema ships EMBEDDED (pinned copy of goldpath-manifest.schema.json, version-stamped);
  `--schema` overrides for air-gapped/forked setups.
- Rules are data where possible (invariant table), code where necessary; every rule has an
  id (`SPEC0xxx`), a message that teaches the fix, and a doc line — the analyzer discipline,
  one layer up.
- Goldpath repo integration: a pinned tool reference + a `spec-lint` CI job in the template
  (joins the gate set); the authoring skills call `mcp` (skills land with this track).

## 3. Decision Points (Ömer)

- **D1 — Repo home & license:** separate PUBLIC repo (the plan says OSS). Options:
  (a) `github.com/omercelikdev/<name>` — your OSS line, the Mediant/Mockifyr pattern:
  Goldpath composes your ecosystem, issues flow the same way;
  (b) a public org repo. **Recommendation: (a)** — the composition loop is proven
  and it keeps the OSS identity consistent. License MIT (gate-compatible).
- **D2 — Name.** Must not carry "Goldpath" (the asset has its own name; the tool is generic:
  "manifest+specs vs repo" linting works for any manifest-driven golden path). Candidates:
  **`specdrift`** (says exactly what it does), `specgate`, `manifold`.
  **Recommendation: `specdrift`** — memorable, honest, npm/nuget-free namespace (verified
  on nuget: no package by that id).
- **D3 — v1 cut = validate + drift(a,b,c) + mcp**, codegen explicitly v2. The Phase 2 gate
  ("business sentence → MR in one session") needs lint + skills, not codegen.
  **Recommendation: this cut.**
- **D4 — Tech:** .NET 10 tool; JsonSchema.Net (MIT) for schema validation; YamlDotNet (MIT)
  for the manifest; the official `ModelContextProtocol` C# SDK (MIT) for the server; zero
  proprietary anything. Own repo gets the SAME discipline: license gate, mutation gate,
  PublicAPI lock — exported from the goldpath repo's scripts. **Recommendation: as listed.**
- **D5 — Drift(a) mechanics:** feature↔wiring detection reads csproj (PackageReference) +
  a bounded grep for the registration call (`AddGoldpath*`/`ApplyGoldpath*` names from a table) —
  TEXTUAL, documented as such; Roslyn-grade analysis stays in the analyzers. Cheap, fast,
  95% of the value. **Recommendation: textual with a documented boundary.**
- **D6 — Versioning contract:** the engine declares which manifest schema versions it
  understands; unknown version = hard fail (never guess forward). Schema stays sourced in
  the goldpath repo; the engine vendors a pinned copy per release. **Recommendation: yes.**

## 4. Milestones (the track, not one MR)

1. **M1 — repo bootstrap**: skeleton + the exported gate set (license/mutation/PublicAPI/CI)
   + `validate` with schema + first 5 cross-field invariants. Proof: goldpath's own corpus
   manifests validate; a broken one fails with a teaching message.
2. **M2 — `drift`**: (a)+(b)+(c) against a generated template app; proof: flip a manifest
   feature without touching code → drift report names the exact gap.
3. **M3 — `mcp`**: both verbs over stdio; proof: a Claude session calls spec_drift and
   patches the gap it names.
4. **M4 — goldpath integration**: pinned tool in the template + `spec-lint` job joins
   the CI pipeline; the deferred manifest-aware analyzer rules (GP0403/1001/1002/1004)
   get REVISITED against the engine (some may move up a layer instead of Roslyn).

## 5. DoD (v1 track)
- [x] D1–D6 locked · M1–M4 shipped with their proofs:
      **M1** validate + own-corpus proof (and two REAL rots caught in this repo on the first
      run — fixed in MR !30) · **M2** drift with the flip-a-feature proof on a generated app
      · **M3** mcp proven at the protocol level (raw JSON-RPC over a child process's stdio)
      · **M4** this MR: the goldpath profile ships in the template (5 invariant rules + the full
      Ring B wiring table + the OpenAPI contract pair), validate-gm.sh runs spec-lint on
      every generated shape (proof: GmSpecLint GREEN — validate clean, drift clean, smoke
      pass), and the CI pipeline gains the spec-lint job (valid corpus must pass WITH rules;
      invalid corpus must fail). Engine repo carries the same gates (54 tests, mutation
      74.5% break 70, license gate, CI).
- [x] goldpath docs updated: module plan "+ row" marked shipped; deferred analyzer ledger
      revisited with per-rule outcomes (goldpath-analyzers.md — notably GP0403 moves up a layer
      to the engine). Engine expressiveness gaps found by the FIRST real profile were fixed
      at the engine (0.4.0: `deny` rules, `in`-gated wiring), not worked around in data.
