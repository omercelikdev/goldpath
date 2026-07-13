using CorPay.EodWorker.Reports;
using Microsoft.EntityFrameworkCore;

// A web host on purpose: readiness/liveness probes are the deployment contract of a
// worker too — the HTTP surface carries probes (and the jobs console), never business APIs.
var builder = WebApplication.CreateBuilder(args);

builder.AddGoldpathServiceDefaults();

// Connection strings come from the AppHost; configuration stays tolerant, usage fails loudly.
var reportsDbConnection = builder.Configuration.GetConnectionString("ordersdb");
builder.AddGoldpathData<WebApplicationBuilder, ReportsDbContext>(options =>
{
    // Design time (`dotnet ef`): the provider must BIND without a connection.
    if (reportsDbConnection is not null)
    {
        options.UseNpgsql(reportsDbConnection);
    }
    else
    {
        options.UseNpgsql();
    }
});

// Clustered jobs (Goldpath.Jobs) on the APP database, as this worker's OWN fleet: the
// SchedulerName separates it from the Api's scheduler — same tables, two clusters,
// zero contention for fires (one scheduler per PROCESS, one fleet per PURPOSE).
builder.AddGoldpathJobs<WebApplicationBuilder, ReportsDbContext>(jobs =>
{
    jobs.ConnectionName = "ordersdb";
    jobs.SchedulerName = "corpay-eodworker";
    jobs.AddJob<NightlyReportJob>(j =>
    {
        j.Cron = "0 0 1 * * ?";                    // nightly at 01:00
        j.Deadline = TimeSpan.FromHours(2);        // every job has an SLA (GP1302)
        j.MaxParallelChunks = 2;
    });
});

var app = builder.Build();

app.MapGoldpathDefaultEndpoints();
app.MapGoldpathJobsAdmin<ReportsDbContext>(exposeUnsecured: true);   // internal fleet console — keep it behind the cluster boundary (H2 opt-out, visible)

app.Run();
