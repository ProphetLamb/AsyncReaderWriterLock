using System.Diagnostics;
using System.Threading.Channels;

namespace System.Threading.Tests;

public delegate Task<bool> FuzzAsync<TLock>(TLock rwLock, TimeSpan holdDelay, int millisecondsTimeout, CancellationToken cancellationToken);

public delegate bool FuzzSync<TLock>(TLock rwLock, TimeSpan holdDelay, int millisecondsTimeout, CancellationToken cancellationToken);

public sealed record FuzzStateAsync<TLock>(FuzzAsync<TLock> Fuzz, List<FuzzAsync<TLock>> TransitionInto, double Probability);

public sealed record FuzzStateSync<TLock>(FuzzSync<TLock> Fuzz, List<FuzzSync<TLock>> TransitionInto, double Probability);

public sealed record FuzzOptions(double ReadWeight, double WriteWeight, double ReadUpgradableWeight, double WriteUpgradedWeight);


public class FuzzHelper
{
    public static async Task FuzzExecuteAsync<TLock>(TLock rwLock, IReadOnlyDictionary<FuzzAsync<TLock>, FuzzStateAsync<TLock>> fuzzStates, Random rng, int iterations)
    {
        CancellationTokenSource cts = new();
        int count = 0;
        List<Task<bool>> pendingTasks = new(4096);
        foreach (var state in FuzzAsyncProvider(fuzzStates, rng, cts.Token))
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
                Debug.Assert(ex is null);
                throw;
            }

            if (count >= iterations)
            {
                break;
            }

