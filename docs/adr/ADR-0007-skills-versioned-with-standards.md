# ADR-0007: Skills live in the same repo/version as the standards and are eval'd

- Status: accepted (2026-07-03)

## Decision
Skills, standards, schemas, and templates live in this monorepo on a single release train.
Every skill has an eval set (golden manifest → expected output); a skill version that does
not pass its evals is not released. Every skill's DoD is code + tests + doc updates +
telemetry recording.

## Rationale
If a standard changes and a skill stays stale, the AI generates code against the old
standard — the most insidious kind of drift. A single release train makes this divergence
structurally impossible.

## Consequences
- A skill change = the same review rigor as a standard change.
- The review agent's rule set is eval'd too (dismiss rate > 40% → revision).
