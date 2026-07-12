using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace Goldpath.Tests;

public class ServiceDefaultsTests
{
    private static async Task<WebApplication> StartAppAsync(
        Action<GoldpathServiceDefaultsOptions>? configure = null,
        string environment = "Production",
        Action<WebApplication>? map = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.AddGoldpathServiceDefaults(configure);

        var app = builder.Build();
        app.MapGoldpathDefaultEndpoints();
        app.MapGet("/ok", () => "ok");
        app.MapGet("/boom", () => { throw new InvalidOperationException("secret internal detail"); });
        map?.Invoke(app);

        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task Goldpath_module_meters_actually_leave_the_process()
    {
        // The regression this guards: every module ships a meter and a Grafana board,
        // but without the AddMeter subscriptions the collector never hears a single
        // measurement — the boards would be BLANK in production.
        var exported = new List<Metric>();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.AddGoldpathServiceDefaults();
        builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddInMemoryExporter(exported));

        await using var app = builder.Build();
        await app.StartAsync();

        using var goldpathMeter = new Meter("Goldpath.ProofOfExport");
        goldpathMeter.CreateCounter<long>("goldpath_proof_total").Add(3);
        using var busMeter = new Meter("MassTransit");
        busMeter.CreateCounter<long>("masstransit_proof_total").Add(1);
        using var strangerMeter = new Meter("SomeApp.Internal");
        strangerMeter.CreateCounter<long>("stranger_total").Add(1);

        app.Services.GetRequiredService<MeterProvider>().ForceFlush();

        Assert.Contains(exported, m => m.Name == "goldpath_proof_total");        // "Goldpath.*" wildcard
        Assert.Contains(exported, m => m.Name == "masstransit_proof_total");
        Assert.DoesNotContain(exported, m => m.Name == "stranger_total");   // no accidental firehose
    }

    [Fact]
    public async Task Goldpath_module_spans_actually_leave_the_process()
    {
        // The tracing twin of the meter proof above: every module starts run/chunk/replay
        // spans, but without the AddSource subscriptions each StartActivity is a silent
        // no-op — the H4 correlation chain would never reach the collector.
        var exported = new List<Activity>();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.AddGoldpathServiceDefaults(o => o.Observability.Profile = ObservabilityProfile.Full);   // AlwaysOn: no sampling noise
        builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(exported));

        await using var app = builder.Build();
        await app.StartAsync();

        using var goldpathSource = new ActivitySource("Goldpath.ProofOfExport");
        goldpathSource.StartActivity("goldpath.proof")?.Dispose();
        using var busSource = new ActivitySource("MassTransit");
        busSource.StartActivity("bus.proof")?.Dispose();
        using var strangerSource = new ActivitySource("SomeApp.Internal");
        strangerSource.StartActivity("stranger.proof")?.Dispose();

        app.Services.GetRequiredService<TracerProvider>().ForceFlush();

        Assert.Contains(exported, s => s.OperationName == "goldpath.proof");   // "Goldpath.*" wildcard
        Assert.Contains(exported, s => s.OperationName == "bus.proof");
        Assert.DoesNotContain(exported, s => s.OperationName == "stranger.proof");   // no accidental firehose
    }

    [Fact]
    public async Task Correlation_id_is_generated_and_echoed_when_absent()
    {
        await using var app = await StartAppAsync();
        var response = await app.GetTestClient().GetAsync("/ok");

        Assert.True(response.Headers.TryGetValues(GoldpathHeaders.CorrelationId, out var values));
        Assert.False(string.IsNullOrWhiteSpace(values!.Single()));
    }

    [Fact]
    public async Task Inbound_correlation_id_is_honored_by_default()
    {
        await using var app = await StartAppAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add(GoldpathHeaders.CorrelationId, "corr-42");

        var response = await client.GetAsync("/ok");

        Assert.Equal("corr-42", response.Headers.GetValues(GoldpathHeaders.CorrelationId).Single());
    }

    [Fact]
    public async Task Inbound_correlation_id_is_ignored_when_AcceptInbound_is_off()
    {
        await using var app = await StartAppAsync(o => o.Correlation.AcceptInbound = false);
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add(GoldpathHeaders.CorrelationId, "corr-42");

        var response = await client.GetAsync("/ok");

        Assert.NotEqual("corr-42", response.Headers.GetValues(GoldpathHeaders.CorrelationId).Single());
    }

    [Fact]
    public async Task Health_endpoints_return_plain_healthy_without_details()
    {
        await using var app = await StartAppAsync();
        var client = app.GetTestClient();

        foreach (var path in new[] { "/health/live", "/health/ready" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Unhandled_exception_becomes_problem_details_without_leaking_internals()
    {
        await using var app = await StartAppAsync();
        var response = await app.GetTestClient().GetAsync("/boom");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("correlationId", body);
        Assert.Contains("traceId", body);
        Assert.DoesNotContain("secret internal detail", body);
        Assert.DoesNotContain("InvalidOperationException", body);
    }

    [Fact]
    public async Task Concurrency_guard_rejects_excess_requests_as_problem_details()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = await StartAppAsync(
            configure: o =>
            {
                o.RateLimiting.ConcurrencyLimit = 1;
                o.RateLimiting.QueueLimit = 0;
            },
            map: a => a.MapGet("/slow", async () =>
            {
                await gate.Task;
                return "done";
            }));

        var client = app.GetTestClient();
        var held = client.GetAsync("/slow");
        await Task.Delay(200); // let the first request occupy the single permit

        var rejected = await client.GetAsync("/ok");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);

        gate.SetResult();
        Assert.Equal(HttpStatusCode.OK, (await held).StatusCode);
    }

    [Fact]
    public async Task Options_bind_from_configuration_section()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Configuration["Goldpath:ServiceDefaults:RateLimiting:ConcurrencyLimit"] = "7";
        builder.Configuration["Goldpath:ServiceDefaults:Observability:Profile"] = "Full";

        GoldpathServiceDefaultsOptions? captured = null;
        builder.AddGoldpathServiceDefaults(o => captured = o);

        Assert.NotNull(captured);
        Assert.Equal(7, captured!.RateLimiting.ConcurrencyLimit);
        Assert.Equal(ObservabilityProfile.Full, captured.Observability.Profile);
    }
}
