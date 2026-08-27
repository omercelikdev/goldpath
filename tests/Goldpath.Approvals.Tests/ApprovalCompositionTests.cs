using Goldpath;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Goldpath.Approvals.Tests;

/// <summary>
/// The composition seam itself: what <c>AddGoldpathApprovals</c> registers, and that the
/// declared configuration section actually binds — the lines an app trusts blindly.
/// </summary>
public class ApprovalCompositionTests
{
    [Fact]
    public void AddGoldpathApprovals_registers_the_engine_over_the_in_memory_store()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddLogging();
        builder.AddGoldpathApprovals(approvals => approvals
            .AddLadder("credit-limit", l => l.TopRung("gm", TimeSpan.FromHours(24))));

        using var app = builder.Build();
        Assert.IsType<GoldpathInMemoryApprovalStore>(app.Services.GetRequiredService<IGoldpathApprovalStore>());
        var options = app.Services.GetRequiredService<GoldpathApprovalsOptions>();
        Assert.True(options.Ladders.ContainsKey("credit-limit"));
        // The engine is SCOPED (it may consume the scoped messaging publisher — outbox).
        using var scope = app.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<GoldpathApprovalEngine>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<GoldpathApprovalEscalationJob>());
    }

    [Fact]
    public void The_Goldpath_Approvals_configuration_section_binds_the_options()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddLogging();
        builder.Configuration["Goldpath:Approvals:MaxDelegationWindow"] = "2.00:00:00";
        builder.AddGoldpathApprovals(approvals => approvals
            .AddLadder("credit-limit", l => l.TopRung("gm", TimeSpan.FromHours(24))));

        using var app = builder.Build();
        Assert.Equal(TimeSpan.FromDays(2), app.Services.GetRequiredService<GoldpathApprovalsOptions>().MaxDelegationWindow);
    }

    [Fact]
    public void The_database_overload_registers_the_EF_store()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddLogging();
        builder.Services.AddDbContext<EfApprovalStoreTests.ApprovalsDbContext>(b => b.UseSqlite(connection));
        builder.AddGoldpathApprovals<HostApplicationBuilder, EfApprovalStoreTests.ApprovalsDbContext>(approvals => approvals
            .AddLadder("credit-limit", l => l.TopRung("gm", TimeSpan.FromHours(24))));

        using var app = builder.Build();
        Assert.IsType<GoldpathEfApprovalStore<EfApprovalStoreTests.ApprovalsDbContext>>(
            app.Services.GetRequiredService<IGoldpathApprovalStore>());
    }
}
