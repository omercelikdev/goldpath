using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Goldpath;

/// <summary>
/// Composition entry point. Declare the ladders with <c>AddGoldpathApprovals</c>; schedule
/// the escalation sweep through the Jobs module; the events publish through the messaging
/// seam when a broker is composed (and stay silent when not).
/// </summary>
public static class GoldpathApprovalsExtensions
{
    /// <summary>Registers the approvals engine and the declared ladders.</summary>
    public static TBuilder AddGoldpathApprovals<TBuilder>(this TBuilder builder, Action<GoldpathApprovalsOptions> configure)
        where TBuilder : IHostApplicationBuilder
    {
        var options = new GoldpathApprovalsOptions();
        builder.Configuration.GetSection("Goldpath:Approvals").Bind(options);
        configure(options);
        builder.Services.AddSingleton(options);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IGoldpathApprovalStore, GoldpathInMemoryApprovalStore>();
        // SCOPED, not singleton: the messaging seam's publisher is scoped ON PURPOSE (a
        // publish rides the current request/consumer scope — the outbox pattern), so the
        // engine that consumes it must live in a scope too. Caught by the GmEverything
        // exam the moment approvals met a broker shape.
        builder.Services.TryAddScoped<GoldpathApprovalEngine>();
        builder.Services.TryAddScoped<GoldpathApprovalEscalationJob>();
        return builder;
    }

    /// <summary>
    /// Registers the approvals engine with the DATABASE-backed store on the app's own
    /// DbContext (map it with <c>modelBuilder.AddGoldpathApprovalModel()</c>) — requests
    /// survive restarts and every node sees the same worklist.
    /// </summary>
    public static TBuilder AddGoldpathApprovals<TBuilder, TContext>(this TBuilder builder, Action<GoldpathApprovalsOptions> configure)
        where TBuilder : IHostApplicationBuilder
        where TContext : DbContext
    {
        builder.Services.AddSingleton<IGoldpathApprovalStore, GoldpathEfApprovalStore<TContext>>();
        return builder.AddGoldpathApprovals(configure);
    }
}
