using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Goldpath;

/// <summary>
/// The message-path floor: MassTransit (8.x OSS line — RFC D1) composed with corporate
/// conventions. The transport is wired by the template from the manifest
/// (<c>providers.broker</c>) inside <c>configureBus</c>; this package stays transport-neutral.
/// </summary>
public static class GoldpathMessagingExtensions
{
    /// <summary>
    /// Registers MassTransit with the Goldpath conventions: kebab-case endpoint naming, the
    /// message-scoped tenant holder, and options bound from <c>Goldpath:Messaging</c>.
    /// Configure consumers AND the transport in <paramref name="configureBus"/>; inside the
    /// transport callback, call <see cref="ConfigureGoldpathEndpoints"/> to apply the corporate
    /// pipeline (guard/propagation filters + retry) and endpoint conventions.
    /// </summary>
    public static TBuilder AddGoldpathMessaging<TBuilder>(
        this TBuilder builder,
        Action<IBusRegistrationConfigurator> configureBus,
        Action<GoldpathMessagingOptions>? configureOptions = null)
        where TBuilder : IHostApplicationBuilder
    {
        var options = new GoldpathMessagingOptions();
        builder.Configuration.GetSection("Goldpath:Messaging").Bind(options);
        configureOptions?.Invoke(options);
        builder.Services.AddSingleton(options);

        builder.Services.TryAddScoped<GoldpathMessageTenantContext>();
        builder.Services.TryAddScoped<ITenantContext>(sp => sp.GetRequiredService<GoldpathMessageTenantContext>());

        builder.Services.AddMassTransit(bus =>
        {
            bus.SetKebabCaseEndpointNameFormatter();
            configureBus(bus);
        });

        return builder;
    }

    /// <summary>
    /// Applies the Goldpath message pipeline to a transport: the integration-event guard and
    /// tenant/correlation propagation filters, immediate retry, delayed redelivery (when
    /// intervals are configured), and MassTransit endpoint conventions. Call this inside the
    /// transport callback (e.g. <c>UsingRabbitMq((ctx, cfg) => { …; cfg.ConfigureGoldpathEndpoints(ctx); })</c>).
    /// </summary>
    public static void ConfigureGoldpathEndpoints<TEndpoint>(this IBusFactoryConfigurator<TEndpoint> configurator, IBusRegistrationContext context)
        where TEndpoint : IReceiveEndpointConfigurator
    {
        var options = context.GetRequiredService<GoldpathMessagingOptions>();

        configurator.UsePublishFilter(typeof(GoldpathPublishFilter<>), context);
        configurator.UseConsumeFilter(typeof(GoldpathConsumeFilter<>), context);

        if (options.Retry.RedeliveryIntervals.Count > 0)
        {
            configurator.UseDelayedRedelivery(retry => retry.Intervals([.. options.Retry.RedeliveryIntervals]));
        }

        configurator.UseMessageRetry(retry => retry.Immediate(options.Retry.ImmediateCount));
        configurator.ConfigureEndpoints(context);
    }

    /// <summary>
    /// Composes MassTransit's Entity Framework transactional outbox with the service's
    /// DbContext (RFC D3: publish commits atomically with business data; consumer inbox dedup
    /// included). The DB lock provider (UsePostgres/UseSqlServer) is set by the template in
    /// <paramref name="configure"/> — provider-neutral here.
    /// </summary>
    public static void AddGoldpathOutbox<TContext>(
        this IBusRegistrationConfigurator configurator,
        Action<IEntityFrameworkOutboxConfigurator>? configure = null)
        where TContext : DbContext
    {
        configurator.AddEntityFrameworkOutbox<TContext>(outbox =>
        {
            outbox.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
            outbox.UseBusOutbox();
            configure?.Invoke(outbox);
        });

        // Consumer-side inbox: every receive endpoint gets exactly-once-processing dedup
        // automatically — without this callback only the publish side would be outboxed.
        configurator.AddConfigureEndpointsCallback((context, _, endpointConfigurator) =>
            endpointConfigurator.UseEntityFrameworkOutbox<TContext>(context));
    }
}
