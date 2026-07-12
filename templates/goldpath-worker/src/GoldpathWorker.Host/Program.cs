#if (UseQueue)
using GoldpathWorker.Host.WorkItems;
using MassTransit;
using Microsoft.EntityFrameworkCore;
#endif
#if (UseSchedule)
using GoldpathWorker.Host.Jobs;
#endif
#if (UseJobs)
using GoldpathWorker.Host.Reports;
using Microsoft.EntityFrameworkCore;
#endif

// A web host on purpose: readiness/liveness probes are the deployment contract of a worker
// too — the HTTP surface carries probes (and a smoke-visible read model), never business APIs.
var builder = WebApplication.CreateBuilder(args);

builder.AddGoldpathServiceDefaults();

#if (UseQueue)
// Connection strings come from the AppHost; configuration stays tolerant, usage fails loudly.
var workDbConnection = builder.Configuration.GetConnectionString("workdb");
builder.AddGoldpathData<WebApplicationBuilder, WorkDbContext>(options =>
{
    // No connection (`dotnet ef` design time): the PROVIDER still binds — the model
    // needs it to exist; nothing connects until a real string is used.
#if (UsePostgres)
    if (workDbConnection is not null)
    {
        options.UseNpgsql(workDbConnection);
    }
    else
    {
        options.UseNpgsql();
    }
#endif
#if (UseSqlServer)
    if (workDbConnection is not null)
    {
        options.UseSqlServer(workDbConnection);
    }
    else
    {
        options.UseSqlServer();
    }
#endif
});

builder.AddGoldpathMessaging(bus =>
{
    bus.AddConsumer<WorkItemQueuedConsumer>();
    // Consumer-side INBOX: every receive endpoint dedups on MessageId — exactly-once processing.
    bus.AddGoldpathOutbox<WorkDbContext>(outbox =>
    {
#if (UsePostgres)
        outbox.UsePostgres();
#endif
#if (UseSqlServer)
        outbox.UseSqlServer();
#endif
    });
    bus.UsingRabbitMq((context, cfg) =>
    {
        if (builder.Configuration.GetConnectionString("messaging") is { } messagingConnection)
        {
            cfg.Host(new Uri(messagingConnection));
        }

        cfg.ConfigureGoldpathEndpoints(context);
    });
});
#endif
#if (UseSchedule)
builder.Services.AddSingleton<IntervalJob>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<IntervalJob>());
#endif
#if (UseJobs)
// Connection strings come from the AppHost; configuration stays tolerant, usage fails loudly.
var reportsDbConnection = builder.Configuration.GetConnectionString("workdb");
builder.AddGoldpathData<WebApplicationBuilder, ReportsDbContext>(options =>
{
    // No connection (`dotnet ef` design time): the PROVIDER still binds — the model
    // needs it to exist; nothing connects until a real string is used.
#if (UsePostgres)
    if (reportsDbConnection is not null)
    {
        options.UseNpgsql(reportsDbConnection);
    }
    else
    {
        options.UseNpgsql();
    }
#endif
#if (UseSqlServer)
    if (reportsDbConnection is not null)
    {
        options.UseSqlServer(reportsDbConnection);
    }
    else
    {
        options.UseSqlServer();
    }
#endif
});

// Clustered jobs (Goldpath.Jobs): exactly-once firing across instances, checkpointed runs that
// RESUME after a kill, live progress + deadline prediction.
builder.AddGoldpathJobs<WebApplicationBuilder, ReportsDbContext>(jobs =>
{
    jobs.ConnectionName = "workdb";
    jobs.AddJob<NightlyReportJob>(j =>
    {
        j.Cron = "0 0 1 * * ?";                    // nightly at 01:00
        j.Deadline = TimeSpan.FromHours(2);        // every job has an SLA (GP1302)
        j.MaxParallelChunks = 2;
    });
});
#endif

var app = builder.Build();

app.MapGoldpathDefaultEndpoints();
#if (UseQueue)
// Smoke-visible read model (what has been processed) — intentionally the only endpoint.
app.MapGet("/api/v1/processed", async (WorkDbContext db) =>
    await db.ProcessedWorkItems.OrderBy(w => w.ProcessedAt).ToListAsync());

// Skip schema work when no database is wired (e.g. tooling runs outside the AppHost).
if (app.Environment.IsDevelopment() && workDbConnection is not null)
{
    // Development only (GP0302): real environments apply the CI migration bundle —
    // and Development walks the SAME migrations (migrations RFC D2).
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<WorkDbContext>();
    await db.Database.MigrateAsync();
}
#endif
#if (UseSchedule)
// Smoke-visible tick counter — the schedule equivalent of "the message arrived".
app.MapGet("/api/v1/ticks", (IntervalJob job) => new { count = job.TickCount });
#endif
#if (UseJobs)
// The fleet's ops surface (§7.1): trigger/pause/reschedule/runs/audit — every verb audited.
// Put it behind the auth floor before exposing beyond the cluster boundary.
app.MapGoldpathJobsAdmin<ReportsDbContext>();

// Skip schema work when no database is wired (e.g. tooling runs outside the AppHost).
if (app.Environment.IsDevelopment() && reportsDbConnection is not null)
{
    // Development only (GP0302): real environments apply the CI migration bundle —
    // and Development walks the SAME migrations (migrations RFC D2).
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ReportsDbContext>();
    await db.Database.MigrateAsync();
}
#endif

app.Run();
