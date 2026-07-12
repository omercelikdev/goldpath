using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Goldpath;

/// <summary>
/// Registers a DbContext on the Goldpath data floor: the save-contributor interceptor is attached,
/// the BCL <see cref="TimeProvider"/> is available, and the provider stays the caller's choice
/// (wired by the template from the manifest — this package is provider-neutral, ADR-0003).
/// </summary>
public static class GoldpathDataExtensions
{
    /// <summary>
    /// Adds <typeparamref name="TContext"/> with the Goldpath save-contributor seam attached.
    /// Configure the provider (UseNpgsql/UseSqlServer/…) in <paramref name="configure"/>.
    /// </summary>
    public static TBuilder AddGoldpathData<TBuilder, TContext>(
        this TBuilder builder,
        Action<DbContextOptionsBuilder> configure)
        where TBuilder : IHostApplicationBuilder
        where TContext : DbContext
    {
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddDbContext<TContext>((serviceProvider, options) =>
        {
            var saveContext = new GoldpathSaveContext(
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetService<ITenantContext>()?.Current,
                serviceProvider.GetService<IUserContext>()?.UserId);

            options.AddInterceptors(new GoldpathSaveChangesInterceptor(
                serviceProvider.GetServices<IEntitySaveContributor>(),
                saveContext));

            configure(options);
        });

        return builder;
    }
}
