using CorPay.Api.Orders;
using CorPay.Api.Orders.Import;
using MassTransit;
using Mediant.AspNetCore.Mapping;
using Mediant.DependencyInjection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddGoldpathServiceDefaults();
builder.AddGoldpathApiDefaults();
builder.AddGoldpathAuth();   // OpenId: set Goldpath:Auth:Authority (+ Audience) in configuration
// goldpath:features registrations — the drift profile is the source of these rows
builder.AddGoldpathMultiTenancy();
builder.AddGoldpathAuditTrail<WebApplicationBuilder, OrdersDbContext>();
builder.AddGoldpathSoftDelete();
builder.AddGoldpathCaching();                       // HybridCache L1+L2 (redis resource in the AppHost)
builder.AddGoldpathIdempotency();
builder.AddGoldpathDataProtection();
builder.AddGoldpathLocking(o =>
{
    o.Provider = GoldpathLockProvider.Postgres;     // the lock lives in the app database — zero new infra
    o.ConnectionName = "ordersdb";
});
builder.AddGoldpathJobs<WebApplicationBuilder, OrdersDbContext>(jobs =>
{
    jobs.ConnectionName = "ordersdb";              // runs + schedules live in the app database
    jobs.AddGoldpathArchivalJobs<OrdersDbContext>();    // archive nightly, purge chained after it, verify weekly
    jobs.AddGoldpathBulkJobs<OrdersDbContext>();        // validate + execute runs (upload verb fires validate immediately)
});
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

    // Slice 2 — the finance card's batch file: same validation table as single submit.
    bulk.AddBatch<CorPay.Api.Payments.Import.PaymentFileRow>("payment-instructions", b => b
        .MaxRows(100_000)
        .RowKey(r => r.Reference)
        .Validate((row, ctx) =>
        {
            foreach (var error in CorPay.Api.Payments.Features.SubmitPaymentInstructionHandler.Validate(
                new(row.Reference, row.DebtorIban, row.CreditorIban, row.Amount, row.Currency)))
            {
                ctx.Fail(error.PropertyName, error.Description);
            }
        }));
});
builder.Services.AddScoped<IGoldpathBulkRowHandler<OrderImportRow>, OrderImportHandler>();
builder.Services.AddScoped<IGoldpathBulkRowHandler<CorPay.Api.Payments.Import.PaymentFileRow>, CorPay.Api.Payments.Import.PaymentFileRowHandler>();

builder.Services.AddMediant(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// Connection strings come from the AppHost; under the build-time OpenAPI generator they
// are absent — configuration stays tolerant, usage would fail loudly anyway.
var ordersDbConnection = builder.Configuration.GetConnectionString("ordersdb");
builder.AddGoldpathData<WebApplicationBuilder, OrdersDbContext>(options =>
{
    // No connection (docgen, `dotnet ef` design time): the PROVIDER still binds — the
    // model needs it to exist; nothing connects until a real string is used.
    if (ordersDbConnection is not null)
    {
        options.UseNpgsql(ordersDbConnection);
    }
    else
    {
        options.UseNpgsql();
    }
});

// The core-banking seam (slice 1): dev pays instantly; production plugs the bank's adapter.
builder.Services.AddSingleton<CorPay.Api.Payments.ICoreBankingClient, CorPay.Api.Payments.DevCoreBankingClient>();

builder.AddGoldpathMessaging(bus =>
{
    // goldpath:features consumers — bus-riding features register here
    bus.AddConsumer<OrderPlacedConsumer>();
    bus.AddConsumer<CorPay.Api.Payments.PaymentExecutedConsumer>();
    bus.AddGoldpathOutbox<OrdersDbContext>(outbox =>
    {
        outbox.UsePostgres();
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

var app = builder.Build();

// goldpath:features middleware — the drift profile is the source of these rows
app.UseGoldpathMultiTenancy();                      // resolve the tenant BEFORE auth binds to it
app.UseGoldpathAuth();
app.MapGoldpathDefaultEndpoints();
app.MapMediantEndpoints(typeof(Program).Assembly);
// goldpath:features endpoints — admin surfaces map here (put them behind the auth floor)
app.MapGoldpathJobsAdmin<OrdersDbContext>();        // run console API: trigger/pause/reschedule/audit — ops policy REQUIRED (H2)
app.MapGoldpathArchivalAdmin<OrdersDbContext>();    // lifecycle verbs: retrieve/hold/erase/verify — ops policy REQUIRED (H2)
app.MapGoldpathBulkAdmin<OrdersDbContext>();        // intake verbs: upload/report/approve/reject — ops policy REQUIRED (H2)

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
