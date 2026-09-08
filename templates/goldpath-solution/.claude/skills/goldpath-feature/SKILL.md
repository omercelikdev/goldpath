---
name: goldpath-feature
description: Turn a business sentence into a merge-ready vertical slice — endpoint + handler + entity deltas + tests — composed from the Goldpath primitives this app's manifest enables. Use when asked to add or change a feature/endpoint/business capability.
---

# goldpath-feature — business sentence → MR-ready slice

You are working in a manifest-driven golden path. The manifest is the single source of
truth; your job is to COMPOSE what it enables, never to invent infrastructure.

The SEQUENCE is `.claude/cycle.md` — nine steps, and this skill is the feature-shaped path
through them. The steps below are what "compose the golden path" means at steps 3 through 6;
`cycle.md` owns everything else, including the two that are easiest to skip: **proving the test
fails** (§4) and **running it for real and measuring** (§7).

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
5. **Tests ride along, and the test is PROVEN**: unit tests for the handler's business rules;
   extend the smoke test only when the happy path changes shape. Derive cases from the
   contract, and hand the spec to `goldpath-test-gen` when the surface is more than trivial.
   Then `cycle.md` §4: break the behaviour deliberately and confirm the new test goes red. A
   test that passes with the feature removed is not testing the feature.
   Pick the layer that can fail (`cycle.md` §5) — a rule the database enforces has no unit
   test that can catch it.
6. **Ask the engine before declaring done** (MCP server `specdrift`):
   - `spec_validate` on `.goldpath/manifest.yaml` (schema + `.specdrift/rules.yaml`) — clean.
   - `spec_drift` on the repo — clean. If you changed the contract, re-export the built
     OpenAPI (build does it) and update the committed copy; SPEC0212 means you forgot.
7. **Full local gate** before offering the change: `dotnet build` (analyzers + PublicAPI
   ride the compiler), `dotnet test`, `dotnet format --verify-no-changes`.
8. **Run it for real** (`cycle.md` §7): bring the stack up and use the feature. If it has a
   screen, open it and sign in if it asks; if it does not, call the endpoint or trigger the job.
   Read the row, the payload, the computed value — "it looks right" is not a result.
9. **Watch the as-is** (`cycle.md` §8): does an existing test encode the old contract, did a
   field's meaning change while its name did not, is there a screen or job left behind? Update
   every document this change made untrue, here, not later.

## What NOT to do

- No new abstractions over Goldpath/Mediant/EF — if a primitive feels missing, say so in the
  MR description instead of wrapping.
- No hand-written DTO plumbing the platform can already express — that is a review flag.
- Never edit `.goldpath/manifest.yaml` as a side effect of a feature; manifest changes are the
  `goldpath-manifest` skill's job and a separate, visible decision.
