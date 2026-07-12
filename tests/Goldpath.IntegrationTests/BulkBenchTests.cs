using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;

namespace Goldpath.Tests.Integration;

/// <summary>
/// Bulk RFC §6 performance proofs (scripts/bench-bulk.sh → ops/bulk-benchmarks.md).
/// Trait-gated out of the normal suite: evidence, not regression tests.
/// </summary>
[Trait("Category", "Bench")]
public sealed class BulkBenchTests : IAsyncLifetime
{
    private sealed class NopHandler : IGoldpathBulkRowHandler<BulkTests.PaymentRow>
    {
        public Task ExecuteAsync(BulkTests.PaymentRow row, GoldpathBulkRowContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private ServiceProvider _services = null!;
    private GoldpathBulkOptions _options = null!;
    private GoldpathBulkEngine<BulkTests.BulkDb> _engine = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new GoldpathBulkOptions();
        _options.AddBatch<BulkTests.PaymentRow>("payments", b => b
            .MaxRows(1_000_000)
            .RowKey(r => r.EndToEndId)
            .Validate((row, ctx) =>
            {
                if (row.Amount <= 0)
                {
                    ctx.Fail(nameof(row.Amount), "amount must be positive");
                }
            }));

        var services = new ServiceCollection();
        services.AddDbContext<BulkTests.BulkDb>(o => o.UseNpgsql(_postgres.GetConnectionString()));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IGoldpathBulkRowHandler<BulkTests.PaymentRow>, NopHandler>();
        _services = services.BuildServiceProvider();
        using var scope = _services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BulkTests.BulkDb>().Database.EnsureCreatedAsync();

        _engine = new GoldpathBulkEngine<BulkTests.BulkDb>(
            _options, new GoldpathBulkFileStore<BulkTests.BulkDb>(TimeProvider.System),
            TimeProvider.System, NullLogger<GoldpathBulkEngine<BulkTests.BulkDb>>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private static MemoryStream Csv(int rows)
    {
        var text = new StringBuilder("EndToEndId,Iban,Amount,Note\n");
        for (var i = 1; i <= rows; i++)
        {
            text.Append("E").Append(i).Append(",TR").Append(i).Append(',').Append(i % 997 + 1).Append(",\n");
        }

        return new MemoryStream(Encoding.UTF8.GetBytes(text.ToString()));
    }

    [Fact]
    public async Task Bench_the_cards_number_10k_file_ingested_and_validated()
    {
        using var scope = _services.CreateScope();
        var watch = Stopwatch.StartNew();
        var (batch, _) = await _engine.IngestAsync(scope.ServiceProvider, "payments", Csv(10_000), "10k.csv", null, CancellationToken.None);
        var upload = watch.Elapsed;
        await _engine.ValidateBatchAsync(scope.ServiceProvider, batch.Id, CancellationToken.None);
        watch.Stop();

        using var check = _services.CreateScope();
        var validated = await check.ServiceProvider.GetRequiredService<BulkTests.BulkDb>()
            .Set<GoldpathBulkBatch>().AsNoTracking().SingleAsync(b => b.Id == batch.Id);
        Assert.Equal(10_000, validated.ValidRows);
        Console.WriteLine($"BENCH-BULK ingest10k upload={upload.TotalSeconds:F2}s total={watch.Elapsed.TotalSeconds:F2}s (budget 300s)");
        Assert.True(watch.Elapsed < TimeSpan.FromMinutes(5), $"10k intake took {watch.Elapsed.TotalSeconds:F1}s — the card's 5-minute budget is broken");
    }

    [Fact]
    public async Task Bench_execute_throughput_100k_rows_noop_handler()
    {
        Guid batchId;
        using (var scope = _services.CreateScope())
        {
            var (batch, _) = await _engine.IngestAsync(scope.ServiceProvider, "payments", Csv(100_000), "100k.csv", null, CancellationToken.None);
            batchId = batch.Id;
            await _engine.ValidateBatchAsync(scope.ServiceProvider, batchId, CancellationToken.None);
            var validated = await scope.ServiceProvider.GetRequiredService<BulkTests.BulkDb>()
                .Set<GoldpathBulkBatch>().AsNoTracking().SingleAsync(b => b.Id == batchId);
            Assert.Equal(GoldpathBulkBatchState.Validated, validated.State);
            Assert.True((await _engine.ApproveAsync(scope.ServiceProvider, batchId, "bench", null, CancellationToken.None)).Ok);
        }

        var watch = Stopwatch.StartNew();
        using (var scope = _services.CreateScope())
        {
            var adopted = await _engine.AdoptForExecutionAsync(scope.ServiceProvider, Guid.NewGuid(), CancellationToken.None);
            var batch = adopted.Single();
            var chunkIndex = 0;
            for (long start = 1; start <= batch.TotalRows; start += _options.ChunkSize)
            {
                var end = Math.Min(start + _options.ChunkSize, batch.TotalRows + 1);
                var chunk = (GoldpathJobChunk)Activator.CreateInstance(typeof(GoldpathJobChunk),
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, [chunkIndex++, $"{batch.Id:N}|{start}:{end}"], null)!;
                await _engine.ExecuteRangeAsync(scope.ServiceProvider, chunk, batch.Id, start, end, CancellationToken.None);
            }
        }

        watch.Stop();
        var rowsPerSecond = 100_000 / watch.Elapsed.TotalSeconds;
        Console.WriteLine($"BENCH-BULK execute100k={watch.Elapsed.TotalSeconds:F1}s rows/s={rowsPerSecond:F0} (chunk {_options.ChunkSize}, claim+stamp overhead only)");

        using var verify = _services.CreateScope();
        var done = await verify.ServiceProvider.GetRequiredService<BulkTests.BulkDb>()
            .Set<GoldpathBulkBatch>().AsNoTracking().SingleAsync(b => b.Id == batchId);
        Assert.Equal(GoldpathBulkBatchState.Completed, done.State);
        Assert.Equal(100_000, done.ExecutedRows);
    }
}
