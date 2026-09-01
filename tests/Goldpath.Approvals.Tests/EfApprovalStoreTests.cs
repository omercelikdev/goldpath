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
        // SQLite cannot ORDER BY a raw DateTimeOffset column; the family answer (same as
        // the Bulk/Archival test contexts) is the binary converter convention.
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
            => configurationBuilder.Properties<DateTimeOffset>()
                .HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();

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

    [Fact]
    public async Task Quorum_signatures_survive_the_engine_a_restarted_engine_completes_the_rung()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-26T09:00:00Z"));
        var quorumOptions = new GoldpathApprovalsOptions().AddLadder("payment-run", l => l
            .Rung("manager", 5_000_000m, TimeSpan.FromHours(8), requiredApprovals: 2)
            .TopRung("general-manager", TimeSpan.FromHours(24)));
        GoldpathApprovalEngine BuildQuorumEngine() => new(
            quorumOptions,
            new GoldpathEfApprovalStore<ApprovalsDbContext>(_provider.GetRequiredService<IServiceScopeFactory>()),
            clock, NullLogger<GoldpathApprovalEngine>.Instance);

        var first = BuildQuorumEngine();
        var request = await first.RequestAsync("payment-run", "K26-104", 1_000_000m, "maker");
        await first.DecideAsync(request.Id, "manager-one", "manager", true, "first signature");

        // A brand-new engine over the SAME database: the collected signature is still there —
        // the same signer is barred, a distinct one completes the rung.
        var restarted = BuildQuorumEngine();
        Assert.Equal(GoldpathApprovalDecisionOutcome.AlreadySigned,
            await restarted.DecideAsync(request.Id, "manager-one", "manager", true, "again"));
        Assert.Equal(GoldpathApprovalDecisionOutcome.Applied,
            await restarted.DecideAsync(request.Id, "manager-two", "manager", true, "second signature"));
        Assert.Equal(GoldpathApprovalStatus.Granted, (await Reload(request.Id)).Status);
    }

    [Fact]
    public async Task Recent_comes_newest_first_and_honors_take_on_the_database_store()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-28T09:00:00Z"));
        var engine = BuildEngine(clock);
        var first = await engine.RequestAsync("credit-limit", "K26-200", 500_000m, "maker");
        clock.Advance(TimeSpan.FromMinutes(1));
        var second = await engine.RequestAsync("credit-limit", "K26-201", 500_000m, "maker");
        clock.Advance(TimeSpan.FromMinutes(1));
        var third = await engine.RequestAsync("credit-limit", "K26-202", 500_000m, "maker");

        var store = new GoldpathEfApprovalStore<ApprovalsDbContext>(_provider.GetRequiredService<IServiceScopeFactory>());
        var recent = await store.GetRecentAsync(2);
        Assert.Equal([third.Id, second.Id], recent.Select(r => r.Id));
        _ = first;
    }

    [Fact]
    public async Task Withdraw_and_the_resubmit_chain_round_trip_through_the_database()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-26T09:00:00Z"));
        var engine = BuildEngine(clock);

        var withdrawn = await engine.RequestAsync("credit-limit", "K26-105", 500_000m, "maker");
        Assert.Equal(GoldpathApprovalDecisionOutcome.Applied, await engine.WithdrawAsync(withdrawn.Id, "maker"));
        Assert.Equal(GoldpathApprovalStatus.Withdrawn, (await Reload(withdrawn.Id)).Status);

        var rejected = await engine.RequestAsync("credit-limit", "K26-106", 500_000m, "maker");
        await engine.DecideAsync(rejected.Id, "checker", "expert", false, "collateral missing");
        var resubmitted = await engine.ResubmitAsync(rejected.Id, "maker");

        var reloaded = await Reload(resubmitted.Id);
        Assert.Equal(rejected.Id, reloaded.SupersedesId);
        Assert.Equal(["requested", "rejected", "superseded"], (await Reload(rejected.Id)).Trail.Select(t => t.Action));
    }

    private async Task<GoldpathApprovalRequest> Reload(Guid id)
    {
        var store = new GoldpathEfApprovalStore<ApprovalsDbContext>(_provider.GetRequiredService<IServiceScopeFactory>());
        return await store.GetAsync(id) ?? throw new InvalidOperationException("missing request");
    }
}
