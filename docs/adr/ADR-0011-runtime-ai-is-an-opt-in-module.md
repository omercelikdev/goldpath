# ADR-0011: Runtime AI is an opt-in module — the core stays AI-free

- Status: accepted (2026-07-26) · Companion to ADR-0006 (narrows, does not replace)

## Decision
Runtime AI capability is permitted ONLY inside the opt-in `Goldpath.Ai` module family
(`features.ai`, default off — absent from the compiled application when disabled,
per ADR-0001's composition rule). It is composed from vendor-neutral abstractions
(`Microsoft.Extensions.AI`, ADR-0003), every AI-influenced decision is recorded (the
decision record), and every model-boundary crossing carries the same tenant and
authorization context as HTTP (admin-contract R1). **No other package may take an AI
dependency** — enforced by analyzer (the Goldpath.Ai RFC's GP19xx set).

## Rationale
ADR-0006 keeps AI in the development layer; enterprise adopters also need a GOVERNED
runtime seam. An opt-in module preserves both: the core's determinism promise stays
byte-for-byte, and the runtime story is a designed decision, never a default.

## Consequences
- `Goldpath.Ai` is the only home for model gateways, tool registries, decision records
  and confidence gates; document AI / RAG / ML serving remain compose-don't-own.
- The admin tool registry is a PROJECTION of the frozen admin contract — ops floor,
  tenant scoping and audit apply unchanged; it is never a second door.
