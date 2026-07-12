using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Xunit;

namespace Goldpath.Tests.Integration;

/// <summary>
/// The finance card's bulk story on real PostgreSQL with the REAL jobs runner: upload a
/// payment file (with seeded poison) → the validate RUN writes the report → the gate →
/// the execute RUN pays the valid rows, isolates the poisoned ones into the repair queue →
/// the admin `replay-items` verb heals them → counts reconcile EXACTLY (no row lost, no
/// row double-executed). Plus content-hash dedup and the gate's refusals, end to end.
/// </summary>
[Collection("quartz-process-globals")]
public sealed class BulkTests : IAsyncLifetime
{
    public sealed class PaymentRow
    {
        public string EndToEndId { get; set; } = "";
        public string Iban { get; set; } = "";
        public decimal Amount { get; set; }
        public string? Note { get; set; }
    }

    /// <summary>The side-effect ledger: one row per handler execution (double-send detector).</summary>
    public sealed class PaymentSink
    {
        public long Id { get; set; }
        public string EndToEndId { get; set; } = "";
        public int RowNumber { get; set; }
    }

    public sealed class BulkDb(DbContextOptions<BulkDb> options) : DbContext(options)
    {
        public DbSet<PaymentSink> Sink => Set<PaymentSink>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddGoldpathBulk();
            modelBuilder.AddGoldpathJobs();
        }
    }

    /// <summary>Pays one row: appends to the sink; "FAIL"-noted rows throw (poison).</summary>
    public sealed class PaymentHandler(BulkDb db) : IGoldpathBulkRowHandler<PaymentRow>
    {
        public async Task ExecuteAsync(PaymentRow row, GoldpathBulkRowContext context, CancellationToken cancellationToken)
        {
            if (row.Note == "FAIL")
            {
                throw new InvalidOperationException("core banking refused the instruction");
            }

            db.Sink.Add(new PaymentSink { EndToEndId = row.EndToEndId, RowNumber = context.RowNumber });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using (var db = new BulkDb(new DbContextOptionsBuilder<BulkDb>().UseNpgsql(_postgres.GetConnectionString()).Options))
        {
            await db.Database.EnsureCreatedAsync();
        }

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:bulkdb"] = _postgres.GetConnectionString();
        builder.Services.AddDbContext<BulkDb>(o => o.UseNpgsql(_postgres.GetConnectionString()));
        builder.Services.AddScoped<IGoldpathBulkRowHandler<PaymentRow>, PaymentHandler>();
        builder.AddGoldpathBulk<HostApplicationBuilder, BulkDb>(bulk =>
        {
            bulk.ChunkSize = 3;   // several checkpoints even in a small file
            bulk.AddBatch<PaymentRow>("payments", b => b
                .MaxRows(100_000)
                .RowKey(r => r.EndToEndId)
                .Validate((row, ctx) =>
                {
                    if (row.Amount <= 0)
                    {
                        ctx.Fail(nameof(row.Amount), "amount must be positive");
                    }
                }));
        });
        builder.AddGoldpathJobs<HostApplicationBuilder, BulkDb>(jobs =>
        {
            jobs.ConnectionName = "bulkdb";
            jobs.SchedulerName = "bulk-it";
            // Far-future crons: the test drives every run through the ADMIN verb, like an operator.
            jobs.AddGoldpathBulkJobs<BulkDb>(validateCron: "0 0 0 1 1 ? 2099", executeCron: "0 0 0 1 1 ? 2099");
        });
        _host = builder.Build();
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        QuartzProcessGlobals.Pin();   // the disposed host may own Quartz's global log provider
        await _postgres.DisposeAsync();
    }

    private static MemoryStream Csv(int rows, params int[] poisoned)
    {
        var text = new StringBuilder("EndToEndId,Iban,Amount,Note\n");
        for (var i = 1; i <= rows; i++)
        {
            text.Append($"E{i},TR{i:D2},{i * 10},{(poisoned.Contains(i) ? "FAIL" : "")}\n");
        }

        return new MemoryStream(Encoding.UTF8.GetBytes(text.ToString()));
    }

    private GoldpathJobsAdminService<BulkDb> Admin => _host.Services.GetRequiredService<GoldpathJobsAdminService<BulkDb>>();

    private GoldpathBulkAdminService<BulkDb> BulkAdmin => _host.Services.GetRequiredService<GoldpathBulkAdminService<BulkDb>>();

    private GoldpathBulkEngine<BulkDb> Engine => _host.Services.GetRequiredService<GoldpathBulkEngine<BulkDb>>();

    private T Query<T>(Func<BulkDb, T> query)
    {
        using var scope = _host.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<BulkDb>());
    }

    /// <summary>D9 discovery is store-driven: wait until the executor persisted its jobs before verbing.</summary>
    private async Task WaitForFleetAsync(CancellationToken token)
    {
        while (true)
        {
            var jobs = await Admin.GetJobsAsync("bulk-it", token);
            if (jobs.Count >= 2)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), token);
        }
    }

    private async Task<GoldpathBulkBatch> WaitForStateAsync(Guid batchId, GoldpathBulkBatchState state, CancellationToken token)
    {
        while (true)
        {
            var batch = Query(db => db.Set<GoldpathBulkBatch>().AsNoTracking().Single(b => b.Id == batchId));
            if (batch.State == state)
            {
                return batch;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), token);
        }
    }

    [Fact]
    public async Task The_finance_story_holds_end_to_end_with_repair_and_replay()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        // UPLOAD through the ADMIN VERB: 10 rows, rows 4 and 8 poisoned at EXECUTION
        // (validation-clean). The verb fires the validate run IMMEDIATELY — no manual
        // trigger and no waiting for the cron safety net.
        await WaitForFleetAsync(timeout.Token);
        var uploaded = await BulkAdmin.UploadAsync("payments", Csv(10, 4, 8), "payments.csv", null, "it-operator", timeout.Token);
        var batchId = uploaded.Id;

        // DEDUP speaks through the verb too: the retry storm answers with the same batch.
        var again = await BulkAdmin.UploadAsync("payments", Csv(10, 4, 8), "payments.csv", null, "it-operator", timeout.Token);
        Assert.Equal(batchId, again.Id);

        var validated = await WaitForStateAsync(batchId, GoldpathBulkBatchState.Validated, timeout.Token);
        Assert.Equal(10, validated.TotalRows);
        Assert.Equal(10, validated.ValidRows);   // poison is an EXECUTION failure, not a validation one

        // The definitions view sees the batch waiting at the gate.
        var status = Assert.Single(await BulkAdmin.GetDefinitionsAsync(timeout.Token));
        Assert.Equal(1, status.AwaitingApproval);

        // THE GATE (actor stamped), then EXECUTE as a real run.
        Assert.True((await BulkAdmin.ApproveAsync(batchId, "treasurer", "four-eyes done", timeout.Token)).Ok);

        Assert.True((await Admin.TriggerAsync("bulk-it", "GoldpathBulkExecuteJob`1", dryRun: false, "it-operator", timeout.Token)).Ok);
        var partial = await WaitForStateAsync(batchId, GoldpathBulkBatchState.CompletedWithFailures, timeout.Token);
        Assert.Equal(8, partial.ExecutedRows);
        Assert.Equal(2, partial.FailedRows);
        Assert.NotNull(partial.RunId);

        // The repair queue holds EXACTLY the poisoned rows; the sink holds each paid row ONCE.
        var detail = await Admin.GetRunAsync(partial.RunId!.Value, timeout.Token);
        Assert.NotNull(detail);
        Assert.Equal(2, detail.OpenFailures.Count);
        Assert.All(detail.OpenFailures, f => Assert.Contains("core banking refused", f.Reason, StringComparison.Ordinal));
        Assert.Equal(8, Query(db => db.Sink.Count()));
        Assert.Equal(8, Query(db => db.Sink.Select(s => s.RowNumber).Distinct().Count()));

        // HEAL the world, then the admin replay verb — rows 4 and 8 pay, the batch flips.
        Query(db =>
        {
            foreach (var row in db.Set<GoldpathBulkRow>().Where(r => r.BatchId == batchId && r.FailedAt != null))
            {
                row.Payload = row.Payload.Replace("FAIL", "ok", StringComparison.Ordinal);
            }

            return db.SaveChanges();
        });
        Assert.True((await Admin.ReplayItemsAsync(partial.RunId.Value, "it-operator", timeout.Token)).Ok);

        var healed = await WaitForStateAsync(batchId, GoldpathBulkBatchState.Completed, timeout.Token);
        Assert.Equal(10, healed.ExecutedRows);
        Assert.Equal(0, healed.FailedRows);
        Assert.Equal(10, Query(db => db.Sink.Count()));
        Assert.Equal(10, Query(db => db.Sink.Select(s => s.RowNumber).Distinct().Count()));   // exactly once, each
    }

    [Fact]
    public async Task The_gate_refuses_a_dirty_batch_and_a_reasonless_rejection()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        Guid batchId;
        using (var scope = _host.Services.CreateScope())
        {
            var csv = new MemoryStream(Encoding.UTF8.GetBytes("EndToEndId,Iban,Amount,Note\nD1,TR1,-5,\nD2,TR2,10,\n"));
            var (batch, _) = await Engine.IngestAsync(scope.ServiceProvider, "payments", csv, "dirty.csv", null, timeout.Token);
            batchId = batch.Id;
            await Engine.ValidateBatchAsync(scope.ServiceProvider, batchId, timeout.Token);

            var approve = await Engine.ApproveAsync(scope.ServiceProvider, batchId, "treasurer", null, timeout.Token);
            Assert.False(approve.Ok);
            Assert.Contains("invalid rows block approval", approve.Message, StringComparison.Ordinal);

            Assert.False((await Engine.RejectAsync(scope.ServiceProvider, batchId, "treasurer", "", timeout.Token)).Ok);
            Assert.True((await Engine.RejectAsync(scope.ServiceProvider, batchId, "treasurer", "negative amount on row 1", timeout.Token)).Ok);
        }

        var rejected = Query(db => db.Set<GoldpathBulkBatch>().AsNoTracking().Single(b => b.Id == batchId));
        Assert.Equal(GoldpathBulkBatchState.Rejected, rejected.State);
        Assert.Equal("treasurer", rejected.DecidedBy);
    }
}
