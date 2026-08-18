using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Goldpath;

/// <summary>
/// Composition entry point. Declare the rails with <c>AddGoldpathFileExchange</c>; schedule
/// pick-up through the Jobs module; the events publish through the messaging seam when a
/// broker is composed (and stay silent when not).
/// </summary>
public static class GoldpathFileExchangeExtensions
{
    /// <summary>Registers the rail engine and the declared rails.</summary>
    public static TBuilder AddGoldpathFileExchange<TBuilder>(this TBuilder builder, Action<GoldpathFileExchangeOptions> configure)
        where TBuilder : IHostApplicationBuilder
    {
        var options = new GoldpathFileExchangeOptions();
        configure(options);
        builder.Services.AddSingleton(options);
        builder.Services.TryAddSingleton<IGoldpathFileLedger, GoldpathInMemoryFileLedger>();
        builder.Services.TryAddSingleton<GoldpathFileRailEngine>();
        return builder;
    }

    /// <summary>
    /// Registers the rail engine with the DATABASE-backed ledger on the app's own DbContext
    /// (map it with <c>modelBuilder.AddGoldpathFileExchangeModel()</c>) — the zero-duplicate
    /// guarantee survives restarts and holds across nodes.
    /// </summary>
    public static TBuilder AddGoldpathFileExchange<TBuilder, TContext>(this TBuilder builder, Action<GoldpathFileExchangeOptions> configure)
        where TBuilder : IHostApplicationBuilder
        where TContext : DbContext
    {
        builder.Services.AddSingleton<IGoldpathFileLedger, GoldpathEfFileLedger<TContext>>();
        return builder.AddGoldpathFileExchange(configure);
    }
}
