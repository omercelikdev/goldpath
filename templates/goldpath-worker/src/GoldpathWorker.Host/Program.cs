#if (UseSchedule)
// Nested on purpose: the template engine flattens `A && (B || C)` (found generating the
// parity shapes 2026-09-03 — every trigger hit the #error), two levels evaluate correctly.
#if (UseAuditTrail || UseSoftDelete || UseLocking || UseNotification || UseFileExchange)
#error features.auditTrail / softDelete / distributedLocking / notification / fileExchange own tables in the worker's database, and a schedule worker (PeriodicTimer) has none. Regenerate with --trigger queue or --trigger jobs, or drop those features.
#endif
#endif
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
// too — the HTTP surface carries probes, the admin surfaces and a smoke-visible read model,
// never business APIs.
var builder = WebApplication.CreateBuilder(args);

builder.AddGoldpathServiceDefaults();
//#if (UseApiKey)
builder.AddGoldpathAuth(o => o.Strategy = GoldpathAuthStrategy.ApiKey);   // the management head demands a principal; probes stay anonymous
//#elif (UseAuth)
builder.AddGoldpathAuth();   // OpenId: set Goldpath:Auth:Authority AND Audience in configuration (authority without audience refuses to start — A3)
//#endif

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
#if (UseSqlServer)
    jobs.Provider = GoldpathJobStoreProvider.SqlServer;
#endif
    jobs.AddJob<NightlyReportJob>(j =>
    {
        j.Cron = "0 0 1 * * ?";                    // nightly at 01:00
        j.Deadline = TimeSpan.FromHours(2);        // every job has an SLA (GP1302)
        j.MaxParallelChunks = 2;
    });
//#if (UseNotification)
    jobs.AddGoldpathNotificationJobs<ReportsDbContext>();   // send (frequent) + body-retention (nightly)
//#endif
//#if (UseFileExchange)
    // your pick-up job rides here (the transport is yours — SFTP/share/object store is composed, not shipped):
    // jobs.AddJob<RegistryPickupJob>(j => { j.Cron = "0 0 6 * * ?"; j.Deadline = TimeSpan.FromMinutes(30); });
//#endif
});
#endif

// goldpath:features registrations — the drift profile is the source of these rows.
// WorkerDbContext is the trigger's own context (GlobalUsings.cs), so a feature row reads
// the same whichever trigger the worker has.
//#if (UseMultiTenancy)
builder.AddGoldpathMultiTenancy();                  // the head resolves the tenant; consumers restore it from message headers
//#endif
//#if (UseAuditTrail)
builder.AddGoldpathAuditTrail<WebApplicationBuilder, WorkerDbContext>();
//#endif
//#if (UseSoftDelete)
builder.AddGoldpathSoftDelete();
//#endif
//#if (UseDataProtection)
builder.AddGoldpathDataProtection();
//#endif
//#if (UseLocking && UsePostgres)
builder.AddGoldpathLocking(o =>
{
    o.Provider = GoldpathLockProvider.Postgres;     // the lock lives in the worker's database — zero new infra
    o.ConnectionName = "workdb";
});
//#endif
//#if (UseLocking && UseSqlServer)
builder.AddGoldpathSqlServerLocking(o => o.ConnectionName = "workdb");
//#endif
//#if (UseFileExchange)
builder.AddGoldpathFileExchange<WebApplicationBuilder, WorkerDbContext>(files =>
{
    // Declare YOUR rails here (goldpath never guesses a counterparty format):
    // files.AddRail<MyRow>("registry-daily", r => r.Header(1)
    //     .ParseLine(MyRow.Parse).ValidateRow(x => x.IsValid ? null : "reason")
    //     .Handle((row, ct) => ApplyAsync(row, ct)));
});
//#endif
//#if (UseNotification)
builder.AddGoldpathNotification<WebApplicationBuilder, WorkerDbContext>(notification =>
{
    // Declare YOUR templates here (goldpath never guesses a wording).
    // Config: Goldpath:Notification:Email { Host, Port, UseSsl, User, Password, From }.
    // notification.AddTemplate("run-finished", t => t
    //     .Channel("email", c => c
    //         .Subject("", "Run {{RunId}} finished")
    //         .Body("", "The nightly run {{RunId}} finished with {{Failures}} failures."))
    //     .DeleteBodyAfter(TimeSpan.FromDays(90)));      // evidence survives, content goes
});
//#endif
//#if (UseRiders && !UseJobs)
// The jobs runtime the riders need (notification RFC D3, fileexchange pick-up): runs and
// schedules live in the worker's database, next to the inbox.
builder.AddGoldpathJobs<WebApplicationBuilder, WorkerDbContext>(jobs =>
{
    jobs.ConnectionName = "workdb";
//#if (UseSqlServer)
    jobs.Provider = GoldpathJobStoreProvider.SqlServer;
//#endif
//#if (UseNotification)
    jobs.AddGoldpathNotificationJobs<WorkerDbContext>();   // send (frequent) + body-retention (nightly)
//#endif
//#if (UseFileExchange)
    // your pick-up job rides here (the transport is yours — SFTP/share/object store is composed, not shipped):
    // jobs.AddJob<RegistryPickupJob>(j => { j.Cron = "0 0 6 * * ?"; j.Deadline = TimeSpan.FromMinutes(30); });
//#endif
});
//#endif

var app = builder.Build();

// goldpath:features middleware — the drift profile is the source of these rows
//#if (UseMultiTenancy)
app.UseGoldpathMultiTenancy();                      // resolve the tenant BEFORE auth binds to it
//#endif
//#if (UseAuth)
app.UseGoldpathAuth();
//#endif
app.MapGoldpathDefaultEndpoints();
#if (UseQueue)
// Smoke-visible read model (what has been processed) — intentionally the only business-shaped endpoint.
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

// goldpath:features endpoints — admin surfaces map here (put them behind the auth floor)
//#if (UseOps)
// The fleet's ops surface (jobs RFC §7.1): trigger/pause/reschedule/runs/audit — every verb audited.
#if (UseAuth)
app.MapGoldpathJobsAdmin<WorkerDbContext>();        // ops policy REQUIRED (H2): the goldpath-ops role
#else
// No auth strategy in this shape: the opt-out is WRITTEN HERE so the decision stays
// visible — acceptable only behind an authenticating boundary (mTLS/gateway/cluster).
app.MapGoldpathJobsAdmin<WorkerDbContext>(exposeUnsecured: true);
#endif
//#endif
//#if (UseNotification)
#if (UseAuth)
app.MapGoldpathNotificationAdmin<WorkerDbContext>();   // read-only evidence views (recipients masked) — ops policy REQUIRED (H2)
#else
// No auth strategy in this shape: the opt-out is WRITTEN HERE so the decision stays
// visible — acceptable only behind an authenticating boundary (mTLS/gateway/cluster).
app.MapGoldpathNotificationAdmin<WorkerDbContext>(exposeUnsecured: true);   // read-only evidence views (recipients masked)
#endif
//#endif
//#if (UseOps)
// The console over those surfaces — served by THIS head from the package's embedded
// assets (no Node in this solution, by construction). One line to remove if your ops team
// drives the API directly; it adds no capability the surfaces above do not already expose.
#if (UseAuth)
app.MapGoldpathConsole();                           // behind the SAME ops floor as the surfaces
#else
// No auth strategy in this shape: the opt-out is WRITTEN HERE so the decision stays
// visible — acceptable only behind an authenticating boundary (mTLS/gateway/cluster).
app.MapGoldpathConsole(exposeUnsecured: true);
#endif
//#endif

app.Run();
