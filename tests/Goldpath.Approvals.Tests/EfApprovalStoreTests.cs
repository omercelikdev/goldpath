using Goldpath;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Goldpath.Approvals.Tests;

/// <summary>
/// The database-backed store on a real (SQLite) database: requests, trails and delegations
/// SURVIVE the store instance — a "restarted" engine over the same database sees the same
/// worklist, which is exactly what the in-memory store cannot promise.
/// </summary>
public sealed class EfApprovalStoreTests : IDisposable
{
    public sealed class ApprovalsDbContext(DbContextOptions<ApprovalsDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.AddGoldpathApprovalModel();
    }

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public EfApprovalStoreTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _provider = new ServiceCollection()
            .AddDbContext<ApprovalsDbContext>(b => b.UseSqlite(_connection))
            .BuildServiceProvider(true);
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApprovalsDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private GoldpathApprovalEngine BuildEngine(FakeTimeProvider clock) => new(
        new GoldpathApprovalsOptions().AddLadder("credit-limit", l => l
            .Rung("expert", 1_000_000m, TimeSpan.FromHours(8))
            .TopRung("general-manager", TimeSpan.FromHours(24))),
        new GoldpathEfApprovalStore<ApprovalsDbContext>(_provider.GetRequiredService<IServiceScopeFactory>()),
        clock, NullLogger<GoldpathApprovalEngine>.Instance);

    [Fact]
    public async Task Requests_survive_the_engine_a_restarted_engine_sees_the_same_worklist()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-18T09:00:00Z"));
        var first = BuildEngine(clock);
        var request = await first.RequestAsync("credit-limit", "K26-100", 500_000m, "maker");

        // A brand-new engine + store over the SAME database — the restart.
        var restarted = BuildEngine(clock);
        var worklist = await restarted.WorklistAsync("checker", "expert");
        Assert.Equal([request.Id], worklist.Select(r => r.Id));

        Assert.Equal(GoldpathApprovalDecisionOutcome.Applied,
            await restarted.DecideAsync(request.Id, "checker", "expert", true, "fits"));
        Assert.Equal(GoldpathApprovalStatus.Granted, (await Reload(request.Id)).Status);
    }

    [Fact]
    public async Task The_trail_round_trips_through_the_json_column()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-18T09:00:00Z"));
        var engine = BuildEngine(clock);
        var request = await engine.RequestAsync("credit-limit", "K26-101", 500_000m, "maker");
        clock.Advance(TimeSpan.FromHours(8));
        await engine.EscalateOverdueAsync();
        await engine.DecideAsync(request.Id, "gm", "general-manager", false, "declined on review");

        var reloaded = await Reload(request.Id);
        Assert.Equal(["requested", "escalated", "rejected"], reloaded.Trail.Select(t => t.Action));
        Assert.Equal("declined on review", reloaded.Reason);
    }

    [Fact]
    public async Task Delegations_persist_and_expire_by_the_clock()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-18T09:00:00Z"));
        var engine = BuildEngine(clock);
        var request = await engine.RequestAsync("credit-limit", "K26-102", 500_000m, "maker");
        await engine.DelegateAsync("expert-user", "stand-in", TimeSpan.FromDays(2));

        // A restarted engine honors the persisted delegation...
        var restarted = BuildEngine(clock);
        Assert.Equal(GoldpathApprovalDecisionOutcome.Applied,
            await restarted.DecideAsync(request.Id, "stand-in", "no-role", true, "delegated"));

        // ...and its expiry.
        var late = await restarted.RequestAsync("credit-limit", "K26-103", 500_000m, "maker");
        clock.Advance(TimeSpan.FromDays(2) + TimeSpan.FromMinutes(1));
        Assert.Equal(GoldpathApprovalDecisionOutcome.WrongRole,
            await restarted.DecideAsync(late.Id, "stand-in", "no-role", true, "too late"));
    }

    private async Task<GoldpathApprovalRequest> Reload(Guid id)
    {
        var store = new GoldpathEfApprovalStore<ApprovalsDbContext>(_provider.GetRequiredService<IServiceScopeFactory>());
        return await store.GetAsync(id) ?? throw new InvalidOperationException("missing request");
    }
}
