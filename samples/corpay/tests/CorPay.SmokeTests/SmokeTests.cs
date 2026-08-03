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

        // EVERY head reaches ready — this is T12's exit proof. The EOD worker used to die
        // right here: its Quartz store validated the shared schema while the API was still
        // migrating it. The AppHost now starts the workers after the API (which owns the
        // DDL, migrations D3), and each worker applies its own PRIVATE migrations.
        var eodWorker = app.CreateHttpClient("eod-worker");
        await WaitUntilAsync(async () =>
            (await eodWorker.GetAsync("/health/ready", timeout.Token)).IsSuccessStatusCode, timeout.Token);
        var paymentsWorker = app.CreateHttpClient("payments-worker");
        await WaitUntilAsync(async () =>
            (await paymentsWorker.GetAsync("/health/ready", timeout.Token)).IsSuccessStatusCode, timeout.Token);

        // The INTERNAL head's console (its own visible `exposeUnsecured: true` choice — the
        // cluster boundary is the guard) answers the PAGE, unlike the API's 401 above.
        var internalConsole = await eodWorker.GetAsync("/goldpath/console/", timeout.Token);
        Assert.Equal(System.Net.HttpStatusCode.OK, internalConsole.StatusCode);
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
