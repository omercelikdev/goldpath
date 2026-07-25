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
        app.MapGet("/ops", () => "ops").RequireAuthorization(GoldpathPolicies.Ops);
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task Strategy_none_stays_open_but_the_ops_floor_refuses_cleanly()
    {
        using var app = await StartAsync(o => o.Strategy = GoldpathAuthStrategy.None);
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/secure")).StatusCode);   // no fallback policy

        // A4: the guarded floor answers an honest 401 through the deny-only scheme —
        // BEFORE this fix the unregistered policy crashed the request with a 500.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/ops")).StatusCode);
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
    public async Task Any_audience_is_an_explicit_optout_never_a_default()
    {
        // The old silent widening now refuses at startup (audit A3)...
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(o =>
        {
            o.Authority = "https://idp.local";
            o.Audience = null;
            o.RequireHttpsMetadata = false;
        }));
        Assert.Contains("AllowAnyAudience", refused.Message, StringComparison.Ordinal);

        // ...and the opt-out is visible, spelled out, and still works.
        using var app = await StartAsync(o =>
        {
            o.Audience = null;
            o.AllowAnyAudience = true;
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
            o.Audience = TestIdp.Audience;
            o.RequireHttpsMetadata = false;
        }, mapOpenApi: true);

        var doc = await app.GetTestClient().GetStringAsync("/openapi/v1.json");
        Assert.Contains("\"goldpath\"", doc);
        Assert.Contains(marker, doc);
    }
}
