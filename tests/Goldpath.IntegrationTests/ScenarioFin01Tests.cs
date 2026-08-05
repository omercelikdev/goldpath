using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Http.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.PostgreSql;
using Xunit;

namespace Goldpath.Tests.Integration;

/// <summary>
/// S-FIN-01 (scenario campaign): "A salary file with one bad row pays everyone except
/// that row, after a second pair of eyes." 10 000 rows on real PostgreSQL with the real
/// jobs runner and a real SMTP relay; row 4 121 is refused by core banking at EXECUTION
/// (validation-clean — a validation failure would BLOCK the gate, which is its own,
/// already-proven refusal). The clerk uploads, the SUPERVISOR approves — two identities,
/// both stamped; 9 999 pay exactly once; 4 121 lands in the repair queue with the
/// refusal's own words; the row's owner is notified with an evidence trail AND a real
/// message in the inbox. Replay: dotnet test --filter FullyQualifiedName~ScenarioFin01.
/// </summary>
[Collection("quartz-process-globals")]
public sealed class ScenarioFin01Tests : IAsyncLifetime
{
    public sealed class SalaryRow
    {
        public string EndToEndId { get; set; } = "";
        public string Iban { get; set; } = "";
        public decimal Amount { get; set; }
        public string? Note { get; set; }
    }

    /// <summary>One row per payout — the double-payment detector.</summary>
    public sealed class PayoutSink
    {
        public long Id { get; set; }
        public string EndToEndId { get; set; } = "";
        public int RowNumber { get; set; }
    }

