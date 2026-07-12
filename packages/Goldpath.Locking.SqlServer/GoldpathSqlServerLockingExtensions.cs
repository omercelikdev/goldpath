using Medallion.Threading;
using Medallion.Threading.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Goldpath;

/// <summary>
/// SQL Server locking registration — the optional-package counterpart of
/// <c>AddGoldpathLocking()</c> (see the package description for WHY it is separate).
/// </summary>
public static class GoldpathSqlServerLockingExtensions
{
    /// <summary>Adds the <c>sp_getapplock</c>-backed provider and the <see cref="GoldpathLockNames"/> helper.</summary>
    public static TBuilder AddGoldpathSqlServerLocking<TBuilder>(this TBuilder builder, Action<GoldpathLockingOptions>? configure = null)
        where TBuilder : IHostApplicationBuilder
    {
        var options = new GoldpathLockingOptions { Provider = GoldpathLockProvider.SqlServer, ConnectionName = "database" };
        builder.Configuration.GetSection("Goldpath:DistributedLocking").Bind(options);
        configure?.Invoke(options);
        builder.Services.AddSingleton(options);

        var configuration = builder.Configuration;
        builder.Services.AddSingleton<IDistributedLockProvider>(_ =>
        {
            var connectionString = configuration.GetConnectionString(options.ConnectionName)
                ?? throw new InvalidOperationException(
                    $"Goldpath:DistributedLocking (SqlServer) needs the '{options.ConnectionName}' connection string.");
            return new GoldpathMeteredLockProvider(new SqlDistributedSynchronizationProvider(connectionString));
        });

        builder.Services.AddScoped<GoldpathLockNames>();
        return builder;
    }
}
