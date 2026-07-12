using System.Collections.Concurrent;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Goldpath.Tests;

public class MessagingTests
{
    public record OrderConfirmed(Guid OrderId) : IIntegrationEvent;

    public record NotAnIntegrationEvent(string Value);

    private sealed class FixedTenantContext : ITenantContext
    {
        public TenantId? Current => TenantId.Create("acme");
    }

    private sealed class Probe
    {
        public TaskCompletionSource<TenantId?> ConsumedTenant { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentBag<DateTimeOffset> Attempts { get; } = [];
    }

    private sealed class OrderConfirmedConsumer(Probe probe, GoldpathMessageTenantContext tenant) : IConsumer<OrderConfirmed>
    {
        public Task Consume(ConsumeContext<OrderConfirmed> context)
        {
            probe.ConsumedTenant.TrySetResult(tenant.Current);
            return Task.CompletedTask;
        }
    }

    public record AlwaysFails(int Id) : IIntegrationEvent;

    private sealed class FailingConsumer(Probe probe) : IConsumer<AlwaysFails>
    {
        public Task Consume(ConsumeContext<AlwaysFails> context)
        {
            probe.Attempts.Add(DateTimeOffset.UtcNow);
            throw new InvalidOperationException("intentional");
        }
    }

    private static async Task<IHost> StartHostAsync(Action<IBusRegistrationConfigurator>? extraBus = null, Action<GoldpathMessagingOptions>? options = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<Probe>();
        builder.Services.AddSingleton<ITenantContext, FixedTenantContext>();

        builder.AddGoldpathMessaging(bus =>
        {
            bus.AddConsumer<OrderConfirmedConsumer>();
            extraBus?.Invoke(bus);
            bus.UsingInMemory((context, cfg) => cfg.ConfigureGoldpathEndpoints(context));
        }, options);

        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    [Fact]
    public async Task Publish_consume_round_trip_restores_tenant_from_headers()
    {
        using var host = await StartHostAsync();
        var probe = host.Services.GetRequiredService<Probe>();

        await host.Services.GetRequiredService<IPublishEndpoint>()
            .Publish(new OrderConfirmed(Guid.NewGuid()));

        var tenant = await probe.ConsumedTenant.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("acme", tenant!.Value.Value);   // stamped on publish, restored on consume
        await host.StopAsync();
    }

    [Fact]
    public async Task Publishing_an_unmarked_type_is_rejected_by_the_boundary_guard()
    {
        using var host = await StartHostAsync();

        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            host.Services.GetRequiredService<IPublishEndpoint>()
                .Publish(new NotAnIntegrationEvent("boom")));

        Assert.Contains("GP0401", Flatten(exception));
        await host.StopAsync();
    }

    [Fact]
    public async Task Failing_consumer_is_retried_immediately_then_faulted()
    {
        using var host = await StartHostAsync(
            extraBus: bus => bus.AddConsumer<FailingConsumer>(),
            options: o =>
            {
                o.Retry.ImmediateCount = 2;
                o.Retry.RedeliveryIntervals.Clear();   // no scheduler in-memory tests
            });
        var probe = host.Services.GetRequiredService<Probe>();

        await host.Services.GetRequiredService<IPublishEndpoint>().Publish(new AlwaysFails(1));

        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (probe.Attempts.Count < 3 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        Assert.Equal(3, probe.Attempts.Count);   // initial + 2 immediate retries, then error queue
        await host.StopAsync();
    }

    [Fact]
    public void Outbox_registration_composes_with_a_dbcontext()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDbContext<OutboxDb>(o => o.UseSqlite("DataSource=:memory:"));
        builder.AddGoldpathMessaging(bus =>
        {
            bus.AddGoldpathOutbox<OutboxDb>();
            bus.UsingInMemory((context, cfg) => cfg.ConfigureGoldpathEndpoints(context));
        });

        using var host = builder.Build();   // container builds: outbox services resolve
        Assert.NotNull(host.Services.GetService<GoldpathMessagingOptions>());
    }

    private sealed class OutboxDb(DbContextOptions<OutboxDb> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddInboxStateEntity();
            modelBuilder.AddOutboxMessageEntity();
            modelBuilder.AddOutboxStateEntity();
        }
    }

    private static string Flatten(Exception exception)
        => exception + (exception.InnerException is { } inner ? Flatten(inner) : "");
}
