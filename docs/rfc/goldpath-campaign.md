# Module RFC: Goldpath.Campaign (L4 — paced, multi-day, policy-governed fan-out)

> Status: v1.0 ACCEPTED — D1–D8 approved by Ömer (2026-07-09; D3 carries the
> single-leader implementation note below). Trigger: a telco-scale device-management (MDM) case
> (30M+ devices, paced bulk operations, screen-trackable by construction). The six design
> constraints from that analysis (goldpath-jobs.md §12) are BINDING inputs, not suggestions.
> Module excellence bar applies: §6–§8 are load-bearing. Effort L (S1–S3) — the LAST rung
> of the execution ladder; everything below it is shipped and proven.

## 0. Position on the ladder — and what is genuinely new

L4 = L2's run model + a POLICY ENGINE + broker fan-out (L1 consumers) + a counter store:

| Ingredient | Status |
|---|---|
| Durable run/item truth, checkpoint/resume, repair queue, console verbs | **Goldpath.Jobs — shipped** |
| Work distribution to competing consumers, inbox dedup | **Goldpath.Messaging (MassTransit) — shipped** |
| Fast counters | **Goldpath.Caching (Redis) — shipped** |
| Per-target message discipline (if a campaign SENDS to humans) | **Goldpath.Notification channel seam — shipped** |
| **Pacing policy (windows / quotas / TPS), leadership, live throttle, outcome aggregation at scale** | **THIS module** |

The genuinely new core is small and sharply bounded: a LEADER that releases work to the
broker no faster than policy allows, and an OUTCOME pipeline that keeps 30M item states
truthful without 30M per-item writes.

## 1. Scope / Non-Goals

**Scope:** runtime-created campaign instances over code-registered campaign types,
streaming target enumeration with a mandatory ceiling, paced release to MassTransit
consumers (windows + daily quota + TPS + max-in-flight), consumer-side claim-before-
external-call, batched outcome aggregation, live policy verbs (pause/resume/abort/
throttle), progress/ETA that an operator can trust, admin API + ops pack.

**Non-goals (deferrals with triggers):**
- Kafka rider (trigger: an inventory whose scale RabbitMQ cannot serve — the MassTransit
  rider seam is the plan of record, constraint 5/6; v1 ships RabbitMQ).
- Cross-campaign global rate governance (per-PROVIDER ceilings shared by campaigns —
  trigger: the first tenant running concurrent campaigns against one SMS gateway).
- Audience/segmentation tooling (the app's selector answers "who"; marketing segmentation
  is a product, not a module).
- A/B testing, multi-step journeys (drip sequences) — campaign orchestration products
  exist; we ship the enterprise DELIVERY layer they all lack.
- Device-protocol specifics from the MDM doc (OMA-DM etc.) — the consumer's domain.

## 2. Seam Map (the six constraints, made concrete)

- **Type vs instance (D1):** a campaign TYPE is code — `AddCampaign<TTarget>("mdm-config-push")`
  binds a streaming target SELECTOR (keyset-paged enumeration out of the app's own data,
  with a MANDATORY `MaxTargets` ceiling) and the consumer-side handler contract. A
  campaign INSTANCE is a ROW created at runtime through the admin API: which type, the
  selector's parameters, the policy, the schedule window. Operators launch campaigns;
  developers ship campaign types through PRs.
- **Leadership without clustered ticks (D2, constraint 1):** the PACER is a LONG-LIVED
  Goldpath.Jobs run — the cron (~1 min) only ensures a pacer exists; once running, the leader
  loops IN-MEMORY (sub-second ticks cost nothing locally), releasing micro-batches to the
  broker while policy allows, extending its run heartbeat, checkpointing released-through
  watermarks per campaign. Death → the next fire takes over from the checkpoint (the
  bulk-adopt pattern, proven). No per-second Quartz cluster locks — constraint 1 honored
  by construction.
- **Counters are cache, tables are truth (D3, constraint 3):** released/sent/failed
  counts and TPS windows live in Redis (Goldpath.Caching) for O(1) pacing decisions; the
  durable campaign/item tables remain the truth; the leader RECONCILES counters from the
  tables on warmup/takeover. Redis loss slows one warmup, corrupts nothing.
- **Fan-out and claim (D4, constraints 2/5/6):** the pacer publishes item batches through
  MassTransit (ONE messaging stack); competing consumers CLAIM the item row (state-guarded
  update) BEFORE any external call — a rebalance mid-item repairs, never double-sends
  (the discipline shipped in bulk/notification, now at L4 scale). Inbox dedup guards
  redelivery.
