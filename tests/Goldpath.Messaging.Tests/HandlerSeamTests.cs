using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Goldpath.Tests;

/// <summary>
/// The consume seam (ADR-0013): a handler that names no bus type receives the event with
/// the pipeline's facts — message id, correlation, tenant, attempt — drains the SAME queue
/// a consumer of that name would, and rides retry like any consumer.
/// </summary>
public class HandlerSeamTests
{
    public record OrderConfirmed(Guid OrderId) : IIntegrationEvent;

    public record Flaky(int Id) : IIntegrationEvent;

    public sealed class Seen
    {
        public TaskCompletionSource<(OrderConfirmed Event, IntegrationEventContext Context)> Order { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<int> Attempts { get; } = [];

        public TaskCompletionSource Faulted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class OrderConfirmedHandler(Seen seen) : IIntegrationEventHandler<OrderConfirmed>
    {
        public Task HandleAsync(OrderConfirmed integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken = default)
        {
            seen.Order.TrySetResult((integrationEvent, context));
            return Task.CompletedTask;
        }
    }

    public sealed class FlakyHandler(Seen seen) : IIntegrationEventHandler<Flaky>
    {
        public Task HandleAsync(Flaky integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken = default)
        {
            lock (seen.Attempts)
            {
                seen.Attempts.Add(context.RetryAttempt);
            }

            throw new InvalidOperationException("intentional");
        }
    }

    private sealed class FlakyFaultConsumer(Seen seen) : IConsumer<Fault<Flaky>>
    {
        public Task Consume(ConsumeContext<Fault<Flaky>> context)
        {
            seen.Faulted.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTenant : ITenantContext
    {
        public TenantId? Current => TenantId.Create("acme");
    }

    private static async Task<IHost> StartAsync(Action<IBusRegistrationConfigurator> registerHandlers, bool tenant = false)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<Seen>();
        if (tenant)
        {
            builder.Services.AddSingleton<ITenantContext, FixedTenant>();
        }

        builder.AddGoldpathMessaging(bus =>
        {
            registerHandlers(bus);
            bus.AddConsumer<FlakyFaultConsumer>();
            bus.UsingInMemory((context, cfg) => cfg.ConfigureGoldpathEndpoints(context));
        }, o =>
        {
            o.Retry.ImmediateCount = 2;
            o.Retry.RedeliveryIntervals.Clear();
        });
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    [Fact]
    public async Task A_handler_receives_the_event_and_the_pipelines_facts()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using var host = await StartAsync(bus => bus.AddGoldpathHandler<OrderConfirmed, OrderConfirmedHandler>(), tenant: true);
        var seen = host.Services.GetRequiredService<Seen>();
        var orderId = Guid.NewGuid();

        using (var activity = new ActivitySource("test").StartActivity("publish"))
        {
            activity!.SetTag("goldpath.correlation_id", "corr-11");
            using var scope = host.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>().PublishAsync(new OrderConfirmed(orderId));
        }

        var (received, context) = await seen.Order.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(orderId, received.OrderId);
        Assert.NotNull(context.MessageId);                       // the inbox's key, exposed without the library
        Assert.Equal("corr-11", context.CorrelationId);
        Assert.Equal("acme", context.Tenant!.Value.Value);
        Assert.Equal(0, context.RetryAttempt);
        Assert.Equal("acme", context.Headers[GoldpathHeaders.TenantId]);   // the raw headers are there for anything else
        await host.StopAsync();
    }

    [Fact]
    public async Task A_throwing_handler_rides_the_retry_ladder_then_faults()
    {
        using var host = await StartAsync(bus => bus.AddGoldpathHandler<Flaky, FlakyHandler>());
        var seen = host.Services.GetRequiredService<Seen>();

        await host.Services.GetRequiredService<IPublishEndpoint>().Publish(new Flaky(1));

        await seen.Faulted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // Initial + two immediate retries, each told which attempt it is.
        Assert.Equal([0, 1, 2], seen.Attempts.Order());
        await host.StopAsync();
    }

    [Fact]
    public async Task Assembly_scanning_registers_every_handler_once()
    {
        using var host = await StartAsync(bus => bus.AddGoldpathHandlers(typeof(HandlerSeamTests).Assembly));
        var seen = host.Services.GetRequiredService<Seen>();

        await host.Services.GetRequiredService<IPublishEndpoint>().Publish(new OrderConfirmed(Guid.NewGuid()));

        var (_, context) = await seen.Order.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(context.Tenant);   // no tenant context registered in this host
        await host.StopAsync();
    }

    [Theory]
    [InlineData(typeof(OrderConfirmedHandler), "order-confirmed")]
    [InlineData(typeof(FlakyHandler), "flaky")]
    [InlineData(typeof(FlakyFaultConsumer), "flaky-fault")]
    public void The_queue_is_named_after_the_handler_exactly_as_a_consumers_would_be(Type handler, string expected)
        // WorkItemQueuedConsumer drained work-item-queued; WorkItemQueuedHandler drains the same
        // queue — moving to the seam changes no wire (exit RFC §7.1 S4).
        => Assert.Equal(expected, GoldpathHandlerEndpoints.NameOf(handler));

    private sealed class HTTPGatewayHandler;

    [Fact]
    public void Acronyms_kebab_the_way_the_floor_always_did()
        => Assert.Equal("http-gateway", GoldpathHandlerEndpoints.NameOf(typeof(HTTPGatewayHandler)));
}
