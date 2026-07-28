# Open threads — deferred work, with its trigger and its proof

Every entry here is work we CHOSE to postpone, not work we forgot. The rule that makes
this file worth keeping: a thread leaves the table only when the **proof** column has
actually run — not when the code lands.

Keep it honest in the same PR that changes reality (same discipline as
[ai-sdlc-status.md](ai-sdlc-status.md) and [coverage-matrix.md](coverage-matrix.md)). A
thread that turns out to be unnecessary is DELETED with a line saying why, never left
pending forever.

| # | Thread | Why it waits | Trigger — when it comes up | What must be PROVEN then |
|---|---|---|---|---|
| T1 | The console driven against **CorPay** (the real sample) | The README pictures are done and honest — captured by `scripts/console-screenshots.sh` from a real app driving real modules — so what is left is narrower: CorPay is the ADOPTER-shaped proof, and its own workers cannot reach ready yet (T12) | T12 closes | `scripts/console-smoke.sh` (or its CorPay twin) drives CorPay's own admin surface end to end, including the console its internal head serves |
| T3 | **Mutation testing for the TypeScript side** | .NET has Stryker; the UI has none. Today's answer is the coverage floor (kit 95/90, console 97/85) plus an adversarial e2e that drives real services | a UI regression that BOTH the coverage floor and the console smoke miss | whatever we adopt must catch that specific regression, replayed |
| T4 | The **review agent as a CI step** (it is a manual script today) | It needs an agent-in-CI story; the same one the LLM-half evals wait on | agent-in-CI exists | the agent's findings gate a PR the way the hooks gate a turn |
| T5 | **LLM-half skill evals** — running the skills themselves per fixture | Deterministic acceptance runs nightly; running the skills needs agent-in-CI | agent-in-CI exists | a skill that fails its evals cannot be released ([ai-sdlc-status.md](ai-sdlc-status.md) §2) |
| T6 | **CLI-as-MCP** and **plugin packaging** | Both are distribution shapes with no adopter asking yet | CLI-as-MCP: the Insurance sample's first run · packaging: the first brownfield adopter | the shape is exercised by that adopter's own flow, not by a demo we write for it |
| T7 | **`Goldpath.Ai`** (runtime AI as an opt-in module, ADR-0011) | The console phase owns the calendar; the RFC is written and the ADR is accepted | after the UI phase | the module composes in and out cleanly (a manifest without it has NO AI code), and its evals run in the same lane as the rest |
| T12 | CorPay's **EOD worker never becomes ready** | It validates the Quartz schema at startup while the API is still migrating the database they share, so its `/health/ready` never goes green. Pre-existing and invisible: nothing had ever waited on that worker — the console smoke asked, and found it | before the sample can claim "runs with one click" for its WORKERS, not just its API | the AppHost orders the migration ahead of the workers (or each worker owns its schema), and the smoke waits on every head's readiness, including the console the internal head serves |
| T11 | A **tenant picker** in the console | On a multi-tenant app resolving tenants by HEADER, the console's calls carry none, so every surface answers "refused" — honest, but useless. Subdomain resolution works today, and R1 already defines `?tenant=` for holders of the all-tenants privilege | the first adopter running header-resolved multitenancy who wants the console | the console offers the tenants that operator may see, sends the scope R1 defines, and keeps saying "refused" for the ones they may not |
| T10 | A **token the console holds** (browser-side OIDC) | The console is a client of the app's own floor: served behind it, it needs no identity library and no per-adopter authority/clientId. Building one is a different product decision | an adopter whose services cannot share an auth boundary or allow the console's origin in CORS | the console signs in once and carries the token to every service, with the same honesty about refusals it has now (console RFC §3 auth, case 3) |
| T9 | Client-side ROUTES for the console (a section is state today, not a URL) | Nobody has asked to bookmark a panel yet, and a fake fallback is worse than none: serving the page at an arbitrary path answers 200 with a document whose relative assets resolve a directory too deep — a blank screen with a green status code | the first operator who wants to share a link to a panel | `MapGoldpathConsole` serves the page for real routes AND injects a `<base href>` so assets resolve; the served-console e2e stops asserting 404 and asserts the panel |
| T14 | Four admin responses are **untyped in OpenAPI** ([#98](https://github.com/omercelikdev/goldpath/issues/98)): archival holds/erasures, bulk batches/errors | R1's per-endpoint tenant wrappers return a raw `IResult`, so the exporter cannot infer their types; the committed CorPay spec hid it for a full train because the sample lane never ran `goldpath check` (the lane runs it now) | the next train (additive: `.Produces<T>` metadata) | CorPay's re-export regains the four schemas and `goldpath check` stays green |
| T15 | **Campaign R1 — device-fleet parity** (`ExcludedDays`, `EndDate`, per-item auto-retry ladder, `GlobalTps`) | A device-management-class plan needs six dials; the policy carries four. RFC revision written and ACCEPTED (goldpath-campaign.md R1) — additive, defaults = today's behavior | ACCEPTED 2026-07-29 — implementation is the next train's work | the R1 test plan runs: excluded-day flip and end-date expiry driven in the console smoke against a real campaign; two campaigns under one GlobalTps never jointly exceed it |
| T16 | **Oracle provider** | The device-management class runs on Oracle by mandate; Goldpath ships postgres/sqlserver. A provider is a Data-module decision (EF provider + Quartz store + keyset translation proofs), not a campaign field | the first committed adopter whose platform mandates Oracle | the golden-manifest matrix gains an Oracle shape and the D7 migration proofs pass on a real Oracle container |
| T8 | Bulk's **`?definition=` filter** on `/batches` ([#72](https://github.com/omercelikdev/goldpath/issues/72)) | The console refuses to fake it client-side: narrowing one take-bounded page would read as "no batches" while more exist | the next bulk surface change | the panel filters by definition through the SERVER, and the smoke walks a definition with more batches than one page |

## Closed threads

Kept short on purpose — the point of the list is what is still open.

- **The console's refusal paths** (auth floor, tenant scoping, mid-session death) — closed
  2026-07-27 by the three-app smoke; found and fixed a discovery bug that reported a
  composed app as having no admin surface at all.
- **UI coverage gaps** — closed 2026-07-27 by a measured sweep and a CI floor; found and
  fixed a deadline verdict that called an overrunning run "on track".
- **Accessibility** — closed 2026-07-27 by the axe gate; found and fixed the secondary
  text's contrast and a confirm dialog Escape could not close.
- **T2 — the UI styling pass (U6)** — closed 2026-07-28. One component vocabulary in the
  kit's token file, every panel swept onto it, density fixes (quiet stamps, truncated
  identities) — with axe and the console smoke green through the whole sweep, which is
  the only proof cosmetics can offer.
- **T13 — the frozen jobs verbs with no screen** — closed 2026-07-28 by U5.
  `pause-all`/`resume-all`, `reschedule`, the calendar CRUD and the admin audit are all
  driven from the console now, and the console smoke exercises each against a real fleet.
  Opening them up also exposed the console reading `job.paused` and `job.nextFireTime` —
  two fields the contract has NEVER sent, so a paused job had always looked exactly like a
  running one. Both are derived from the job's triggers now.
