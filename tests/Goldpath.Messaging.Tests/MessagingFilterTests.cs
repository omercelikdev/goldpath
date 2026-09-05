using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Goldpath.Tests;

/// <summary>
/// The corners the first mutation run left alive (2026-09-03, 69%): correlation across the
/// hop, the ambient tenant restored after consume, a message with NO tenant header, the
/// framework's own messages passing the boundary guard, and the options surface.
/// </summary>
public class MessagingFilterTests
{
    public record Ping(Guid Id) : IIntegrationEvent;

    public record Boom(int Id) : IIntegrationEvent;

    private sealed class Seen
    {
        public TaskCompletionSource<(TenantId? Tenant, string? Correlation, TenantId? Ambient)> Ping { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<string> Fault { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class PingConsumer(Seen seen, GoldpathMessageTenantContext tenant) : IConsumer<Ping>
    {
        public Task Consume(ConsumeContext<Ping> context)
        {
            // The filter tags the activity that was current when IT ran; the consumer runs
            // one child activity further down, so a reader walks up — exactly as the
            // publish filter does on the other side of the hop.
            string? tag = null;
            for (var a = Activity.Current; a is not null && tag is null; a = a.Parent)
            {
                tag = a.GetTagItem("goldpath.correlation_id") as string;
            }

            var header = context.Headers.Get<string>(GoldpathHeaders.CorrelationId);
            seen.Ping.TrySetResult((tenant.Current, header is null ? null : $"{header}|{tag}", GoldpathAmbientTenant.Current));
            return Task.CompletedTask;
        }
    }

    private sealed class BoomConsumer : IConsumer<Boom>
    {
        public Task Consume(ConsumeContext<Boom> context) => throw new InvalidOperationException("intentional");
    }

    // A Fault<T> is MassTransit's OWN message: it carries no IIntegrationEvent marker and
    // still must cross the boundary guard (the namespace exemption).
    private sealed class BoomFaultConsumer(Seen seen) : IConsumer<Fault<Boom>>
    {
        public Task Consume(ConsumeContext<Fault<Boom>> context)
        {
            seen.Fault.TrySetResult(context.Message.Exceptions.FirstOrDefault()?.Message ?? "");
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTenant(string value) : ITenantContext
    {
        public TenantId? Current => TenantId.Create(value);
    }

    private static async Task<IHost> StartAsync(Action<IServiceCollection>? services = null, Action<GoldpathMessagingOptions>? options = null, Action<HostApplicationBuilder>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<Seen>();
        services?.Invoke(builder.Services);
        configure?.Invoke(builder);
        builder.AddGoldpathMessaging(bus =>
        {
            bus.AddConsumer<PingConsumer>();
            bus.AddConsumer<BoomConsumer>();
            bus.AddConsumer<BoomFaultConsumer>();
            bus.UsingInMemory((context, cfg) => cfg.ConfigureGoldpathEndpoints(context));
        }, options ?? (o => o.Retry.RedeliveryIntervals.Clear()));
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    [Fact]
    public async Task Correlation_rides_the_headers_and_the_ambient_tenant_is_restored_after_the_hop()
    {
        // MassTransit starts its own activities on consume; a listener makes them real so
        // Activity.Current exists where the consume filter tags it.
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using var host = await StartAsync(s => s.AddSingleton<ITenantContext>(new FixedTenant("acme")));
        var seen = host.Services.GetRequiredService<Seen>();
        var previousAmbient = TenantId.Create("caller");
        GoldpathAmbientTenant.Current = previousAmbient;

        using (var activity = new ActivitySource("test").StartActivity("publish"))
        {
            activity!.SetTag("goldpath.correlation_id", "corr-7");
            await host.Services.GetRequiredService<IPublishEndpoint>().Publish(new Ping(Guid.NewGuid()));
        }

        var (tenant, correlation, ambient) = await seen.Ping.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("acme", tenant!.Value.Value);
        Assert.Equal("acme", ambient!.Value.Value);            // the ambient truth inside the consumer is the ORIGIN tenant
        Assert.Equal("corr-7|corr-7", correlation);              // stamped on publish (header), tagged on consume (activity)
        Assert.Equal(previousAmbient, GoldpathAmbientTenant.Current);   // and restored on this thread afterwards — never leaked
        GoldpathAmbientTenant.Current = null;
        await host.StopAsync();
    }

    [Fact]
    public async Task A_message_without_a_tenant_header_leaves_the_consumer_tenantless()
    {
        using var host = await StartAsync();   // no ITenantContext registered: nothing to stamp
        var seen = host.Services.GetRequiredService<Seen>();

        await host.Services.GetRequiredService<IPublishEndpoint>().Publish(new Ping(Guid.NewGuid()));

        var (tenant, _, ambient) = await seen.Ping.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(tenant);
        Assert.Null(ambient);
        await host.StopAsync();
    }

    [Fact]
    public async Task The_frameworks_own_fault_messages_pass_the_boundary_guard()
    {
        using var host = await StartAsync(options: o =>
        {
            o.Retry.ImmediateCount = 0;
            o.Retry.RedeliveryIntervals.Clear();
        });
        var seen = host.Services.GetRequiredService<Seen>();

        await host.Services.GetRequiredService<IPublishEndpoint>().Publish(new Boom(1));

        // Fault<Boom> lives in the MassTransit namespace and carries no marker — the guard
        // must let it through, or every consumer failure would be swallowed at the boundary.
        Assert.Equal("intentional", await seen.Fault.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        await host.StopAsync();
    }

    [Fact]
    public async Task Options_bind_from_configuration_then_the_callback_applies_on_top()
    {
        GoldpathMessagingOptions? captured = null;
        using var host = await StartAsync(
            configure: b => b.Configuration["Goldpath:Messaging:Retry:ImmediateCount"] = "5",
            options: o =>
            {
                captured = o;
                o.Retry.RedeliveryIntervals.Clear();
            });

        Assert.NotNull(captured);
        Assert.Equal(5, captured!.Retry.ImmediateCount);                       // bound from configuration
        Assert.Same(captured, host.Services.GetRequiredService<GoldpathMessagingOptions>());
        await host.StopAsync();
    }

    [Fact]
    public void The_defaults_are_the_RFC_D4_ladder()
    {
        var options = new GoldpathMessagingOptions();
        Assert.Equal(3, options.Retry.ImmediateCount);
        Assert.Equal([TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30)], options.Retry.RedeliveryIntervals);
    }

    [Fact]
    public async Task The_publisher_seam_is_scoped_and_delegates_to_the_bus()
    {
        using var host = await StartAsync();
        var seen = host.Services.GetRequiredService<Seen>();
        using var scope = host.Services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>().PublishAsync(new Ping(Guid.NewGuid()));

        var (tenant, _, _) = await seen.Ping.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(tenant);
        // Scoped, because the outbox's publish endpoint is: the same scope resolves the same instance.
        Assert.Same(scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>(), scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>());
        await host.StopAsync();
    }
}
