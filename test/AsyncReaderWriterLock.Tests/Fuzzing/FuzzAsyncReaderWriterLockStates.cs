using System.Diagnostics;

namespace System.Threading.Tests.Fuzzing;

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
