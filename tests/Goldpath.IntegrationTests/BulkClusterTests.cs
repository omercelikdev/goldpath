using System.Diagnostics;
using System.Text;
using Goldpath.Jobs.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Xunit;

namespace Goldpath.Tests.Integration;

/// <summary>
/// H6: the kill-9 recovery proof EXTENDED TO BULK (jobs proved two-node recovery; bulk was
/// proven single-executor). A real executor process dies mid-batch; the second executor
/// adopts the recovered fire and finishes. The iron assert is the finance one: NO ROW IS
/// PAID TWICE by the executors — a row mid-flight at the kill lands in the REPAIR queue
/// (MDM constraint 2: confirm downstream, then replay), never silently re-sent.
/// </summary>
[Collection("quartz-process-globals")]
public sealed class BulkClusterTests : IAsyncLifetime
{
    private readonly string _fleet = $"bulkc-{Guid.NewGuid():N}"[..16];   // Quartz's SchedulerRepository is process-global
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private IHost _management = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connection = _postgres.GetConnectionString();
        await using (var db = NewDb())
        {
            await db.Database.EnsureCreatedAsync();
        }

        // The test process is the OPERATOR: bulk engine for upload/validate/approve (direct,
        // no scheduler) + a management head for the trigger/replay verbs on the cluster.
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:jobsdb"] = connection;
        builder.Services.AddDbContext<ClusterDb>(o => o.UseNpgsql(connection));
        builder.Services.AddScoped<IGoldpathBulkRowHandler<ClusterPaymentRow>, SlowPaymentHandler>();
        builder.AddGoldpathBulk<HostApplicationBuilder, ClusterDb>(bulk =>
        {
            bulk.ChunkSize = 5;
            bulk.AddBatch<ClusterPaymentRow>("payments", b => b.MaxRows(10_000).RowKey(r => r.EndToEndId));
        });
        builder.AddGoldpathJobsManagement<HostApplicationBuilder, ClusterDb>(jobs => jobs.ConnectionName = "jobsdb");
        _management = builder.Build();
        await _management.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _management.StopAsync();
        _management.Dispose();
        QuartzProcessGlobals.Pin();   // the disposed host may own Quartz's global log provider
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Kill9_midbatch_recovers_on_the_other_executor_without_a_double_payment()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var connection = _postgres.GetConnectionString();
        var admin = _management.Services.GetRequiredService<GoldpathJobsAdminService<ClusterDb>>();
        var engine = _management.Services.GetRequiredService<GoldpathBulkEngine<ClusterDb>>();

