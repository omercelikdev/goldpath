using Mediant.Behaviors.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Goldpath;

/// <summary>
/// Registers Ring B idempotency: the HTTP <c>Idempotency-Key</c> middleware plus Mediant's
/// coordinator/store (composed, not rewritten — the command path's <c>[Idempotent]</c> uses
/// the very same store, so HTTP and command semantics can never drift apart).
/// </summary>
public static class GoldpathIdempotencyExtensions
{
    /// <summary>
    /// Adds the idempotency layer. Options bind from <c>Goldpath:Idempotency</c>;
    /// <paramref name="configure"/> applies on top. The store is Mediant's
    /// distributed-cache-backed <c>IIdempotencyStore</c> over whatever
    /// <c>IDistributedCache</c> the host registers (Redis in the golden path; in-memory in dev/tests).
    /// </summary>
    public static TBuilder AddGoldpathIdempotency<TBuilder>(this TBuilder builder, Action<GoldpathIdempotencyOptions>? configure = null)
        where TBuilder : IHostApplicationBuilder
    {
        var options = new GoldpathIdempotencyOptions();
        builder.Configuration.GetSection("Goldpath:Idempotency").Bind(options);
        configure?.Invoke(options);
        builder.Services.AddSingleton(options);

        builder.Services.AddMediantDistributedCacheIdempotencyStore();   // store + coordinator (TryAdd)
        builder.Services.AddTransient<IStartupFilter, GoldpathIdempotencyStartupFilter>();
        return builder;
    }
}

internal sealed class GoldpathIdempotencyStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseMiddleware<GoldpathIdempotencyMiddleware>();
        next(app);
    };
}
