using System.Net.Http.Json;
using Aspire.Hosting.Testing;
#if (UseQueue)
using GoldpathWorker.Host.WorkItems;
using MassTransit;
#endif
#if (UseJobs)
using System.Text.Json;
#endif
#if (UseSchedule)
using GoldpathWorker.Host.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
#endif
using Xunit;

namespace GoldpathWorker.SmokeTests;

/// <summary>
/// The "runs with one click" proof for the worker kind: the REAL AppHost starts (containers
/// included) and the trigger is exercised end to end — a published message lands in the
/// processed store exactly once (queue), or the interval job ticks (schedule). No mocks.
/// </summary>
public class SmokeTests
{
#if (UseQueue)
    [Fact]
    public async Task Queued_work_is_processed_exactly_once()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.GoldpathWorker_AppHost>(timeout.Token);
        await using var app = await appHost.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);

        var client = app.CreateHttpClient("worker");

        // Readiness (containers + schema + bus).
        await WaitUntilAsync(async () =>
            (await client.GetAsync("/health/ready", timeout.Token)).IsSuccessStatusCode, timeout.Token);

        // Publish INTO the running broker, exactly as an upstream service would.
        var messagingConnection = await app.GetConnectionStringAsync("messaging", timeout.Token);
        var bus = Bus.Factory.CreateUsingRabbitMq(cfg => cfg.Host(new Uri(messagingConnection!)));
        await bus.StartAsync(timeout.Token);
        try
        {
            var workItemId = Guid.NewGuid();
            var messageId = Guid.NewGuid();
            await bus.Publish(new WorkItemQueued(workItemId, "smoke-payload"),
                ctx => ctx.MessageId = messageId, timeout.Token);

            // 1. The message round-trips broker → consumer → processed store.
            await WaitUntilAsync(async () =>
                (await ProcessedAsync(client, timeout.Token)).Count == 1, timeout.Token);

            // 2. Same MessageId again: the inbox must dedup (exactly-once processing).
            await bus.Publish(new WorkItemQueued(workItemId, "smoke-payload"),
                ctx => ctx.MessageId = messageId, timeout.Token);
            await Task.Delay(TimeSpan.FromSeconds(5), timeout.Token);
            var processed = Assert.Single(await ProcessedAsync(client, timeout.Token));
            Assert.Equal(workItemId, processed.Id);
        }
        finally
        {
            await bus.StopAsync(timeout.Token);
        }
    }

    private static async Task<List<ProcessedWorkItem>> ProcessedAsync(HttpClient client, CancellationToken token)
        => await client.GetFromJsonAsync<List<ProcessedWorkItem>>("/api/v1/processed", token) ?? [];
#endif
#if (UseJobs)
    [Fact]
    public async Task Nightly_report_runs_through_the_audited_admin_verbs()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.GoldpathWorker_AppHost>(timeout.Token);
        await using var app = await appHost.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);

        var client = app.CreateHttpClient("worker");
        await WaitUntilAsync(async () =>
            (await client.GetAsync("/health/ready", timeout.Token)).IsSuccessStatusCode, timeout.Token);

        // The fleet is discovered from the store (jobs RFC D9) — no configuration here.
        const string fleet = "GoldpathWorker.Host";
        await WaitUntilAsync(async () =>
            (await client.GetAsync($"/goldpath/admin/jobs/fleets/{fleet}/jobs", timeout.Token)).IsSuccessStatusCode, timeout.Token);

        // Trigger through the ADMIN VERB (what the dashboard, the portal and the AI skills use).
        var trigger = await client.PostAsync($"/goldpath/admin/jobs/fleets/{fleet}/jobs/NightlyReportJob/trigger", null, timeout.Token);
        Assert.True(trigger.IsSuccessStatusCode, $"trigger failed: {trigger.StatusCode}");

        // The run completes: 6 chunks (30 days / 5), checkpointed, zero repair items.
        await WaitUntilAsync(async () =>
        {
            var runs = await client.GetFromJsonAsync<JsonElement>($"/goldpath/admin/jobs/fleets/{fleet}/runs?job=NightlyReportJob", timeout.Token);
            return runs.ValueKind == JsonValueKind.Array
                && runs.EnumerateArray().Any(r => r.GetProperty("status").GetString() == "Completed");
        }, timeout.Token);

        var completed = (await client.GetFromJsonAsync<JsonElement>($"/goldpath/admin/jobs/fleets/{fleet}/runs?job=NightlyReportJob", timeout.Token))
            .EnumerateArray().First(r => r.GetProperty("status").GetString() == "Completed");
        Assert.Equal(6, completed.GetProperty("totalChunks").GetInt32());
        Assert.Equal(6, completed.GetProperty("completedChunks").GetInt32());
        Assert.Equal(0, completed.GetProperty("itemFailures").GetInt32());

        // Iron rule 2: the verb left an audit row.
        var audit = await client.GetFromJsonAsync<JsonElement>("/goldpath/admin/jobs/audit", timeout.Token);
        Assert.Contains(audit.EnumerateArray(), a => a.GetProperty("action").GetString() == "trigger");
    }
#endif
#if (UseSchedule)
    [Fact]
    public async Task Interval_job_ticks_against_the_real_host()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.GoldpathWorker_AppHost>(timeout.Token);
        await using var app = await appHost.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);

        var client = app.CreateHttpClient("worker");
        await WaitUntilAsync(async () =>
            (await client.GetAsync("/health/ready", timeout.Token)).IsSuccessStatusCode, timeout.Token);

        // The AppHost pins Worker:Interval to 1s for the dev loop — a tick must land quickly.
        await WaitUntilAsync(async () =>
        {
            var tick = await client.GetFromJsonAsync<TickReport>("/api/v1/ticks", timeout.Token);
            return tick is { Count: >= 1 };
        }, timeout.Token);
    }

    [Fact]
    public async Task Tick_unit_is_directly_testable_without_time()
    {
        // Time-abstracted on purpose: the schedule is a hosting concern, the tick is the unit.
        var job = new IntervalJob(NullLogger<IntervalJob>.Instance, new ConfigurationBuilder().Build());
        await job.RunTickAsync();
        Assert.Equal(1, job.TickCount);
    }

    private sealed record TickReport(int Count);
#endif

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken token)
    {
        while (!await SafeCheckAsync(condition))
        {
            await Task.Delay(TimeSpan.FromSeconds(1), token);
        }
    }

    private static async Task<bool> SafeCheckAsync(Func<Task<bool>> condition)
    {
        try
        {
            return await condition();
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}
