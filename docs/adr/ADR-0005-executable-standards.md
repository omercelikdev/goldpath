# ADR-0005: Standards are executable

- Status: accepted (2026-07-03)

## Decision
A standard = schema + analyzer + architecture test + CI rule. A rule that is written in a
document but not machine-verified does not count as a standard. Code style comes from a single
source: `.editorconfig` + analyzers + `dotnet format` (mandatory in CI); XML summaries are
required on public APIs; skill output passes through the same rules (the single-handwriting
rule).

## Rationale
If AI is going to produce code, compliance is enforced by the compiler/CI, not by the prompt.
A standard that relies on human discipline erodes within three months.

## Consequences
- Every new standard is merged together with its verifier (with a rule ID).
- Suppressions are JUSTIFIED and visible at every layer (Delivery Report "Honesty" section).
