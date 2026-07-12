using GoldpathTemplate.Api.Orders;
#if (UseBulk)
using GoldpathTemplate.Api.Orders.Import;
#endif
#if (UseNotification)
using GoldpathTemplate.Api.Orders.Notifications;
#endif
#if (UseCampaign)
using GoldpathTemplate.Api.Orders.Campaigns;
#endif
#if (UseCampaign && !UseBroker)
#error features.campaign REQUIRES a broker (campaign RFC D8): the release path IS broker fan-out. Regenerate with --broker rabbitmq (or kafka).
#endif
#if (UseBroker)
using MassTransit;
#endif
using Mediant.AspNetCore.Mapping;
using Mediant.DependencyInjection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddGoldpathServiceDefaults();
builder.AddGoldpathApiDefaults();
//#if (UseApiKey)
builder.AddGoldpathAuth(o => o.Strategy = GoldpathAuthStrategy.ApiKey);
//#elif (UseAuth)
builder.AddGoldpathAuth();   // OpenId: set Goldpath:Auth:Authority (+ Audience) in configuration
//#endif
// goldpath:features registrations — the drift profile is the source of these rows
//#if (UseMultiTenancy)
builder.AddGoldpathMultiTenancy();
//#endif
//#if (UseAuditTrail)
builder.AddGoldpathAuditTrail<WebApplicationBuilder, OrdersDbContext>();
//#endif
//#if (UseSoftDelete)
builder.AddGoldpathSoftDelete();
//#endif
//#if (UseCaching)
builder.AddGoldpathCaching();                       // HybridCache L1+L2 (redis resource in the AppHost)
//#endif
//#if (UseIdempotency && !UseCaching)
builder.Services.AddDistributedMemoryCache();  // idempotency store fallback; enable caching for Redis-backed keys
//#endif
//#if (UseIdempotency)
builder.AddGoldpathIdempotency();
//#endif
//#if (UseDataProtection)
builder.AddGoldpathDataProtection();
//#endif
//#if (UseLocking && UsePostgres)
builder.AddGoldpathLocking(o =>
{
    o.Provider = GoldpathLockProvider.Postgres;     // the lock lives in the app database — zero new infra
    o.ConnectionName = "ordersdb";
});
//#endif
//#if (UseLocking && UseSqlServer)
builder.AddGoldpathSqlServerLocking(o => o.ConnectionName = "ordersdb");
//#endif
//#if (UseArchival || UseBulk || UseNotification || UseCampaign)
builder.AddGoldpathJobs<WebApplicationBuilder, OrdersDbContext>(jobs =>
{
    jobs.ConnectionName = "ordersdb";              // runs + schedules live in the app database
//#if (UseSqlServer)
    jobs.Provider = GoldpathJobStoreProvider.SqlServer;
//#endif
//#if (UseArchival)
    jobs.AddGoldpathArchivalJobs<OrdersDbContext>();    // archive nightly, purge chained after it, verify weekly
//#endif
//#if (UseBulk)
    jobs.AddGoldpathBulkJobs<OrdersDbContext>();        // validate + execute runs (upload verb fires validate immediately)
//#endif
//#if (UseNotification)
    jobs.AddGoldpathNotificationJobs<OrdersDbContext>();   // send (frequent) + body-retention (nightly)
//#endif
//#if (UseCampaign)
    jobs.AddGoldpathCampaignJobs<OrdersDbContext>();       // pacer: the cron guarantees a LEADER exists; pacing is in-memory ticks
//#endif
});
//#endif
//#if (UseArchival)
builder.AddGoldpathArchival<WebApplicationBuilder, OrdersDbContext>(archival =>
{
    // The walking-skeleton lifecycle: confirmed orders archive after a year, retained ten.
    archival.AddArchive<Order>(a => a
        .Key(o => o.Id)
        .DueWhen(o => o.Status == OrderStatus.Confirmed, o => o.CreatedAt)
        .ArchiveAfter(TimeSpan.FromDays(365))
        .RetainFor(years: 10)
        .DeleteHotRowsAfterArchive());
});
//#endif
//#if (UseBulk)
builder.AddGoldpathBulk<WebApplicationBuilder, OrdersDbContext>(bulk =>
{
    // The walking-skeleton intake: an order-import file — validated, gated, executed as a run.
    bulk.AddBatch<OrderImportRow>("orders", b => b
        .MaxRows(10_000)
        .RowKey(r => r.Reference)
        .Validate((row, ctx) =>
        {
            if (row.Amount <= 0)
            {
                ctx.Fail(nameof(row.Amount), "amount must be positive");
            }
        }));
});
builder.Services.AddScoped<IGoldpathBulkRowHandler<OrderImportRow>, OrderImportHandler>();
//#endif
//#if (UseNotification)
builder.AddGoldpathNotification<WebApplicationBuilder, OrdersDbContext>(notification =>
{
    // The walking-skeleton notice: order confirmed -> one evidence-backed email.
    // Config: Goldpath:Notification:Email { Host, Port, UseSsl, User, Password, From }.
    notification.AddTemplate(OrderNotifications.OrderConfirmedTemplate, t => t
        .Channel("email", c => c
            .Subject("", "Your order {{Reference}} is confirmed")
            .Body("", "Dear customer, your order {{Reference}} ({{Amount}}) is confirmed."))
        .DeleteBodyAfter(TimeSpan.FromDays(90)));      // evidence survives, content goes
});
//#endif
//#if (UseCampaign)
builder.AddGoldpathCampaign<WebApplicationBuilder, OrdersDbContext>(campaign =>
{
    // The walking-skeleton campaign: win back customers who went quiet for a year.
    // Operators CREATE instances over this type through /goldpath/admin/campaign (audited).
    campaign.AddCampaign<DormantCustomerTarget>(OrderCampaigns.WinbackType, c => c
        .MaxTargets(1_000_000)                         // the ceiling is a decision, not a default (GP1701)
        .Targets((services, parameters) => services.GetRequiredService<OrdersDbContext>()
            .Orders.AsNoTracking()
            .Where(o => o.CreatedAt < DateTimeOffset.UtcNow.AddDays(-int.Parse(parameters["quietDays"])))
            .OrderBy(o => o.Id)                        // stable order: the watermark resumes BY COUNT
            .Select(o => new DormantCustomerTarget(o.Id, o.Reference))
            .AsAsyncEnumerable())
        .DefaultPolicy(p => p with { Tps = 50, MaxInFlight = 1_000 }));
});
builder.Services.AddScoped<IGoldpathCampaignItemHandler<DormantCustomerTarget>, WinbackHandler>();
//#endif

