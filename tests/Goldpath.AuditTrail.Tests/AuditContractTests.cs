using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Goldpath.Tests;

/// <summary>
/// Contract-level coverage the behavior tests can't see (mutation-gate findings):
/// the audit model IS a schema contract, the claims fallback chain, and the
/// correlation-id source order.
/// </summary>
public sealed class AuditContractTests
{
    private sealed class ContractDb(DbContextOptions<ContractDb> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddGoldpathAuditLog();
    }

    [Fact]
    public void Audit_model_is_a_schema_contract()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = new ContractDb(new DbContextOptionsBuilder<ContractDb>().UseSqlite(connection).Options);

        var entity = db.Model.FindEntityType(typeof(GoldpathAuditLogEntry))!;
        Assert.Equal("GoldpathAuditLog", entity.GetTableName());
        Assert.Equal(256, entity.FindProperty(nameof(GoldpathAuditLogEntry.EntityType))!.GetMaxLength());
        Assert.Equal(16, entity.FindProperty(nameof(GoldpathAuditLogEntry.Action))!.GetMaxLength());

        var indexes = entity.GetIndexes().ToList();
        Assert.Contains(indexes, i => i.Properties.Select(p => p.Name).SequenceEqual(
            [nameof(GoldpathAuditLogEntry.EntityType), nameof(GoldpathAuditLogEntry.EntityKey)]));
        Assert.Contains(indexes, i => i.Properties.Single().Name == nameof(GoldpathAuditLogEntry.Timestamp));
    }

    private sealed class FixedAccessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    [Fact]
    public void Claims_user_context_walks_nameidentifier_then_name_then_null()
    {
        var withId = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "user-7"), new Claim(ClaimTypes.Name, "Seven")], "test")),
        };
        Assert.Equal("user-7", new HttpClaimsUserContext(new FixedAccessor(withId)).UserId);

        var nameOnly = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Seven")], "test")),
        };
        Assert.Equal("Seven", new HttpClaimsUserContext(new FixedAccessor(nameOnly)).UserId);

        Assert.Null(new HttpClaimsUserContext(new FixedAccessor(null)).UserId);
    }

    [Fact]
    public async Task Correlation_prefers_the_goldpath_tag_and_falls_back_to_the_trace_id()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AuditFlowDb>()
            .UseSqlite(connection)
            .AddInterceptors(new GoldpathSaveChangesInterceptor(
                [new AuditChangeLogContributor(new GoldpathAuditTrailOptions())],
                new GoldpathSaveContext(TimeProvider.System, null)))
            .Options;

        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource("audit-contract-tests");

        // 1) explicit goldpath tag wins
        using (var activity = source.StartActivity("tagged"))
        {
            activity!.SetTag("goldpath.correlation_id", "corr-42");
            using var db = new AuditFlowDb(options);
            db.Database.EnsureCreated();
            db.Add(new Order { Id = 1 });
            await db.SaveChangesAsync();
            Assert.All(await db.Set<GoldpathAuditLogEntry>().ToListAsync(),
                e => Assert.Equal("corr-42", e.CorrelationId));
        }

        // 2) no tag → the activity's trace id
        using (var activity = source.StartActivity("untagged"))
        {
            using var db = new AuditFlowDb(options);
            db.Add(new Order { Id = 2 });
            await db.SaveChangesAsync();
            var rows = await db.Set<GoldpathAuditLogEntry>().Where(e => e.EntityKey == "2").ToListAsync();
            Assert.All(rows, e => Assert.Equal(activity!.TraceId.ToString(), e.CorrelationId));
        }
    }

    private sealed class Order : IAuditLogged
    {
        public long Id { get; set; }
    }

    private sealed class AuditFlowDb(DbContextOptions<AuditFlowDb> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddGoldpathAuditLog();
    }
}
