using System.Net;
using System.Net.Http.Headers;
using Mediant.Abstractions;
using Mediant.Behaviors.Attributes;
using Mediant.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Goldpath.Tests;

public sealed class OpenIdAuthTests
{
    [Authorize(Roles = "loan-officer")]
    public sealed record ApproveLoanCommand : ICommand<string>;

    public sealed class ApproveLoanHandler : ICommandHandler<ApproveLoanCommand, string>
    {
        public ValueTask<string> Handle(ApproveLoanCommand command, CancellationToken cancellationToken)
            => ValueTask.FromResult("approved");
    }

    private static async Task<IHost> StartAsync(bool withTenancy = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMediant(cfg => cfg.RegisterServicesFromAssembly(typeof(OpenIdAuthTests).Assembly));
        if (withTenancy)
        {
            builder.AddGoldpathMultiTenancy();
        }

        builder.AddGoldpathAuth(o =>
        {
            o.Audience = TestIdp.Audience;
            o.RequireHttpsMetadata = false;
        });
        TestIdp.WireValidation(builder.Services);

        var app = builder.Build();
        if (withTenancy)
        {
            app.UseGoldpathMultiTenancy();
        }

        app.UseGoldpathAuth();
        app.MapGet("/secure", (HttpContext ctx) => ctx.User.Identity!.Name ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        app.MapGet("/public", () => "open").AllowAnonymous();
        app.MapPost("/approve", async (ISender sender) => await sender.Send(new ApproveLoanCommand()));

        await app.StartAsync();
        return app;
    }

    private static HttpClient Client(IHost app, string? token = null, string? tenantHeader = null)
    {
        var client = app.GetTestClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (tenantHeader is not null)
        {
            client.DefaultRequestHeaders.Add(GoldpathHeaders.TenantId, tenantHeader);
        }

        return client;
    }

    [Fact]
    public async Task Secure_by_default_no_token_means_401_valid_token_means_200()
    {
        using var app = await StartAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(app).GetAsync("/secure")).StatusCode);

        var ok = await Client(app, TestIdp.Token(subject: "user-42")).GetAsync("/secure");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("user-42", await ok.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("wrong-audience")]
    [InlineData("bad-signature")]
    public async Task Broken_tokens_are_rejected_by_the_real_validation_path(string kind)
    {
        using var app = await StartAsync();
        var token = kind switch
        {
            "expired" => TestIdp.Token(lifetime: TimeSpan.FromMinutes(-10)),
            "wrong-audience" => TestIdp.Token(audience: "someone-else"),
            _ => TestIdp.Token(wrongKey: true),
        };

        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(app, token).GetAsync("/secure")).StatusCode);
    }

    [Fact]
    public async Task AllowAnonymous_is_the_explicit_escape()
    {
        using var app = await StartAsync();
        var response = await Client(app).GetAsync("/public");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("open", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Token_tenant_binding_rejects_the_stolen_token_topology()
    {
        using var app = await StartAsync(withTenancy: true);
        var acmeToken = TestIdp.Token(tenant: "acme");

        // Claim matches the resolved tenant → fine.
        var match = await Client(app, acmeToken, tenantHeader: "acme").GetAsync("/secure");
        Assert.Equal(HttpStatusCode.OK, match.StatusCode);

        // acme token riding a globex header → 403, fail-closed.
        var stolen = await Client(app, acmeToken, tenantHeader: "globex").GetAsync("/secure");
        Assert.Equal(HttpStatusCode.Forbidden, stolen.StatusCode);

        // Token without the claim → binding not enforced (gateway-owns-tenancy topology).
        var clean = await Client(app, TestIdp.Token(), tenantHeader: "globex").GetAsync("/secure");
        Assert.Equal(HttpStatusCode.OK, clean.StatusCode);
    }

    [Fact]
    public async Task Mediant_Authorize_sees_the_same_principal_as_the_endpoint()
    {
        using var app = await StartAsync();

        var officer = await Client(app, TestIdp.Token(role: "loan-officer")).PostAsync("/approve", null);
        Assert.Equal(HttpStatusCode.OK, officer.StatusCode);
        Assert.Equal("approved", await officer.Content.ReadAsStringAsync());

        // Mediant fails CLOSED with an exception; bare TestServer rethrows it to the caller
        // (real hosts shape it into ProblemDetails via the Ring A exception pipeline).
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Client(app, TestIdp.Token(role: "clerk")).PostAsync("/approve", null));
    }
}
