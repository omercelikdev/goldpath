using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Goldpath.Tests;

public class IdempotencyTests
{
    private sealed class HeaderTenantContext(IHttpContextAccessor accessor) : ITenantContext
    {
        public TenantId? Current =>
            accessor.HttpContext?.Request.Headers.TryGetValue("X-Test-Tenant", out var value) == true
            && TenantId.TryCreate(value.ToString(), out var tenant)
                ? tenant
                : null;
    }

    private static int s_executionCounter;

    private static async Task<WebApplication> StartAppAsync(
        Action<GoldpathIdempotencyOptions>? configure = null,
        Func<Task>? gate = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ITenantContext, HeaderTenantContext>();
        builder.Services.AddProblemDetails();
        builder.AddGoldpathIdempotency(configure);

        var app = builder.Build();
        app.MapPost("/api/v1/orders", async (HttpContext _) =>
        {
            if (gate is not null)
            {
                await gate();
            }

            var execution = Interlocked.Increment(ref s_executionCounter);
            return Results.Ok(new { execution });
        });
        app.MapGet("/api/v1/orders", () => Results.Ok(new { listed = true }));

        await app.StartAsync();
        return app;
    }

    private static HttpRequestMessage Request(string key, object body, string? tenant = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add(GoldpathHeaders.IdempotencyKey, key);
        if (tenant is not null)
        {
            request.Headers.Add("X-Test-Tenant", tenant);
        }

        return request;
    }

    [Fact]
    public async Task Retry_with_same_key_replays_the_stored_response_byte_for_byte()
    {
        await using var app = await StartAppAsync();
        var client = app.GetTestClient();

        var first = await client.SendAsync(Request("key-1", new { amount = 10 }));
        var firstBody = await first.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.False(first.Headers.Contains(GoldpathIdempotencyMiddleware.ReplayHeader));

        var retry = await client.SendAsync(Request("key-1", new { amount = 10 }));
        var retryBody = await retry.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(firstBody, retryBody);                                        // handler did NOT run again
        Assert.Equal("true", retry.Headers.GetValues(GoldpathIdempotencyMiddleware.ReplayHeader).Single());
    }

    [Fact]
    public async Task Same_key_with_different_payload_is_rejected_with_422()
    {
        await using var app = await StartAppAsync();
        var client = app.GetTestClient();

        await client.SendAsync(Request("key-2", new { amount = 10 }));
        var mismatch = await client.SendAsync(Request("key-2", new { amount = 999 }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, mismatch.StatusCode);
        Assert.Equal("application/problem+json", mismatch.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Concurrent_same_key_gets_409_while_first_is_in_flight()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = await StartAppAsync(gate: () => gate.Task);
        var client = app.GetTestClient();

        var held = client.SendAsync(Request("key-3", new { amount = 1 }));
        await Task.Delay(300);                                                     // let it take the lock

        var concurrent = await client.SendAsync(Request("key-3", new { amount = 1 }));
        Assert.Equal(HttpStatusCode.Conflict, concurrent.StatusCode);

        gate.SetResult();
        Assert.Equal(HttpStatusCode.OK, (await held).StatusCode);
    }

    [Fact]
    public async Task Different_tenants_with_the_same_key_are_isolated()
    {
        await using var app = await StartAppAsync();
        var client = app.GetTestClient();

        var tenantA = await client.SendAsync(Request("key-4", new { amount = 1 }, tenant: "acme"));
        var tenantB = await client.SendAsync(Request("key-4", new { amount = 1 }, tenant: "globex"));

        Assert.False(tenantB.Headers.Contains(GoldpathIdempotencyMiddleware.ReplayHeader));   // B executed, no replay
        Assert.NotEqual(
            await tenantA.Content.ReadAsStringAsync(),
            await tenantB.Content.ReadAsStringAsync());                                  // separate executions
    }

    [Fact]
    public async Task Requests_without_the_header_and_GETs_pass_through()
    {
        await using var app = await StartAppAsync();
        var client = app.GetTestClient();

        var first = await client.PostAsJsonAsync("/api/v1/orders", new { amount = 1 });
        var second = await client.PostAsJsonAsync("/api/v1/orders", new { amount = 1 });
        Assert.NotEqual(
            await first.Content.ReadAsStringAsync(),
            await second.Content.ReadAsStringAsync());                                   // both executed

        var get = await client.GetAsync("/api/v1/orders");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    }

    [Fact]
    public async Task Wait_mode_serializes_and_replays_instead_of_409()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = await StartAppAsync(
            configure: o => o.OnConflict = IdempotencyConflictBehavior.Wait,
            gate: () => gate.Task);
        var client = app.GetTestClient();

        var held = client.SendAsync(Request("key-5", new { amount = 1 }));
        await Task.Delay(300);
        var waiting = client.SendAsync(Request("key-5", new { amount = 1 }));
        await Task.Delay(300);
        Assert.False(waiting.IsCompleted);                                        // genuinely waiting, not 409

        gate.SetResult();
        var heldResponse = await held;
        var waitedResponse = await waiting;

        Assert.Equal(HttpStatusCode.OK, waitedResponse.StatusCode);
        Assert.Equal(
            await heldResponse.Content.ReadAsStringAsync(),
            await waitedResponse.Content.ReadAsStringAsync());                    // replayed the first result
    }
}