- **Outcomes without per-item hammering (D5, constraint 4):** consumers report outcomes
  as events; a SINK consumer buffers and flushes BATCHED durable updates (single UPDATE …
  WHERE Id IN (…) per flush) and bumps the fast counters. Per-item terminal evidence
  survives (who/when/what per target); write amplification does not.
- **Policy engine (D6):** window (start/end + timezone; outside it the pacer sleeps),
  daily quota, TPS ceiling, max-in-flight; ALL adjustable live on a RUNNING campaign
  (throttle down a screaming gateway without aborting the night). Pause/resume/abort are
  audited verbs; abort drains claims gracefully.
- **Human sends compose Notification's seam:** an item handler that messages a person
  SHOULD go through `IGoldpathNotificationChannel`/`IGoldpathNotifier` — evidence discipline is
  already built; campaign does not reinvent it (guidance + sample, not enforcement).

## 3. Manifest Surface

```yaml
features:
  campaign: true      # REQUIRES a broker — the schema rule enforces it (fan-out is the point)
```

Schema key joins WITH S3, plus a cross-field rule: `campaign` without a broker is a
validation error (like outbox). Drift rows: the guard pair (`Goldpath.Campaign`/`AddGoldpathCampaign`
+ `Goldpath.Jobs`/`AddGoldpathJobs`) — and `Goldpath.Messaging` presence rides the broker rule.

## 4. API Surface

```csharp
builder.AddGoldpathCampaign<WebApplicationBuilder, OrdersDbContext>(campaign =>
{
    campaign.AddCampaign<DeviceTarget>("mdm-config-push", c => c
        .MaxTargets(50_000_000)                              // the ceiling is a decision (GP1701)
        .Targets((services, parameters) => services.GetRequiredService<OrdersDbContext>()
            .Set<Device>()                                   // streaming keyset enumeration
            .Where(d => d.FirmwareVersion < parameters["minVersion"])
            .OrderBy(d => d.Id)                              // STABLE order: takeover resumes by count
            .Select(d => new DeviceTarget(d.Id, d.PushToken))
            .AsAsyncEnumerable())
        .DefaultPolicy(p => { p.Tps = 200; p.DailyQuota = 2_000_000; p.Window("09:00", "21:00", "Europe/Istanbul"); }));
});
builder.Services.AddScoped<IGoldpathCampaignItemHandler<DeviceTarget>, ConfigPushHandler>();

// Jobs wiring:  jobs.AddGoldpathCampaignJobs<OrdersDbContext>();   // pacer (leader) + reconciler
// Messaging:    bus.AddGoldpathCampaignConsumers<OrdersDbContext>();
// DbContext:    modelBuilder.AddGoldpathCampaign();  modelBuilder.AddGoldpathJobs();
```

**Admin API** (`/goldpath/admin/campaign`, every verb audited): create/schedule an instance
from a registered type (parameters + policy), campaign list/detail (released/succeeded/
failed/remaining, current TPS vs ceiling, ETA under current policy, window state),
pause/resume/abort, LIVE throttle (policy patch on a running campaign), item drill-down
(failed items delegate replay to the jobs console — same repair discipline).

## 5. Analyzer Rules (GOLDPATH17xx — new block)

- **GP1701 (error):** a campaign type without `MaxTargets` — an unbounded enumeration
  at L4 scale is an outage, not a campaign.
- **GP1702 (warning):** an item handler calling `SaveChanges` directly — outcomes flow
  through the sink (constraint 4); per-item saves at 30M melt the database.
- **GP1703 (info):** a campaign type whose handler messages humans without the
  notification seam — evidence discipline exists; bypassing it should be visible.

## 6. Performance (measured, not promised)

Reference profile, `scripts/bench-campaign.sh` → `ops/campaign-benchmarks.md`:
- Enumeration + item materialization: 1M targets (rows/s, memory flat).
- Pacer precision: configured TPS 200 → measured release rate within ±5% over a minute;
  throttle to 50 mid-run takes effect within one leader tick.
- Outcome sink throughput: 100k outcomes batched-flushed (rows/s, flush latency).
- End-to-end 100k-item campaign on RabbitMQ: wall time, exactly-once-or-repair
  reconciliation (sink counts == durable counts == consumer executions).
- Counter warmup: reconcile 1M-item campaign state from tables (seconds).

## 7. Observability (shipped)

