using System.Runtime.CompilerServices;

namespace System.Threading;

/// <summary>
/// Exposes methods for interacting with <see cref="AsyncReaderWriterLock"/>.
/// </summary>
public static class AsyncReaderWriterLockMarshal
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryEnterRead(AsyncReaderWriterLock rwLock)
    {
        SpinWait wait = new();
        return rwLock.TryEnterRead(ref wait, false);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<bool> EnterRead(AsyncReaderWriterLock rwLock, int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        return rwLock.EnterRead(timeoutMilliseconds, cancellationToken);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ExitRead(AsyncReaderWriterLock rwLock)
    {
        SpinWait wait = new();
        rwLock.ExitRead(ref wait, 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryEnterReadUpgrade(AsyncReaderWriterLock rwLock)
    {
        SpinWait wait = new();
        return rwLock.TryEnterReadUpgrade(ref wait, false);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<bool> EnterReadUpgrade(AsyncReaderWriterLock rwLock, int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        return rwLock.EnterReadUpgrade(timeoutMilliseconds, cancellationToken);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ExitReadUpgrade(AsyncReaderWriterLock rwLock)
    {
        SpinWait wait = new();
        rwLock.ExitReadUpgrade(ref wait);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryEnterWrite(AsyncReaderWriterLock rwLock)
    {
        SpinWait wait = new();
        return rwLock.TryEnterWrite(ref wait, false);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<bool> EnterWrite(AsyncReaderWriterLock rwLock, int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        return rwLock.EnterWrite(timeoutMilliseconds, cancellationToken);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ExitWrite(AsyncReaderWriterLock rwLock)
    {
        SpinWait wait = new();
        rwLock.ExitWrite(ref wait);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryEnterWriteUpgrade(AsyncReaderWriterLock rwLock)
    {
        SpinWait wait = new();
        return rwLock.TryEnterWriteUpgrade(ref wait, false);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<bool> EnterWriteUpgrade(AsyncReaderWriterLock rwLock, int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        return rwLock.EnterWriteUpgrade(timeoutMilliseconds, cancellationToken);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ExitWriteUpgrade(AsyncReaderWriterLock rwLock)
    {
        SpinWait wait = new();
        rwLock.ExitWriteUpgrade(ref wait);
    }
}
