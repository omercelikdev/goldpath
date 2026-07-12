using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Goldpath.Campaign.Tests;

/// <summary>
/// Hardening H2: the admin surface is fail-closed OUT OF THE BOX. Endpoint metadata is
/// the contract — no server needed. One mapper carries the proof; all five ride the
/// identical guard block.
/// </summary>
public class AdminAuthFloorTests
{
    private static List<RouteEndpoint> Map(bool exposeUnsecured)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDbContext<CampaignTestContext>(o => o.UseSqlite("DataSource=:memory:"));
        builder.Services.AddSingleton(new GoldpathCampaignOptions());
        using var app = builder.Build();
        app.MapGoldpathCampaignAdmin<CampaignTestContext>(exposeUnsecured: exposeUnsecured);
        return [.. ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()];
    }

    [Fact]
    public void The_admin_surface_demands_the_ops_policy_by_default()
    {
        var endpoints = Map(exposeUnsecured: false);
        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, e =>
        {
            var authorize = e.Metadata.GetMetadata<IAuthorizeData>();
            Assert.NotNull(authorize);
            Assert.Equal(GoldpathPolicies.Ops, authorize.Policy);
        });
    }

    [Fact]
    public void The_opt_out_is_explicit_and_leaves_no_policy_behind()
    {
        var endpoints = Map(exposeUnsecured: true);
        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, e => Assert.Null(e.Metadata.GetMetadata<IAuthorizeData>()));
    }

    [Fact]
    public void The_policy_name_is_the_cross_package_contract()
        => Assert.Equal("goldpath-ops", GoldpathPolicies.Ops);
}
