using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Goldpath.Tests;

public class ApiDefaultsTests
{
    private enum OrderStatus
    {
        PendingApproval,
    }

    private sealed record WireProbe(OrderStatus Status, string? AbsentWhenNull);

    private static async Task<WebApplication> StartAppAsync(string environment = "Production")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.AddGoldpathApiDefaults();

        var app = builder.Build();
        var api = app.MapGoldpathApi();
        api.MapGet("/wire-probe", () => new WireProbe(OrderStatus.PendingApproval, null));
        api.MapGet("/orders", () => new Page<string>(["a", "b"], GoldpathCursor.Encode(2L), 50));

        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task Versioned_route_group_serves_under_api_v1()
    {
        await using var app = await StartAppAsync();
        var response = await app.GetTestClient().GetAsync("/api/v1/orders");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Json_wire_is_camelCase_with_enums_as_strings_and_no_null_writes()
    {
        await using var app = await StartAppAsync();
        var body = await app.GetTestClient().GetStringAsync("/api/v1/wire-probe");

        Assert.Contains("\"status\":\"PendingApproval\"", body); // camelCase key + enum as string
        Assert.DoesNotContain("absentWhenNull", body);           // null not written
    }

    [Fact]
    public async Task Page_serializes_items_nextCursor_size()
    {
        await using var app = await StartAppAsync();
        var body = await app.GetTestClient().GetStringAsync("/api/v1/orders");

        Assert.Contains("\"items\":[\"a\",\"b\"]", body);
        Assert.Contains("\"nextCursor\":", body);
        Assert.Contains("\"size\":50", body);
    }

    [Fact]
    public async Task OpenApi_document_is_served_in_development_only()
    {
        await using var dev = await StartAppAsync("Development");
        var devResponse = await dev.GetTestClient().GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, devResponse.StatusCode);

        await using var prod = await StartAppAsync("Production");
        var prodResponse = await prod.GetTestClient().GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.NotFound, prodResponse.StatusCode);
    }

    [Fact]
    public async Task OpenApi_document_is_deterministic()
    {
        await using var app = await StartAppAsync("Development");
        var client = app.GetTestClient();

        var first = await client.GetStringAsync("/openapi/v1.json");
        var second = await client.GetStringAsync("/openapi/v1.json");
        Assert.Equal(first, second);
    }
}
