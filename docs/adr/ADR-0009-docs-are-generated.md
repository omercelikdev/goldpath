# ADR-0009: Docs are generated, not handwritten

- Status: accepted (2026-07-03)

## Decision
Three document classes, three freshness mechanisms: **generated** (API/config reference,
CHANGELOG, topology — regenerated on every build), **living context** (the CLAUDE.md family,
domain memory — updated via skill DoD), **curated** (ADRs, onboarding, runbooks — CI
freshness checks). If a piece of information can be generated from code/specs, it is
generated. "No doc = no module", "no runbook = no module".

## Rationale
Handwritten docs drift; the maintenance promise only holds if documentation updates itself.

## Consequences
- Code references inside docs are matched against real code in CI; broken ones raise warnings.
- Demo/training examples are generated only from the reference application (OrderPlatform).
