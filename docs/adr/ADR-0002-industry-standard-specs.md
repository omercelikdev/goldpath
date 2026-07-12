# ADR-0002: Spec formats are industry standards, no custom DSL

- Status: accepted (2026-07-03)

## Decision
API = OpenAPI 3.1, events = AsyncAPI 3, data model = JSON Schema. The enterprise manifest is
only a thin binding layer. Business logic, flow definitions, and endpoint behavior NEVER go
into the manifest.

## Rationale
A custom DSL creates its own maintenance burden and learning curve; industry standards come
with ready-made tooling, and LLMs know them best. We are avoiding the spec-flavored version
of the "write an abstraction on top of Microsoft" mistake.

## Consequences
- Spec Engine capabilities (validate/drift/diff/generate/test/mock/docs) are built on top of these formats.
- A need for a new spec type = search existing standards first; if none fits, write an RFC.
