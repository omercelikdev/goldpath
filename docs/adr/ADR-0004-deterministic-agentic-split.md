# ADR-0004: Deterministic/agentic split

- Status: accepted (2026-07-03)

## Decision
The boundary layer (DTOs, clients, contracts, skeletons, mock stubs, contract tests) =
deterministic codegen (Spec Engine; same input → always the same output, never calls an LLM).
Business logic = skills (AI). AI output passes through the guardrail chain and human review.

## Rationale
What is deterministic is 100% testable and auditable (a banking requirement). AI's strength
is not in repeatable generation but in business logic that requires context. Mixing the two
weakens both.

## Consequences
- Spec Engine is 100% deterministic; AI CALLS it via MCP, it does not live inside it.
- Which side a given generation step belongs to is stated explicitly in its RFC.
