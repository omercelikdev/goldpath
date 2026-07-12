using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Goldpath.Tests;

public sealed class ApiKeyAuthTests
{
    private static async Task<IHost> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.AddGoldpathAuth(o =>
        {
            o.Strategy = GoldpathAuthStrategy.ApiKey;
            o.ApiKeys["batch-runner"] = "secret-key-1";
            o.ApiKeys["legacy-crm"] = "secret-key-2";
        });

        var app = builder.Build();
        app.UseGoldpathAuth();
        app.MapGet("/secure", (HttpContext ctx) => ctx.User.Identity!.Name!);
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task Valid_key_authenticates_as_the_named_client()
    {
        using var app = await StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add(GoldpathHeaders.ApiKey, "secret-key-2");

        var response = await client.GetAsync("/secure");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("legacy-crm", await response.Content.ReadAsStringAsync());   // audit says WHO
    }

    [Fact]
    public async Task Wrong_or_missing_key_is_401()
    {
        using var app = await StartAsync();

        var missing = await app.GetTestClient().GetAsync("/secure");
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

        var wrong = app.GetTestClient();
        wrong.DefaultRequestHeaders.Add(GoldpathHeaders.ApiKey, "not-a-key");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync("/secure")).StatusCode);
    }
}
