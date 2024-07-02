using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System.Threading;

public partial class AsyncReaderWriterLock
{
    /// <inheritdoc cref="UsingReadAsync(bool, int, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe Locker UsingRead(bool throwIfTimeoutExpired, int timeoutMilliseconds, CancellationToken cancellationToken = default)
    {
        return CreateLocker(this, &AsyncReaderWriterLockMarshal.EnterRead, &AsyncReaderWriterLockMarshal.ExitRead, throwIfTimeoutExpired, timeoutMilliseconds, cancellationToken);
    }
    /// <inheritdoc cref="UsingReadAsync(bool, int, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Locker UsingRead(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        return UsingRead(true, (int)timeout.TotalMilliseconds, cancellationToken);
    }
    /// <inheritdoc cref="UsingReadAsync(bool, int, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Locker UsingRead(CancellationToken cancellationToken = default)
    {
        return UsingRead(true, Timeout.Infinite, cancellationToken);
    }

    /// <inheritdoc cref="UsingReadUpgradableAsync(bool, int, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe LockerUpgrade UsingReadUpgradable(bool throwIfTimeoutExpired, int timeoutMilliseconds, CancellationToken cancellationToken = default)
    {
        return CreateLocker(this, &AsyncReaderWriterLockMarshal.EnterReadUpgrade, &AsyncReaderWriterLockMarshal.ExitReadUpgrade, throwIfTimeoutExpired, timeoutMilliseconds, cancellationToken).AsUpgrade();
    }
    /// <inheritdoc cref="UsingReadUpgradableAsync(bool, int, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LockerUpgrade UsingReadUpgradable(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        return UsingReadUpgradable(true, (int)timeout.TotalMilliseconds, cancellationToken);
    }
    /// <inheritdoc cref="UsingReadUpgradableAsync(bool, int, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LockerUpgrade UsingReadUpgradable(CancellationToken cancellationToken = default)
    {
        return UsingReadUpgradable(true, Timeout.Infinite, cancellationToken);
    }

    /// <inheritdoc cref="UsingWriteAsync(bool, int, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe Locker UsingWrite(bool throwIfTimeoutExpired, int timeoutMilliseconds, CancellationToken cancellationToken = default)
    {
        return CreateLocker(this, &AsyncReaderWriterLockMarshal.EnterWrite, &AsyncReaderWriterLockMarshal.ExitWrite, throwIfTimeoutExpired, timeoutMilliseconds, cancellationToken);
    }
    /// <inheritdoc cref="UsingWriteAsync(bool, int, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Locker UsingWrite(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        return UsingWrite(true, (int)timeout.TotalMilliseconds, cancellationToken);
    }
    /// <inheritdoc cref="UsingWriteAsync(bool, int, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Locker UsingWrite(CancellationToken cancellationToken = default)
    {
        return UsingWrite(true, Timeout.Infinite, cancellationToken);
    }

    private static unsafe Locker CreateLocker(AsyncReaderWriterLock rwLock, delegate*<AsyncReaderWriterLock, int, CancellationToken, ValueTask<bool>> enterLock, delegate*<AsyncReaderWriterLock, void> exitLock, bool throwIfTimeoutExpired, int timeoutMilliseconds, CancellationToken cancellationToken = default)
    {
        var task = enterLock(rwLock, timeoutMilliseconds, cancellationToken);
        var exposed = task.GetExposed();
        if (exposed.Obj is null && exposed.Result)
        {
            return new(rwLock, exitLock);
        }

        if (exposed.Obj is RequestNode node)
        {
            var awaiter = TaskHelper.ValueTaskSourceAwaiter.Pool.Rent(node, exposed.Token);
            if (awaiter.Wait(timeoutMilliseconds, cancellationToken))
            {
                TaskHelper.ValueTaskSourceAwaiter.Pool.Return(awaiter);
                return new(rwLock, exitLock);
            }
            TaskHelper.ValueTaskSourceAwaiter.Pool.Return(awaiter);
        }

        if (exposed.Obj is Task t)
        {
            t.GetAwaiter().GetResult();
            Debug.Fail("expected any ValueTask backed by a Task to throw when awaited");
        }

        if (throwIfTimeoutExpired)
        {
            ThrowHelper.ThrowTimeout("The timeout expired before the lock was acquired", null);
        }

        return default;
    }

    /// <inheritdoc cref="ILocker"/>
    public unsafe readonly ref struct Locker
    {
        private readonly AsyncReaderWriterLock? _rwLock;
        private readonly delegate*<AsyncReaderWriterLock, void> _exitLock;

        /// <inheritdoc cref="ILocker.IsAcquired"/>
        public bool IsAcquired => _rwLock is not null;

        internal Locker(AsyncReaderWriterLock? rwLock, delegate*<AsyncReaderWriterLock, void> exitLock)
        {
            _rwLock = rwLock;
            _exitLock = exitLock;
        }

        internal LockerUpgrade AsUpgrade()
        {
            return new(_rwLock, _exitLock);
        }

        public void Dispose()
        {
            if (_rwLock is not null)
            {
                _exitLock(_rwLock);
            }
        }
    }

    /// <inheritdoc cref="ILocker"/>
    public unsafe readonly ref struct LockerUpgrade
    {
        private readonly AsyncReaderWriterLock? _rwLock;
        private readonly delegate*<AsyncReaderWriterLock, void> _exitLock;

        /// <inheritdoc cref="ILocker.IsAcquired"/>
        public bool IsAcquired => _rwLock is not null;

        internal LockerUpgrade(AsyncReaderWriterLock? rwLock, delegate*<AsyncReaderWriterLock, void> exitLock)
        {
            _rwLock = rwLock;
            _exitLock = exitLock;
        }

        /// <inheritdoc cref="ILockerUpgrade.UsingWriteUpgradeAsync(bool, int, CancellationToken)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Locker UsingWriteUpgrade(bool throwIfTimeoutExpired, int timeout, CancellationToken cancellationToken = default)
        {
            if (_rwLock is null && throwIfTimeoutExpired)
            {
                ThrowHelper.ThrowTimeout("The upgradable read lock could not be acquired", null);
            }

            if (_rwLock is null)
            {
                return default;
            }

            return CreateLocker(_rwLock, &AsyncReaderWriterLockMarshal.EnterWriteUpgrade, &AsyncReaderWriterLockMarshal.ExitWriteUpgrade, throwIfTimeoutExpired, timeout, cancellationToken);
        }

        /// <inheritdoc cref="ILockerUpgrade.UsingWriteUpgradeAsync(bool, int, CancellationToken)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Locker UsingWriteUpgrade(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return UsingWriteUpgrade(true, (int)timeout.TotalMilliseconds, cancellationToken);
        }
        /// <inheritdoc cref="ILockerUpgrade.UsingWriteUpgradeAsync(bool, int, CancellationToken)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Locker UsingWriteUpgrade(CancellationToken cancellationToken = default)
        {
            return UsingWriteUpgrade(true, Timeout.Infinite, cancellationToken);
        }

        public void Dispose()
        {
            if (_rwLock is not null)
            {
                _exitLock(_rwLock);
            }
        }
    }
}
