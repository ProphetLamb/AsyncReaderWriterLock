using System.Diagnostics;
using ARWL = System.Threading.AsyncReaderWriterLock;
namespace AsyncReaderWriterLock.Tests;

public sealed class FuzzingTests
{
    [Test]
    public async Task FuzzARWL()
    {
        ARWL rwLock = new();
        await FuzzExecute(rwLock, FuzzARWLStates.Create(new(0.6, 0.2, 0.2, 0.5)), Random.Shared);
    }

    private static async Task FuzzExecute<TLock>(TLock rwLock, IReadOnlyDictionary<Fuzz<TLock>, FuzzState<TLock>> fuzzStates, Random rng)
    {
        CancellationTokenSource cts = new();
        int count = 0;
        List<Task> pendingTasks = new(4096);
        foreach (var state in FuzzProvider(fuzzStates, rng, cts.Token))
        {
            var holdDelay = TimeSpan.FromMilliseconds(rng.NextDouble() * 10);
            try
            {
                var t = state(rwLock, holdDelay, Timeout.Infinite, default);
                if (!t.IsCompletedSuccessfully)
                {
                    pendingTasks.Add(t);
                }
            }
            catch (Exception ex)
            {
                Debug.Assert(ex is null);
                throw;
            }

            if (count >= 100000)
            {
                cts.Cancel(true);
            }

            count++;
        }
        await Task.WhenAll(pendingTasks.Where(t => !t.IsCompletedSuccessfully));
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

    private sealed record FuzzOptions(double ReadWeight, double WriteWeight, double ReadUpgradableWeight, double WriteUpgradedWeight);

    private static class FuzzARWLStates
    {
        internal static Dictionary<Fuzz<ARWL>, FuzzState<ARWL>> Create(FuzzOptions options)
        {
            List<Fuzz<ARWL>> rorwru = [FuzzRead, FuzzWrite, FuzzReadUpgradable];
            return new()
            {
                [FuzzNoop<ARWL>] = new(FuzzNoop<ARWL>, rorwru, 0.0),
                [FuzzRead] = new(FuzzRead, rorwru, options.ReadWeight),
                [FuzzReadUpgradable] = new(FuzzReadUpgradable, [.. rorwru, FuzzWriteUpgraded], options.ReadUpgradableWeight),
                [FuzzWrite] = new(FuzzWrite, rorwru, options.WriteWeight),
                [FuzzWriteUpgraded] = new(FuzzWriteUpgraded, [.. rorwru, FuzzWriteUpgraded], options.WriteUpgradedWeight),
            };
        }

        private static async Task<bool> FuzzRead(ARWL rwLock, TimeSpan holdDelay, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            using var l = await rwLock.UsingReadAsync(false, millisecondsTimeout, cancellationToken);
            await Task.Delay(holdDelay, cancellationToken);
            return l.IsAcquired;
        }
        private static async Task<bool> FuzzWrite(ARWL rwLock, TimeSpan holdDelay, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            using var l = await rwLock.UsingWriteAsync(false, millisecondsTimeout, cancellationToken);
            await Task.Delay(holdDelay, cancellationToken);
            return l.IsAcquired;
        }
        private static async Task<bool> FuzzReadUpgradable(ARWL rwLock, TimeSpan holdDelay, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            using var l = await rwLock.UsingReadUpgradableAsync(false, millisecondsTimeout, cancellationToken);
            await Task.Delay(holdDelay, cancellationToken);
            return l.IsAcquired;
        }
        private static async Task<bool> FuzzWriteUpgraded(ARWL rwLock, TimeSpan holdDelay, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            var isAcquired = await AsyncReaderWriterLockMarshal.EnterWriteUpgrade(rwLock, millisecondsTimeout, cancellationToken);
            try
            {
                await Task.Delay(holdDelay, cancellationToken);
                return isAcquired;
            }
            finally
            {
                AsyncReaderWriterLockMarshal.ExitWriteUpgrade(rwLock);
            }
        }
    }
}