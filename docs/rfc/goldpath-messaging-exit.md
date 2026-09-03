# RFC: the messaging dependency — exposure, and the exit that is not thin

- Status: **ACCEPTED** — option **A** (stay pinned on the 8.x line), owner-decided 2026-08-10,
  with **D** as the named fallback and the triggers below as the only things that move us
- Date: 2026-08-09 (decided and §5 implemented 2026-08-10)
- Supersedes the recommendation in [goldpath-messaging](goldpath-messaging.md) §9 D1
- Ledger: issue [#11](https://github.com/omercelikdev/goldpath/issues/11) (owner-prioritized),
  master plan "Open, with an owner" item 10

## 1. Scope / Non-Goals

**Scope.** Decide how Goldpath responds to MassTransit's licence change, with the real
exposure measured rather than assumed. Define what an exit would cost, what would have to
be true before we take one, and what we do NOW so the cost stops growing.

**Non-goals.** This RFC **moves no dependency**: MassTransit 8.5.10 stays pinned exactly
where it was. It does not choose a replacement library — that is a second RFC, opened only
if a trigger below fires — and it does not change the manifest.

*(As drafted this section also said the RFC ships no code. §5 was then decided and
implemented, so it ships exactly one thing: the publish seam. Corrected here rather than
left to read as a promise we broke.)*

## 2. Seam Map — where MassTransit actually is (measured 2026-08-09)

§9 D1 of the messaging RFC recorded a comforting claim:

> "the Goldpath surface (`AddGoldpathMessaging`, filters, conventions) is our own thin
> layer, so a future move … changes the composition, **not consumer code**."

**That claim is false, and it is the most important thing in this RFC.** Three greps:

| Surface | What is there | Why it matters |
|---|---|---|
| **The template** — every generated app | `CreateOrderHandler(IOrdersDbContext db, IPublishEndpoint publisher)`; `OrderPlacedConsumer : IConsumer<OrderPlaced>` with `Consume(ConsumeContext<OrderPlaced>)` | This IS consumer code. A generated app's own handlers name MassTransit types on day one. |
| **Our frozen public API** | `PublicAPI.Shipped.txt` of `Goldpath.Campaign` exposes `Consume(MassTransit.ConsumeContext<…>)` and `AddGoldpathCampaignConsumers(this MassTransit.IBusRegistrationConfigurator)`; `Goldpath.Messaging` exposes `GoldpathConsumeFilter<T>.Send(MassTransit.ConsumeContext<T>, MassTransit.IPipe<…>)` | These are SHIPPED public signatures. Changing them is a major version under our own versioning contract, not an internal swap. |
| **The reference app** | 79 MassTransit/`IPublishEndpoint`/`IConsumer` references in `samples/corpay/src` | The proof that the template's shape is what adopters actually write. |

The coupling is not an accident: ADR-0003 says what the ecosystem provides is **composed,
not rewritten**, and composing means naming the library's types. That was the right call and
it has a bill; this RFC is the bill arriving, not the decision being wrong.

**What is genuinely ours** (would survive any move unchanged): the `IIntegrationEvent`
boundary marker, the tenant/correlation header contract, the outbox's atomicity guarantee,
the analyzer rules GP0401-0403, and the manifest's `providers.broker` vocabulary.

## 3. Manifest Surface

Unchanged. `providers.broker` keeps `rabbitmq | kafka | inmemory | none` with today's
honesty annotations (kafka and inmemory are SCHEMA-ONLY). A future transport change is a
composition detail behind the same manifest key — that part of D1's claim IS true.

## 4. API Surface — the options, with what each actually costs

| # | Option | What it costs | When it is right |
|---|---|---|---|
| **A** | **Stay on 8.x, pinned** (today's position: 8.5.10, Apache-2.0) | Nothing now. Accepts that fixes stop when the v8 maintenance window closes (~end 2026 per the vendor's own statements) | Until a trigger fires. **This is the recommendation.** |
| **B** | **Buy v9** | Unknown — no list price is published anywhere, which is itself a procurement finding: an enterprise buyer cannot budget a dependency whose cost they cannot look up | Only if an adopter already holds a licence |
| **C** | **Fork v8** | We become the maintainer of a message bus. Directly against ADR-0003 (do not rewrite what the ecosystem provides) and against our own staffing reality | Never, on today's team |
| **D** | **Move to Wolverine** (MIT; JasperFx has publicly declined to commercialize it) | A MAJOR version: our public API changes, the template's handlers change, every adopter's consumers change. Needs a migration guide and the `upgrade` skill that does not exist yet | If v8 stops being safe to ship |
| **E** | **Grow Mediant toward transport** (issue #11's GO/NO-GO) | The largest: we would own a transport. Same ADR-0003 objection as C unless the scope stays tiny (in-process + one broker) | Only with real usage data showing how little of MassTransit we consume |
| **F** | **Seam + conformance suite now; a Goldpath-family bus as its own open-source library, swapped in only when the suite proves it** (added 2026-09-03) | Three bills, in order. (1) The CONSUME seam — a Goldpath-owned handler contract, registration, outbox/inbox configuration and test harness, plus an analyzer banning the library's namespaces from application code: 3–4 days, and a MAJOR version once (the same major D would cost — paid once, for every future engine). (2) The **bus conformance suite** — 60–100 transport-agnostic scenarios with MassTransit 8.5.10 as the oracle, mined from the library's public issue history and our own edge cases: 1–2 weeks. (3) The library itself, scoped to what Goldpath consumes (RabbitMQ transport, envelope serialisation, consumer pipeline with retry + error queue, EF outbox/inbox, tenant/correlation headers, OTel, in-memory test transport, Aspire wiring — NOT sagas, routing slips or other transports): 4–6 weeks to a first working adapter with Claude Code, then a shadow lane of ≥4 weeks before any default flips. Written clean-room with the 8.x line as the declared behaviour reference (ADR-0013) | **Chosen 2026-09-03.** (1) and (2) are needed by EVERY exit and by option A itself (they make the seam honest); (3) runs beside the sync product, never on the accelerator's critical path, and flips the default only through §7's switch proofs |

**Recommendation as of 2026-08-10: A now, with D as the named fallback** — and the honest
sentence a client must hear is *"a migration would be a major version, not a patch"*,
because the measurement above says so.

**Decision 2026-09-03 — option F, recorded as [ADR-0013](../adr/ADR-0013-messaging-bus-is-a-swappable-engine.md).**
The owner's reasoning: the platform should own its runtime compatibility rather than
track a vendor's licensing calendar, and "as good as the library we replace" must be a
scenario corpus both engines pass, not an opinion. The staged shape is what keeps that
honest — the seam and the suite come first because they are needed whatever the engine
turns out to be; D (Wolverine) stays the measured fallback if the family bus stalls, and
the suite makes trying it a one-week adapter, not a research project. A stays the shipped
position until the switch proofs pass.

## 5. What we do NOW so the bill stops growing

One rule, no code today, and it is deliberately small:

> **New adopter-facing code does not take a NEW hard dependency on the messaging library
> where a Goldpath seam already exists.**

Concretely, the publish side is the cheap half: a handler that needs to publish could depend
on a Goldpath-owned `IIntegrationEventPublisher` instead of `IPublishEndpoint`, and the
consume side (which genuinely needs the library's pipeline) could stay as it is. That single
narrowing would take the template's per-app exposure from "every command handler" to "only
consumers".

**DECIDED and implemented 2026-08-10** — the owner's argument was the one that settles it:
we have **zero adopters today**, so moving the template's publish line costs nothing; with N
adopters it costs N migrations plus a guide plus the `upgrade` skill that does not exist.
The asymmetry is the whole argument.

What shipped:

- `IIntegrationEventPublisher` in `Goldpath.Messaging`, registered by `AddGoldpathMessaging`,
  implemented by a one-line delegation to `IPublishEndpoint`. **Not** a bus abstraction: no
  retry policy, no topology, no routing of our own — ADR-0003 stands. Everything Goldpath
  adds to the publish path already lives in the pipeline filters, where it applies to every
  publish regardless of caller.
- Both template layouts publish through it; `using MassTransit` is gone from the command
  handlers of a generated app.
- **GP0404** (warning) flags application code injecting `IPublishEndpoint`, exempting the
  Goldpath namespace that owns the delegation — the seam has an enforcing mechanism, per
  ADR-0005, so it cannot erode by habit.

**The consume side deliberately stays on the library's types.** A consumer genuinely uses the
pipeline (headers, retry, redelivery, tenant restore); wrapping that would be a leaky
abstraction costing more than the migration it saves. So the honest promise to an adopter is
bounded and true: *if the transport changes, your command handlers do not move; your
consumers do.*

**Sequencing, learned by building it:** CorPay binds to the PUBLISHED train, so it cannot
take the seam until preview.7 carries it — the attempt failed to compile against preview.6,
which is the adopter discipline working exactly as designed. CorPay migrates its four publish
sites when it takes the next train, in the same slice, the way it took preview.6.

## 6. Ops Package

None — this RFC changes no runtime behaviour. If an exit ever happens, its ops obligation is
the migration guide plus the `upgrade` skill (`ai-sdlc-status.md` records it as NOT BUILT),
because a major version without a migration path is how an accelerator loses the adopter it
was supposed to accelerate.

## 7. Test Plan — what an exit would have to prove

Recorded now so the cost is not rediscovered later. A move is not done until:

1. The **outbox atomicity** proof passes on the new transport (the existing Testcontainers
   Postgres test, unchanged in intent).
2. The **tenant + correlation headers** still propagate end to end (H4's trace-correlation
   proof).
3. **GP0401-0403** still mean something — the boundary between in-process notifications and
   integration events survives, or the analyzers are rewritten with it.
4. The **golden-manifest matrix** stays green on every broker-bearing shape.
5. CorPay migrates with a written guide, and the guide is what an adopter would follow.

### 7.1 The switch proofs (added 2026-09-03 with option F)

The owner's question was *"will the switch really be a flip, with nothing breaking?"* It is
— **only** if all six of these have run; short of that the honest word is "migration", not
"flip". Recorded so the day the flip is proposed, the bar is already written.

| # | Proof | What it rules out |
|---|---|---|
| S1 | **Seam completeness** — the analyzer bans the library's namespaces in application code; a generated app (every GM broker-bearing shape), CorPay and api-portal compile UNCHANGED against either adapter | a "flip" that is secretly a code change in every consumer |
| S2 | **Conformance suite green on both adapters** — publish/consume, retry + error queue, outbox atomicity INCLUDING crash between commit and publish, inbox de-duplication, tenant/correlation headers, per-queue ordering, concurrency/prefetch, poison messages, broker restart recovery, graceful shutdown drain, large payloads, message versioning (added property, renamed type), OTel spans | the long tail the library's decade of fixes covers and a rewrite forgets |
| S3 | **Differential run** — the same scenarios on both adapters with observable outcomes compared, mock-engine style (oracle = MassTransit 8.5.10) | "passes our tests" meaning "passes tests we wrote around our own behaviour" |
| S4 | **Mixed-fleet wire compatibility** — old publisher → new consumer and the reverse, on the 8.x envelope and topology; outbox rows written under the old adapter delivered by the new. If the library chooses NOT to be wire-compatible, the flip becomes a drained cutover with a maintenance window, and the upgrade guide must say so in its first line | a rolling deployment that strands in-flight messages |
| S5 | **Shadow lane** — the GM broker shapes, CorPay and api-portal run nightly on the new adapter for ≥4 weeks before the default flips; the library carries its own mutation gate (break 70, like every package) | a default flip on a green afternoon |
| S6 | **Rollback proven** — the flip back to the MassTransit adapter passes S1–S4 the other way, and the upgrade guide carries both directions | a one-way door presented as a switch |

The seam (S1) and the suite (S2/S3) are September 2026 work on the accelerator; S4–S6 are
the library's own DoD and cannot start before it exists.

## 8. DoD — closed 2026-08-10

- [x] Owner decides A–E → **A** (stay pinned on 8.x), fallback **D**. Decided 2026-08-10.
- [x] The false claim in `goldpath-messaging.md` §9 D1 is corrected in place, pointing here.
- [x] The triggers that would move us off A are written into
      [open-threads.md](../strategy/open-threads.md) as **T18**, each with the proof that
      must run before the thread closes.
- [x] The narrowing candidate in §5 is scoped and **shipped** — `IIntegrationEventPublisher`
      plus GP0404, in the same train as this decision (PR #159).

**What is deliberately NOT closed by this RFC**, so nobody reads A as safety: the 8.x
maintenance window is finite, and A is a decision to WATCH, not a decision to relax. T18 is
where that watching lives; if it goes stale, this decision has quietly expired.

**2026-09-03 addendum — the watch became a plan.** Option F chosen (ADR-0013). New DoD
lines, open until each proof runs:

- [ ] The consume seam ships (handler contract, registration, outbox/inbox configuration,
      test harness — all Goldpath types) with the namespace-ban analyzer; the two template
      layouts, CorPay and api-portal consume through it. A MAJOR train boundary with an
      upgrade guide.
- [ ] The bus conformance suite exists in this repo, transport-agnostic, green against the
      MassTransit 8.5.10 adapter, its scenario list reviewed by the owner.
- [ ] The family bus has its own RFC and repository (qorpe organisation, open source,
      clean-room per ADR-0013); its adapter enters the suite.
- [ ] §7.1 S1–S6 pass; the default flips at a train boundary; T18 closes.