builder.Services.AddMediant(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// Connection strings come from the AppHost; under the build-time OpenAPI generator they
// are absent — configuration stays tolerant, usage would fail loudly anyway.
var ordersDbConnection = builder.Configuration.GetConnectionString("ordersdb");
builder.AddGoldpathData<WebApplicationBuilder, OrdersDbContext>(options =>
{
    // No connection (docgen, `dotnet ef` design time): the PROVIDER still binds — the
    // model needs it to exist; nothing connects until a real string is used.
#if (UsePostgres)
    if (ordersDbConnection is not null)
    {
        options.UseNpgsql(ordersDbConnection);
    }
    else
    {
        options.UseNpgsql();
    }
#endif
#if (UseSqlServer)
    if (ordersDbConnection is not null)
    {
        options.UseSqlServer(ordersDbConnection);
    }
    else
    {
        options.UseSqlServer();
    }
#endif
});

#if (UseBroker)
builder.AddGoldpathMessaging(bus =>
{
    // goldpath:features consumers — bus-riding features register here
    bus.AddConsumer<OrderPlacedConsumer>();
//#if (UseCampaign)
    bus.AddGoldpathCampaignConsumers<OrdersDbContext>();    // claim-before-execute item consumer + batching outcome sink
//#endif
    bus.AddGoldpathOutbox<OrdersDbContext>(outbox =>
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

var app = builder.Build();

// goldpath:features middleware — the drift profile is the source of these rows
//#if (UseMultiTenancy)
app.UseGoldpathMultiTenancy();                      // resolve the tenant BEFORE auth binds to it
//#endif
//#if (UseAuth)
app.UseGoldpathAuth();
//#endif
app.MapGoldpathDefaultEndpoints();
app.MapMediantEndpoints(typeof(Program).Assembly);
// goldpath:features endpoints — admin surfaces map here (put them behind the auth floor)
//#if (UseArchival || UseBulk || UseNotification || UseCampaign)
#if (UseAuth)
app.MapGoldpathJobsAdmin<OrdersDbContext>();        // run console API: trigger/pause/reschedule/audit — ops policy REQUIRED (H2)
#else
// No auth strategy in this shape: the opt-out is WRITTEN HERE so the decision stays
// visible — acceptable only behind an authenticating boundary (mTLS/gateway).
app.MapGoldpathJobsAdmin<OrdersDbContext>(exposeUnsecured: true);        // run console API: trigger/pause/reschedule/audit
#endif
//#endif
//#if (UseArchival)
#if (UseAuth)
app.MapGoldpathArchivalAdmin<OrdersDbContext>();    // lifecycle verbs: retrieve/hold/erase/verify — ops policy REQUIRED (H2)
#else
// No auth strategy in this shape: the opt-out is WRITTEN HERE so the decision stays
// visible — acceptable only behind an authenticating boundary (mTLS/gateway).
app.MapGoldpathArchivalAdmin<OrdersDbContext>(exposeUnsecured: true);    // lifecycle verbs: retrieve/hold/erase/verify
#endif
//#endif
//#if (UseBulk)
#if (UseAuth)
app.MapGoldpathBulkAdmin<OrdersDbContext>();        // intake verbs: upload/report/approve/reject — ops policy REQUIRED (H2)
#else
// No auth strategy in this shape: the opt-out is WRITTEN HERE so the decision stays
// visible — acceptable only behind an authenticating boundary (mTLS/gateway).
app.MapGoldpathBulkAdmin<OrdersDbContext>(exposeUnsecured: true);        // intake verbs: upload/report/approve/reject
#endif
//#endif
//#if (UseNotification)
#if (UseAuth)
app.MapGoldpathNotificationAdmin<OrdersDbContext>();   // read-only evidence views (recipients masked) — ops policy REQUIRED (H2)
#else
// No auth strategy in this shape: the opt-out is WRITTEN HERE so the decision stays
// visible — acceptable only behind an authenticating boundary (mTLS/gateway).
app.MapGoldpathNotificationAdmin<OrdersDbContext>(exposeUnsecured: true);   // read-only evidence views (recipients masked)
#endif
//#endif
//#if (UseCampaign)
#if (UseAuth)
app.MapGoldpathCampaignAdmin<OrdersDbContext>();       // audited verbs: create/pause/resume/abort/throttle — ops policy REQUIRED (H2)
#else
// No auth strategy in this shape: the opt-out is WRITTEN HERE so the decision stays
// visible — acceptable only behind an authenticating boundary (mTLS/gateway).
app.MapGoldpathCampaignAdmin<OrdersDbContext>(exposeUnsecured: true);       // audited verbs: create/pause/resume/abort/throttle
#endif
//#endif

// Skip schema work under the build-time OpenAPI generator (no database there).
var isDocGen = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
if (app.Environment.IsDevelopment() && !isDocGen)
{
    // Development only (GP0302): real environments apply the CI migration bundle —
    // and Development walks the SAME migrations (EnsureCreated writes no history row;
    // a database born from it can never take a migration later — migrations RFC D2).
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();
