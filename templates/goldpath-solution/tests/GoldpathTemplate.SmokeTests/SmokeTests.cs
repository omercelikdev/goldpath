using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Testing;
//#if (UseMultiTenancy)
using Goldpath;
//#endif
using Xunit;

namespace GoldpathTemplate.SmokeTests;

//#if (UseAuth)
/// <summary>
/// The "runs with one click" proof for the AUTHED shape: the REAL AppHost starts
/// (containers included), probes go green, and the auth floor holds — business endpoints
/// answer 401 without a token. The full order flow needs your IdP (Goldpath.Auth README);
/// this smoke deliberately claims exactly what it asserts.
/// </summary>
//#else
/// <summary>
/// The "runs with one click" proof: the REAL AppHost starts (containers included), an order is
/// created over HTTP, the integration event round-trips the broker through the outbox, and the
/// keyset-paginated list shows the confirmed order. No mocks on the happy path.
/// </summary>
//#endif
public class SmokeTests
{
    [Fact]
//#if (UseAuth)
    public async Task Secure_by_default_probes_green_and_the_auth_floor_holds()
//#else
    public async Task Order_flow_end_to_end()
//#endif
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.GoldpathTplSafe_AppHost>(timeout.Token);
        await using var app = await appHost.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);

        var client = app.CreateHttpClient("api");
//#if (UseMultiTenancy)
        client.DefaultRequestHeaders.Add(GoldpathHeaders.TenantId, "smoke-tenant");   // fail-closed tenancy
//#endif

        // Readiness (containers + schema + bus).
        await WaitUntilAsync(async () =>
            (await client.GetAsync("/health/ready", timeout.Token)).IsSuccessStatusCode, timeout.Token);

        // goldpath:smoke heads — additional heads (goldpath new service|gateway) prove here

//#if (UseAuth)
        // Secure-by-default proof: with auth enabled and no token, business endpoints are
        // 401 while probes stay green — that IS the first-click contract for authed shapes.
        // (Full-flow smoke needs your IdP; see the Goldpath.Auth README.)
        var unauthorized = await client.PostAsJsonAsync("/api/v1/orders",
            new { reference = "smoke-001", amount = 42.50m }, timeout.Token);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, unauthorized.StatusCode);

//#if (UseArchival || UseBulk || UseNotification || UseCampaign)
        // The console rides the same floor: an operator without a principal is refused the
        // PAGE, not just the calls behind it. This app cannot ship an unauthenticated
        // console by accident.
        var console = await client.GetAsync("/goldpath/console/", timeout.Token);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, console.StatusCode);
//#endif
//#else
//#if (UseArchival || UseBulk || UseNotification || UseCampaign)
        // No auth in this shape, so the console SERVES — and its embedded bundle must come
        // with it. A page that loads without its assets is a blank screen with a green
        // status code, which is the worst failure a console can have.
        var console = await client.GetAsync("/goldpath/console/", timeout.Token);
        console.EnsureSuccessStatusCode();
        var consoleHtml = await console.Content.ReadAsStringAsync(timeout.Token);
        var bundle = System.Text.RegularExpressions.Regex.Match(consoleHtml, @"src=""\./(assets/[^""]+)""").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(bundle), "the served console must reference its own bundle");
        (await client.GetAsync($"/goldpath/console/{bundle}", timeout.Token)).EnsureSuccessStatusCode();
//#endif

        // 1. Create — walking-skeleton command (Mediant [HttpEndpoint] + outbox publish).
        var create = await client.PostAsJsonAsync("/api/v1/orders",
            new { reference = "smoke-001", amount = 42.50m }, timeout.Token);
        Assert.True(create.IsSuccessStatusCode, $"create failed: {create.StatusCode}");

        // 2. The event round-trips broker → consumer confirms the order.
        JsonElement item = default;
        await WaitUntilAsync(async () =>
        {
            var page = await client.GetFromJsonAsync<JsonElement>("/api/v1/orders?size=10", timeout.Token);
            var items = page.GetProperty("items");
            if (items.GetArrayLength() == 0)
            {
                return false;
            }

            item = items[0];
            return item.GetProperty("status").GetString() == "Confirmed";
        }, timeout.Token);

        // 3. Wire contract sanity: camelCase, enum-as-string, cursor shape.
        Assert.Equal("smoke-001", item.GetProperty("reference").GetString());
        var page = await client.GetFromJsonAsync<JsonElement>("/api/v1/orders?size=10", timeout.Token);
        // Wire policy: nulls are not written — an absent nextCursor means the last page.
        Assert.False(page.TryGetProperty("nextCursor", out var next) && next.ValueKind is not JsonValueKind.Null);
        Assert.Equal(10, page.GetProperty("size").GetInt32());
//#endif
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

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }
}
