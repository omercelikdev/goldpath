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
| T3 | **Mutation testing for the TypeScript side** | .NET has Stryker; the UI has none. Today's answer is the coverage floors (the extracted kit's 95/90 lives in github.com/qorpe/ui with gates G1–G6; the console's 97/85 here) plus an adversarial e2e that drives real services | a UI regression that BOTH the coverage floor and the console smoke miss | whatever we adopt must catch that specific regression, replayed |
| T4 | The **review agent as a CI step** — the workflow LANDED 2026-08-03 (`review-agent.yml`: every PR, `--gate` fails the job on hard-stop findings only, verdict uploaded as an artifact; the script's output contract hardened — schema-valid extraction, one corrective retry, six parse fixtures proven) | What remains is the KEY: the workflow skips honestly until the `ANTHROPIC_API_KEY` repo secret exists (the repo's first stored secret — an owner decision, not a code change) | the owner sets the secret | the agent's findings gate a PR the way the hooks gate a turn — the first keyed PR with a hard-stop must fail the check |
| T5 | **LLM-half skill evals** — running the skills themselves per fixture | Deterministic acceptance runs nightly; running the skills needs agent-in-CI | agent-in-CI exists | a skill that fails its evals cannot be released ([ai-sdlc-status.md](ai-sdlc-status.md) §2) |
| T6 | **CLI-as-MCP** and **plugin packaging** | Both are distribution shapes with no adopter asking yet | CLI-as-MCP: the Insurance sample's first run · packaging: the first brownfield adopter | the shape is exercised by that adopter's own flow, not by a demo we write for it |
| T7 | **`Goldpath.Ai`** (runtime AI as an opt-in module, ADR-0011) | The console phase owns the calendar; the RFC is written and the ADR is accepted | after the UI phase | the module composes in and out cleanly (a manifest without it has NO AI code), and its evals run in the same lane as the rest |
| T11 | A **tenant picker** in the console | On a multi-tenant app resolving tenants by HEADER, the console's calls carry none, so every surface answers "refused" — honest, but useless. Subdomain resolution works today, and R1 already defines `?tenant=` for holders of the all-tenants privilege | the first adopter running header-resolved multitenancy who wants the console | the console offers the tenants that operator may see, sends the scope R1 defines, and keeps saying "refused" for the ones they may not |
| T10 | A **token the console holds** (browser-side OIDC) | The console is a client of the app's own floor: served behind it, it needs no identity library and no per-adopter authority/clientId. Building one is a different product decision | an adopter whose services cannot share an auth boundary or allow the console's origin in CORS | the console signs in once and carries the token to every service, with the same honesty about refusals it has now (console RFC §3 auth, case 3) |
| T9 | Client-side ROUTES for the console (a section is state today, not a URL) | Nobody has asked to bookmark a panel yet, and a fake fallback is worse than none: serving the page at an arbitrary path answers 200 with a document whose relative assets resolve a directory too deep — a blank screen with a green status code | the first operator who wants to share a link to a panel | `MapGoldpathConsole` serves the page for real routes AND injects a `<base href>` so assets resolve; the served-console e2e stops asserting 404 and asserts the panel |
| T16 | **Oracle provider** | The device-management class runs on Oracle by mandate; Goldpath ships postgres/sqlserver. A provider is a Data-module decision (EF provider + Quartz store + keyset translation proofs), not a campaign field | the first committed adopter whose platform mandates Oracle | the golden-manifest matrix gains an Oracle shape and the D7 migration proofs pass on a real Oracle container |

## Closed threads

Moved out of the open table 2026-08-09, after an audit found four CLOSED rows still
sitting in it. That made the register read worse than reality AND buried the two
threads whose triggers had genuinely fired (T1, T7) among rows that were already done:

- **T12** — CorPay's **EOD worker never becomes ready** — CLOSED 2026-08-03.
- **T14** — Four admin responses were **untyped in OpenAPI** ([#98](https://github.com/omercelikdev/goldpath/issues/98)) — CLOSED 2026-08-05.
- **T15** — **Campaign R1 — device-fleet parity** (`ExcludedDays`, `EndDate`, per-item auto-retry ladder, `GlobalTps`) — CLOSED 2026-08-03.
- **T8** — Bulk's **`?definition=` filter** on `/batches` ([#72](https://github.com/omercelikdev/goldpath/issues/72)) — CLOSED 2026-08-03.


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
