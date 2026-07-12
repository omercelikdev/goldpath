using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Goldpath.Campaign.Tests;

/// <summary>
/// H8: the admin surface is the UI's FROZEN contract (docs/rfc/goldpath-admin-contract.md).
/// This test is the freeze's teeth on the campaign surface — a renamed or added route fails
/// here first, forcing the contract document into the same PR.
/// </summary>
public class RouteContractTests
{
    [Fact]
    public void The_campaign_surface_matches_the_frozen_contract()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDbContext<CampaignTestContext>(o => o.UseSqlite("DataSource=:memory:"));
        builder.Services.AddSingleton(new GoldpathCampaignOptions());
        using var app = builder.Build();
        app.MapGoldpathCampaignAdmin<CampaignTestContext>();

        var actual = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => $"{e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single()} {e.RoutePattern.RawText}")
            .Order()
            .ToArray();

        string[] frozen =
        [
            "GET /goldpath/admin/campaign/",
            "GET /goldpath/admin/campaign/{id:guid}",
            "GET /goldpath/admin/campaign/{id:guid}/audit",
            "GET /goldpath/admin/campaign/{id:guid}/failures",
            "POST /goldpath/admin/campaign/",
            "POST /goldpath/admin/campaign/{id:guid}/abort",
            "POST /goldpath/admin/campaign/{id:guid}/pause",
            "POST /goldpath/admin/campaign/{id:guid}/resume",
            "POST /goldpath/admin/campaign/{id:guid}/throttle",
        ];
        Assert.Equal(frozen, actual);
    }
}
