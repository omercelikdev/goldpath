using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Goldpath.Tests;

/// <summary>Edge coverage from the mutation-gate findings: option branches must be OBSERVED, not just wired.</summary>
public sealed class AuthEdgeTests
{
    private static async Task<IHost> StartAsync(Action<GoldpathAuthOptions> configure, bool withTenancy = false, bool mapOpenApi = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        if (withTenancy)
        {
            builder.AddGoldpathMultiTenancy();
        }

        if (mapOpenApi)
        {
            builder.Services.AddOpenApi();
        }

        builder.AddGoldpathAuth(configure);
        TestIdp.WireValidation(builder.Services);

        var app = builder.Build();
        if (withTenancy)
        {
            app.UseGoldpathMultiTenancy();
        }

        app.UseGoldpathAuth();
        if (mapOpenApi)
        {
            app.MapOpenApi().AllowAnonymous();
        }

        app.MapGet("/secure", () => "in");
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task Strategy_none_wires_nothing_by_design()
    {
        using var app = await StartAsync(o => o.Strategy = GoldpathAuthStrategy.None);
        var response = await app.GetTestClient().GetAsync("/secure");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);   // no fallback policy, no schemes
    }

    [Fact]
    public async Task Binding_can_be_disabled_for_gateway_owned_tenancy()
    {
        using var app = await StartAsync(o =>
        {
            o.Audience = TestIdp.Audience;
            o.RequireHttpsMetadata = false;
            o.BindTenant = false;
        }, withTenancy: true);

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestIdp.Token(tenant: "acme"));
        client.DefaultRequestHeaders.Add(GoldpathHeaders.TenantId, "globex");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/secure")).StatusCode);
    }

    [Fact]
    public async Task Binding_rejection_body_names_the_problem()
    {
        using var app = await StartAsync(o =>
        {
            o.Audience = TestIdp.Audience;
            o.RequireHttpsMetadata = false;
        }, withTenancy: true);

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestIdp.Token(tenant: "acme"));
        client.DefaultRequestHeaders.Add(GoldpathHeaders.TenantId, "globex");

        var response = await client.GetAsync("/secure");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("not valid for this tenant", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unset_audience_skips_audience_validation()
    {
        using var app = await StartAsync(o =>
        {
            o.Audience = null;
            o.RequireHttpsMetadata = false;
        });

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestIdp.Token(audience: "totally-different"));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/secure")).StatusCode);
    }

    [Fact]
    public async Task Api_key_header_name_is_configurable_and_equal_length_wrong_keys_fail()
    {
        using var app = await StartAsync(o =>
        {
            o.Strategy = GoldpathAuthStrategy.ApiKey;
            o.ApiKeyHeader = "X-Custom-Key";
            o.ApiKeys["job"] = "12345678";
        });

        var right = app.GetTestClient();
        right.DefaultRequestHeaders.Add("X-Custom-Key", "12345678");
        Assert.Equal(HttpStatusCode.OK, (await right.GetAsync("/secure")).StatusCode);

        var sameLengthWrong = app.GetTestClient();
        sameLengthWrong.DefaultRequestHeaders.Add("X-Custom-Key", "12345679");   // constant-time branch
        Assert.Equal(HttpStatusCode.Unauthorized, (await sameLengthWrong.GetAsync("/secure")).StatusCode);

        var defaultHeader = app.GetTestClient();
        defaultHeader.DefaultRequestHeaders.Add(GoldpathHeaders.ApiKey, "12345678"); // wrong header name
        Assert.Equal(HttpStatusCode.Unauthorized, (await defaultHeader.GetAsync("/secure")).StatusCode);
    }

    [Theory]
    [InlineData(GoldpathAuthStrategy.OpenId, "\"scheme\": \"bearer\"")]
    [InlineData(GoldpathAuthStrategy.ApiKey, "\"in\": \"header\"")]
    public async Task OpenApi_document_carries_the_security_scheme(GoldpathAuthStrategy strategy, string marker)
    {
        using var app = await StartAsync(o =>
        {
            o.Strategy = strategy;
            o.RequireHttpsMetadata = false;
        }, mapOpenApi: true);

        var doc = await app.GetTestClient().GetStringAsync("/openapi/v1.json");
        Assert.Contains("\"goldpath\"", doc);
        Assert.Contains(marker, doc);
    }
}
