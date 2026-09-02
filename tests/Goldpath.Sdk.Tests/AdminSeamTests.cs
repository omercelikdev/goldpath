using System.Diagnostics;
using Goldpath;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Goldpath.Sdk.Tests;

/// <summary>
/// The small pure seams of the module SDK: the paging clamp every list verb rides, and
/// the trace link that lets a Quartz-born span point at the request that caused it.
/// </summary>
public class AdminSeamTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-25, 1)]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(AdminPaging.MaxTake, AdminPaging.MaxTake)]
    [InlineData(AdminPaging.MaxTake + 1, AdminPaging.MaxTake)]
    [InlineData(int.MaxValue, AdminPaging.MaxTake)]
    public void The_paging_clamp_answers_one_row_at_least_and_the_ceiling_at_most(int asked, int answered)
        => Assert.Equal(answered, AdminPaging.Clamp(asked));

    [Fact]
    public void A_stored_traceparent_becomes_exactly_one_remote_link()
    {
        var context = new ActivityContext(
            ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);
        var parent = $"00-{context.TraceId}-{context.SpanId}-01";

        var links = TraceLink.To(parent);

        var link = Assert.Single(links!);
        Assert.Equal(context.TraceId, link.Context.TraceId);
        Assert.Equal(context.SpanId, link.Context.SpanId);
        Assert.True(link.Context.IsRemote);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-traceparent")]
    [InlineData("00-corrupt-corrupt-01")]
    public void A_missing_or_corrupt_traceparent_never_breaks_a_span(string? stored)
        => Assert.Null(TraceLink.To(stored));

    [Fact]
    public void The_secured_path_stamps_the_ops_policy_on_the_group()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddLogging();
        var app = builder.Build();
        var group = app.MapGroup("/goldpath/admin/probe");
        group.MapGet("/rows", () => Results.Ok());

        AdminSurfaceGuard.Apply(app, group, "/goldpath/admin/probe", exposeUnsecured: false);

        // The policy is metadata on the GROUP: every endpoint inside inherits the floor.
        var endpoint = Assert.Single(((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints));
        var authorize = endpoint.Metadata
            .OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>()
            .Single();
        Assert.Equal(GoldpathPolicies.Ops, authorize.Policy);
    }

    [Fact]
    public void The_optout_leaves_the_group_open_and_that_is_the_point_of_the_warning()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddLogging();
        var app = builder.Build();
        var group = app.MapGroup("/goldpath/admin/probe");
        group.MapGet("/rows", () => Results.Ok());

        AdminSurfaceGuard.Apply(app, group, "/goldpath/admin/probe", exposeUnsecured: true);

        var endpoint = Assert.Single(((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints));
        Assert.Empty(endpoint.Metadata.OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>());
    }
}
