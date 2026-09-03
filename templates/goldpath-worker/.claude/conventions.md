# Conventions — GoldpathWorker

- Language: code, identifiers, commits in English.
- Consumers: one class per message (`<Message>Consumer`), inbox-guarded; the body commits
  its result in the same transaction as the dedup bookkeeping. Idempotent by design anyway.
- Jobs: one class per job (`<Name>Job : IGoldpathJob`), chunk-shaped — plan by COUNT,
  execute a chunk per call, never loop inside a chunk. Every job declares a deadline.
- Contracts: broker-bound records implement `IIntegrationEvent`; never share entity types
  over the wire.
- Headers: use `GoldpathHeaders` constants (tenant, correlation); never string literals.
- The management head carries probes, admin surfaces and smoke-visible read models only —
  business APIs belong to a solution head.
- Tests: xunit; smoke tests drive the real AppHost (containers), no mocks for the happy path.
