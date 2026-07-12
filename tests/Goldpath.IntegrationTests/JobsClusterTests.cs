using System.Diagnostics;
using Goldpath.Jobs.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using Testcontainers.PostgreSql;
using Xunit;

namespace Goldpath.Tests.Integration;

/// <summary>
/// THE clustered proofs of the jobs RFC (S1 DoD) on real PostgreSQL:
/// 1. kill-9 mid-run → the OTHER node recovers the fire and RESUMES from the checkpoint
///    (real processes — in-process hosts can only die gracefully);
/// 2. completion chaining fires the successor exactly once;
/// 3. a management-mode member can verb the cluster but never executes;
/// 4. graceful shutdown drains and the next fire resumes in-process.
/// </summary>
[Collection("quartz-process-globals")]
public sealed class JobsClusterTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = new ClusterDb(new DbContextOptionsBuilder<ClusterDb>()
            .UseNpgsql(_postgres.GetConnectionString()).Options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Kill9_midrun_is_recovered_on_the_other_node_from_the_checkpoint()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var connection = _postgres.GetConnectionString();

        // Staggered start (rolling-deploy reality): node A boots the brand-new store and
        // reports READY before node B joins — concurrent COLD-boot of an empty qrtz schema
        // can contend on the store's own lock bootstrap.
        using var nodeA = StartHostProcess(connection, trigger: true);
        await WaitUntilAsync(() => Task.FromResult(_childOutput.Any(l => l.Contains("TESTHOST-READY", StringComparison.Ordinal))), timeout.Token);
        using var nodeB = StartHostProcess(connection, trigger: false);
        try
        {
            // Wait until node A is visibly executing (≥3 checkpointed chunks), then KILL it.
            try
            {
                await WaitUntilAsync(async () => await CountSinkAsync(nameof(SlowClusterJob)) >= 3, timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new Xunit.Sdk.XunitException(
                    "the triggered run never became visible; child output:\n" + string.Join('\n', _childOutput));
            }
            nodeA.Kill(entireProcessTree: true);
            await nodeA.WaitForExitAsync(timeout.Token);

            // Quartz clustering detects the missed check-ins, re-fires with RequestsRecovery
            // on node B; the runner resumes from the checkpoint.
            await WaitUntilAsync(async () =>
            {
                var run = await QueryRunAsync(nameof(SlowClusterJob));
                return run?.Status == GoldpathJobRunStatus.Completed;
            }, timeout.Token);

            var completed = await QueryRunAsync(nameof(SlowClusterJob));
            Assert.NotNull(completed);
            Assert.Equal(30, completed.TotalChunks);
            Assert.Equal(30, completed.CompletedChunks);
            Assert.True(completed.Executions >= 2, "recovery must have consumed a second fire");

            // Checkpoint honesty: every chunk exactly once, EXCEPT possibly the single chunk
            // that was mid-flight at the kill (its claim reset; the side effect may repeat).
            var sink = await QuerySinkAsync(nameof(SlowClusterJob));
            var duplicated = sink.GroupBy(s => s.ChunkIndex).Where(g => g.Count() > 1).ToList();
            Assert.Equal(30, sink.Select(s => s.ChunkIndex).Distinct().Count());
            Assert.True(duplicated.Count <= 1,
                $"at most the in-flight chunk may repeat; got: {string.Join(",", duplicated.Select(g => g.Key))}");
            Assert.Equal(2, sink.Select(s => s.Instance).Distinct().Count());   // both nodes really worked

            // Chaining: completion triggered the successor exactly once (on the surviving node).
            await WaitUntilAsync(async () => await CountSinkAsync(nameof(ChainedProofJob)) >= 1, timeout.Token);
            Assert.Equal(1, await CountSinkAsync(nameof(ChainedProofJob)));
            var chained = await QueryRunAsync(nameof(ChainedProofJob));
            Assert.Equal(GoldpathJobRunStatus.Completed, chained!.Status);
        }
        finally
        {
            KillQuietly(nodeA);
            KillQuietly(nodeB);
        }
    }

    [Fact]
    public async Task Management_head_verbs_the_cluster_through_the_admin_service_and_never_executes()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var connection = _postgres.GetConnectionString();

        // One real executor process + one in-process MANAGEMENT head (no scheduler at all —
        // fleets discovered from the store, verbs via on-demand never-started schedulers).
        using var executor = StartHostProcess(connection, trigger: false);
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:jobsdb"] = connection;
        builder.Services.AddDbContext<ClusterDb>(o => o.UseNpgsql(connection));
        builder.AddGoldpathJobsManagement<HostApplicationBuilder, ClusterDb>(jobs =>
        {
            jobs.ConnectionName = "jobsdb";
        });
        using var management = builder.Build();
        await management.StartAsync(timeout.Token);
        try
        {
            var admin = management.Services.GetRequiredService<GoldpathJobsAdminService<ClusterDb>>();

            // D9 zero-config discovery: the fleet APPEARS because the executor exists.
            await WaitUntilAsync(async () =>
                (await admin.GetFleetsAsync(timeout.Token)).Any(f => f.SchedulerName == "it-cluster" && f.JobCount >= 2), timeout.Token);
            var fleet = (await admin.GetFleetsAsync(timeout.Token)).Single(f => f.SchedulerName == "it-cluster");
            Assert.NotEmpty(fleet.Nodes);   // the executor checks in

            var jobs = await admin.GetJobsAsync("it-cluster", timeout.Token);
            Assert.Contains(jobs, j => j.Name == nameof(SlowClusterJob) && j.RequestsRecovery);

            // Dry-run fires nothing; the real trigger runs on the EXECUTOR process.
            var dry = await admin.TriggerAsync("it-cluster", nameof(SlowClusterJob), dryRun: true, "it-operator", timeout.Token);
            Assert.True(dry.Ok);
            Assert.StartsWith("dry-run", dry.Message, StringComparison.Ordinal);
            Assert.Equal(0, await CountSinkAsync(nameof(SlowClusterJob)));

            Assert.True((await admin.TriggerAsync("it-cluster", nameof(SlowClusterJob), dryRun: false, "it-operator", timeout.Token)).Ok);
            await WaitUntilAsync(async () =>
                (await QueryRunAsync(nameof(SlowClusterJob)))?.Status == GoldpathJobRunStatus.Completed, timeout.Token);

            var sink = await QuerySinkAsync(nameof(SlowClusterJob));
            Assert.NotEmpty(sink);   // executed — and only by the executor (management runs no scheduler)

            // Runtime schedule override (D7) + calendar CRUD, all audited.
            Assert.True((await admin.RescheduleAsync("it-cluster", nameof(SlowClusterJob), "0 0 3 * * ?", null, "it-operator", timeout.Token)).Ok);
            var rescheduled = (await admin.GetJobsAsync("it-cluster", timeout.Token))
                .Single(j => j.Name == nameof(SlowClusterJob));
            Assert.Contains(rescheduled.Triggers, t => t.CronExpression == "0 0 3 * * ?");

            Assert.True((await admin.PauseJobAsync("it-cluster", nameof(SlowClusterJob), "it-operator", timeout.Token)).Ok);
            Assert.Contains((await admin.GetJobsAsync("it-cluster", timeout.Token)).Single(j => j.Name == nameof(SlowClusterJob)).Triggers,
                t => t.State == "Paused");
            Assert.True((await admin.ResumeJobAsync("it-cluster", nameof(SlowClusterJob), "it-operator", timeout.Token)).Ok);

            Assert.True((await admin.PutCalendarAsync("it-cluster", "weekends-off",
                new GoldpathCalendarSpec("weekly", "no weekends", null, [DayOfWeek.Saturday, DayOfWeek.Sunday], null), "it-operator", timeout.Token)).Ok);
            Assert.Contains(await admin.GetCalendarsAsync("it-cluster", timeout.Token), c => c.Name == "weekends-off");
            Assert.True((await admin.DeleteCalendarAsync("it-cluster", "weekends-off", "it-operator", timeout.Token)).Ok);

            // Iron rule 2: every verb left an audit row with the actor.
            var audit = await admin.GetAuditAsync(50, timeout.Token);
            var actions = audit.Select(a => a.Action).ToList();
            Assert.All(audit, a => Assert.Equal("it-operator", a.Actor));
            Assert.Contains("trigger", actions);
            Assert.Contains("reschedule", actions);
            Assert.Contains("pause", actions);
            Assert.Contains("resume", actions);
            Assert.Contains("calendar-put", actions);
            Assert.Contains("calendar-delete", actions);
            Assert.DoesNotContain(audit, a => a.Action == "trigger" && a.Detail == "dry-run");   // dry-run never audits a fire
        }
        finally
        {
            await management.StopAsync(CancellationToken.None);
            management.Dispose();
            QuartzProcessGlobals.Pin();   // the disposed host may own Quartz's global log provider
            KillQuietly(executor);
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _childOutput = new();

    private Process StartHostProcess(string connection, bool trigger)
    {
        var hostDll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../Goldpath.Jobs.TestHost/bin/Debug/net10.0/Goldpath.Jobs.TestHost.dll"));
        Assert.True(File.Exists(hostDll), $"build Goldpath.Jobs.TestHost first (expected at {hostDll})");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(hostDll);
        startInfo.ArgumentList.Add(connection);
        if (trigger)
        {
            startInfo.ArgumentList.Add("--trigger");
        }

        var process = Process.Start(startInfo)!;
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { _childOutput.Enqueue($"[out]{e.Data}"); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { _childOutput.Enqueue($"[err]{e.Data}"); } };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // already gone
        }
    }

    private async Task<GoldpathJobRun?> QueryRunAsync(string jobName)
    {
        await using var db = new ClusterDb(new DbContextOptionsBuilder<ClusterDb>()
            .UseNpgsql(_postgres.GetConnectionString()).Options);
        return await db.Set<GoldpathJobRun>().AsNoTracking().SingleOrDefaultAsync(r => r.JobName == jobName);
    }

    private async Task<List<SinkEntry>> QuerySinkAsync(string jobName)
    {
        await using var db = new ClusterDb(new DbContextOptionsBuilder<ClusterDb>()
            .UseNpgsql(_postgres.GetConnectionString()).Options);
        return await db.Sink.AsNoTracking().Where(s => s.JobName == jobName).ToListAsync();
    }

    private async Task<int> CountSinkAsync(string jobName)
    {
        await using var db = new ClusterDb(new DbContextOptionsBuilder<ClusterDb>()
            .UseNpgsql(_postgres.GetConnectionString()).Options);
        return await db.Sink.AsNoTracking().CountAsync(s => s.JobName == jobName);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken token)
    {
        while (!await condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), token);
        }
    }
}
