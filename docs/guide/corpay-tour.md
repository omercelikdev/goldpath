# The CorPay tour — a bank's payment platform, built only with the public verbs

`samples/corpay` is not demo-ware: it was built exactly the way an adopter would build
(published packages, public CLI verbs, the skills), and every place the platform fell
short went into [the gap ledger](../../samples/GAP-LEDGER.md) — four findings became
same-day fixes, four became issues. This tour walks what's there and why.

## How it was born (reproducible)

```bash
goldpath new solution -n CorPay -o . --db postgresql --broker rabbitmq --auth openid \
  --features multitenancy --features audittrail --features softdelete \
  --features idempotency --features dataprotection --features caching \
  --features locking --features archival --features bulk
goldpath add worker payments --trigger queue     # payment instruction consumer
goldpath add worker eod --trigger jobs           # EOD reconciliation fleet
goldpath db add AddWorkers
```

## The five slices (finance card: docs/scenarios/finance-payments.md)

1. **Single instruction** — `POST /api/v1/payment-instructions`: validation table
   (amount, IBAN shape, currency whitelist, per-tenant duplicate reference), the
   core-banking PORT (`ICoreBankingClient`; dev impl pays instantly), and ONE
   transaction around persist → execute → outboxed `PaymentExecuted` → the consumer's
   ledger-feed row. IBANs are classified (`[GoldpathPersonalData]`) — masked in audit
   rows and logs. The "pays once" rule is DATABASE-enforced: a unique
   `(TenantId, Reference)` index backs the fast-path checks.
2. **Batch file** — a bulk definition whose validate stage reuses the SAME rule table
   (one contract, two intakes); the row handler moves money inside the chunk's batched
   save; a replayed row never pays twice.
3. **Four-eyes** — at/above `PaymentPolicy.FourEyesThreshold` NEITHER intake moves
   money on submit; `approve` demands a DIFFERENT authenticated person, `reject`
   demands a reason. Both are evidence (row stamps + audit change rows).
4. **The treasurer's reads** — keyset-paginated list (`?status=PendingApproval` is the
   approver's worklist) and the day report: per-state counts, executed total, ledger
   rows — the same two numbers EOD reconciles.
5. **EOD reconciliation** — banking days only (a Quartz business-days calendar), 23:30
   start + 7.5 h deadline so the 07:00 breach is PREDICTED; one chunk per tenant so a
   poisoned tenant never blocks the rest; executed-but-never-fed lands in the repair
   queue; replay re-checks a single reference. The worker reads api-owned tables via
   the map-don't-own idiom (`ExcludeFromMigrations`).

## What the build itself proved

23 spec-derived unit tests through the REAL save pipeline, `goldpath check` green after
every slice, smoke on the real AppHost (api + two workers + postgres + rabbitmq +
redis), a nightly job that keeps it all green against the published train — and
[NFR.md](../../samples/corpay/NFR.md) mapping the card's performance demands to where
each proof lives, including the honest line about what belongs to adopter staging.
