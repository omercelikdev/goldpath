using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Goldpath.Jobs.Tests;

/// <summary>A row a test job can write; duplicate keys poison the runner's checkpoint save.</summary>
public sealed class WorkRow
{
    public int Key { get; set; }
    public string Value { get; set; } = "";
}

public class JobsTestContext(DbContextOptions<JobsTestContext> options) : DbContext(options)
{
    public DbSet<WorkRow> WorkRows => Set<WorkRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkRow>(row =>
        {
            row.HasKey(r => r.Key);
            row.Property(r => r.Key).ValueGeneratedNever();
        });
        modelBuilder.AddGoldpathJobs();
    }
}

/// <summary>
/// A sqlite-backed harness around the REAL runner: real EF claims, real checkpoints, no
/// Quartz (the runner is scheduler-agnostic on purpose — Quartz arrives in integration).
/// </summary>
public sealed class RunnerFixture : IDisposable
{
    private readonly SqliteConnection _connection;

    public RunnerFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<JobsTestContext>(o => o.UseSqlite(_connection));
        services.AddSingleton(TimeProvider.System);
        Services = services.BuildServiceProvider();

        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<JobsTestContext>().Database.EnsureCreated();

        Runner = new GoldpathJobRunner<JobsTestContext>(
            Services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<GoldpathJobRunner<JobsTestContext>>.Instance);
    }

    public ServiceProvider Services { get; }

    public GoldpathJobRunner<JobsTestContext> Runner { get; }

    public GoldpathFireFacts Fire(string id = "fire-1", string instance = "node-a")
        => new("test-scheduler", instance, id, Recovering: false);

    public T Query<T>(Func<JobsTestContext, T> query)
    {
        using var scope = Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<JobsTestContext>());
    }

    public void Mutate(Action<JobsTestContext> mutate)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JobsTestContext>();
        mutate(db);
        db.SaveChanges();
    }

    public void Dispose()
    {
        Services.Dispose();
        _connection.Dispose();
    }
}

/// <summary>A configurable job: counts executions, can fail chunks, can cancel mid-run.</summary>
public sealed class ScriptedJob : IGoldpathJob
{
    public int TotalItems { get; init; } = 10;
    public int ChunkSize { get; init; } = 2;

    /// <summary>Chunk indexes that always throw.</summary>
    public HashSet<int> PoisonChunks { get; } = [];

    /// <summary>Item keys reported as failed (per chunk) without failing the chunk.</summary>
    public Func<GoldpathJobChunk, IEnumerable<(string, string)>>? ItemFailures { get; init; }

    /// <summary>Rows added into the RUNNER'S context per chunk — they commit WITH the checkpoint.</summary>
    public Func<GoldpathJobChunk, IEnumerable<int>>? WorkRowKeys { get; init; }

    /// <summary>Cancels this source after N successful chunk executions (interrupt simulation).</summary>
    public CancellationTokenSource? CancelAfterChunks { get; init; }
    public int CancelThreshold { get; init; }

    public ConcurrentBag<int> ExecutedChunks { get; } = [];
    public List<int> ExecutionOrder { get; } = [];
    public List<bool> ResumedFlags { get; } = [];
    public string? SeenInputVersion { get; private set; }

    public Task<GoldpathJobPlan> PlanAsync(GoldpathJobContext context, CancellationToken cancellationToken)
        => Task.FromResult(GoldpathJobPlanner.ByRange(TotalItems, ChunkSize));

    public Task ExecuteChunkAsync(GoldpathJobChunk chunk, GoldpathJobContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (PoisonChunks.Contains(chunk.Index))
        {
            throw new InvalidOperationException($"poison chunk {chunk.Index}");
        }

        lock (ResumedFlags)
        {
            ResumedFlags.Add(context.Resumed);
            ExecutionOrder.Add(chunk.Index);
        }

        SeenInputVersion = context.InputVersion;
        ExecutedChunks.Add(chunk.Index);
        if (WorkRowKeys is not null)
        {
            var db = context.Services.GetRequiredService<JobsTestContext>();
            foreach (var key in WorkRowKeys(chunk))
            {
                db.WorkRows.Add(new WorkRow { Key = key, Value = $"chunk-{chunk.Index}" });
            }
        }

        foreach (var (key, reason) in ItemFailures?.Invoke(chunk) ?? [])
        {
            chunk.ReportItemFailure(key, reason);
        }

        if (CancelAfterChunks is not null && ExecutedChunks.Count >= CancelThreshold)
        {
            CancelAfterChunks.Cancel();
        }

        return Task.CompletedTask;
    }
}
