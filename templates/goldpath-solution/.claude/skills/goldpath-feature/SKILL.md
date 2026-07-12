---
name: goldpath-feature
description: Turn a business sentence into a merge-ready vertical slice — endpoint + handler + entity deltas + tests — composed from the Goldpath primitives this app's manifest enables. Use when asked to add or change a feature/endpoint/business capability.
---

# goldpath-feature — business sentence → MR-ready slice

You are working in a manifest-driven golden path. The manifest is the single source of
truth; your job is to COMPOSE what it enables, never to invent infrastructure.

## Hard steps — skipping any of these means the work is NOT done

1. **Read the manifest first** (`.goldpath/manifest.yaml`). It tells you which cross-cutting
   capabilities exist in this app (idempotency, audit, soft delete, multi-tenancy, caching,
   locking, auth strategy). Compose accordingly: e.g. a state-changing command in an
   idempotency-enabled app carries `[Idempotent]`; an auth-enabled app needs roles/policy
   decisions made explicit.
2. **Contract before code.** A new or changed endpoint is FIRST reflected in the committed
   OpenAPI document (`specs/`), then implemented to match. If the contract question is
   ambiguous (status codes, error shapes), decide from `.claude/conventions.md`, not taste.
3. **The slice shape is fixed**: one file per feature under `<Area>/Features/` holding the
   Mediant command/query record (`[HttpEndpoint]`, `Result<T>` responses) + handler
   (+ validator). Look at an existing feature file and match it exactly — style drift is a
   defect.
4. **Compose, never rebuild**: pagination is `ToPageAsync` (keyset), events crossing the
   broker implement `IIntegrationEvent` and go through the outbox, timestamps are
   `DateTimeOffset`, headers come from `GoldpathHeaders`. The analyzers enforce most of this at
   build time — treat every GOLDPATH diagnostic as a design instruction, not noise.
5. **Tests ride along**: unit tests for the handler's business rules; extend the smoke test
   only when the happy path changes shape. Derive cases from the contract, and hand the
   spec to `goldpath-test-gen` when the surface is more than trivial.
6. **Ask the engine before declaring done** (MCP server `specdrift`):
   - `spec_validate` on `.goldpath/manifest.yaml` (schema + `.specdrift/rules.yaml`) — clean.
   - `spec_drift` on the repo — clean. If you changed the contract, re-export the built
     OpenAPI (build does it) and update the committed copy; SPEC0212 means you forgot.
7. **Full local gate** before offering the change: `dotnet build` (analyzers + PublicAPI
   ride the compiler), `dotnet test`, `dotnet format --verify-no-changes`.

## What NOT to do

- No new abstractions over Goldpath/Mediant/EF — if a primitive feels missing, say so in the
  MR description instead of wrapping.
- No hand-written DTO plumbing the platform can already express — that is a review flag.
- Never edit `.goldpath/manifest.yaml` as a side effect of a feature; manifest changes are the
  `goldpath-manifest` skill's job and a separate, visible decision.
