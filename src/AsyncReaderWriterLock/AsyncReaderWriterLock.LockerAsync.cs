using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System.Threading;

public partial class AsyncReaderWriterLock
{
    /// <summary>
    /// The lock enters a read only state.
    /// </summary>
    /// <param name="rwLock">The lock</param>
    /// <param name="throwIfTimeoutExpired">
    /// Throws a <see cref="TimeoutException"/> if the timeout expired before the lock is acquired;
    /// <br/>
    /// otherwise, returns a locker with <see cref="ILocker.IsAcquired"/> set to <c>false</c>.
    /// </param>
    /// <param name="timeoutMilliseconds">The time in milliseconds to wait to acquire the lock. Only used when <see cref="AsyncReaderWriterLockOptions.VacuumQueueInterval"/> is not <c>null</c>.</param>
    /// <param name="cancellationToken">The token cancelling acquiring the lock.</param>
    /// <returns>A disposable locker releasing the lock.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ValueTask<ILocker> UsingReadAsync(bool throwIfTimeoutExpired, int timeoutMilliseconds, CancellationToken cancellationToken = default)
    {
        return CreateLockerAsync(this, &AsyncReaderWriterLockMarshal.EnterRead, &AsyncReaderWriterLockMarshal.ExitRead, throwIfTimeoutExpired, timeoutMilliseconds, cancellationToken).Cast<LockerAsync, ILocker>();
    }
    /// <inheritdoc cref="UsingReadAsync(bool, int, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ILocker> UsingReadAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        return UsingReadAsync(true, (int)timeout.TotalMilliseconds, cancellationToken);
    }
    /// <inheritdoc cref="UsingReadAsync(bool, int, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ILocker> UsingReadAsync(CancellationToken cancellationToken = default)
    {
        return UsingReadAsync(true, Timeout.Infinite, cancellationToken);
    }

    /// <summary>
    /// The lock enters a read only state.
    /// <br/>
    /// Allows upgrading to write using <see cref="ILockerUpgrade.UsingWriteUpgradeAsync"/> when only this read lock remains.
    /// <br/>
    /// Only one upgradable read lock can be held at a time.
    /// </summary>
    /// <param name="rwLock">The lock</param>
    /// <param name="throwIfTimeoutExpired">
    /// Throws a <see cref="TimeoutException"/> if the timeout expired before the lock is acquired;
    /// <br/>
    /// otherwise, returns a locker with <see cref="ILocker.IsAcquired"/> set to <c>false</c>.
    /// </param>
    /// <param name="timeoutMilliseconds">The time in milliseconds to wait to acquire the lock. Only used when <see cref="AsyncReaderWriterLockOptions.VacuumQueueInterval"/> is not <c>null</c>.</param>
    /// <param name="cancellationToken">The token cancelling acquiring the lock.</param>
    /// <returns>A disposable locker releasing the lock.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ValueTask<ILockerUpgrade> UsingReadUpgradableAsync(bool throwIfTimeoutExpired, int timeoutMilliseconds, CancellationToken cancellationToken = default)
    {
        return CreateLockerAsync(this, &AsyncReaderWriterLockMarshal.EnterReadUpgrade, &AsyncReaderWriterLockMarshal.ExitReadUpgrade, throwIfTimeoutExpired, timeoutMilliseconds, cancellationToken).Cast<LockerAsync, ILockerUpgrade>();
    }
    /// <inheritdoc cref="UsingReadUpgradableAsync(bool, int, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ILockerUpgrade> UsingReadUpgradableAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        return UsingReadUpgradableAsync(true, (int)timeout.TotalMilliseconds, cancellationToken);
    }
    /// <inheritdoc cref="UsingReadUpgradableAsync(bool, int, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ILockerUpgrade> UsingReadUpgradableAsync(CancellationToken cancellationToken = default)
    {
        return UsingReadUpgradableAsync(true, Timeout.Infinite, cancellationToken);
    }

    /// <summary>
    /// The lock enters a write state for only this locker.
    /// <br/>
    /// Only one write lock can be held at a time.
    /// </summary>
    /// <param name="rwLock">The lock</param>
    /// <param name="throwIfTimeoutExpired">
    /// Throws a <see cref="TimeoutException"/> if the timeout expired before the lock is acquired;
    /// <br/>
    /// otherwise, returns a locker with <see cref="ILocker.IsAcquired"/> set to <c>false</c>.
    /// </param>
    /// <param name="timeoutMilliseconds">The time in milliseconds to wait to acquire the lock. Only used when <see cref="AsyncReaderWriterLockOptions.VacuumQueueInterval"/> is not <c>null</c>.</param>
    /// <param name="cancellationToken">The token cancelling acquiring the lock.</param>
    /// <returns>A disposable locker releasing the lock.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ValueTask<ILocker> UsingWriteAsync(bool throwIfTimeoutExpired, int timeoutMilliseconds, CancellationToken cancellationToken = default)
    {
        return CreateLockerAsync(this, &AsyncReaderWriterLockMarshal.EnterWrite, &AsyncReaderWriterLockMarshal.ExitWrite, throwIfTimeoutExpired, timeoutMilliseconds, cancellationToken).Cast<LockerAsync, ILocker>();
    }
    /// <inheritdoc cref="UsingWriteAsync(bool, int, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ILocker> UsingWriteAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        return UsingWriteAsync(true, (int)timeout.TotalMilliseconds, cancellationToken);
    }
    /// <inheritdoc cref="UsingWriteAsync(bool, int, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ILocker> UsingWriteAsync(CancellationToken cancellationToken = default)
    {
        return UsingWriteAsync(true, Timeout.Infinite, cancellationToken);
    }

    private static unsafe ValueTask<LockerAsync> CreateLockerAsync(AsyncReaderWriterLock rwLock, delegate*<AsyncReaderWriterLock, int, CancellationToken, ValueTask<bool>> enterLock, delegate*<AsyncReaderWriterLock, void> exitLock, bool throwIfTimeoutExpired, int timeoutMilliseconds, CancellationToken cancellationToken = default)
    {
        var task = enterLock(rwLock, timeoutMilliseconds, cancellationToken);
        var exposed = task.GetExposed();
        if (exposed.Obj is null && exposed.Result)
        {
            return new(LockerAsync.Pool.Rent(rwLock, exitLock, throwIfTimeoutExpired));
        }

        if (exposed.Obj is RequestNode node)
        {
            var resultVts = TaskHelper.ValueTaskSourceContinuationAction<LockerAsync>.Pool.Rent(node, static (vts, token, state) =>
            {
                var locker = (LockerAsync)state!;
                var node = (RequestNode)vts!;
                if (node.GetResult(token))
                {
                    return locker;
                }

                if (locker.ThrowIfTimeoutExpired)
                {
                    locker.Dispose();
                    ThrowHelper.ThrowTimeout("The timeout expired before the lock was acquired", null);
                }

                locker.Clear();
                return locker;
            }, LockerAsync.Pool.Rent(rwLock, exitLock, throwIfTimeoutExpired));

            return new(resultVts, exposed.Token);
        }

        if (exposed.Obj is Task t)
        {
            t.GetAwaiter().GetResult();
            Debug.Fail("expected any ValueTask backed by a Task returned from AsyncReaderWriterLock to throw when awaited");
        }

        if (throwIfTimeoutExpired)
        {
            ThrowHelper.ThrowTimeout("The timeout expired before the lock was acquired", null);
        }

        return default;
    }

    /// <summary>
    /// Logical handle for an acquired lock state. Exit the state by disposing.
    /// </summary>
    /// <remarks>
    /// For use in the `using` control flow.
    /// <br/>
    /// <code>
    /// using var lr1 = await rwLock.UsingReadAsync();
    /// // do something under read lock
    /// </code>
    /// </remarks>
    public interface ILocker : IDisposable
    {
        /// <summary>
        /// Indicates whether the lock has been acquired, or not.
        /// </summary>
        bool IsAcquired { get; }
    }

    /// <inheritdoc cref="ILocker"/>
    public interface ILockerUpgrade : ILocker
    {
        /// <summary>
        /// Upgrade the lock from upgradable read stat upgraded write.
        /// <br/>
        /// Only one upgraded write lock can be held at a time.
        /// </summary>
        /// <param name="rwLock">The lock</param>
        /// <param name="throwIfTimeoutExpired">
        /// Throws a <see cref="TimeoutException"/> if the timeout expired before the lock is acquired;
        /// <br/>
        /// otherwise, returns a locker with <see cref="ILocker.IsAcquired"/> set to <c>false</c>.
        /// </param>
        /// <param name="timeoutMilliseconds">The time in milliseconds to wait to acquire the lock. Only used when <see cref="AsyncReaderWriterLockOptions.VacuumQueueInterval"/> is not <c>null</c>.</param>
        /// <param name="cancellationToken">The token cancelling acquiring the lock.</param>
        /// <returns>A disposable locker releasing the lock.</returns>
        ValueTask<ILocker> UsingWriteUpgradeAsync(bool throwIfTimeoutExpired, int timeout, CancellationToken cancellationToken = default);

        /// <inheritdoc cref="UsingWriteUpgradeAsync(bool, int, CancellationToken)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask<ILocker> UsingWriteUpgradeAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
        /// <inheritdoc cref="UsingWriteUpgradeAsync(bool, int, CancellationToken)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask<ILocker> UsingWriteUpgradeAsync(CancellationToken cancellationToken = default);
    }

    private unsafe sealed class LockerAsync : ILockerUpgrade, QueueHelper.IQueueNode<LockerAsync>
    {
        private AsyncReaderWriterLock? _rwLock;
        private delegate*<AsyncReaderWriterLock, void> _exitLock;
        private bool _throwIfTimeoutExpired;

        public bool IsAcquired => _rwLock is not null;

        public bool ThrowIfTimeoutExpired => _throwIfTimeoutExpired;

        public LockerAsync? QueueNext;

        public ref LockerAsync? GetNext()
        {
            return ref QueueNext;
        }

        private void PoolInitialize(AsyncReaderWriterLock? rwLock, delegate*<AsyncReaderWriterLock, void> exitLock, bool throwIfTimeoutExpired)
        {
            Debug.Assert(_rwLock is null);
            _rwLock = rwLock;
            _exitLock = exitLock;
            _throwIfTimeoutExpired = throwIfTimeoutExpired;
        }

        private void PoolDeinitialize()
        {
            _rwLock = null;
            _exitLock = default;
            _throwIfTimeoutExpired = false;
        }

        public void Clear() => PoolDeinitialize();

        public void Dispose()
        {
            if (_rwLock is not null)
            {
                _exitLock(_rwLock);
                Pool.Return(this);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask<ILocker> UsingWriteUpgradeAsync(bool throwIfTimeoutExpired, int timeout, CancellationToken cancellationToken = default)
        {
            if (_rwLock is null && throwIfTimeoutExpired)
            {
                ThrowHelper.ThrowTimeout("The upgradable read lock could not be acquired", null);
            }

            if (_rwLock is null)
            {
                return new(Pool.Rent(null, default, throwIfTimeoutExpired));
            }

            return CreateLockerAsync(_rwLock, &AsyncReaderWriterLockMarshal.EnterWriteUpgrade, &AsyncReaderWriterLockMarshal.ExitWriteUpgrade, throwIfTimeoutExpired, timeout, cancellationToken).Cast<LockerAsync, ILocker>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask<ILocker> UsingWriteUpgradeAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return UsingWriteUpgradeAsync(true, (int)timeout.TotalMilliseconds, cancellationToken);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask<ILocker> UsingWriteUpgradeAsync(CancellationToken cancellationToken = default)
        {
            return UsingWriteUpgradeAsync(true, Timeout.Infinite, cancellationToken);
        }

        internal static class Pool
        {
            [ThreadStatic] private static LockerAsync? t_localValue;
            private static LockerAsync? s_head;
            private static LockerAsync? s_tail;

            internal static LockerAsync Rent(AsyncReaderWriterLock? rwLock, delegate*<AsyncReaderWriterLock, void> exitLock, bool throwIfTimeoutExpired)
            {
                if (t_localValue is { } value)
                {
                    t_localValue = null;
                    value.PoolInitialize(rwLock, exitLock, throwIfTimeoutExpired);
                    return value;
                }

                if (!QueueHelper.TryDequeueConcurrent(ref s_head, out value) || value is null)
                {
                    value = new();
                }

                value.PoolInitialize(rwLock, exitLock, throwIfTimeoutExpired);
                return value;
            }

            internal static void Return(LockerAsync value)
            {
                var result = false;
                value.PoolDeinitialize();
                if (QueueHelper.TryEnqueueConcurrent(ref s_tail, value))
                {
                    result = true;
                }
                if (t_localValue is not null)
                {
                    // attempt to enqueue the local tail to the queue
                    QueueHelper.TryEnqueueConcurrent(ref s_tail, t_localValue);
                }
                else if (!result)
                {
                    t_localValue = value;
                    result = true;
                }

                if (!result)
                {
                    // retry enqueuing
                    result = QueueHelper.TryEnqueueConcurrent(ref s_tail, value);
                }

                // if result is false, let the GC destroy the value
            }
        }

    }
}
