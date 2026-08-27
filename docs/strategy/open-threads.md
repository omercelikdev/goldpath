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
| T4 | The **review agent as a CI step** — the workflow LANDED 2026-08-03 (`review-agent.yml`: every PR, `--gate` fails the job on hard-stop findings only, verdict uploaded as an artifact; the script's output contract hardened — schema-valid extraction, one corrective retry, six parse fixtures proven) | What remains is the KEY — and **as of 2026-08-10 the owner has decided NOT to set it yet**, on the recommendation that the review already happens where it is cheapest and strictest: every PR in this session was reviewed by running `scripts/review-agent.sh <PR#>` locally under the subscription, and its findings were answered before merge (#159's prefix-exemption bug is the proof it catches real defects). A stored key buys AUTOMATION, not better review, and it puts the repo's first long-lived secret on a public repository. The workflow skips honestly meanwhile — it does not pretend to have run | the review discipline stops being applied by hand (a merge lands with findings unanswered), or the repo gains contributors who are not the owner | the agent's findings gate a PR the way the hooks gate a turn — the first keyed PR with a hard-stop must fail the check |
| T5 | **LLM-half skill evals** — running the skills themselves per fixture | Deterministic acceptance runs nightly; running the skills needs agent-in-CI | agent-in-CI exists | a skill that fails its evals cannot be released ([ai-sdlc-status.md](ai-sdlc-status.md) §2) |
| T6 | **CLI-as-MCP** and **plugin packaging** | Both are distribution shapes with no adopter asking yet | CLI-as-MCP: the Insurance sample's first run · packaging: the first brownfield adopter | the shape is exercised by that adopter's own flow, not by a demo we write for it |
| T7 | **`Goldpath.Ai`** (runtime AI as an opt-in module, ADR-0011) | The console phase owns the calendar; the RFC is written and the ADR is accepted | after the UI phase | the module composes in and out cleanly (a manifest without it has NO AI code), and its evals run in the same lane as the rest |
| T11 | A **tenant picker** in the console | On a multi-tenant app resolving tenants by HEADER, the console's calls carry none, so every surface answers "refused" — honest, but useless. Subdomain resolution works today, and R1 already defines `?tenant=` for holders of the all-tenants privilege | the first adopter running header-resolved multitenancy who wants the console | the console offers the tenants that operator may see, sends the scope R1 defines, and keeps saying "refused" for the ones they may not |
| T10 | A **token the console holds** (browser-side OIDC) | The console is a client of the app's own floor: served behind it, it needs no identity library and no per-adopter authority/clientId. Building one is a different product decision | an adopter whose services cannot share an auth boundary or allow the console's origin in CORS | the console signs in once and carries the token to every service, with the same honesty about refusals it has now (console RFC §3 auth, case 3) |
| T9 | Client-side ROUTES for the console (a section is state today, not a URL) | Nobody has asked to bookmark a panel yet, and a fake fallback is worse than none: serving the page at an arbitrary path answers 200 with a document whose relative assets resolve a directory too deep — a blank screen with a green status code | the first operator who wants to share a link to a panel | `MapGoldpathConsole` serves the page for real routes AND injects a `<base href>` so assets resolve; the served-console e2e stops asserting 404 and asserts the panel |
| T16 | **Oracle provider** | The device-management class runs on Oracle by mandate; Goldpath ships postgres/sqlserver. A provider is a Data-module decision (EF provider + Quartz store + keyset translation proofs), not a campaign field | the first committed adopter whose platform mandates Oracle | the golden-manifest matrix gains an Oracle shape and the D7 migration proofs pass on a real Oracle container |
| T17 | **Per-package ops packs for the three floor packages** (Messaging, Data, ApiDefaults) | Their RFC §6 sections describe dashboards (consumer lag, outbox backlog, EF query duration, deprecated-version traffic) that are NOT packaged — the signals reach OTel through ServiceDefaults, whose ops pack is what an adopter gets. Found by an audit 2026-08-09; the RFC sections now say so | the first adopter who asks for a floor dashboard, OR the first incident whose triage needed one | the pack ships with the same shape the five module packs have (runbook + Grafana JSON), and the RFC §6 correction note is removed |
| T18 | **The MassTransit 8.x exit** — the watch that keeps option A honest | [goldpath-messaging-exit](../rfc/goldpath-messaging-exit.md) decided **A: stay pinned on 8.5.10** (Apache-2.0), because a move is a MAJOR version, not an internal swap — measured, not assumed. The publish seam already shipped (`IIntegrationEventPublisher` + GP0404), so a generated app's COMMAND HANDLERS no longer name the library; its CONSUMERS still do, deliberately | **any one of four**: an unpatched CVE in the 8.x line · the vendor's v8 maintenance window closing (~end 2026 per their own statements) · an adopter's licensing constraint · a customer requiring a bus we do not compose | the five proofs in that RFC §7 — outbox atomicity on the new transport · tenant+correlation headers still propagate (H4) · GP0401-0403 still mean something · the golden-manifest matrix green on every broker-bearing shape · CorPay migrated with a written guide an adopter could actually follow |
| T20 | **The specanchor rehearsal** — the transformation composition's proof ([specanchor-composition](../rfc/specanchor-composition.md)) | specanchor's own chain is proven on its rig (74 tests, mutation 94.8%, first skill eval 7/7), but the COMPOSITION — §9's method running on specanchor's machinery with Goldpath as target — has never run end to end; per the RFC's D4/D5, no profile code is written before it does | the fake-legacy → Goldpath migration rehearsal is scheduled (before the factoring engagement's PoC) | Discovery Zero → dual-track slices → approved specs → `goldpath new` target → parity with Mockifyr equalization → db-compare reconciliation → cutover evidence bundle, with the Discovery Zero playbook written from the rehearsal's diary; only then do ai-sdlc-status's `reverse-engineer`/`differential-test` rows leave NOT BUILT |
| T21 | **`Goldpath.Approvals`** — human approval workflows: maker-checker, four-eyes, amount-laddered authority, delegation, escalation, and the task worklist/inbox that feeds them | A Ring B candidate surfaced by the factoring-class engagement: SIX of its twelve common processes run approvals over e-mail today, and the amount ladder (expert → deputy → manager → GM) is domain-agnostic across banking/insurance/telco. Distinct from the T19 saga non-goal — these are HUMAN approval chains, not process orchestration. The RFC is ACCEPTED and the build STARTED 2026-08-18 by explicit owner decision (the trigger pulled early — recorded in the RFC header): `Goldpath.Approvals` core landed with 17 deterministic tests, manifest key + CLI recipe wired. 2026-08-18 (later the same day) the database-backed store landed (EF on the app's own DbContext, proven on real storage) and the template flag shipped (a generated app with the flag BUILDS against the train). Open before the thread closes: console federation, dashboard JSON — and the adopter proof below | The engagement's implementation phase needs its first systematized approval flow (the §5.1 by-product rule: born inside a real deliverable, never as a third front) | The RFC through the eight sections; the four Ring B criteria confirmed in review; one real amount-laddered approval flow from the adopter runs end to end on the module, with its audit trail **2026-08-26 addendum:** the api-portal product built the sibling governed-change engine and proved SIX rules the ladder lacks — recorded as [goldpath-approvals-v2](../rfc/goldpath-approvals-v2.md); owner approved 2026-08-26 and the six rules LANDED 2026-08-27 (withdraw, quorum rungs, chain-wide distinct-eyes, mandatory rejection reason, resubmit chain, signature store seam — 17 new facts, mutation-gated). Still open here: console federation, dashboard JSON, and the adopter proof |
| T22 | **`Goldpath.FileExchange`** — file-based integration rails: scheduled pick-up/drop, validation, idempotent ingestion, reprocessing, archival | Banking file rails (nightly registry FTP batches, 2×/day bank status files, MT940, CSV extraction contracts) recur in THREE of the engagement's common processes; Jobs+Bulk cover pieces but not the rail as a unit. The RFC is ACCEPTED and the build STARTED 2026-08-18 by explicit owner decision (the trigger pulled early — recorded in the RFC header): `Goldpath.FileExchange` core landed with the 7-test planted-fault rig, manifest key + CLI recipe wired. 2026-08-18 (later the same day) the database-backed ledger landed (EF on the app's own DbContext, proven on real storage) and the template flag shipped (a generated app with the flag BUILDS against the train). Open before the thread closes: transport adapters, console federation, dashboard JSON — and the adopter proof below | The first adopter integration that is file-based and bidirectional enters an implementation backlog | One real file rail runs with replay + idempotency + failure reprocessing + its ops runbook, per the module ops-pack shape |
| T23 | **Factoring industry pack + vertical template** — glossary seeds (with false-friend notes), edge-case catalogs, regulatory constraint register skeleton, accounting event dictionary patterns, DMN pricing patterns, integration blueprints (spec + Mockifyr stubs), and a `goldpath new` template pre-setting the domain's bounded-context skeleton | Foundation §6.3's industry-pack layer is the sanctioned home for domain knowledge — the core stays domain-free. The pack is DISTILLED from engagement work, never built up front; the leakage red line holds: public/regulatory knowledge + generalized patterns only, client IP contractually excluded | The engagement's first phase proves the patterns in production AND a second factoring-class prospect appears | The template stands up the domain skeleton via `goldpath new`; the pack's seeds pass the same eval discipline as skills; zero client-confidential content, confirmed by written IP review |

