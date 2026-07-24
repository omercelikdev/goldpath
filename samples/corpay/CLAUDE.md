# CLAUDE.md — CorPay

Golden-path Goldpath solution. The manifest (`.goldpath/manifest.yaml`) is the single source of truth;
a disabled feature does not exist in this codebase (compile-time composition).

## Rules
- Conventions: `.claude/conventions.md`. Constitution and rationale: the Goldpath repo (`docs/adr`).
- Vertical slice: one file per feature under `src/CorPay.Api/Orders/Features/`.
- Broker-bound events implement `IIntegrationEvent`; in-process events are Mediant
  notifications — never both (GP0401/0402).
- Lists are keyset-paginated (`ToPageAsync`); `Skip/Take` is analyzer-flagged.
- Entities use `DateTimeOffset` (UTC policy); schema changes go through migrations
  (Development auto-creates; production applies the CI bundle).

## Skills (agent workflows)
- `goldpath-feature` — business sentence → merge-ready vertical slice (contract-first, engine-checked).
- `goldpath-manifest` — enable/disable capabilities; manifest + wiring change together, engine-checked.
- `goldpath-test-gen` — spec-derived tests; NEVER reads `Features/` implementations (foundation §8.2).
- `breaker` agent — adversarial scenarios as executable tests; succeeds by finding failures.
The deterministic engine is registered in `.mcp.json` (`specdrift mcp`); "done" without a
clean `spec_validate` + `spec_drift` is not done.

## Guardrail hooks (`.claude/settings.json` — in-loop, unskippable)
- Post-edit: touched `.cs` files are whitespace-formatted automatically.
- Stop gate: the agent cannot end a turn with a red `dotnet build`; `specdrift drift`
  runs too and blocks on error-level findings (install: `dotnet tool install -g specdrift`).
Hooks live in `.claude/hooks/` — delete `settings.json` to opt out (not recommended).

## Run
`dotnet run --project src/CorPay.AppHost` → containers start, dashboard opens.
`dotnet test` → smoke: probes green + the auth floor holds (full order flow needs your IdP).
