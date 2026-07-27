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
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));

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
    }

    [Fact]
    public async Task The_internal_worker_head_serves_the_console_it_opted_into()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.CorPay_AppHost>(timeout.Token);
        await using var app = await appHost.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);

        var worker = app.CreateHttpClient("eod-worker");
        await WaitUntilAsync(async () =>
            (await worker.GetAsync("/health/ready", timeout.Token)).IsSuccessStatusCode, timeout.Token);

        // This head chose `exposeUnsecured: true` for its fleet surface (the cluster
        // boundary is what protects it), and the console rides the same choice — visibly,
        // in the same file. What must hold is that the page and its assets actually SERVE
        // from the package, because nothing else in CorPay would notice if they did not.
        var page = await worker.GetAsync("/goldpath/console/", timeout.Token);
        page.EnsureSuccessStatusCode();
        Assert.Contains("text/html", page.Content.Headers.ContentType!.ToString());

        var html = await page.Content.ReadAsStringAsync(timeout.Token);
        var asset = System.Text.RegularExpressions.Regex.Match(html, @"src=""\./(assets/[^""]+)""").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(asset), "the served page must reference its own bundle");

        var bundle = await worker.GetAsync($"/goldpath/console/{asset}", timeout.Token);
        bundle.EnsureSuccessStatusCode();
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