        // Executor A boots the store alone (staggered start, rolling-deploy reality).
        using var nodeA = StartExecutor(connection);
        await WaitUntilAsync(() => Task.FromResult(_childOutput.Any(l => l.Contains("TESTHOST-READY", StringComparison.Ordinal))), timeout.Token);
        using var nodeB = StartExecutor(connection);
        try
        {
            // Upload + validate + approve as the operator (engine-direct; no fire needed).
            Guid batchId;
            using (var scope = _management.Services.CreateScope())
            {
                var (batch, _) = await engine.IngestAsync(scope.ServiceProvider, "payments", Csv(30), "cluster.csv", null, timeout.Token);
                batchId = batch.Id;
                await engine.ValidateBatchAsync(scope.ServiceProvider, batchId, timeout.Token);
                Assert.True((await engine.ApproveAsync(scope.ServiceProvider, batchId, "treasurer", "four-eyes done", timeout.Token)).Ok);
            }

            // Fire the execute run on the cluster and wait until an executor is visibly
            // paying (into chunk 2). EITHER node may win the fire — identify the winner
            // from the sink's process id and KILL THAT one mid-chunk.
            await WaitUntilAsync(async () =>
                (await admin.GetJobsAsync(_fleet, timeout.Token)).Any(j => j.Name.StartsWith("GoldpathBulkExecuteJob", StringComparison.Ordinal)), timeout.Token);
            Assert.True((await admin.TriggerAsync(_fleet, "GoldpathBulkExecuteJob`1", dryRun: false, "it-operator", timeout.Token)).Ok);
            await WaitUntilAsync(async () => await CountPaidAsync() >= 6, timeout.Token);
            var winnerPid = (await QueryPaymentsAsync())[0].Instance;
            var victim = winnerPid == nodeA.Id.ToString() ? nodeA : nodeB;
            victim.Kill(entireProcessTree: true);
            await victim.WaitForExitAsync(timeout.Token);

            // Quartz recovery re-fires on B; the runner resumes the SAME run from the
            // checkpoint; the batch reaches a terminal state.
            var terminal = await WaitForTerminalBatchAsync(batchId, timeout.Token);

            // THE IRON ASSERT: the executors never paid a row twice — a duplicate here
            // is a double payment, the exact failure bulk exists to prevent.
            var paid = await QueryPaymentsAsync();
            Assert.Equal(paid.Count, paid.Select(p => p.RowNumber).Distinct().Count());
            Assert.True(paid.Select(p => p.Instance).Distinct().Count() >= 2, "both executors must have paid rows");

            // Books balance: every valid row is either paid or sits in the repair queue.
            Assert.Equal(30, terminal.ExecutedRows + terminal.FailedRows);
            Assert.True(terminal.FailedRows <= 5, "at most the in-flight chunk's rows may need repair");

            if (terminal.FailedRows > 0)
            {
                // The repair path heals through the SAME cluster verb; the interrupted row
                // that already reached the sink may legitimately repeat NOW — replay after
                // downstream confirmation is the operator's documented contract.
                Assert.Equal(GoldpathBulkBatchState.CompletedWithFailures, terminal.State);
                Assert.True((await admin.ReplayItemsAsync(terminal.RunId!.Value, "it-operator", timeout.Token)).Ok);
                await WaitUntilAsync(async () =>
                    (await QueryBatchAsync(batchId)).State == GoldpathBulkBatchState.Completed, timeout.Token);

                var healed = await QueryBatchAsync(batchId);
                Assert.Equal(30, healed.ExecutedRows);
                Assert.Equal(0, healed.FailedRows);
                Assert.Equal(30, (await QueryPaymentsAsync()).Select(p => p.RowNumber).Distinct().Count());
            }
            else
            {
                Assert.Equal(GoldpathBulkBatchState.Completed, terminal.State);
                Assert.Equal(30, paid.Select(p => p.RowNumber).Distinct().Count());
            }
        }
        finally
        {
            KillQuietly(nodeA);
            KillQuietly(nodeB);
        }
    }

    private static MemoryStream Csv(int rows)
    {
        var text = new StringBuilder("EndToEndId,Amount\n");
        for (var i = 1; i <= rows; i++)
        {
            text.Append($"E{i},{i * 10}\n");
        }

        return new MemoryStream(Encoding.UTF8.GetBytes(text.ToString()));
    }

    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _childOutput = new();

    private Process StartExecutor(string connection)
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
        startInfo.ArgumentList.Add("--bulk");
        startInfo.ArgumentList.Add("--fleet");
        startInfo.ArgumentList.Add(_fleet);

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

    private ClusterDb NewDb() => new(new DbContextOptionsBuilder<ClusterDb>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private async Task<GoldpathBulkBatch> QueryBatchAsync(Guid batchId)
    {
        await using var db = NewDb();
        return await db.Set<GoldpathBulkBatch>().AsNoTracking().SingleAsync(b => b.Id == batchId);
    }

    private async Task<GoldpathBulkBatch> WaitForTerminalBatchAsync(Guid batchId, CancellationToken token)
    {
        while (true)
        {
            var batch = await QueryBatchAsync(batchId);
            if (batch.State is GoldpathBulkBatchState.Completed or GoldpathBulkBatchState.CompletedWithFailures)
            {
                return batch;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), token);
        }
    }

    private async Task<List<PaymentSinkEntry>> QueryPaymentsAsync()
    {
        await using var db = NewDb();
        return await db.PaymentSink.AsNoTracking().ToListAsync();
    }

    private async Task<int> CountPaidAsync()
    {
        await using var db = NewDb();
        return await db.PaymentSink.AsNoTracking().CountAsync();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken token)
    {
        while (!await condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), token);
        }
    }
}
