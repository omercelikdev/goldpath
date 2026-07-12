// The killable half of the clustered-recovery proofs: a REAL executor process the
// integration test spawns and Process.Kill()s mid-run (in-process hosts can only die
// gracefully — a kill-9 needs a process). Usage:
//   Goldpath.Jobs.TestHost <connectionString> [--trigger]            (jobs cluster mode)
//   Goldpath.Jobs.TestHost <connectionString> --bulk --fleet <name>  (bulk executor mode)
using Goldpath;
using Goldpath.Jobs.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;

var connectionString = args[0];
var bulkMode = args.Contains("--bulk");
var fleet = args.Contains("--fleet") ? args[Array.IndexOf(args, "--fleet") + 1] : "it-cluster";

var builder = Host.CreateApplicationBuilder();
builder.Configuration["ConnectionStrings:jobsdb"] = connectionString;
builder.Services.AddDbContext<ClusterDb>(o => o.UseNpgsql(connectionString));
if (bulkMode)
{
    builder.Services.AddScoped<IGoldpathBulkRowHandler<ClusterPaymentRow>, SlowPaymentHandler>();
    builder.AddGoldpathBulk<HostApplicationBuilder, ClusterDb>(bulk =>
    {
        bulk.ChunkSize = 5;   // 30 valid rows -> 6 chunks: wide enough for a mid-run kill
        bulk.AddBatch<ClusterPaymentRow>("payments", b => b
            .MaxRows(10_000)
            .RowKey(r => r.EndToEndId));
    });
}

builder.AddGoldpathJobs<HostApplicationBuilder, ClusterDb>(jobs =>
{
    jobs.SchedulerName = fleet;
    jobs.ConnectionName = "jobsdb";
    jobs.CheckinInterval = TimeSpan.FromSeconds(1);
    jobs.CheckinMisfireThreshold = TimeSpan.FromSeconds(3);
    if (bulkMode)
    {
        // Far-future crons: the TEST drives the execute run through the admin verb.
        jobs.AddGoldpathBulkJobs<ClusterDb>(validateCron: "0 0 0 1 1 ? 2099", executeCron: "0 0 0 1 1 ? 2099");
    }
    else
    {
        jobs.AddJob<SlowClusterJob>();
        jobs.AddJob<ChainedProofJob>(j => j.StartAfter<SlowClusterJob>());
    }
});

var host = builder.Build();
await host.StartAsync();
Console.WriteLine("TESTHOST-READY");

if (args.Contains("--trigger"))
{
    var factory = host.Services.GetRequiredService<ISchedulerFactory>();
    var scheduler = await factory.GetScheduler();
    await scheduler.TriggerJob(new JobKey(nameof(SlowClusterJob), GoldpathJobsExtensions.JobGroup));
}

await host.WaitForShutdownAsync();

namespace Goldpath.Jobs.TestHost
{
    /// <summary>Shared model: run + bulk tables + cross-process execution sinks.</summary>
    public class ClusterDb(DbContextOptions<ClusterDb> options) : DbContext(options)
    {
        public DbSet<SinkEntry> Sink => Set<SinkEntry>();

        public DbSet<PaymentSinkEntry> PaymentSink => Set<PaymentSinkEntry>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddGoldpathJobs();
            modelBuilder.AddGoldpathBulk();
        }
    }

    /// <summary>One chunk execution, recorded durably so the test sees across processes.</summary>
    public class SinkEntry
    {
        public long Id { get; set; }
        public string JobName { get; set; } = "";
        public int ChunkIndex { get; set; }
        public string Instance { get; set; } = "";
    }

    /// <summary>One paid row — the double-payment detector across processes.</summary>
    public class PaymentSinkEntry
    {
        public long Id { get; set; }
        public string EndToEndId { get; set; } = "";
        public int RowNumber { get; set; }
        public string Instance { get; set; } = "";
    }

    /// <summary>The payment instruction shape of the bulk kill-9 proof.</summary>
    public sealed class ClusterPaymentRow
    {
        public string EndToEndId { get; set; } = "";
        public decimal Amount { get; set; }
    }

    /// <summary>Pays one row SLOWLY (so a kill lands mid-chunk) and records the side effect.</summary>
    public sealed class SlowPaymentHandler(ClusterDb db) : IGoldpathBulkRowHandler<ClusterPaymentRow>
    {
        public async Task ExecuteAsync(ClusterPaymentRow row, GoldpathBulkRowContext context, CancellationToken cancellationToken)
        {
            db.PaymentSink.Add(new PaymentSinkEntry
            {
                EndToEndId = row.EndToEndId,
                RowNumber = context.RowNumber,
                Instance = Environment.ProcessId.ToString(),
            });
            await db.SaveChangesAsync(cancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);   // wide kill window
        }
    }

    /// <summary>30 slow chunks — wide enough for a mid-run kill to land.</summary>
    public sealed class SlowClusterJob : IGoldpathJob
    {
        public Task<GoldpathJobPlan> PlanAsync(GoldpathJobContext context, CancellationToken cancellationToken)
            => Task.FromResult(GoldpathJobPlanner.ByRange(30, 1));

        public async Task ExecuteChunkAsync(GoldpathJobChunk chunk, GoldpathJobContext context, CancellationToken cancellationToken)
        {
            var db = context.Services.GetRequiredService<ClusterDb>();
            db.Sink.Add(new SinkEntry { JobName = nameof(SlowClusterJob), ChunkIndex = chunk.Index, Instance = context.InstanceName });
            await db.SaveChangesAsync(cancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }

    /// <summary>Chaining proof: must run exactly once, AFTER the slow job completes.</summary>
    public sealed class ChainedProofJob : IGoldpathJob
    {
        public Task<GoldpathJobPlan> PlanAsync(GoldpathJobContext context, CancellationToken cancellationToken)
            => Task.FromResult(GoldpathJobPlanner.ByRange(1, 1));

        public async Task ExecuteChunkAsync(GoldpathJobChunk chunk, GoldpathJobContext context, CancellationToken cancellationToken)
        {
            var db = context.Services.GetRequiredService<ClusterDb>();
            db.Sink.Add(new SinkEntry { JobName = nameof(ChainedProofJob), ChunkIndex = chunk.Index, Instance = context.InstanceName });
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
