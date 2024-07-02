using System.Diagnostics;
using NUnit.Framework.Internal;
namespace System.Threading.Tests;

public sealed class FuzzingTests
{
    private static IEnumerable<FuzzOptions> GetFuzzOptions()
    {
        double[] readWeights = [0.80, 0.79, 0.70, 0.50, 0.25, 0.00];
        double[] writeWeights = [0.00, 0.01, 0.10, 0.25];
        double[] readUpgradeableWeights = [0.00, 0.10, 0.25];
        double[] writeUpgradedWeights = [0.00, 0.10, 0.25];
        return readWeights
            .CombineIntersection(writeWeights)
            .CombineIntersection(readUpgradeableWeights)
            .CombineIntersection(writeUpgradedWeights)
            .Select(t => new FuzzOptions(t.Inner.Inner.Inner, t.Inner.Inner.Outer, t.Inner.Outer, t.Outer))
            .Where(o => o.ReadWeight + o.WriteWeight + o.ReadUpgradableWeight + o.WriteUpgradedWeight > double.Epsilon); // remove cases with no read or write weight
    }

    private static IEnumerable<TestCaseData> GetParameters()
    {
        return TestHelper.GetOptionCombinations()
            .CombineIntersection(GetFuzzOptions())
            .Select(t => new TestCaseData(t.Inner, t.Outer));
    }

    [Test, TestCaseSource(nameof(GetParameters))]
    public async Task FuzzAsyncReaderWriterLock(AsyncReaderWriterLockOptions lockOptions, FuzzOptions fuzzOptions)
    {
        AsyncReaderWriterLock rwLock = new(lockOptions);
        await FuzzExecute(rwLock, FuzzAsyncReaderWriterLockStates.Create(fuzzOptions), new Random(unchecked((int)0xbeefcace)));
    }

    private static async Task FuzzExecute<TLock>(TLock rwLock, IReadOnlyDictionary<Fuzz<TLock>, FuzzState<TLock>> fuzzStates, Random rng)
    {
        CancellationTokenSource cts = new();
        int count = 0;
        List<Task<bool>> pendingTasks = new(4096);
        foreach (var state in FuzzProvider(fuzzStates, rng, cts.Token))
        {
            var holdDelay = TimeSpan.FromMicroseconds(rng.NextDouble() * 50);
            try
            {
                var t = state(rwLock, holdDelay, 10000, default);
                if (!t.IsCompletedSuccessfully)
                {
                    pendingTasks.Add(t);
                }
            }
            catch (Exception ex)
            {
                Assert.That(ex is null);
                throw;
            }

            if (count >= 100000)
            {
                break;
            }

            count++;
        }

        try
        {
            var results = await Task.WhenAll(pendingTasks.Where(t => !t.IsCompletedSuccessfully));
            cts.Cancel(false);
            Assert.That(results.All(isAcquired => isAcquired));
        }
        catch (OperationCanceledException)
        {
            // expected cancellation
        }
    }

    private static IEnumerable<Fuzz<TLock>> FuzzProvider<TLock>(IReadOnlyDictionary<Fuzz<TLock>, FuzzState<TLock>> fuzzStates, Random rng, CancellationToken cancellationToken)
    {
        Fuzz<TLock>? current = FuzzNoop;
        while (!cancellationToken.IsCancellationRequested)
        {
            current = PickNext(current, fuzzStates, rng);
            yield return current;

            cancellationToken.ThrowIfCancellationRequested();
        }

        static Fuzz<TLock> PickNext(Fuzz<TLock> current, IReadOnlyDictionary<Fuzz<TLock>, FuzzState<TLock>> states, Random rng)
        {
            var state = states[current];
            var rv = rng.NextDouble();
            var ptot = 0.0;
            foreach (var next in state.TransitionInto)
            {
                ptot += states[next].Probability;
            }
            var pick = rv * ptot;
            foreach (var next in state.TransitionInto)
            {
                pick -= states[next].Probability;
                if (pick <= double.Epsilon)
                {
                    return next;
                }
            }
            return state.TransitionInto.Last();
        }
    }


    public delegate Task<bool> Fuzz<TLock>(TLock rwLock, TimeSpan holdDelay, int millisecondsTimeout, CancellationToken cancellationToken);

    private static Task<bool> FuzzNoop<TLock>(TLock rwLock, TimeSpan holdDelay, int millisecondsTimeout, CancellationToken cancellationToken) => Task.FromResult(false);


    private sealed record FuzzState<TLock>(Fuzz<TLock> Fuzz, List<Fuzz<TLock>> TransitionInto, double Probability);

    public sealed record FuzzOptions(double ReadWeight, double WriteWeight, double ReadUpgradableWeight, double WriteUpgradedWeight);

    private static class FuzzAsyncReaderWriterLockStates
    {
        internal static Dictionary<Fuzz<AsyncReaderWriterLock>, FuzzState<AsyncReaderWriterLock>> Create(FuzzOptions options)
        {
            List<Fuzz<AsyncReaderWriterLock>> states = [FuzzRead, FuzzWrite, FuzzReadUpgradable, FuzzWriteUpgraded];
            return new()
            {
                [FuzzNoop<AsyncReaderWriterLock>] = new(FuzzNoop<AsyncReaderWriterLock>, states, 0.0),
                [FuzzRead] = new(FuzzRead, states, options.ReadWeight),
                [FuzzReadUpgradable] = new(FuzzReadUpgradable, states, options.ReadUpgradableWeight),
                [FuzzWrite] = new(FuzzWrite, states, options.WriteWeight),
                [FuzzWriteUpgraded] = new(FuzzWriteUpgraded, states, options.WriteUpgradedWeight),
            };
        }

        private static async Task<bool> FuzzRead(AsyncReaderWriterLock rwLock, TimeSpan holdDelay, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            using var l = await rwLock.UsingReadAsync(false, millisecondsTimeout, cancellationToken);
            Assert.That(l.IsAcquired);
            await Task.Delay(holdDelay, cancellationToken);
            return l.IsAcquired;
        }
        private static async Task<bool> FuzzWrite(AsyncReaderWriterLock rwLock, TimeSpan holdDelay, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            using var l = await rwLock.UsingWriteAsync(false, millisecondsTimeout, cancellationToken);
            Assert.That(l.IsAcquired);
            await Task.Delay(holdDelay, cancellationToken);
            return l.IsAcquired;
        }
        private static async Task<bool> FuzzReadUpgradable(AsyncReaderWriterLock rwLock, TimeSpan holdDelay, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            using var l = await rwLock.UsingReadUpgradableAsync(false, millisecondsTimeout, cancellationToken);
            Assert.That(l.IsAcquired);
            await Task.Delay(holdDelay, cancellationToken);
            return l.IsAcquired;
        }
        private static async Task<bool> FuzzWriteUpgraded(AsyncReaderWriterLock rwLock, TimeSpan holdDelay, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            using var l = await rwLock.UsingReadUpgradableAsync(false, millisecondsTimeout, cancellationToken);
            Assert.That(l.IsAcquired);
            using var l2 = await l.UsingWriteUpgradeAsync(false, millisecondsTimeout, cancellationToken);
            Assert.That(l2.IsAcquired);
            await Task.Delay(holdDelay, cancellationToken);
            return l2.IsAcquired;
        }
    }
}