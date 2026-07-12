# ADR-0008: "Runs with one click" is a proven feature

- Status: accepted (2026-07-03)

## Decision
The golden manifest matrix (6 persona GMs, `docs/strategy/golden-manifests-v1.md`) runs in CI
on every template or core package change: generate → build → spin up → smoke → tear down.
All dependency versions are pinned; "latest" is forbidden. Container/package sources are
pulled from the corporate registry mirror (air-gapped networks are a first-class scenario).

## Rationale
"It works" is not a hope; it is a CI result re-proven on every commit. The cost of crashing
on the first run in a customer demo is the narrative itself.

## Consequences
- Every new module RFC must enter at least one GM.
- A combination that blows up in the field becomes a permanent regression manifest; a GM
  change demands review as rigorous as a schema change.