    public sealed class SalaryDb(DbContextOptions<SalaryDb> options) : DbContext(options)
    {
        public DbSet<PayoutSink> Sink => Set<PayoutSink>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddGoldpathBulk();
            modelBuilder.AddGoldpathNotification();
            modelBuilder.AddGoldpathJobs();
        }
    }

    public sealed class SalaryHandler(SalaryDb db) : IGoldpathBulkRowHandler<SalaryRow>
    {
        public async Task ExecuteAsync(SalaryRow row, GoldpathBulkRowContext context, CancellationToken cancellationToken)
        {
            if (row.Note == "FAIL")
            {
                throw new InvalidOperationException("core banking refused the instruction: IBAN failed clearing");
            }

            db.Sink.Add(new PayoutSink { EndToEndId = row.EndToEndId, RowNumber = context.RowNumber });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// APP code, exactly the shape the template teaches (GP1601: the app requests, the
    /// module sends): after a partial run, map every repair-queue failure to ONE
    /// dedup-keyed notification to the row's owner. The scenario drives THIS — the
    /// causal chain (execute fails → owner notified) runs through a real component,
    /// never a hand-built request (review R1 on the introducing PR).
    /// </summary>
    /// <summary>
    /// APP code, exactly the shape the template teaches (GP1601: the app requests, the
    /// module sends): after a partial run, resolve the batch's FAILED rows from the
    /// app's own data (the payload carries the business identity — the repair queue's
    /// ItemKey is a run coordinate, not a business key), pair them with the run's
    /// refusal reasons, and request ONE dedup-keyed notification per row to its owner.
    /// The scenario drives THIS — the causal chain (execute fails → owner notified)
    /// runs through a real component, never a hand-built request (review R1).
    /// </summary>
    public sealed class RepairQueueNotifier(SalaryDb db, GoldpathJobsAdminService<SalaryDb> admin, IGoldpathNotifier notifier)
    {
        public async Task<IReadOnlyList<Guid>> NotifyOpenFailuresAsync(Guid batchId, Guid runId, CancellationToken ct)
        {
            var detail = await admin.GetRunAsync(runId, ct)
                ?? throw new InvalidOperationException($"run {runId} not found");
            var reasons = string.Join("; ", detail.OpenFailures.Select(f => f.Reason).Distinct(StringComparer.Ordinal));

            var failedRows = await db.Set<GoldpathBulkRow>().AsNoTracking()
                .Where(r => r.BatchId == batchId && r.FailedAt != null)
                .ToListAsync(ct);
            var ids = new List<Guid>();
            foreach (var row in failedRows)
            {
                var payload = System.Text.Json.JsonSerializer.Deserialize<SalaryRow>(row.Payload)
                    ?? throw new InvalidOperationException($"row {row.RowNumber} has no payload");
                var notification = await notifier.RequestAsync(new GoldpathNotificationRequest(
                    "salary-row-failed", "email", OwnerOf(payload.EndToEndId), "",
                    new Dictionary<string, string>
                    {
                        ["EndToEndId"] = payload.EndToEndId,
                        ["Reason"] = reasons,
                    },
                    dedupKey: $"salary-row-failed:{batchId}:{row.RowNumber}"), ct);
                ids.Add(notification.Id);
            }

            return ids;
        }

        // The owner lookup is the app's own concern (HR directory in real life).
        private static string OwnerOf(string endToEndId) => $"owner-{endToEndId.ToLowerInvariant()}@example.test";
    }

    private const int Rows = 10_000;
    private const int BadRow = 4_121;

    private readonly string _fleet = $"fin01-{Guid.NewGuid():N}"[..16];   // Quartz's SchedulerRepository is process-global
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly IContainer _smtp = new ContainerBuilder("rnwood/smtp4dev:3.8.6")
        .WithPortBinding(25, assignRandomHostPort: true)
        .WithPortBinding(80, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(80).ForPath("/api/version")))
        .Build();
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _smtp.StartAsync());
        await using (var db = new SalaryDb(new DbContextOptionsBuilder<SalaryDb>().UseNpgsql(_postgres.GetConnectionString()).Options))
        {
            await db.Database.EnsureCreatedAsync();
        }

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:salarydb"] = _postgres.GetConnectionString();
        builder.Services.AddDbContext<SalaryDb>(o => o.UseNpgsql(_postgres.GetConnectionString()));
        builder.Services.AddScoped<IGoldpathBulkRowHandler<SalaryRow>, SalaryHandler>();
        builder.Services.AddScoped<RepairQueueNotifier>();
        builder.AddGoldpathBulk<HostApplicationBuilder, SalaryDb>(bulk =>
        {
            bulk.ChunkSize = 500;   // 20 checkpoints across the file
            bulk.AddBatch<SalaryRow>("salaries", b => b
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
        builder.AddGoldpathNotification<HostApplicationBuilder, SalaryDb>(notification =>
        {
            notification.Email(e =>
            {
                e.Host = _smtp.Hostname;
                e.Port = _smtp.GetMappedPublicPort(25);
                e.From = "payroll@goldpath.local";
                // The dev relay speaks plaintext — the A3 contract makes that an explicit pair.
                e.UseSsl = false;
                e.AllowInsecureTransport = true;
            });
            notification.AddTemplate("salary-row-failed", t => t
                .Channel("email", c => c
                    .Subject("", "Salary instruction {{EndToEndId}} could not be paid")
                    .Body("", "Instruction {{EndToEndId}} was refused: {{Reason}}. It sits in the repair queue for replay."))
                .DeleteBodyAfter(TimeSpan.FromDays(90)));
        });
        builder.AddGoldpathJobs<HostApplicationBuilder, SalaryDb>(jobs =>
        {
            jobs.ConnectionName = "salarydb";
            jobs.SchedulerName = _fleet;
            // Far-future crons: the scenario drives every run through the ADMIN verbs, like an operator.
            jobs.AddGoldpathBulkJobs<SalaryDb>(validateCron: "0 0 0 1 1 ? 2099", executeCron: "0 0 0 1 1 ? 2099");
            jobs.AddGoldpathNotificationJobs<SalaryDb>(sendCron: "0 0 0 1 1 ? 2099", retentionCron: "0 0 0 1 1 ? 2099");
        });
        _host = builder.Build();
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        QuartzProcessGlobals.Pin();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _smtp.DisposeAsync().AsTask());
    }

    private GoldpathJobsAdminService<SalaryDb> Admin => _host.Services.GetRequiredService<GoldpathJobsAdminService<SalaryDb>>();

    private GoldpathBulkAdminService<SalaryDb> BulkAdmin => _host.Services.GetRequiredService<GoldpathBulkAdminService<SalaryDb>>();

    private T Query<T>(Func<SalaryDb, T> query)
    {
        using var scope = _host.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<SalaryDb>());
    }

    private static MemoryStream SalaryFile()
    {
        var text = new StringBuilder("EndToEndId,Iban,Amount,Note\n");
        for (var i = 1; i <= Rows; i++)
        {
            text.Append($"SAL-{i:D5},TR{i:D24},{1000 + i % 500},{(i == BadRow ? "FAIL" : "")}\n");
        }

        return new MemoryStream(Encoding.UTF8.GetBytes(text.ToString()));
    }

    private async Task WaitForFleetAsync(CancellationToken token)
    {
        while (true)
        {
            try
            {
                if ((await Admin.GetJobsAsync(_fleet, token)).Count >= 4)
                {
                    return;
                }
            }
            catch (Quartz.JobPersistenceException)
            {
                // Connection warm-up on a container that JUST reported ready — transient.
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

            await Task.Delay(TimeSpan.FromMilliseconds(500), token);
        }
    }

    [Fact]
    public async Task A_salary_file_with_one_bad_row_pays_everyone_except_that_row_after_a_second_pair_of_eyes()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        await WaitForFleetAsync(timeout.Token);

        // GIVEN a 10 000-row salary file, row 4 121 doomed at the core-banking port.
        // WHEN the clerk uploads it (the verb fires validation immediately)…
        var uploaded = await BulkAdmin.UploadAsync("salaries", SalaryFile(), "salaries-2026-08.csv", null, "payroll-clerk", timeout.Token);
        var validated = await WaitForStateAsync(uploaded.Id, GoldpathBulkBatchState.Validated, timeout.Token);
        Assert.Equal(Rows, validated.TotalRows);
        Assert.Equal(Rows, validated.ValidRows);   // the bad row is execution-bad, not intake-bad

        // …and a DIFFERENT user approves — the second pair of eyes. The gate actor is
        // stamped on the batch (DecidedBy); the CLERK's identity is durable too, as the
        // validate trigger's actor in the jobs admin audit — two identities, two records.
        Assert.True((await BulkAdmin.ApproveAsync(uploaded.Id, "payroll-supervisor", "totals checked against HR export", timeout.Token)).Ok);
        var approved = Query(db => db.Set<GoldpathBulkBatch>().AsNoTracking().Single(b => b.Id == uploaded.Id));
        Assert.Equal("payroll-supervisor", approved.DecidedBy);
        var audit = await Admin.GetAuditAsync(take: 50, timeout.Token);
        Assert.Contains(audit, entry => entry.Actor == "payroll-clerk");
        Assert.NotEqual("payroll-clerk", approved.DecidedBy);

        // …then the execute run pays the file.
        Assert.True((await Admin.TriggerAsync(_fleet, "GoldpathBulkExecuteJob`1", dryRun: false, "payroll-supervisor", timeout.Token)).Ok);
        var partial = await WaitForStateAsync(uploaded.Id, GoldpathBulkBatchState.CompletedWithFailures, timeout.Token);

        // THEN 9 999 executed EXACTLY once…
        Assert.Equal(Rows - 1, partial.ExecutedRows);
        Assert.Equal(1, partial.FailedRows);
        Assert.Equal(Rows - 1, Query(db => db.Sink.Count()));
        Assert.Equal(Rows - 1, Query(db => db.Sink.Select(s => s.RowNumber).Distinct().Count()));
        Assert.DoesNotContain($"SAL-{BadRow:D5}", Query(db => db.Sink.Select(s => s.EndToEndId).ToList()));

        // …row 4 121 sits in the repair queue with the refusal's own words…
        Assert.NotNull(partial.RunId);
        var detail = await Admin.GetRunAsync(partial.RunId!.Value, timeout.Token);
        Assert.NotNull(detail);
        var failure = Assert.Single(detail!.OpenFailures);
        Assert.Contains("core banking refused", failure.Reason, StringComparison.Ordinal);

        // …and the row's owner is notified through the APP's own failure→notification
        // component: repair queue in, dedup-keyed requests out (GP1601). Driving the
        // component IS the causal chain the story claims (review R1).
        Guid notificationId;
        using (var scope = _host.Services.CreateScope())
        {
            var ids = await scope.ServiceProvider.GetRequiredService<RepairQueueNotifier>()
                .NotifyOpenFailuresAsync(uploaded.Id, partial.RunId!.Value, timeout.Token);
            notificationId = Assert.Single(ids);

            // Replaying the component cannot double-mail: the dedup key answers the same id.
            var again = await scope.ServiceProvider.GetRequiredService<RepairQueueNotifier>()
                .NotifyOpenFailuresAsync(uploaded.Id, partial.RunId!.Value, timeout.Token);
            Assert.Equal(notificationId, Assert.Single(again));
        }

        Assert.True((await Admin.TriggerAsync(_fleet, "GoldpathNotificationSendJob`1", dryRun: false, "payroll-supervisor", timeout.Token)).Ok);
        while (Query(db => db.Set<GoldpathNotification>().AsNoTracking().Single(n => n.Id == notificationId).State)
               != GoldpathNotificationState.Sent)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token);
        }

        // The evidence row AND the real inbox agree.
        using var http = new HttpClient { BaseAddress = new Uri($"http://{_smtp.Hostname}:{_smtp.GetMappedPublicPort(80)}") };
        var inbox = await http.GetFromJsonAsync<Smtp4DevPage>("/api/messages", timeout.Token);
        var message = Assert.Single(inbox!.Results);
        Assert.Contains($"SAL-{BadRow:D5}", message.Subject, StringComparison.Ordinal);
        Assert.Contains("owner-sal-04121@example.test", message.To);
    }

    private sealed record Smtp4DevPage(List<Smtp4DevMessage> Results);

    private sealed record Smtp4DevMessage(string Subject, List<string> To);
}
