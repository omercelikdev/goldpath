# ADR-0006: AI lives in the development layer, not in the library

- Status: accepted (2026-07-03)

## Decision
There is no AI inside the Goldpath NuGet packages — plain, deterministic, stable .NET code.
AI = context files (the CLAUDE.md family, domain memory) + skills + review agent +
delivery telemetry. Skills are defined model-agnostically (markdown + MCP); whatever model
sits behind the enterprise LLM gateway is the one used (model adequacy is proven by an
eval matrix).

## Rationale
The library's stability/maintenance promise is incompatible with a model dependency. The
banking reality: external cloud LLMs are usually forbidden; the possibility of an on-prem
model is a design input from day one. The quality floor is guaranteed by the guardrail
chain, not by the model — a weaker model reduces speed, not quality.

## Consequences
- Runtime AI needs (in-app LLM) are a SEPARATE, optional module; they do not enter the core.
- Domain knowledge accumulates in the customer's repo, not in the model (artifact = learning).
