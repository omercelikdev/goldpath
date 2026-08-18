using Goldpath;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Goldpath.FileExchange.Tests;

/// <summary>
/// The database-backed ledger on a real (SQLite) database: the zero-duplicate guarantee
/// SURVIVES the engine instance — a "restarted" engine over the same database skips every
/// already-applied row, which is exactly what the in-memory ledger cannot promise.
/// </summary>
public sealed class EfFileLedgerTests : IDisposable
{
    public sealed class LedgerDbContext(DbContextOptions<LedgerDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.AddGoldpathFileExchangeModel();
    }

    private sealed record Row(string Reference, decimal Amount);

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly List<string> _applied = [];

    public EfFileLedgerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _provider = new ServiceCollection()
            .AddDbContext<LedgerDbContext>(b => b.UseSqlite(_connection))
            .BuildServiceProvider(true);
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<LedgerDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private GoldpathFileRailEngine BuildEngine() => new(
        new GoldpathFileExchangeOptions().AddRail<Row>("registry-daily", rail => rail
            .Header(1)
            .ParseLine(line =>
            {
                var parts = line.Split(';');
                return new Row(parts[0], decimal.Parse(parts[1]));
            })
            .ValidateRow(row => row.Amount > 0 ? null : "non-positive amount")
            .Handle((row, _) =>
            {
                _applied.Add(row.Reference);
                return Task.CompletedTask;
            })),
        new GoldpathEfFileLedger<LedgerDbContext>(_provider.GetRequiredService<IServiceScopeFactory>()),
        NullLogger<GoldpathFileRailEngine>.Instance);

    [Fact]
    public async Task Zero_duplicates_survive_a_restart()
    {
        string[] file = ["H;2", "A-1;100.50", "A-2;200.00"];
        var first = BuildEngine();
        Assert.Equal(2, (await first.ProcessAsync("registry-daily", "reg.csv", file)).Processed);

        // A brand-new engine + ledger over the SAME database — the redelivery after restart.
        var restarted = BuildEngine();
        var replay = await restarted.ProcessAsync("registry-daily", "reg.csv", file);
        Assert.Equal(0, replay.Processed);
        Assert.Equal(2, replay.SkippedAsDuplicate);
        Assert.Equal(["A-1", "A-2"], _applied);
    }

    [Fact]
    public async Task Quarantine_persists_with_its_reason_and_clears_on_reprocess()
    {
        string[] broken = ["H;2", "A-1;100.50", "A-2;-1"];
        var engine = BuildEngine();
        await engine.ProcessAsync("registry-daily", "reg2.csv", broken);

        var restarted = BuildEngine();
        var ledger = new GoldpathEfFileLedger<LedgerDbContext>(_provider.GetRequiredService<IServiceScopeFactory>());
        var quarantine = await ledger.GetQuarantineAsync("registry-daily", "reg2.csv");
        Assert.Equal([(3, "non-positive amount")], quarantine.Select(q => (q.Line, q.Reason)));

        string[] fixedFile = ["H;2", "A-1;100.50", "A-2;75.00"];
        var result = await restarted.ProcessAsync("registry-daily", "reg2.csv", fixedFile);
        Assert.Equal(1, result.Processed);
        Assert.Equal(1, result.SkippedAsDuplicate);
        Assert.Empty(await ledger.GetQuarantineAsync("registry-daily", "reg2.csv"));
    }
}
