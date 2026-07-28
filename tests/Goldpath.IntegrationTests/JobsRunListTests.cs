using Goldpath.Jobs.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Testcontainers.PostgreSql;
using Xunit;

namespace Goldpath.Tests.Integration;

/// <summary>
/// The run list of the admin contract's R2.4 — filters and the keyset walk — against REAL
/// PostgreSQL, because that is where it has to work.
/// <para>
/// It cannot be proven anywhere cheaper: the list orders by <c>DateTimeOffset</c>, which
/// SQLite refuses to translate outright, so the package's own sqlite fixture would only
/// prove behaviour on a store Goldpath never ships on. Finding that out is itself the
/// point — until R2 there was no storage-level test over this query at all.
/// </para>
/// </summary>
public sealed class JobsRunListTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = Db();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Runs_narrow_to_a_status_and_to_a_window()
    {
        var admin = Admin();
        var noon = new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);
        await SeedAsync(("nightly", GoldpathJobRunStatus.Completed, noon.AddHours(-26)),
                        ("nightly", GoldpathJobRunStatus.Failed, noon.AddHours(-2)),
                        ("nightly", GoldpathJobRunStatus.Completed, noon.AddHours(-1)));

        var failed = await admin.GetRunsAsync("fleet", job: null, take: 50, default, status: GoldpathJobRunStatus.Failed);
        var yesterday = await admin.GetRunsAsync("fleet", job: null, take: 50, default,
            from: noon.AddHours(-30), to: noon.AddHours(-24));

        Assert.Single(failed);
        Assert.Equal(GoldpathJobRunStatus.Failed, failed[0].Status);
        // The window includes its ends and nothing else — an operator asking for yesterday
        // must not be handed this morning's run as well.
        Assert.Single(yesterday);
        Assert.Equal(noon.AddHours(-26), yesterday[0].StartedAt);
    }

    [Fact]
    public async Task The_keyset_walk_visits_every_run_exactly_once_even_when_they_start_in_the_SAME_instant()
    {
        var admin = Admin();
        // The case a StartedAt-only cursor loses: a fleet whose jobs fire together, so
        // several runs share an instant to the tick. A single-column cursor would either
        // skip the siblings or serve them forever.
        var instant = new DateTimeOffset(2026, 7, 28, 3, 0, 0, TimeSpan.Zero);
        await SeedAsync(("a", GoldpathJobRunStatus.Completed, instant),
                        ("b", GoldpathJobRunStatus.Completed, instant),
                        ("c", GoldpathJobRunStatus.Completed, instant),
                        ("d", GoldpathJobRunStatus.Completed, instant.AddMinutes(-5)),
                        ("e", GoldpathJobRunStatus.Completed, instant.AddMinutes(-10)));

        var seen = new List<Guid>();
        Guid? cursor = null;
        for (var page = 0; page < 10; page++)
        {
            var batch = await admin.GetRunsAsync("fleet", job: null, take: 2, default, afterId: cursor);
            if (batch.Count == 0)
            {
                break;
            }

            seen.AddRange(batch.Select(r => r.Id));
            cursor = batch[^1].Id;
        }

        Assert.Equal(5, seen.Count);                 // nothing skipped
        Assert.Equal(5, seen.Distinct().Count());    // nothing served twice
    }

    [Fact]
    public async Task A_cursor_that_names_no_run_ENDS_the_walk_instead_of_restarting_it()
    {
        var admin = Admin();
        await SeedAsync(("a", GoldpathJobRunStatus.Completed, new DateTimeOffset(2026, 7, 28, 1, 0, 0, TimeSpan.Zero)));

        var page = await admin.GetRunsAsync("fleet", job: null, take: 50, default, afterId: Guid.NewGuid());

        // Ignoring an unknown cursor would answer with page one, which reads as "the list
        // started over" — a caller paging through an incident would loop forever.
        Assert.Empty(page);
    }

    // ── harness ────────────────────────────────────────────────────────────────

    private ClusterDb Db() => new(new DbContextOptionsBuilder<ClusterDb>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private async Task SeedAsync(params (string Job, string Status, DateTimeOffset StartedAt)[] runs)
    {
        await using var db = Db();
        // Each test owns the table: a filter proof reading another test's rows proves
        // nothing about the filter.
        await db.Set<GoldpathJobRun>().ExecuteDeleteAsync();
        foreach (var (job, status, startedAt) in runs)
        {
            db.Add(new GoldpathJobRun
            {
                Id = Guid.NewGuid(),
                SchedulerName = "fleet",
                JobName = job,
                Status = status,
                StartedAt = startedAt,
            });
        }

        await db.SaveChangesAsync();
    }

    private GoldpathJobsAdminService<ClusterDb> Admin()
    {
        var services = new ServiceCollection();
        var connection = _postgres.GetConnectionString();
        services.AddDbContext<ClusterDb>(o => o.UseNpgsql(connection));
        var provider = services.BuildServiceProvider();
        return new GoldpathJobsAdminService<ClusterDb>(
            new NoSchedulerRegistry(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System);
    }

    /// <summary>The run list never reaches Quartz — asking for a scheduler here is a bug.</summary>
    private sealed class NoSchedulerRegistry : IGoldpathJobsFleetRegistry
    {
        public Task<IReadOnlyList<GoldpathFleetInfo>> GetFleetsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<GoldpathFleetInfo>>([]);

        public Task<IScheduler> GetSchedulerAsync(string schedulerName, CancellationToken ct)
            => throw new InvalidOperationException("the run list reads the store, never the scheduler");
    }
}
