# CLI reference — every verb the tool has

The complete surface of `goldpath` (install: `dotnet tool install -g Goldpath.Cli`). The CLI is
thin and deterministic by design: it wraps the templates, the anchors, the drift profile and the
Spec Engine — it invents no semantics of its own. Exit codes mirror specdrift: **0** ok,
**1** failure/findings, **2** usage.

`goldpath --help` prints this surface; `goldpath --version` reports the installed assembly.

## Generate

| Verb | What it does |
|---|---|
| `goldpath new` | The **wizard**: asks what the app DOES (modules, auth, layout) and derives the infrastructure, saying why each piece joins or goes ("no broker needed — removed"). Prints the equivalent `new solution` command and delegates to it — no private path. |
| `goldpath new solution -n <Name> [--db …] [--auth …] [--layout …] [--features …]` | A whole solution from the golden template: AppHost, api, tests, manifest, migrations. |
| `goldpath new service <Name> [--path <dir>]` | A second service head INSIDE an existing solution — its own database, its own manifest (`kind: service`), and the deployment model flips to microservice. |
| `goldpath new gateway [--path <dir>]` | A YARP gateway over service discovery, routing `/{head}/{**rest}` to every head it finds. |
| `goldpath new worker -n <Name>` | A **standalone** worker solution (its own repo/solution) from the worker template. For a worker INSIDE an existing solution, use `add worker` below — that is the common case. |
| `goldpath init [--path <dir>]` | **Attach** a manifest to a solution that already exists (L2 brownfield). It attaches, never rewrites: a rejected manifest leaves nothing behind. Rewiring is the transformation pack's job, not this verb's. |

## Extend

| Verb | What it does |
|---|---|
| `goldpath add feature <name> [--path <dir>]` | Wires a Ring B capability into an existing app: package reference, registration, model, manifest — an anchor-driven textual transform, specdrift-verified, rolled back whole if the engine refuses. Features: multitenancy, audittrail, softdelete, idempotency, dataprotection, caching, locking, archival, bulk, notification, campaign. |
| `goldpath add worker <name> [--trigger queue\|schedule\|jobs] [--path <dir>]` | Adds a worker PROJECT to an existing solution, wired into the AppHost with the trigger you chose. |

## Database

| Verb | What it does |
|---|---|
| `goldpath db init [--path <dir>]` | The Initial migration for a fresh app (the template generates migration-ready, not migration-full). |
| `goldpath db add <name> [--path <dir>]` | A migration, owner-aware: it knows which project owns the schema (D3). |
| `goldpath db status [--path <dir>]` | What is applied, what is pending — also inlined into `check`. |
| `goldpath db bundle [--path <dir>]` | A self-contained migration bundle for environments where the SDK is not installed. |

## Inspect

| Verb | What it does |
|---|---|
| `goldpath check [--path <dir>]` | The one gate an adopter runs: `specdrift validate` + `drift` + db status + build. This is what CI runs per manifest. |
| `goldpath discover [--path <dir>]` | Lists every Goldpath manifest under a tree, with what each DECLARES (kind, name, products). The unit Goldpath binds to is the manifest, never the repo — so a monorepo, a workspace of product repos, or a laptop full of clones is inventoried without anyone keeping a list by hand. Read-only: no engine call, no network, no write. Line-oriented so a CI matrix can be built from it. |

```console
$ goldpath discover --path ~/Repositories
goldpath/samples/corpay  kind=solution  name=CorPay
goldpath/templates/goldpath-solution  kind=solution  name=GoldpathTemplate
goldpath/templates/goldpath-worker  kind=worker  name=GoldpathWorker
── 3 manifest(s) under /Users/omercelik/Repositories
```

## What the CLI deliberately does NOT do

- It never invents semantics the templates and the drift profile do not already have.
- It never edits what the engine would reject: every transform is validated, and a refusal
  rolls the change back whole.
- It calls no LLM. The AI skills call the CLI and the engine, never the reverse (ADR-0004).
