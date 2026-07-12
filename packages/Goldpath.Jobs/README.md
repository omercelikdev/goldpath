# Goldpath.Jobs

L2 of the Goldpath execution ladder: scheduled and long-running work on a **clustered**
Quartz.NET persistent store, with the Goldpath run model on top — chunked, checkpointed,
resumable runs; per-item failure isolation; live progress and deadline prediction;
completion chaining.

## Shape

```csharp
// Worker (executor — one cluster per worker kind):
builder.AddGoldpathJobs<WebApplicationBuilder, WorkDbContext>(jobs =>
{
    jobs.ConnectionName = "workdb";
    jobs.AddCalendar("banking-tr", GoldpathCalendars.BusinessDays(holidayTable));
    jobs.AddJob<EodReconciliationJob>(j =>
    {
        j.Cron = "0 30 1 * * ?";
        j.Calendar = "banking-tr";
        j.Deadline = TimeSpan.FromHours(5.5);
        j.MaxParallelChunks = 4;
    });
});

// API head (management — verbs over every fleet, executes nothing):
builder.AddGoldpathJobsManagement<WebApplicationBuilder, OrdersDbContext>(jobs => jobs.ConnectionName = "ordersdb");

// DbContext (the store and the run model ride normal migrations):
protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddGoldpathJobs();
```

Author jobs as chunked work:

```csharp
public sealed class EodReconciliationJob(OrdersDbContext db) : IGoldpathJob
{
    public async Task<GoldpathJobPlan> PlanAsync(GoldpathJobContext ctx, CancellationToken ct)
        => GoldpathJobPlanner.ByRange(await db.Payments.CountAsync(ct), chunkSize: 500);

    public async Task ExecuteChunkAsync(GoldpathJobChunk chunk, GoldpathJobContext ctx, CancellationToken ct)
    {
        foreach (var payment in await LoadRangeAsync(chunk.Payload, ct))
        {
            if (!await ReconcileAsync(payment, ct))
            {
                chunk.ReportItemFailure(payment.Id.ToString(), "ledger mismatch");   // repair queue, run continues
            }
        }
    }
}
```

## Guarantees

- **Exactly-once firing across the cluster** (Quartz clustered store + `DisallowConcurrentExecution`).
- **Kill-9 mid-run** → recovery re-fires on a healthy node → the runner **resumes from the
  last checkpoint** (at most one chunk repeats; its claim resets).
- **A poisoned item never stops the night**: item failures isolate into the repair queue;
  a chunk exhausting its attempts isolates too, the rest of the run continues.
- State writes are **batched per chunk**, never per item.

The `qrtz_` schema and the run model are EF model contributions — normal migrations, no
side-channel SQL. Admin API + dashboard arrive with S2/S2b (see docs/rfc/goldpath-jobs.md).
