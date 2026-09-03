---
name: goldpath-manifest
description: Change what this worker IS — enable/disable manifest features, explain the trade-offs, and keep manifest and repository telling the same story. Use when asked to turn a capability on/off (multi-tenancy, audit, soft delete, data protection, locking, notification, file exchange, the head's auth strategy).
---

# goldpath-manifest — the authoring wizard (worker kind)

`.goldpath/manifest.yaml` is the single source of truth: a disabled feature does not exist in
this codebase at all (compile-time composition). Editing it is an architectural act — treat
it that way.

## Hard steps

1. **Name the consequences before editing.** Every toggle changes the dependency graph and
   the wiring. Tell the user exactly what appears/disappears BEFORE you change anything:
   which `Goldpath.*` package, which registration call (`AddGoldpath*` in Program.cs), which
   model call (`ApplyGoldpath*` / `AddGoldpath*` in the trigger's DbContext), and any
   infrastructure the feature rides (the jobs runtime for notification/fileExchange, an IdP
   for `providers.auth: openid`).
2. **Know what a worker can compose.** multiTenancy, auditTrail, softDelete, dataProtection,
   distributedLocking, notification, fileExchange — and the head's auth strategy. The ones
   that own tables need the worker's database (queue or jobs trigger); a schedule worker has
   none, and the template refuses the combination at build. Solution-shaped features (layout,
   idempotency, caching, archival, bulk, campaign, approvals) are not offered here — say so
   instead of improvising a wiring.
3. **Edit manifest + wiring TOGETHER.** The manifest says it, the csproj references it, the
   code registers it — one MR, all three. The drift profile (`.specdrift/drift.yaml`) is
   the authoritative feature⇄package⇄call table; follow it, don't guess.
4. **Round-trip the engine after every edit** (MCP server `specdrift`):
   - `spec_validate` with `.specdrift/rules.yaml` — the cross-field invariants fire here
     with messages that teach the fix. A validate finding means the DESIGN is incomplete,
     not the file.
   - `spec_drift` — clean means manifest and repository agree again.
5. **Fail-closed features deserve a warning to the user**: enabling multiTenancy changes
   runtime behaviour for EVERY request on the head (400s where none existed) and every
   message (the tenant is restored from headers); enabling auth puts the admin surfaces and
   the console behind the ops floor. Say so.
6. **Full local gate** (`dotnet build && dotnet test`) before offering the change — a
   toggled feature that breaks the smoke test was not ready to toggle.

## What NOT to do

- Never leave the manifest saying one thing and the code doing another "temporarily" —
  that is the exact rot this platform exists to prevent.
- Never remove a feature's data artifacts (tables, audit rows) as part of a toggle-off;
  flag data-lifecycle consequences and leave them to a human decision.
