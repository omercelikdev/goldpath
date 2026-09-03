using Goldpath;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Goldpath.FileExchange.Tests;

/// <summary>
/// The composition contract: what <c>AddGoldpathFileExchange</c> actually registers — the
/// declared rails as options, the in-memory ledger as the DEFAULT, the database ledger
/// when a context is named, and the engine SCOPED (the messaging seam's publisher is
/// scoped, so the engine that consumes it must be too — the GmEverything finding).
/// </summary>
public class CompositionTests
{
    private sealed record Row(string Reference);

    private static void DeclareRail(GoldpathFileExchangeOptions options)
        => options.AddRail<Row>("wire", rail => rail
            .ParseLine(line => new Row(line))
            .Handle((_, _) => Task.CompletedTask));

    [Fact]
    public void The_default_composition_is_in_memory_and_the_engine_is_scoped()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddLogging();
        builder.AddGoldpathFileExchange(DeclareRail);

        var engineLifetime = builder.Services.Single(d => d.ServiceType == typeof(GoldpathFileRailEngine)).Lifetime;
        Assert.Equal(ServiceLifetime.Scoped, engineLifetime);

        using var app = builder.Build();
        Assert.IsType<GoldpathInMemoryFileLedger>(app.Services.GetRequiredService<IGoldpathFileLedger>());
        Assert.True(app.Services.GetRequiredService<GoldpathFileExchangeOptions>().Rails.ContainsKey("wire"));

        using var scope = app.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<GoldpathFileRailEngine>());
    }

    public sealed class ComposedContext(DbContextOptions<ComposedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.AddGoldpathFileExchangeModel();
    }

    [Fact]
    public void Naming_a_context_swaps_the_ledger_for_the_database_one()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddLogging();
        builder.Services.AddDbContext<ComposedContext>(o => o.UseSqlite("DataSource=:memory:"));
        builder.AddGoldpathFileExchange<HostApplicationBuilder, ComposedContext>(DeclareRail);

        using var app = builder.Build();
        Assert.IsType<GoldpathEfFileLedger<ComposedContext>>(app.Services.GetRequiredService<IGoldpathFileLedger>());
    }
}
