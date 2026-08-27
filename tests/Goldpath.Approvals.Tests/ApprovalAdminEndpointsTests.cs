using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Goldpath;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Goldpath.Approvals.Tests;

/// <summary>
/// The admin surface over real HTTP (TestServer): routes, the R3 query shape, and the
/// frozen verb envelope — including the rule-name-as-message refusal the console prints
/// verbatim. Decisions run the ENGINE; the principal-less test host decides as
/// "anonymous", which four-eyes treats like any other identity.
/// </summary>
public sealed class ApprovalAdminEndpointsTests : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly GoldpathApprovalEngine _engine;

    public ApprovalAdminEndpointsTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.AddGoldpathApprovals(approvals => approvals
            .AddLadder("credit-limit", l => l
                .Rung("expert", 1_000_000m, TimeSpan.FromHours(8))
                .TopRung("general-manager", TimeSpan.FromHours(24))));

        _app = builder.Build();
        _app.MapGoldpathApprovalsAdmin(exposeUnsecured: true);
        _app.Start();
        _client = _app.GetTestClient();
        _engine = _app.Services.GetRequiredService<GoldpathApprovalEngine>();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task The_list_answers_with_R3_filters_and_the_detail_carries_the_trail()
    {
        var pending = await _engine.RequestAsync("credit-limit", "K-1", 500_000m, "maker");
        var rejected = await _engine.RequestAsync("credit-limit", "K-2", 500_000m, "maker");
        await _engine.DecideAsync(rejected.Id, "checker", "expert", false, "collateral missing");

        var all = await _client.GetFromJsonAsync<JsonElement>("/goldpath/admin/approvals/requests?take=50");
        Assert.Equal(2, all.GetArrayLength());

        var filtered = await _client.GetFromJsonAsync<JsonElement>(
            "/goldpath/admin/approvals/requests?status=Pending&ladder=credit-limit&take=50");
        Assert.Equal(pending.Id.ToString(), Assert.Single(filtered.EnumerateArray()).GetProperty("id").GetString());

        var detail = await _client.GetFromJsonAsync<JsonElement>($"/goldpath/admin/approvals/requests/{rejected.Id}");
        Assert.Equal("Rejected", detail.GetProperty("request").GetProperty("status").GetString());
        Assert.Equal(2, detail.GetProperty("trail").GetArrayLength());

        var missing = await _client.GetAsync($"/goldpath/admin/approvals/requests/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Approve_answers_the_frozen_envelope_and_the_engine_applied_it()
    {
        var request = await _engine.RequestAsync("credit-limit", "K-3", 500_000m, "maker");

        var response = await _client.PostAsJsonAsync(
            $"/goldpath/admin/approvals/requests/{request.Id}/approve",
            new { role = "expert", reason = "fits the limit" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(envelope.GetProperty("ok").GetBoolean());
        Assert.Equal("Applied", envelope.GetProperty("message").GetString());

        var detail = await _client.GetFromJsonAsync<JsonElement>($"/goldpath/admin/approvals/requests/{request.Id}");
        Assert.Equal("Granted", detail.GetProperty("request").GetProperty("status").GetString());
        // The principal-less host decided as "anonymous" — the trail names it honestly.
        Assert.Equal("anonymous", detail.GetProperty("request").GetProperty("decidedBy").GetString());
    }

    [Fact]
    public async Task A_refusal_is_a_400_whose_message_is_the_rule_name()
    {
        var request = await _engine.RequestAsync("credit-limit", "K-4", 500_000m, "maker");

        // Blank rejection reason: the ENGINE's rule refuses, the envelope names it.
        var response = await _client.PostAsJsonAsync(
            $"/goldpath/admin/approvals/requests/{request.Id}/reject",
            new { role = "expert", reason = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(envelope.GetProperty("ok").GetBoolean());
        Assert.Equal("ReasonRequired", envelope.GetProperty("message").GetString());

        // A decision on an unknown id speaks through the SAME envelope — the engine's
        // NotFound outcome is a refusal message, not an HTTP 404 (that is the GET's job).
        var missing = await _client.PostAsJsonAsync(
            $"/goldpath/admin/approvals/requests/{Guid.NewGuid()}/approve", new { role = "expert" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var missingEnvelope = await missing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NotFound", missingEnvelope.GetProperty("message").GetString());
    }
}
