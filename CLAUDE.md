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

## Status (2026-07-24)
Phases 0–1 and the hardening set (H1–H8) are complete; the `0.1.0-preview.2` train is on
nuget.org (plus `specdrift` 0.4.1 as tool/MCP/Docker/Action). Phase D shipped the CorPay
reference app (`samples/corpay`, proven nightly against the published packages). The UI
phase is active (`docs/rfc/goldpath-console.md`, U1 in flight). Live status ledgers —
keep them updated in the same PR that changes reality: `docs/strategy/ai-sdlc-status.md`
(AI-assisted SDLC vs reality) and `docs/strategy/coverage-matrix.md` (capability × sample).
Roadmap gates: `docs/strategy/foundation.md` §12.