## Closed threads

Moved out of the open table 2026-08-09, after an audit found four CLOSED rows still
sitting in it. That made the register read worse than reality AND buried the two
threads whose triggers had genuinely fired (T1, T7) among rows that were already done:

- **T12** — CorPay's **EOD worker never becomes ready** — CLOSED 2026-08-03.
- **T14** — Four admin responses were **untyped in OpenAPI** ([#98](https://github.com/omercelikdev/goldpath/issues/98)) — CLOSED 2026-08-05.
- **T15** — **Campaign R1 — device-fleet parity** (`ExcludedDays`, `EndDate`, per-item auto-retry ladder, `GlobalTps`) — CLOSED 2026-08-03.
- **T8** — Bulk's **`?definition=` filter** on `/batches` ([#72](https://github.com/omercelikdev/goldpath/issues/72)) — CLOSED 2026-08-03.
- **T19** — **Orchestration composes on top of Goldpath** — CLOSED 2026-08-18. The saga
  non-goal's claim — that the host's atomic outbox, kill-9-survivable run model and
  idempotent retry are exactly what an orchestrator needs — now stands on TEST, not on
  assertion. An adopter-shaped PoC (private repo, binding the published preview.6 train
  like any adopter) composed a MassTransit state machine for a post-payout cancellation
  flow and proved all three host properties: **compensation + duplicate-step idempotency**
  at the saga level; **restart-resume on real Postgres saga storage** (whose first run
  caught a genuine distributed race — a step reply outrunning the saga's INSERT was
  silently discarded; fixed with OnMissingInstance→Fault + retry — the gate-catches-bugs
  thesis, demonstrated on ourselves); and the final leg, **the atomic outbox-join**: the
  saga's state mapped into the app's OWN DbContext (the one carrying the outbox tables),
  with an injected SaveChanges fault proving the saga row and its outbox rows commit or
  roll back in ONE transaction, then commit together on redelivery. GP0401 held the event
  boundary throughout — the analyzer itself forced `IIntegrationEvent` on the saga's
  events at first compile. The thread's proof column had assigned this last leg to the
  rehearsal; the PoC ran it earlier, on the app's own pipeline, which is the stronger
  venue. The rehearsal (T20) still owns the METHOD's end-to-end proof — but the saga
  non-goal no longer owes it anything.


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
