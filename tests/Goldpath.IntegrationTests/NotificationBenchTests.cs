using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;

namespace Goldpath.Tests.Integration;

/// <summary>
/// Notification RFC §6 performance proofs (scripts/bench-notification.sh →
/// ops/notification-benchmarks.md). Trait-gated: evidence, not regression tests.
/// </summary>
[Trait("Category", "Bench")]
public sealed class NotificationBenchTests : IAsyncLifetime
{
    private sealed class NopChannel : IGoldpathNotificationChannel
    {
        public string Name => "nop";

        public Task SendAsync(GoldpathNotificationMessage message, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private ServiceProvider _services = null!;
    private GoldpathNotificationOptions _options = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new GoldpathNotificationOptions { ChunkSize = 500 };
        _options.AddTemplate("renewal", t => t.Channel("nop", c => c
            .Subject("", "Policy {{PolicyNo}} renews")
            .Body("", "Dear {{Name}}, policy {{PolicyNo}} renews on {{RenewalDate}}.")));

        var services = new ServiceCollection();
        services.AddDbContext<NotificationTests.NotifyDb>(o => o.UseNpgsql(_postgres.GetConnectionString()));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_options);
        services.AddScoped<IGoldpathNotifier, GoldpathNotifier<NotificationTests.NotifyDb>>();
        services.AddLogging();
        _services = services.BuildServiceProvider();
        using var scope = _services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<NotificationTests.NotifyDb>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public void Bench_render_micro()
    {
        var template = _options.Template("renewal").ChannelTemplate("nop");
        var tokens = new Dictionary<string, string> { ["Name"] = "Ömer", ["PolicyNo"] = "P-42", ["RenewalDate"] = "2026-08-01" };
        for (var warm = 0; warm < 1_000; warm++)
        {
            template.Render("tr-TR", tokens);
        }

        var watch = Stopwatch.StartNew();
        const int N = 100_000;
        for (var i = 0; i < N; i++)
        {
            template.Render("tr-TR", tokens);
        }

        watch.Stop();
        var microseconds = watch.Elapsed.TotalMicroseconds / N;
        Console.WriteLine($"BENCH-NOTIFICATION render={microseconds:F1}us/render (budget 1000us)");
        Assert.True(microseconds < 1_000, $"render {microseconds:F1}us exceeds the sub-millisecond budget");
    }

    [Fact]
    public async Task Bench_the_insurance_night_10k_requests_then_the_send_pass()
    {
        // REQUEST phase: 10k rendered + evidence-persisted notifications.
        var watch = Stopwatch.StartNew();
        using (var scope = _services.CreateScope())
        {
            var notifier = scope.ServiceProvider.GetRequiredService<IGoldpathNotifier>();
            for (var i = 0; i < 10_000; i++)
            {
                await notifier.RequestAsync(new GoldpathNotificationRequest(
                    "renewal", "nop", $"holder-{i}@example.test", "",
                    new Dictionary<string, string> { ["Name"] = $"H{i}", ["PolicyNo"] = $"P-{i}", ["RenewalDate"] = "2026-08-01" },
                    dedupKey: $"renewal:P-{i}:2026-08"), CancellationToken.None);
            }
        }

        var requestSeconds = watch.Elapsed.TotalSeconds;

        // SEND phase: the claim+stamp pipeline against a no-op channel.
        watch.Restart();
        var job = new GoldpathNotificationSendJob<NotificationTests.NotifyDb>(
            _options, [new NopChannel()], TimeProvider.System,
            NullLogger<GoldpathNotificationSendJob<NotificationTests.NotifyDb>>.Instance);
        using (var scope = _services.CreateScope())
        {
            var context = (GoldpathJobContext)Activator.CreateInstance(typeof(GoldpathJobContext),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
                [Guid.NewGuid(), "bench", "node", "job", false, null, scope.ServiceProvider], null)!;
            var plan = await job.PlanAsync(context, CancellationToken.None);
            for (var i = 0; i < plan.ChunkPayloads.Count; i++)
            {
                var chunk = (GoldpathJobChunk)Activator.CreateInstance(typeof(GoldpathJobChunk),
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, [i, plan.ChunkPayloads[i]], null)!;
                await job.ExecuteChunkAsync(chunk, context, CancellationToken.None);
            }
        }

        watch.Stop();
        using var check = _services.CreateScope();
        var sent = await check.ServiceProvider.GetRequiredService<NotificationTests.NotifyDb>()
            .Set<GoldpathNotification>().CountAsync(n => n.State == GoldpathNotificationState.Sent);
        Assert.Equal(10_000, sent);
        Console.WriteLine(
            $"BENCH-NOTIFICATION request10k={requestSeconds:F1}s send10k={watch.Elapsed.TotalSeconds:F1}s rows/s={10_000 / watch.Elapsed.TotalSeconds:F0}");
    }
}
