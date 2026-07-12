using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Goldpath.Archival.Tests;

/// <summary>A claim-shaped aggregate (the insurance card's file): root + owned graph.</summary>
public sealed class Claim
{
    public Guid Id { get; set; }
    public string Reference { get; set; } = "";
    public string? TenantId { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public List<Decision> Decisions { get; set; } = [];
    public List<ClaimDocument> Documents { get; set; } = [];
}

public sealed class Decision
{
    public Guid Id { get; set; }
    public Guid ClaimId { get; set; }
    public string Verdict { get; set; } = "";
}

public sealed class ClaimDocument
{
    public Guid Id { get; set; }
    public Guid ClaimId { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>High-volume detail for the row-retention shape (the telco card's CDRs).</summary>
public sealed class UsageDetail
{
    public long Id { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public bool RolledUp { get; set; }
}

public sealed class ArchiveTestContext(DbContextOptions<ArchiveTestContext> options) : DbContext(options)
{
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<UsageDetail> Usage => Set<UsageDetail>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite cannot compare DateTimeOffset columns — store UTC ticks (order-correct).
        configurationBuilder.Properties<DateTimeOffset>()
            .HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Claim>().Property(c => c.Id).ValueGeneratedNever();
        modelBuilder.Entity<Decision>().Property(d => d.Id).ValueGeneratedNever();
        modelBuilder.Entity<ClaimDocument>().Property(d => d.Id).ValueGeneratedNever();
        modelBuilder.Entity<Claim>().HasMany(c => c.Decisions).WithOne().HasForeignKey(d => d.ClaimId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Claim>().HasMany(c => c.Documents).WithOne().HasForeignKey(d => d.ClaimId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.AddGoldpathArchiveModel();
        modelBuilder.AddGoldpathJobs();   // the runner drives archival in the jobs-composition test
    }
}

public sealed class ArchiveFixture : IDisposable
{
    private readonly SqliteConnection _connection;

    public ArchiveFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<ArchiveTestContext>(o => o.UseSqlite(_connection));
        services.AddSingleton(TimeProvider.System);
        Services = services.BuildServiceProvider();

        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ArchiveTestContext>().Database.EnsureCreated();

        Engine = new GoldpathArchiveEngine<ArchiveTestContext>(
            TimeProvider.System, NullLogger<GoldpathArchiveEngine<ArchiveTestContext>>.Instance);
    }

    public ServiceProvider Services { get; }

    /// <summary>The live in-memory connection — sibling providers share ONE database.</summary>
    public SqliteConnection Connection => _connection;

    public GoldpathArchiveEngine<ArchiveTestContext> Engine { get; }

    /// <summary>The standard claim archive: graph + move + 10y retention.</summary>
    public static GoldpathArchiveDefinition ClaimArchive(bool deleteHot = true, int retainYears = 10)
    {
        var options = new GoldpathArchivalOptions();
        options.AddArchive<Claim>(a =>
        {
            a.Graph(c => c.Decisions, c => c.Documents)
                .Key(c => c.Id)
                .DueWhen(c => c.ClosedAt != null, c => c.ClosedAt!.Value)
                .ArchiveAfter(TimeSpan.FromDays(30))
                .RetainFor(retainYears)
                .Tenant(c => c.TenantId);
            if (deleteHot)
            {
                a.DeleteHotRowsAfterArchive();
            }
        });
        return options.Archives[0];
    }

    public Claim SeedClaim(int decisions = 2, int documents = 1, DateTimeOffset? closedAt = null, string? tenant = null)
    {
        var claim = new Claim
        {
            Id = Guid.NewGuid(),
            Reference = $"CLM-{Guid.NewGuid():N}"[..12],
            TenantId = tenant,
            ClosedAt = closedAt ?? DateTimeOffset.UtcNow.AddDays(-90),
        };
        for (var i = 0; i < decisions; i++)
        {
            claim.Decisions.Add(new Decision { Id = Guid.NewGuid(), ClaimId = claim.Id, Verdict = $"verdict-{i}" });
        }

        for (var i = 0; i < documents; i++)
        {
            claim.Documents.Add(new ClaimDocument { Id = Guid.NewGuid(), ClaimId = claim.Id, Name = $"doc-{i}.pdf" });
        }

        Mutate(db => db.Claims.Add(claim));
        return claim;
    }

    public IServiceScope Scope() => Services.CreateScope();

    public T Query<T>(Func<ArchiveTestContext, T> query)
    {
        using var scope = Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ArchiveTestContext>());
    }

    public void Mutate(Action<ArchiveTestContext> mutate)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArchiveTestContext>();
        mutate(db);
        db.SaveChanges();
    }

    public void Dispose()
    {
        Services.Dispose();
        _connection.Dispose();
    }
}
