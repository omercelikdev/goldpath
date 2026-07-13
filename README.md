# Goldpath — Enterprise .NET Asset

AI-native, spec-driven enterprise .NET accelerator: composable NuGet libraries +
templates + AI skills + guardrails + living documentation. Not a framework — a
**golden path**: a road paved with enterprise opinion on top of Microsoft (Aspire, Extensions.*).

> Conceptual grounding: [Strategy/Foundation](docs/strategy/foundation.md) ·
> Constitution: [ADR-0001..0010](docs/adr/README.md)

## What it is / is not

- ✔ Composable packages — added to an existing project in 5 minutes (L1) or scaffolded from scratch (L3)
- ✔ Spec-driven: `.goldpath/manifest.yaml` + OpenAPI/AsyncAPI as the single source of truth
- ✔ AI in the development layer: skills generate, guardrails verify, humans approve
- ✘ Not a framework (imposes no structure), no custom DSL, no wrappers around Microsoft

## Monorepo Layout

| Directory | Contents | Status |
|---|---|---|
| `docs/strategy/` | Strategy documents (foundation, manifest, testing, telemetry…) | ✔ v1 |
| `docs/adr/` | Constitution — 10 ADRs | ✔ accepted |
| `docs/rfc/` | Module RFCs (template + Idempotency reference example) | ✔ template |
| `schemas/manifest/v1/` | Manifest JSON Schema + valid/invalid corpus | ✔ ajv-validated |
| `packages/` | Goldpath NuGet packages (Phase 1: Abstractions → ServiceDefaults → …) | empty — [plan](docs/strategy/module-plan-v1.md) |
| `templates/` | `dotnet new` template pack + AppHost | empty |
| `skills/` | AI skill definitions (authoring, new-service, add-feature…) | empty |
| `analyzers/` | GOLDPATH Roslyn rules | empty |
| `rulesets/` | Enterprise Spec Engine ruleset package (private content) | empty |
| `samples/` | OrderPlatform reference application (dogfood) | empty |

## Language Policy

Everything in this repo — documentation, code, identifiers, commits, PRs — is **English**.
Conversation with the team may be Turkish. In customer repos, the domain-memory language is
chosen per customer (Turkish by default for Turkish customers — see domain-memory-v1 §4).

## Dependency Policy

Every dependency, including first-party OSS (Mediant, Mockifyr, Spec Engine — personal GitHub),
is consumed as a **published, pinned NuGet package** (nuget.org → internal mirror where required). Source
references/submodules are forbidden. Details: foundation §10.

## Versioning & Support

One version train: every `Goldpath.*` package, the CLI and the template pack ship the
same version. SemVer with the pre-1.0 rules spelled out — `0.x.y` patches are always
safe to take blind; `0.(x+1)` minors may break but never silently: every break ships
with a step-by-step upgrade guide (`docs/upgrades/`), and the `PublicAPI.*.txt` ledger
diff is the mechanical proof of what changed. Support: latest release only pre-1.0;
from 1.0, previous major gets security fixes for 6 months. The full written contract:
[docs/rfc/goldpath-versioning.md](docs/rfc/goldpath-versioning.md).