            count++;
        }

        try
        {
            var results = await Task.WhenAll(pendingTasks.Where(t => !t.IsCompletedSuccessfully));
            cts.Cancel(false);
            Debug.Assert(results.All(isAcquired => isAcquired));
        }
        catch (OperationCanceledException)
        {
            // expected cancellation
        }
    }

    public static async Task FuzzExecuteSync<TLock>(TLock rwLock, IReadOnlyDictionary<FuzzSync<TLock>, FuzzStateSync<TLock>> fuzzStates, Random rng, int iterations)
    {
        var channel = Channel.CreateUnbounded<FuzzSync<TLock>>(new() { SingleWriter = true });
        CancellationTokenSource cts = new();

        List<Thread> ts = [];
        for (int i = 0; i < Environment.ProcessorCount; i++)
        {
            ts.Add(new(Write));
        }
        var read = Read();

        await Task.WhenAll(ts.Select(t => Task.Run(() => t.Join())));
        await read;

        async Task Read()
        {
            var count = 0;
            var writer = channel.Writer;
            foreach (var state in FuzzSyncProvider(fuzzStates, rng, cts.Token))
            {
                await writer.WriteAsync(state);

                if (count >= iterations)
                {
                    break;
                }

                count++;
            }
            cts.Cancel(false);
            writer.Complete();
        }

        void Write()
        {
            var cancellationToken = cts.Token;
            var reader = channel.Reader;
            SpinWait wait = new();
            while (!cancellationToken.IsCancellationRequested)
            {
                FuzzSync<TLock> state;
                while (!reader.TryRead(out state!))
                {
                    if (wait.NextSpinWillYield && reader.Completion.IsCompleted)
                    {
                        return;
                    }
                    wait.SpinOnce();
                }
                var holdDelay = TimeSpan.FromMicroseconds(rng.NextDouble() * 50);
                state(rwLock, holdDelay, 10000, default);
            }
        }
    }


    public static IEnumerable<FuzzAsync<TLock>> FuzzAsyncProvider<TLock>(IReadOnlyDictionary<FuzzAsync<TLock>, FuzzStateAsync<TLock>> fuzzStates, Random rng, CancellationToken cancellationToken)
    {
        FuzzAsync<TLock>? current = FuzzNoopAsync;
        while (!cancellationToken.IsCancellationRequested)
        {
            current = PickNext(current, fuzzStates, rng);
            yield return current;

            cancellationToken.ThrowIfCancellationRequested();
        }

        static FuzzAsync<TLock> PickNext(FuzzAsync<TLock> current, IReadOnlyDictionary<FuzzAsync<TLock>, FuzzStateAsync<TLock>> states, Random rng)
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

    public static Task<bool> FuzzNoopAsync<TLock>(TLock rwLock, TimeSpan holdDelay, int millisecondsTimeout, CancellationToken cancellationToken) => Task.FromResult(false);

    public static bool FuzzNoopSync<TLock>(TLock rwLock, TimeSpan holdDelay, int millisecondsTimeout, CancellationToken cancellationToken) => false;

    private static IEnumerable<FuzzSync<TLock>> FuzzSyncProvider<TLock>(IReadOnlyDictionary<FuzzSync<TLock>, FuzzStateSync<TLock>> fuzzStates, Random rng, CancellationToken cancellationToken)
    {
        FuzzSync<TLock>? current = FuzzNoopSync;
        while (!cancellationToken.IsCancellationRequested)
        {
            current = PickNext(current, fuzzStates, rng);
            yield return current;

            cancellationToken.ThrowIfCancellationRequested();
        }

        static FuzzSync<TLock> PickNext(FuzzSync<TLock> current, IReadOnlyDictionary<FuzzSync<TLock>, FuzzStateSync<TLock>> states, Random rng)
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
}

public static class FuzzAsyncReaderWriterLockStates
{
    internal static Dictionary<FuzzAsync<AsyncReaderWriterLock>, FuzzStateAsync<AsyncReaderWriterLock>> CreateAsync(FuzzOptions options)
    {
        List<FuzzAsync<AsyncReaderWriterLock>> states = [FuzzReadAsync, FuzzWriteAsync, FuzzReadUpgradableAsync, FuzzWriteUpgradedAsync];
        return new()
        {
            [FuzzHelper.FuzzNoopAsync<AsyncReaderWriterLock>] = new(FuzzHelper.FuzzNoopAsync<AsyncReaderWriterLock>, states, 0.0),
            [FuzzReadAsync] = new(FuzzReadAsync, states, options.ReadWeight),
            [FuzzReadUpgradableAsync] = new(FuzzReadUpgradableAsync, states, options.ReadUpgradableWeight),
            [FuzzWriteAsync] = new(FuzzWriteAsync, states, options.WriteWeight),
            [FuzzWriteUpgradedAsync] = new(FuzzWriteUpgradedAsync, states, options.WriteUpgradedWeight),
        };
    }
    internal static Dictionary<FuzzSync<AsyncReaderWriterLock>, FuzzStateSync<AsyncReaderWriterLock>> CreateSync(FuzzOptions options)
    {
        List<FuzzSync<AsyncReaderWriterLock>> states = [FuzzReadSync, FuzzWriteSync, FuzzReadUpgradableSync, FuzzWriteUpgradedSync];
        return new()
        {
            [FuzzHelper.FuzzNoopSync<AsyncReaderWriterLock>] = new(FuzzHelper.FuzzNoopSync<AsyncReaderWriterLock>, states, 0.0),
            [FuzzReadSync] = new(FuzzReadSync, states, options.ReadWeight),
            [FuzzReadUpgradableSync] = new(FuzzReadUpgradableSync, states, options.ReadUpgradableWeight),
            [FuzzWriteSync] = new(FuzzWriteSync, states, options.WriteWeight),
            [FuzzWriteUpgradedSync] = new(FuzzWriteUpgradedSync, states, options.WriteUpgradedWeight),
        };
    }

    private static async Task<bool> FuzzReadAsync(AsyncReaderWriterLock rwLock, TimeSpan holdDelay, int millisecondsTimeout, CancellationToken cancellationToken)
    {
        using var l = await rwLock.UsingReadAsync(false, millisecondsTimeout, cancellationToken);
        Debug.Assert(l.IsAcquired);
        await Task.Delay(holdDelay, cancellationToken);
        return l.IsAcquired;
    }

    private static bool FuzzReadSync(AsyncReaderWriterLock rwLock, TimeSpan holdDelay, int millisecondsTimeout, CancellationToken cancellationToken)
    {
        using var l = rwLock.UsingRead(false, millisecondsTimeout, cancellationToken);
        Debug.Assert(l.IsAcquired);
        Thread.Sleep(holdDelay);
        return l.IsAcquired;
    }

    private static async Task<bool> FuzzWriteAsync(AsyncReaderWriterLock rwLock, TimeSpan holdDelay, int millisecondsTimeout, CancellationToken cancellationToken)
    {
        using var l = await rwLock.UsingWriteAsync(false, millisecondsTimeout, cancellationToken);
        Debug.Assert(l.IsAcquired);
        await Task.Delay(holdDelay, cancellationToken);
        return l.IsAcquired;
    }

    private static bool FuzzWriteSync(AsyncReaderWriterLock rwLock, TimeSpan holdDelay, int millisecondsTimeout, CancellationToken cancellationToken)
    {
        using var l = rwLock.UsingWrite(false, millisecondsTimeout, cancellationToken);
        Debug.Assert(l.IsAcquired);
        Thread.Sleep(holdDelay);
        return l.IsAcquired;
    }

    private static async Task<bool> FuzzReadUpgradableAsync(AsyncReaderWriterLock rwLock, TimeSpan holdDelay, int millisecondsTimeout, CancellationToken cancellationToken)
    {
        using var l = await rwLock.UsingReadUpgradableAsync(false, millisecondsTimeout, cancellationToken);
        Debug.Assert(l.IsAcquired);
        await Task.Delay(holdDelay, cancellationToken);
        return l.IsAcquired;
    }

    private static bool FuzzReadUpgradableSync(AsyncReaderWriterLock rwLock, TimeSpan holdDelay, int millisecondsTimeout, CancellationToken cancellationToken)
    {
        using var l = rwLock.UsingReadUpgradable(false, millisecondsTimeout, cancellationToken);
        Debug.Assert(l.IsAcquired);
        Thread.Sleep(holdDelay);
        return l.IsAcquired;
    }

    private static async Task<bool> FuzzWriteUpgradedAsync(AsyncReaderWriterLock rwLock, TimeSpan holdDelay, int millisecondsTimeout, CancellationToken cancellationToken)
    {
        using var l = await rwLock.UsingReadUpgradableAsync(false, millisecondsTimeout, cancellationToken);
        Debug.Assert(l.IsAcquired);
        using var l2 = await l.UsingWriteUpgradeAsync(false, millisecondsTimeout, cancellationToken);
        Debug.Assert(l2.IsAcquired);
        await Task.Delay(holdDelay, cancellationToken);
        return l2.IsAcquired;
    }

    private static bool FuzzWriteUpgradedSync(AsyncReaderWriterLock rwLock, TimeSpan holdDelay, int millisecondsTimeout, CancellationToken cancellationToken)
    {
        using var l = rwLock.UsingReadUpgradable(false, millisecondsTimeout, cancellationToken);
        Debug.Assert(l.IsAcquired);
        using var l2 = l.UsingWriteUpgrade(false, millisecondsTimeout, cancellationToken);
        Debug.Assert(l2.IsAcquired);
        Thread.Sleep(holdDelay);
        return l2.IsAcquired;
    }
}
