# RFC: the messaging dependency — exposure, and the exit that is not thin

- Status: **draft** (owner decision required — this RFC decides nothing by itself)
- Date: 2026-08-09
- Supersedes the recommendation in [goldpath-messaging](goldpath-messaging.md) §9 D1
- Ledger: issue [#11](https://github.com/omercelikdev/goldpath/issues/11) (owner-prioritized),
  master plan "Open, with an owner" item 10

## 1. Scope / Non-Goals

**Scope.** Decide how Goldpath responds to MassTransit's licence change, with the real
exposure measured rather than assumed. Define what an exit would cost, what would have to
be true before we take one, and what we do NOW so the cost stops growing.

**Non-goals.** This RFC ships **no code** and moves no dependency. It does not choose a
replacement library (that is a second RFC, opened only if a trigger below fires), and it
does not change the manifest, the template or any package. Nothing an adopter runs today
changes because this document exists.

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

**Recommendation: A now, with D as the named fallback** — and the honest sentence a client
must hear is *"a migration would be a major version, not a patch"*, because the measurement
above says so.

## 5. What we do NOW so the bill stops growing

One rule, no code today, and it is deliberately small:

> **New adopter-facing code does not take a NEW hard dependency on the messaging library
> where a Goldpath seam already exists.**

Concretely, the publish side is the cheap half: a handler that needs to publish could depend
on a Goldpath-owned `IIntegrationEventPublisher` instead of `IPublishEndpoint`, and the
consume side (which genuinely needs the library's pipeline) could stay as it is. That single
narrowing would take the template's per-app exposure from "every command handler" to "only
consumers".

This is a **candidate**, not a decision: it adds an abstraction ADR-0003 is suspicious of,
and it must be judged against "does an adopter benefit today, or only in a migration that
may never come?" It belongs in the second RFC, with the pilot's real usage as evidence.

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

## 8. DoD

- [ ] Owner decides A–E (recommendation: **A**, fallback **D**).
- [ ] The false claim in `goldpath-messaging.md` §9 D1 is corrected in place, pointing here.
- [ ] The triggers that would move us off A are written into `open-threads.md`: an unpatched
      CVE in the 8.x line · the vendor's maintenance window closing · an adopter's licensing
      constraint · a customer requiring a bus we do not compose.
- [ ] The narrowing candidate in §5 is either scoped into a second RFC or recorded as
      declined, with the reason.
