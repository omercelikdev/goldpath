# Goldpath.Campaign

Governed mass-execution — **L4 of the execution ladder**, the top rung. A campaign is a
durable plan: targets are enumerated ahead into rows, then RELEASED to competing broker
consumers at a pace the operator governs live:

```
create → enumerate (streaming, ceilinged) → release under policy → consumers execute
              │                                  │                        │
              │ watermark checkpoint             │ TPS / daily quota /    │ claim-before-
              │ (takeover-safe)                  │ window / max-in-flight │ execute, then
              │                                  │ (all LIVE-adjustable)  │ publish outcome
              └── ceiling hit → PAUSED           └── single leader,       └── batching sink
                  (a decision, not a default)        in-memory ticks          writes truth
```

The pacer is a LONG-LIVED leader run on Goldpath.Jobs: the ~1-minute cron only guarantees a
leader exists; pacing happens on in-memory ticks (250ms default) against durable
watermarks — leader death means the next fire takes over from the row, mid-campaign.

## Wire it

```csharp
builder.AddGoldpathCampaign<WebApplicationBuilder, OrdersDbContext>(campaign =>
{
    campaign.AddCampaign<DormantCustomer>("winback", c => c
        .MaxTargets(2_000_000)                 // MANDATORY — unbounded L4 is an outage
        .Targets((services, parameters) => services.GetRequiredService<OrdersDbContext>()
            .Customers.Where(x => x.LastOrderAt < DateTime.Parse(parameters["before"]))
            .OrderBy(x => x.Id)                // stable order — the watermark depends on it
            .Select(x => new DormantCustomer(x.Id, x.Email))
            .AsAsyncEnumerable())
        .DefaultPolicy(p => p with { Tps = 100, DailyQuota = 500_000, MaxInFlight = 2_000 }));
});
builder.Services.AddScoped<IGoldpathCampaignItemHandler<DormantCustomer>, WinbackHandler>();

builder.AddGoldpathJobs<WebApplicationBuilder, OrdersDbContext>(jobs =>
{
    jobs.ConnectionName = "ordersdb";
    jobs.AddGoldpathCampaignJobs<OrdersDbContext>();          // the pacer leader
});

builder.AddGoldpathMessaging<WebApplicationBuilder>(bus =>    // campaign REQUIRES a broker
{
    bus.AddGoldpathCampaignConsumers<OrdersDbContext>();
});

// DbContext: modelBuilder.AddGoldpathCampaign();  modelBuilder.AddGoldpathJobs();
```

## The guarantees

- **The ceiling is mandatory:** a type without `MaxTargets` refuses to bake (GP1701);
  an enumeration that exceeds it PAUSES the campaign for a human decision.
- **Policy is live:** TPS, daily quota, send window (timezone-aware, overnight windows
  supported) and max-in-flight are row values the pacer re-reads every tick — a throttle
  takes effect within one tick, no restart, no redeploy.
- **Double-send is structurally impossible:** the consumer CLAIMS the item row
  (state-guarded update) before the handler's external call; a broker redelivery claims
  zero rows and drops. Claimed-but-unstamped items are swept to the repair queue —
  confirm with the provider, then replay; never silently re-send.
- **30M items never mean 30M writes:** outcomes are published, batched by the sink, and
  flushed as set-based updates; enumeration and release are batched the same way.
- **Repair, not requeue:** every failed item lands in the jobs repair queue with its
  coordinate (`{campaign}#{seq}`); `replay-items` re-executes through your handler.
- **Takeover-safe:** enumeration and release advance durable watermarks; a new leader
  resumes exactly where the dead one stopped.

Ops surface (create/pause/resume/abort/throttle admin API, per-campaign panel, runbooks)
ships in S2; run views live in the JOBS console today — the pacer IS a jobs run.
