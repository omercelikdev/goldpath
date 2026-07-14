# Event contracts between the api and added workers (ledger G5, event half)

Status: ACCEPTED (2026-07-14). Closes the idiom question issue #33 tracked.

## The decision

**A broker-bound event consumed by more than one process lives in a per-app
`<Name>.Contracts` class library**, referenced by both the publisher and the consumer.
MassTransit matches messages by type identity — sharing the RECORD is the only honest
option; duplicating it invites silent topology drift, and referencing the whole Api
project from a worker drags the entire application graph across the process boundary.

Rules of the Contracts project:
- Records implementing `IIntegrationEvent` (and nothing else — no logic, no EF, no DI).
- It is a WIRE CONTRACT: renames/renames-of-namespace are breaking (they change the
  message urn) and follow the versioning contract like any other break.
- An event consumed ONLY in-process (the walking skeleton's `OrderPlaced`) may stay in
  the Api — the Contracts project earns its existence with the FIRST cross-process
  consumer, and the event MOVES there in that same change (a rename-shaped break while
  still cheap, exactly like CorPay would do for `PaymentExecuted` if its ledger feed
  moved into the payments worker).

## Who teaches it

`goldpath add worker` prints the rule in its next-steps; this document is the depth.
Template generation of the Contracts project is deliberately deferred — creating an
empty project every time would violate "a capability that is off does not exist";
written trigger: the first template shape whose DEFAULT composition crosses processes.
