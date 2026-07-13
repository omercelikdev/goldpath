# Goldpath — Enterprise .NET Asset

[![ci](https://github.com/omercelikdev/goldpath/actions/workflows/ci.yml/badge.svg)](https://github.com/omercelikdev/goldpath/actions/workflows/ci.yml)
[![nightly](https://github.com/omercelikdev/goldpath/actions/workflows/nightly.yml/badge.svg)](https://github.com/omercelikdev/goldpath/actions/workflows/nightly.yml)
[![license](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)
[![NuGet](https://img.shields.io/nuget/vpre/Goldpath.Abstractions.svg?label=nuget)](https://www.nuget.org/packages?q=Goldpath)

AI-native, spec-driven enterprise .NET accelerator: composable NuGet libraries +
templates + AI skills + guardrails + living documentation. Not a framework — a
**golden path**: a road paved with enterprise opinion on top of Microsoft (Aspire, Extensions.*).

> Conceptual grounding: [Strategy/Foundation](docs/strategy/foundation.md) ·
> Constitution: [ADR-0001..0010](docs/adr/README.md)

## What it is / is not

- ✔ Composable packages — added to an existing project in 5 minutes (L1) or scaffolded from scratch (L3)
- ✔ Spec-driven: `.goldpath/manifest.yaml` + OpenAPI/AsyncAPI as the single source of truth
- ✔ AI in the development layer: skills generate, guardrails verify, humans approve
- ✔ Proof-driven: every claim below is a test/bench that runs in CI (nightly 7-shape matrix, real containers)
- ✘ Not a framework (imposes no structure), no custom DSL, no wrappers around Microsoft

## Quickstart

```bash
dotnet new install Goldpath.Templates@0.1.0-preview.1    # preview: pin the version
dotnet tool install -g Goldpath.Cli --prerelease

dotnet new goldpath-solution -n AcmeOrders --db postgresql --broker rabbitmq --features bulk
cd AcmeOrders && goldpath check           # spec validate + drift + build, one verb
dotnet run --project src/AcmeOrders.AppHost
```

> Use an undotted solution name for now — dotted names hit
> [#24](https://github.com/omercelikdev/goldpath/issues/24) until the template fix ships.

Grow it feature by feature: `goldpath add feature notification`, `goldpath add worker`,
`goldpath db add AddInvoices`. Every admin surface (`/goldpath/admin/*`) is fail-closed,
audited and [contract-frozen](docs/rfc/goldpath-admin-contract.md); every module ships
its Grafana board, runbooks and [measured performance](docs/ops/release-checklist.md)
on a pinned CI profile.

## What's in the train

| Layer | Packages |
|---|---|
| Floor (Ring A) | Abstractions · ServiceDefaults · ApiDefaults · Data · Messaging |
| Cross-cutting (Ring B) | Auth · Idempotency · AuditTrail · MultiTenancy · SoftDelete · Locking (+SqlServer) · Caching · DataProtection |
| Execution ladder (L2→L4) | Jobs (clustered, checkpointed, kill-9-recoverable) · Archival · Bulk (finance-grade intake→gate→execute→repair) · Notification · Campaign (paced fan-out, live throttle) |
| Tooling | Analyzers (GP#### executable standards) · `goldpath` CLI · `Goldpath.Templates` |

## Monorepo Layout

| Directory | Contents |
|---|---|
| `docs/strategy/` · `docs/adr/` · `docs/rfc/` | Strategy, the 10-ADR constitution, module RFCs + frozen contracts |
| `docs/ops/` · `docs/upgrades/` | Migrations/trace/release runbooks · per-release upgrade guides |
| `schemas/manifest/v1/` | Manifest JSON Schema + valid/invalid corpus (ajv-validated) |
| `packages/` · `analyzers/` | The NuGet train (19 packages) · GP#### Roslyn rules |
| `templates/` · `tools/` | `dotnet new` pack (solution + worker) · the `goldpath` CLI |
| `tests/` | 586 unit + 34 integration proofs (Testcontainers) + bench suite |
| `skills/` · `rulesets/` · `samples/` | AI skill layer · Spec Engine rulesets · reference app (Phase D) |

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
