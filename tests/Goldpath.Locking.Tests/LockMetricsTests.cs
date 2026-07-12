using System.Diagnostics.Metrics;
using Medallion.Threading;
using Xunit;

namespace Goldpath.Tests;

/// <summary>The metrics ARE the ops contract — observe them, don't trust the wiring.</summary>
public sealed class LockMetricsTests
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
    public void Acquire_outcomes_and_wait_time_land_on_the_meters()
    {
        var acquires = new List<(long Value, string? Outcome)>();
        var waits = new List<double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Goldpath.Locking")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            if (instrument.Name == "goldpath_lock_acquire_total")
            {
                var outcome = tags.ToArray().FirstOrDefault(t => t.Key == "outcome").Value as string;
                lock (acquires)
                {
                    acquires.Add((value, outcome));
                }
            }
        });
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
        {
            if (instrument.Name == "goldpath_lock_wait_seconds")
            {
                lock (waits)
                {
                    waits.Add(value);
                }
            }
        });
        listener.Start();

        var winner = new GoldpathMeteredLockProvider(new FakeProvider(contended: false)).CreateLock("x");
        var loser = new GoldpathMeteredLockProvider(new FakeProvider(contended: true)).CreateLock("x");

        // Per-path deltas: EVERY acquire shape must record its outcome — a missing Record
        // in any one path is a mutation this test exists to kill.
        int CountOf(string outcome)
        {
            lock (acquires)
            {
                return acquires.Count(m => m.Outcome == outcome);
            }
        }

        void AssertDelta(string outcome, Action act)
        {
            var before = CountOf(outcome);
            act();
            Assert.True(CountOf(outcome) > before, $"no '{outcome}' measurement for this path");
        }

        AssertDelta("acquired", () => winner.TryAcquire());
        AssertDelta("acquired", () => winner.TryAcquireAsync().AsTask().GetAwaiter().GetResult());
        AssertDelta("acquired", () => winner.Acquire());
        AssertDelta("acquired", () => winner.AcquireAsync().AsTask().GetAwaiter().GetResult());
        AssertDelta("timeout", () => loser.TryAcquire());
        AssertDelta("timeout", () => loser.TryAcquireAsync().AsTask().GetAwaiter().GetResult());
        AssertDelta("timeout", () => Assert.Throws<TimeoutException>(() => loser.Acquire()));
        AssertDelta("timeout", () => Assert.ThrowsAsync<TimeoutException>(
            async () => await loser.AcquireAsync()).GetAwaiter().GetResult());

        // The meter is process-global; parallel test classes may still be emitting —
        // stop listening, then assert containment and a floor on snapshots, never exact counts.
        listener.Dispose();
        (long, string?)[] acquireSnapshot;
        double[] waitSnapshot;
        lock (acquires)
        {
            acquireSnapshot = acquires.ToArray();
        }

        lock (waits)
        {
            waitSnapshot = waits.ToArray();
        }

        Assert.Contains(acquireSnapshot, m => m is (1, "acquired"));
        Assert.Contains(acquireSnapshot, m => m is (1, "timeout"));
        Assert.True(waitSnapshot.Length >= 2);
        Assert.All(waitSnapshot, w => Assert.True(w >= 0));
    }
}
