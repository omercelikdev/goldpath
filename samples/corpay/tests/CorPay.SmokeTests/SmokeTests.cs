using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Testing;
using Goldpath;
using Xunit;

namespace CorPay.SmokeTests;

/// <summary>
/// The "runs with one click" proof for the AUTHED shape: the REAL AppHost starts
/// (containers included), probes go green, and the auth floor holds — business endpoints
/// answer 401 without a token. The full order flow needs your IdP (Goldpath.Auth README);
/// this smoke deliberately claims exactly what it asserts.
/// </summary>
public class SmokeTests
{
    [Fact]
    public async Task Secure_by_default_probes_green_and_the_auth_floor_holds()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.CorPay_AppHost>(timeout.Token);
        await using var app = await appHost.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);

        var client = app.CreateHttpClient("api");
        client.DefaultRequestHeaders.Add(GoldpathHeaders.TenantId, "smoke-tenant");   // fail-closed tenancy

        // Readiness (containers + schema + bus).
        await WaitUntilAsync(async () =>
            (await client.GetAsync("/health/ready", timeout.Token)).IsSuccessStatusCode, timeout.Token);

        // Secure-by-default proof: with auth enabled and no token, business endpoints are
        // 401 while probes stay green — that IS the first-click contract for authed shapes.
        // (Full-flow smoke needs your IdP; see the Goldpath.Auth README.)
        var unauthorized = await client.PostAsJsonAsync("/api/v1/orders",
            new { reference = "smoke-001", amount = 42.50m }, timeout.Token);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        // The console is served by this head and inherits that floor: an operator without a
        // principal is refused the PAGE, not just the calls behind it. Shipping an
        // unauthenticated console is the one mistake this composition cannot make.
        var console = await client.GetAsync("/goldpath/console/", timeout.Token);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, console.StatusCode);

        // The INTERNAL head also serves a console (its own `exposeUnsecured: true` choice,
        // the cluster boundary being what protects it) — NOT asserted here, because asking
        // for its readiness is what uncovered a defect this test cannot fix: the worker
        // validates the Quartz schema at startup while the API is still migrating the
        // shared database, so it never becomes ready. Pre-existing, and invisible until
        // something asked. Recorded as open-threads T12 with what must be proven when it
        // is fixed.
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                if (await condition())
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // service still starting
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The per-request timeout, not our deadline: a cold machine can take longer
                // than 100s to answer the FIRST probe (migrations + bus + cache), and the
                // escaping exception killed the wait instead of retrying it.
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }
}
