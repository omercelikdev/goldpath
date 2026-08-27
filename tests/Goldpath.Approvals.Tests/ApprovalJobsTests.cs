using Goldpath;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Goldpath.Approvals.Tests;

/// <summary>
/// The escalation sweep as a Jobs run: what <c>AddGoldpathApprovalsJobs</c> registers, and
/// the sweep executing end to end on the real runner — an overdue rung moves up without
/// anyone hand-rolling a loop.
/// </summary>
public sealed class ApprovalJobsTests : IDisposable
{
    public sealed class JobsHostContext(DbContextOptions<JobsHostContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.AddGoldpathJobs();
    }

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public ApprovalJobsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _provider = new ServiceCollection()
            .AddDbContext<JobsHostContext>(b => b.UseSqlite(_connection))
            .BuildServiceProvider(true);
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<JobsHostContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void The_job_pack_registers_the_sweep_with_its_defaults()
    {
        var jobs = new GoldpathJobsOptions();
        jobs.AddGoldpathApprovalsJobs();

        var sweep = Assert.Single(jobs.Jobs);
        Assert.Equal(typeof(GoldpathApprovalEscalationJob), sweep.JobType);
        Assert.Equal("0 */5 * * * ?", sweep.Cron);              // five-minute granularity never moves an hour-scale SLA
        Assert.Equal(TimeSpan.FromMinutes(5), sweep.Deadline);  // GP1302: every job declares its deadline
        Assert.Equal(1, sweep.MaxParallelChunks);               // one sweep, one pass
    }

    [Fact]
    public void The_cron_and_deadline_stay_declarable()
    {
        var jobs = new GoldpathJobsOptions();
        jobs.AddGoldpathApprovalsJobs(escalationCron: "0 0 * * * ?", deadline: TimeSpan.FromMinutes(10));

        var sweep = Assert.Single(jobs.Jobs);
        Assert.Equal("0 0 * * * ?", sweep.Cron);
        Assert.Equal(TimeSpan.FromMinutes(10), sweep.Deadline);
    }

    [Fact]
    public async Task The_sweep_escalates_an_overdue_request_on_the_real_runner()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-27T09:00:00Z"));
        var store = new GoldpathInMemoryApprovalStore();
        var engine = new GoldpathApprovalEngine(
            new GoldpathApprovalsOptions().AddLadder("credit-limit", l => l
                .Rung("expert", 1_000_000m, TimeSpan.FromHours(8))
                .TopRung("general-manager", TimeSpan.FromHours(24))),
            store, clock, NullLogger<GoldpathApprovalEngine>.Instance);

        var request = await engine.RequestAsync("credit-limit", "K26-400", 500_000m, "maker");
        clock.Advance(TimeSpan.FromHours(8));

        var jobs = new GoldpathJobsOptions();
        jobs.AddGoldpathApprovalsJobs();
        var runner = new GoldpathJobRunner<JobsHostContext>(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            clock, NullLogger<GoldpathJobRunner<JobsHostContext>>.Instance);

        var status = await runner.RunAsync(
            new GoldpathApprovalEscalationJob(engine),
            jobs.Jobs.Single(),
            new GoldpathFireFacts("approvals", "n1", "f1", false),
            CancellationToken.None);

        Assert.Equal(GoldpathJobRunStatus.Completed, status);
        var reloaded = await store.GetAsync(request.Id);
        Assert.Equal("general-manager", reloaded!.PendingRole);
        Assert.Contains(reloaded.Trail, t => t.Action == "escalated");
    }
}
