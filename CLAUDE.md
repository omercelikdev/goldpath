# CLAUDE.md — Goldpath Monorepo Operating Manual

## Project
AI-native, spec-driven enterprise .NET accelerator (a golden path; NOT a framework).
Conceptual grounding: `docs/strategy/foundation.md`. Constitution: `docs/adr/` (10 ADRs — do
not propose anything that contradicts them; changes only via a superseding ADR).

## Language rule
- Docs, code, identifiers, XML docs, commits, PRs: ENGLISH

## Decision process
1. New module/feature → RFC first (`docs/rfc/` — template: the 8 sections of goldpath-idempotency.md).
2. Check for conflicts with the constitution (ADRs) and strategy documents; if there is a conflict, do not write code — discuss.
3. Get approval before writing code; stop/show at every checkpoint.

## Invariant rules (summary — details in the ADRs)
- The manifest is the single source of truth; a disabled module does not exist in the application AT ALL (compile-time composition).
- What Microsoft/Mediant provides is not rewritten — it is composed (ADR-0003).
- Deterministic generation (Spec Engine) never calls an LLM; AI skills call it via MCP (ADR-0004).
- Every standard ships with its verifier; suppression without justification is forbidden (ADR-0005).
- No merge while the golden manifest matrix (GM-1..6) is red (ADR-0008).
- "latest" dependencies are forbidden; everything is pinned. Air-gapped networks are a first-class scenario.
- Code style: `.editorconfig` + analyzers + `dotnet format`; XML summaries mandatory on public APIs.

## Status (2026-08-09)
Phases 0–1 and the hardening set (H1–H8) are complete; the `0.1.0-preview.7` train (the
platform train: Goldpath.Sdk + ADR-0012, the adopter CLI verbs, Approvals + FileExchange
on nuget for the first time, the console's sixth module, SBOM + provenance on every
train) is on nuget.org (plus `specdrift` 0.4.2 as tool/MCP/Docker/Action). Mockifyr is THE mock system
(foundation §5.1 — no second provider). Phase D shipped the CorPay reference app
(`samples/corpay`, proven nightly against the published packages). The console phase is
COMPLETE: U1–U9 met (`docs/rfc/goldpath-console.md`; family standards ui-standard v1.3
§7–§9 — one visual family with qorpe/mockifyr), the admin contract is at Revision R3
(repeatable OR filters), and the console smoke (28 journeys + axe) drives three real
apps and the app-SERVED console. Steps 1–5 of `docs/strategy/master-plan-2026-08.md` are DONE, and so is 6.0: the family UI
kit is extracted to [qorpe/ui](https://github.com/qorpe/ui), published to npm through OIDC
trusted publishing, with BOTH consoles running on the published package. The **finalize set
F1–F3** (2026-08-09) closed the paperwork the engineering had outrun: five ledgers
reconciled with GitHub behind two new gates (`ledger-check.sh`, `schema-honesty.sh`), an
SBOM + signed provenance on every train with `SECURITY.md`, and the measured messaging-exit
RFC. Next: the pilot product module (step 6.1), then the scenario campaign and the
Insurance/Telco samples as the full-set exam. Live status ledgers —
keep them updated in the same PR that changes reality: `docs/strategy/ai-sdlc-status.md`
(AI-assisted SDLC vs reality), `docs/strategy/coverage-matrix.md` (capability × sample),
and `docs/strategy/open-threads.md` (deferred work with its TRIGGER and the proof that
must run before the thread closes — nothing is postponed without landing there).
Roadmap gates: `docs/strategy/foundation.md` §12.
