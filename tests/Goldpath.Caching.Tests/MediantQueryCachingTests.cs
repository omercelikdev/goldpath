using Mediant.Abstractions;
using Mediant.Behaviors.Attributes;
using Mediant.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Goldpath.Tests;

public sealed class MediantQueryCachingTests
{
    public sealed class ExecutionCounter
    {
        private int _count;
        public int Count => _count;
        public void Bump() => Interlocked.Increment(ref _count);
    }

    [Cacheable(300, CacheKeyPrefix = "rates")]
    public sealed record GetRatesQuery : IQuery<string>;

    public sealed class GetRatesHandler(ExecutionCounter counter) : IQueryHandler<GetRatesQuery, string>
    {
        public ValueTask<string> Handle(GetRatesQuery query, CancellationToken cancellationToken)
        {
            counter.Bump();
            return ValueTask.FromResult("rates-v1");
        }
    }

    [InvalidatesCache("rates")]
    public sealed record UpdateRatesCommand : ICommand<string>;

    public sealed class UpdateRatesHandler : ICommandHandler<UpdateRatesCommand, string>
    {
        public ValueTask<string> Handle(UpdateRatesCommand command, CancellationToken cancellationToken)
            => ValueTask.FromResult("updated");
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<ExecutionCounter>();
        builder.Services.AddMediant(cfg => cfg.RegisterServicesFromAssembly(typeof(MediantQueryCachingTests).Assembly));
        builder.AddGoldpathCaching(o => o.Levels = ["l1"]);   // in-proc store: no broker/redis in unit tests
        return builder.Build();
    }

    [Fact]
    public async Task Cacheable_query_executes_once_and_serves_repeats_from_cache()
    {
        using var host = BuildHost();
        var sender = host.Services.GetRequiredService<ISender>();
        var counter = host.Services.GetRequiredService<ExecutionCounter>();

        Assert.Equal("rates-v1", await sender.Send(new GetRatesQuery(), CancellationToken.None));
        Assert.Equal("rates-v1", await sender.Send(new GetRatesQuery(), CancellationToken.None));
        Assert.Equal(1, counter.Count);                                   // second hit came from cache
    }

    /// <summary>
    /// The gap this test used to PIN (mediant#131) shipped in Mediant 1.2.0 — exactly as
    /// designed, the pinned no-op assertion broke and flipped to the real semantics:
    /// a command marked [InvalidatesCache] evicts, and the next query re-executes.
    /// </summary>
    [Fact]
    public async Task InvalidatesCache_really_invalidates_since_mediant_1_2()
    {
        using var host = BuildHost();
        var sender = host.Services.GetRequiredService<ISender>();
        var counter = host.Services.GetRequiredService<ExecutionCounter>();

        await sender.Send(new GetRatesQuery(), CancellationToken.None);
        await sender.Send(new GetRatesQuery(), CancellationToken.None);
        Assert.Equal(1, counter.Count);                                   // cached

        await sender.Send(new UpdateRatesCommand(), CancellationToken.None);
        await sender.Send(new GetRatesQuery(), CancellationToken.None);
        Assert.Equal(2, counter.Count);                                   // invalidated → re-executed
    }
}
