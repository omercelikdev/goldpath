# ADR-0003: The Microsoft layer is configured, not wrapped

- Status: accepted (2026-07-03)

## Decision
Infrastructure concerns (logging, health, resilience, rate limiting, OTel) are covered by
Microsoft/Aspire packages; the Goldpath template sets up best-practice configuration and does not
write wrappers. Goldpath NuGet packages contain only the enterprise patterns Microsoft does NOT
provide (Ring B/C). The same principle applies to first-party OSS (e.g. what Mediant
provides is not rewritten; it is composed).

## Rationale
The cause of death for in-house frameworks: Microsoft steamrolling their path + the
maintenance burden. The added value is not in wrapping but in curated, validated
configuration.

## Consequences
- Every Ring B/C module RFC includes a "what happens if Microsoft ships this" exit plan.
- Ring B entry criteria (all 4 required): MS doesn't provide it + ≥2 industries + domain-agnostic + leak-free.
