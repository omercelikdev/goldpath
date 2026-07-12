# Eval: goldpath-feature — the Phase 2 gate demo

**Fixture:** a freshly generated default app (`dotnet new goldpath-solution`, defaults).

**Input sentence (verbatim, nothing more):**
> A customer can cancel an order that has not been confirmed yet. Cancelling a confirmed
> order must be rejected. A cancelled order still appears in the list, marked cancelled.

**Acceptance (machine-checked by `accept.sh <APP_DIR>`; outcomes only):**
1. The committed OpenAPI document contains a cancel operation under `/api/v1/orders`.
2. A `CancelOrder` feature file exists in the slice shape (`Orders/Features/CancelOrder.cs`).
3. Tests referencing CancelOrder exist and the FULL suite is green.
4. `specdrift validate` (schema + rules) is clean; `specdrift drift` is clean — meaning the
   committed contract was re-exported, not hand-edited into drift.
5. `dotnet format --verify-no-changes` passes; the manifest was NOT modified.