`Goldpath.Campaign` meter: released/succeeded/failed totals per campaign, CURRENT TPS vs
ceiling (the governor panel), in-flight gauge, remaining + ETA-at-current-policy, window
state, quota consumption, sink flush lag, counter-reconcile events. Grafana board: the
operator's single screen per campaign (progress, rate governor, failure rate, ETA) —
"screen-trackable by construction" is the case's own bar. Runs ride the jobs dashboard.

## 8. Operational (runbook or it didn't happen)

1. Campaign not progressing (window closed? paused? leader dead → takeover; broker
   backlog vs consumer health).
2. Gateway screaming / provider rate-limits (LIVE throttle verb — the whole point;
   confirm on the TPS panel before and after).
3. Failure rate climbing (pause, drill into failed items, fix the world, resume; item
   replay via the jobs console).
4. Leader takeover after a crash (checkpointed watermark + counter warmup reconcile —
   what the operator sees and how long warmup takes at 1M/30M).
5. Redis loss mid-campaign (pacing degrades to reconcile-on-warmup; truth intact —
   the drill that proves constraint 3).
6. Abort semantics (drain in-flight claims, terminal-state the remainder as Aborted —
   evidence of what was NOT sent is evidence too).

## 9. Test Plan

- **Unit:** policy math (window/timezone edges, quota rollover at midnight tenant-time,
  TPS token accounting), pacer batch-size decisions, type registration guards, EF model
  contract, entity defaults, golden plan/watermark payloads.
- **Mutation:** ≥ 70 break, the standard config.
- **Integration (pg + RabbitMQ, real containers):** a 10k-item campaign end to end —
  create via admin → pacer releases under TPS → consumers claim + execute → sink
  reconciles EXACTLY (no item lost/double-executed); kill the pacer mid-campaign →
  takeover resumes from the watermark with counter reconcile; LIVE throttle observably
  changes the release rate; pause/resume/abort; window boundary honored; a poisoned
  item repairs + replays.
- **Bench:** §6 numbers.

## 10. Slices & DoD

- **S1 — the engine:** model (campaign/item/watermark), type registration, streaming
  enumeration, LONG-LIVED leader pacer with policy + counters + reconcile, MassTransit
  fan-out + consumer claim + outcome sink, kill/takeover proof, bench baseline.
- **S2 — the ops surface:** admin API (create/pause/resume/abort/throttle, audited) +
  the operator panel + runbooks + GP1701-1703.
- **S3 — the manifest word:** `features.campaign` + the broker cross-field rule +
  template sample + CLI recipe (JobsOptionsLines seam, fourth rider) + GM (broker'lı
  shape) → module closes → **the execution ladder is COMPLETE**.
- DoD: the case's architecture answered line by line; excellence-bar artifacts present;
  ledger updated.

## 11. Decision Points (Ömer)

- **D1 — Types are code, instances are data:** developers ship campaign TYPES through
  PRs (selector + handler + ceiling + default policy); operators create/schedule
  INSTANCES at runtime through the audited admin API. Accept?
- **D2 — The pacer is a LONG-LIVED jobs run** (leader loops in-memory, no per-second
  cluster ticks — constraint 1; cron only guarantees existence; takeover from the
  checkpointed watermark). Accept?
- **D3 — counters are CACHE, tables are truth, warmup reconciles** (constraint 3).
  Implementation note (S1): pacing is SINGLE-LEADER, so the fastest correct cache tier
  is the leader's own memory (one writer, zero network); takeover reconciles from the
  tables exactly as the constraint demands. A shared-Redis implementation slots behind
  the same `IGoldpathCampaignCounters` seam when the cross-campaign/global-governance
  trigger fires — Goldpath.Caching's HybridCache is a get-or-create cache, not an atomic
  counter store, and misusing it would fake the constraint rather than honor it.
  ACCEPTED (with this note, 2026-07-09). S1 !52, S2 !53, S3 shipped 2026-07-10 — module COMPLETE; the execution ladder is closed.
- **D4 — MassTransit/RabbitMQ v1; Kafka rider deferred** with the written 30M-scale
  trigger (constraints 5/6). Accept?
- **D5 — Outcomes flow through a batching SINK consumer** (constraint 4): per-item
  terminal evidence kept, per-item write amplification not. Accept?
- **D6 — Policy = window(+timezone) / daily quota / TPS / max-in-flight, ALL live-
  adjustable on a running campaign;** pause/resume/abort audited. Accept?
- **D7 — GP1701-1703** (ceiling-less type error; SaveChanges-in-handler warning;
  human-send-without-notification-seam info). Accept?
- **D8 — `features.campaign` REQUIRES a broker** (schema cross-field rule, like outbox);
  the schema key lands WITH S3. Accept?
