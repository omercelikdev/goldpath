// The killable half of the clustered-recovery proof: a REAL executor process the
// integration test spawns and Process.Kill()s mid-run (in-process hosts can only die
// gracefully — a kill-9 needs a process). Usage:
//   Goldpath.Jobs.TestHost <connectionString> [--trigger]
using Goldpath;
using Goldpath.Jobs.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;

var connectionString = args[0];
var builder = Host.CreateApplicationBuilder();
builder.Configuration["ConnectionStrings:jobsdb"] = connectionString;
builder.Services.AddDbContext<ClusterDb>(o => o.UseNpgsql(connectionString));
builder.AddGoldpathJobs<HostApplicationBuilder, ClusterDb>(jobs =>
{
    jobs.SchedulerName = "it-cluster";
    jobs.ConnectionName = "jobsdb";
    jobs.CheckinInterval = TimeSpan.FromSeconds(1);
    jobs.CheckinMisfireThreshold = TimeSpan.FromSeconds(3);
    jobs.AddJob<SlowClusterJob>();
    jobs.AddJob<ChainedProofJob>(j => j.StartAfter<SlowClusterJob>());
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
    /// <summary>Shared model: run tables + a cross-process execution sink.</summary>
    public class ClusterDb(DbContextOptions<ClusterDb> options) : DbContext(options)
    {
        public DbSet<SinkEntry> Sink => Set<SinkEntry>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddGoldpathJobs();
    }

    /// <summary>One chunk execution, recorded durably so the test sees across processes.</summary>
    public class SinkEntry
    {
        public long Id { get; set; }
        public string JobName { get; set; } = "";
        public int ChunkIndex { get; set; }
        public string Instance { get; set; } = "";
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
