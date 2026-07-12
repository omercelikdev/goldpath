using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Goldpath.Tests;

/// <summary>Edge coverage from the mutation-gate findings: every option branch observed.</summary>
public sealed class TenancyEdgeTests
{
    private static async Task<IHost> BuildServerAsync(Action<GoldpathMultiTenancyOptions>? configure = null)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    var options = new GoldpathMultiTenancyOptions();
                    configure?.Invoke(options);
                    services.AddSingleton(options);
                    services.AddScoped<ITenantContext, AmbientTenantContext>();
                })
                .Configure(app =>
                {
                    app.UseGoldpathMultiTenancy();
                    app.Run(async ctx => await ctx.Response.WriteAsync(
                        ctx.RequestServices.GetRequiredService<ITenantContext>().Current?.Value ?? "<none>"));
                }))
            .StartAsync();
        return host;
    }

    [Theory]
    [InlineData("two-labels.example")]     // 2 labels, not localhost → no tenant
    [InlineData("127.0.0.1")]              // IP literal → no tenant
    [InlineData("plainhost")]              // bare host → no tenant
    public async Task Hosts_without_a_tenant_subdomain_fail_closed(string host)
    {
        using var server = await BuildServerAsync(o => o.Strategy = GoldpathTenantStrategy.Subdomain);
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://{host}/loans");
        var response = await server.GetTestClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Two_label_localhost_is_the_dev_convenience()
    {
        using var server = await BuildServerAsync(o => o.Strategy = GoldpathTenantStrategy.Subdomain);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://acme.localhost/loans");

        Assert.Equal("acme", await (await server.GetTestClient().SendAsync(request)).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Custom_exempt_paths_replace_the_defaults()
    {
        using var server = await BuildServerAsync(o => o.ExemptPaths = ["/custom"]);
        var client = server.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/custom/x")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/health/ready")).StatusCode);   // default no longer applies
    }

    [Fact]
    public async Task The_400_body_names_the_missing_header()
    {
        using var server = await BuildServerAsync();
        var response = await server.GetTestClient().GetAsync("/loans");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Tenant could not be resolved", body);
        Assert.Contains(GoldpathHeaders.TenantId, body);
    }

    [Fact]
    public void Scopes_nest_and_restore_their_previous_state()
    {
        using (GoldpathTenant.Use("outer"))
        {
            using (GoldpathTenant.Use("inner"))
            {
                Assert.Equal("inner", GoldpathAmbientTenant.Current!.Value.Value);
            }

            Assert.Equal("outer", GoldpathAmbientTenant.Current!.Value.Value);   // restored, not cleared

            using (GoldpathTenant.Bypass())
            {
                Assert.True(GoldpathTenant.IsBypassed);
                using (GoldpathTenant.Bypass())
                {
                    Assert.True(GoldpathTenant.IsBypassed);
                }

                Assert.True(GoldpathTenant.IsBypassed);                          // nested dispose keeps outer bypass
            }

            Assert.False(GoldpathTenant.IsBypassed);
        }

        Assert.Null(GoldpathAmbientTenant.Current);
    }

    [Fact]
    public void Guard_messages_teach_the_fix_and_the_contributor_runs_first()
    {
        var contributor = new TenantStampContributor();
        Assert.Equal(-200, contributor.Order);   // before SoftDelete(-100) and the audit observers

        using var scope = GoldpathTenant.Use("acme");
        var ex = Assert.Throws<GoldpathCrossTenantWriteException>(() =>
            ThrowViaStamp("globex"));
        Assert.Contains("globex", ex.Message);
        Assert.Contains("acme", ex.Message);
        Assert.Contains("GoldpathTenant.Use", ex.Message);
    }

    private static void ThrowViaStamp(string foreignTenant)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = new GuardDb(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<GuardDb>()
            .UseSqlite(connection)
            .AddInterceptors(new GoldpathSaveChangesInterceptor(
                [new TenantStampContributor()], new GoldpathSaveContext(TimeProvider.System, null)))
            .Options);
        db.Database.EnsureCreated();
        db.Add(new Loan { Id = 1, TenantId = TenantId.Create(foreignTenant) });
        db.SaveChanges();
    }

    private sealed class Loan : IMultiTenant
    {
        public long Id { get; set; }
        public TenantId TenantId { get; set; }
    }

    private sealed class GuardDb(Microsoft.EntityFrameworkCore.DbContextOptions<GuardDb> options)
        : Microsoft.EntityFrameworkCore.DbContext(options)
    {
        public Microsoft.EntityFrameworkCore.DbSet<Loan> Loans => Set<Loan>();

        protected override void OnModelCreating(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
            => modelBuilder.ApplyGoldpathMultiTenancy(this);
    }
}
