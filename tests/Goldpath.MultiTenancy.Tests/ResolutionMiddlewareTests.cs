using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Goldpath.Tests;

public sealed class ResolutionMiddlewareTests
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
                    app.Run(async ctx =>
                    {
                        var tenant = ctx.RequestServices.GetRequiredService<ITenantContext>().Current;
                        await ctx.Response.WriteAsync(tenant?.Value ?? "<none>");
                    });
                }))
            .StartAsync();
        return host;
    }

    [Fact]
    public async Task Header_strategy_resolves_the_tenant_into_the_scoped_context()
    {
        using var host = await BuildServerAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(GoldpathHeaders.TenantId, "acme");

        Assert.Equal("acme", await client.GetStringAsync("/loans"));
    }

    [Fact]
    public async Task Missing_or_malformed_tenant_fails_closed_with_400_in_strict_mode()
    {
        using var host = await BuildServerAsync();
        var client = host.GetTestClient();

        var missing = await client.GetAsync("/loans");
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        client.DefaultRequestHeaders.Add(GoldpathHeaders.TenantId, new string('x', TenantId.MaxLength + 1));
        var malformed = await client.GetAsync("/loans");
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);   // 400, never a 500
    }

    [Fact]
    public async Task Exempt_paths_pass_without_a_tenant()
    {
        using var host = await BuildServerAsync();
        var response = await host.GetTestClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("<none>", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Non_strict_mode_lets_tenantless_requests_through()
    {
        using var host = await BuildServerAsync(o => o.Strict = false);
        var response = await host.GetTestClient().GetAsync("/loans");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("<none>", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Subdomain_strategy_takes_the_first_host_label()
    {
        using var host = await BuildServerAsync(o => o.Strategy = GoldpathTenantStrategy.Subdomain);
        var client = host.GetTestClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://acme.api.bank.local/loans");
        Assert.Equal("acme", await (await client.SendAsync(request)).Content.ReadAsStringAsync());

        var bare = new HttpRequestMessage(HttpMethod.Get, "http://localhost/loans");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(bare)).StatusCode);
    }

    [Fact]
    public async Task Ambient_tenant_does_not_leak_past_the_request()
    {
        using var host = await BuildServerAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(GoldpathHeaders.TenantId, "acme");
        await client.GetStringAsync("/loans");

        Assert.Null(GoldpathAmbientTenant.Current);
    }
}
