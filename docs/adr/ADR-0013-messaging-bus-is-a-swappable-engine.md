# ADR-0013: The message bus is a swappable engine behind a Goldpath-owned seam

- Status: **accepted** (2026-09-03 — owner decision, recorded the same day in
  [goldpath-messaging-exit](../rfc/goldpath-messaging-exit.md) §4 option F) · Scoped
  amendment of ADR-0003 (configure-not-wrap) for ONE concern; composes with ADR-0005
  (the seam has an enforcing analyzer), ADR-0008 (no default flip while the GM matrix is
  red) and ADR-0012 (the bus library is a separate open-source repository that Goldpath
  binds to as a published package, never as source)

## Decision

1. Application code — a generated app, a sample, a product module — names **no messaging
   library type**. It publishes through `IIntegrationEventPublisher` (shipped 2026-08-10)
   and consumes through a Goldpath-owned handler contract; registration, outbox/inbox
   configuration and the test harness are Goldpath types too. An analyzer forbids the
   library's namespaces in application code the way GP0404 already forbids
   `IPublishEndpoint` injection.
2. The library behind that seam is an **adapter**, and there are two of them: the
   MassTransit 8.x adapter (today's engine, Apache-2.0, pinned) and a **Goldpath-family
   bus** developed as its own open-source repository under the qorpe organisation, in
   parallel with the sync product, never on the accelerator's critical path.
3. A **bus conformance suite** — transport-agnostic scenarios with MassTransit 8.5.10 as
   the oracle — is the definition of "a bus that Goldpath can run on". The default adapter
   flips only when the suite, the differential run and the shadow lane in the RFC's §7
   have all passed; the flip is reversible by the same proof run the other way.
4. The family bus is written **clean-room**: MassTransit 8.x (Apache-2.0) is the declared
   behaviour reference — its documentation, its issue history and its observable behaviour
   under the suite — and no code is derived from it. The commercially licensed v9 source is
   never consulted. This is stated in the library's README, not hidden.

## Rationale

ADR-0003's rule ("what the ecosystem provides is not rewritten") assumed the ecosystem
keeps providing it. The messaging library's maintained line moved to a commercial licence
and its free line has a finite maintenance window; the RFC measured that a forced
migration would be a MAJOR version because our public API and every adopter's consumers
named the library. The owner's position is that the platform must own its own runtime
compatibility rather than depend on a vendor's licensing calendar — and that the honest
way to claim "as good as the library we replace" is a scenario corpus both engines pass,
not an opinion. The seam and the suite are the parts every option needs; the family bus
is the part the owner chose to build once they exist. The exception is deliberately narrow:
it covers the bus engine only — logging, health, resilience, OTel, EF, Aspire stay composed
exactly as ADR-0003 says.

## Consequences

- `Goldpath.Messaging` becomes seam + adapters; the manifest keeps `providers.broker` and
  gains an engine choice the RFC defines. A manifest without messaging still has NO bus code
  (ADR-0001).
- The consume seam and the suite land first (September 2026); the library's own RFC and
  repository come after, on the sync product's timeline; the default flip is a train
  boundary with an upgrade guide and the rollback proven.
- ADR-0003 stays accepted; this ADR narrows it for one concern and says why. Any second
  such exception needs its own ADR with the same measured reasoning — this is not a
  precedent for wrapping the Microsoft layer.
