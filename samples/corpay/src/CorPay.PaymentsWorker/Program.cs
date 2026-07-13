using CorPay.PaymentsWorker.WorkItems;
using MassTransit;
using Microsoft.EntityFrameworkCore;

// A web host on purpose: readiness/liveness probes are the deployment contract of a
// worker too — the HTTP surface carries probes, never business APIs.
var builder = WebApplication.CreateBuilder(args);

builder.AddGoldpathServiceDefaults();

// Connection strings come from the AppHost; configuration stays tolerant, usage fails loudly.
var workDbConnection = builder.Configuration.GetConnectionString("ordersdb");
builder.AddGoldpathData<WebApplicationBuilder, WorkDbContext>(options =>
{
    // Design time (`dotnet ef`): the provider must BIND without a connection.
    if (workDbConnection is not null)
    {
        options.UseNpgsql(workDbConnection);
    }
    else
    {
        options.UseNpgsql();
    }
});

builder.AddGoldpathMessaging(bus =>
{
    bus.AddConsumer<WorkItemQueuedConsumer>();
    // Consumer-side INBOX: every receive endpoint dedups on MessageId — exactly-once processing.
    bus.AddGoldpathOutbox<WorkDbContext>(outbox => outbox.UsePostgres());
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

app.MapGoldpathDefaultEndpoints();
// Smoke-visible read model (what has been processed) — intentionally the only endpoint.
app.MapGet("/api/v1/processed", async (WorkDbContext db) =>
    await db.ProcessedWorkItems.OrderBy(w => w.ProcessedAt).ToListAsync());

app.Run();
