---
name: goldpath-manifest
description: Change what this app IS — enable/disable manifest features, explain the trade-offs, and keep manifest and repository telling the same story. Use when asked to turn a capability on/off (idempotency, audit, multi-tenancy, caching, locking, archival, bulk, notification, campaign, auth strategy, outbox...).
---

# goldpath-manifest — the authoring wizard

`.goldpath/manifest.yaml` is the single source of truth: a disabled feature does not exist in
this codebase at all (compile-time composition). Editing it is an architectural act — treat
it that way.

## Hard steps

1. **Name the consequences before editing.** Every toggle changes the dependency graph and
   the wiring. Tell the user exactly what appears/disappears BEFORE you change anything:
   which `Goldpath.*` package, which registration call (`AddGoldpath*` in Program.cs), which model
   call (`ApplyGoldpath*` / `AddGoldpathAuditLog` in OnModelCreating), and any infrastructure the
   feature rides (broker, redis, an IdP).
2. **Edit manifest + wiring TOGETHER.** The manifest says it, the csproj references it, the
   code registers it — one MR, all three. The drift profile (`.specdrift/drift.yaml`) is
   the authoritative feature⇄package⇄call table; follow it, don't guess.
3. **Round-trip the engine after every edit** (MCP server `specdrift`):
   - `spec_validate` with `.specdrift/rules.yaml` — the cross-field invariants (outbox
     needs a broker, l2 needs a cache provider, ...) fire here with messages that teach the
     fix. A validate finding means the DESIGN is incomplete, not the file.
   - `spec_drift` — clean means manifest and repository agree again.
4. **Fail-closed features deserve a warning to the user**: enabling multiTenancy or auth
   changes runtime behavior for EVERY request (400s/401s where none existed). Say so.
5. **Full local gate** (`dotnet build && dotnet test`) before offering the change — a
   toggled feature that breaks the smoke test was not ready to toggle.

## What NOT to do

- Never leave the manifest saying one thing and the code doing another "temporarily" —
  that is the exact rot this platform exists to prevent.
- Never remove a feature's data artifacts (tables, audit rows) as part of a toggle-off;
  flag data-lifecycle consequences and leave them to a human decision.
