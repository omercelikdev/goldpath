using Medallion.Threading;
using Xunit;

namespace Goldpath.Tests;

/// <summary>
/// Unit coverage of the metrics decorator: outcomes flow through unchanged on every
/// acquire path, contention surfaces as null (try) or TimeoutException (acquire).
/// </summary>
public sealed class MeteredLockProviderTests
{
    private sealed class FakeHandle : IDistributedSynchronizationHandle
    {
        public CancellationToken HandleLostToken => CancellationToken.None;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeLock(bool contended) : IDistributedLock
    {
        public string Name => "fake";

        public IDistributedSynchronizationHandle Acquire(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
            => contended ? throw new TimeoutException() : new FakeHandle();

        public ValueTask<IDistributedSynchronizationHandle> AcquireAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
            => contended ? throw new TimeoutException() : ValueTask.FromResult<IDistributedSynchronizationHandle>(new FakeHandle());

        public IDistributedSynchronizationHandle? TryAcquire(TimeSpan timeout = default, CancellationToken cancellationToken = default)
            => contended ? null : new FakeHandle();

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireAsync(TimeSpan timeout = default, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IDistributedSynchronizationHandle?>(contended ? null : new FakeHandle());
    }

    private sealed class FakeProvider(bool contended) : IDistributedLockProvider
    {
        public IDistributedLock CreateLock(string name) => new FakeLock(contended);
    }

    [Fact]
    public async Task Uncontended_paths_return_handles_on_all_four_acquire_shapes()
    {
        var @lock = new GoldpathMeteredLockProvider(new FakeProvider(contended: false)).CreateLock("x");

        Assert.Equal("fake", @lock.Name);
        Assert.NotNull(@lock.Acquire());
        Assert.NotNull(await @lock.AcquireAsync());
        Assert.NotNull(@lock.TryAcquire());
        Assert.NotNull(await @lock.TryAcquireAsync());
    }

    [Fact]
    public async Task Contended_paths_keep_the_librarys_semantics()
    {
        var @lock = new GoldpathMeteredLockProvider(new FakeProvider(contended: true)).CreateLock("x");

        Assert.Null(@lock.TryAcquire());                                        // try → null
        Assert.Null(await @lock.TryAcquireAsync());
        Assert.Throws<TimeoutException>(() => @lock.Acquire());                 // acquire → throw
        await Assert.ThrowsAsync<TimeoutException>(async () => await @lock.AcquireAsync());
    }
}
