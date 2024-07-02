using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Sources;

namespace System.Threading;

internal static class TaskHelper
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ExposedValueTask GetExposed(this ValueTask task)
    {
        var exposed = Unsafe.As<ValueTask, ExposedValueTask>(ref task);
        return exposed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ExposedValueTask<T> GetExposed<T>(this ValueTask<T> task)
    {
        var exposed = Unsafe.As<ValueTask<T>, ExposedValueTask<T>>(ref task);
        return exposed;
    }

    public static ValueTask<T> ToTask<T>(this ExposedValueTask<T> task) => Unsafe.As<ExposedValueTask<T>, ValueTask<T>>(ref task);
    public static ValueTask ToTask(this ExposedValueTask task) => Unsafe.As<ExposedValueTask, ValueTask>(ref task);

    public static ValueTask<U> Cast<T, U>(this ValueTask<T> task)
        where T : class, U
        where U : class
    {
        return Unsafe.As<ValueTask<T>, ValueTask<U>>(ref task);
    }

    public static bool Wait(this ValueTask task, int millisecondsTimeout, CancellationToken cancellationToken)
    {
        var exposed = task.GetExposed();
        if (exposed.Obj is null)
        {
            return true;
        }

        if (exposed.Obj is Task t)
        {
            if (t.IsCompletedSuccessfully)
            {
                return true;
            }
            return t.Wait(millisecondsTimeout, cancellationToken);
        }

        if (exposed.Obj is IValueTaskSource vts)
        {
            if (vts.GetStatus(exposed.Token) != ValueTaskSourceStatus.Pending)
            {
                vts.GetResult(exposed.Token);
                return true;
            }

            var awaiter = ValueTaskSourceAwaiter.Pool.Rent(vts, exposed.Token);
            bool result = awaiter.Wait(millisecondsTimeout, cancellationToken);
            ValueTaskSourceAwaiter.Pool.Return(awaiter);
            return result;
        }

        return Task.Run(async () => await task, cancellationToken).Wait(millisecondsTimeout, cancellationToken);
    }

    internal unsafe sealed class ValueTaskSourceContinuationAction<T> : IValueTaskSource<T>, QueueHelper.IQueueNode<ValueTaskSourceContinuationAction<T>>
    {
        private IValueTaskSource? _vts;
        private Func<IValueTaskSource?, short, object?, T> _getResultFactory = default!;
        private object? _state;

        public ValueTaskSourceContinuationAction<T>? QueueNext;

        private void PoolInitialize(IValueTaskSource vts, Func<IValueTaskSource?, short, object?, T> getResultFactory, object? state)
        {
            Debug.Assert(_vts is null);
            _vts = vts;
            _getResultFactory = getResultFactory;
            _state = state;
        }

        private void PoolDeinitialize()
        {
            _vts = null;
            _getResultFactory = default!;
            _state = null;
        }

        public ref ValueTaskSourceContinuationAction<T>? GetNext()
        {
            return ref QueueNext;
        }

        public T GetResult(short token)
        {
            ObjectDisposedException.ThrowIf(_vts is null, this);
            return _getResultFactory(_vts, token, _state);
        }

        public ValueTaskSourceStatus GetStatus(short token)
        {
            ObjectDisposedException.ThrowIf(_vts is null, this);
            return _vts.GetStatus(token);
        }

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            ObjectDisposedException.ThrowIf(_vts is null, this);
            _vts.OnCompleted(continuation, state, token, flags);
        }

        internal static class Pool
        {
            [ThreadStatic] private static ValueTaskSourceContinuationAction<T>? t_localValue;
            private static ValueTaskSourceContinuationAction<T>? s_head;
            private static ValueTaskSourceContinuationAction<T>? s_tail;

            internal static ValueTaskSourceContinuationAction<T> Rent(IValueTaskSource vts, Func<IValueTaskSource?, short, object?, T> getResultFactory, object? state)
            {
                if (t_localValue is { } value)
                {
                    t_localValue = null;
                    value.PoolInitialize(vts, getResultFactory, state);
                    return value;
                }

                if (!QueueHelper.TryDequeueConcurrent(ref s_head, out value) || value is null)
                {
                    value = new();
                }

                value.PoolInitialize(vts, getResultFactory, state);
                return value;
            }

            internal static void Return(ValueTaskSourceContinuationAction<T> value)
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

    /// <summary>
    /// Stripped implementation of <see cref="ManualResetEventSlim"/>
    /// </summary>
    internal sealed class ValueTaskSourceAwaiter : QueueHelper.IQueueNode<ValueTaskSourceAwaiter>
    {
        private IValueTaskSource? _vts;
        private short _token;
        private int _isSet;

        public ValueTaskSourceAwaiter? QueueNext;

        private void PoolInitialize(IValueTaskSource vts, short token)
        {
            Debug.Assert(vts.GetStatus(token) == ValueTaskSourceStatus.Pending);
            vts.OnCompleted(static o => ((ValueTaskSourceAwaiter)o!).OnValueTaskSourceCompleted(), this, token, ValueTaskSourceOnCompletedFlags.None);
            _vts = vts;
            _token = token;
            _isSet = 0;
        }

        private void PoolDeinitialize()
        {
            _vts = default;
            _token = default;
            _isSet = 1;
        }
        public bool Wait(int timeout, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_vts is null, this);
            if (_isSet == 1)
            {
                return true;
            }
            if (timeout == 0)
            {
                return false;
            }
            var bNeedTimeoutAdjustment = timeout == Timeout.Infinite;
            var startTime = bNeedTimeoutAdjustment ? (uint)Environment.TickCount : 0;
            var remainingTime = timeout;

            SpinWait wait = new();
            while (wait.Count < 128)
            {
                wait.SpinOnce(-1);
                if (_isSet == 1)
                {
                    return true;
                }

                if (wait.Count >= 100 && wait.Count % 10 == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            using var r = cancellationToken.UnsafeRegister(static o => ((ValueTaskSourceAwaiter)o!).OnCancellationRegistrationCancelled(), this);
            lock (this)
            {
                while (_isSet == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    remainingTime = bNeedTimeoutAdjustment ? timeout : UpdateTimeOut(startTime, timeout);
                    if (bNeedTimeoutAdjustment && remainingTime <= 0)
                    {
                        return false;
                    }

                    if (_isSet == 1)
                    {
                        return true;
                    }

                    if (!Monitor.Wait(this, remainingTime))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private void OnValueTaskSourceCompleted()
        {
            lock (this)
            {
                Monitor.PulseAll(this);
            }
        }

        private void OnCancellationRegistrationCancelled()
        {
            lock (this)
            {
                Monitor.PulseAll(this);
            }
        }

        public static int UpdateTimeOut(uint startTime, int originalWaitMillisecondsTimeout)
        {
            // The function must be called in case the time out is not infinite
            Debug.Assert(originalWaitMillisecondsTimeout != Timeout.Infinite);

            uint elapsedMilliseconds = (uint)Environment.TickCount - startTime;

            // Check the elapsed milliseconds is greater than max int because this property is uint
            if (elapsedMilliseconds > int.MaxValue)
            {
                return 0;
            }

            // Subtract the elapsed time from the current wait time
            int currentWaitTimeout = originalWaitMillisecondsTimeout - (int)elapsedMilliseconds;
            if (currentWaitTimeout <= 0)
            {
                return 0;
            }

            return currentWaitTimeout;
        }

        public ref ValueTaskSourceAwaiter? GetNext()
        {
            return ref QueueNext;
        }

        internal static class Pool
        {
            [ThreadStatic] private static ValueTaskSourceAwaiter? t_localValue;
            private static ValueTaskSourceAwaiter? s_head;
            private static ValueTaskSourceAwaiter? s_tail;

            internal static ValueTaskSourceAwaiter Rent(IValueTaskSource vts, short token)
            {
                if (t_localValue is { } value)
                {
                    t_localValue = null;
                    value.PoolInitialize(vts, token);
                    return value;
                }

                if (!QueueHelper.TryDequeueConcurrent(ref s_head, out value) || value is null)
                {
                    value = new();
                }

                value.PoolInitialize(vts, token);
                return value;
            }

            internal static void Return(ValueTaskSourceAwaiter value)
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

    [StructLayout(LayoutKind.Auto)]
    public readonly struct ExposedValueTask(object? obj, short token, bool continueOnCapturedContext)
    {
        /// <summary>null if representing a successful synchronous completion, otherwise a <see cref="Task"/> or a <see cref="IValueTaskSource"/>.</summary>
        internal readonly object? Obj = obj;
        /// <summary>Opaque value passed through to the <see cref="IValueTaskSource"/>.</summary>
        internal readonly short Token = token;
        /// <summary>true to continue on the captured context; otherwise, false.</summary>
        /// <remarks>Stored in the <see cref="ValueTask"/> rather than in the configured awaiter to utilize otherwise padding space.</remarks>
        internal readonly bool ContinueOnCapturedContext = continueOnCapturedContext;
    }

    [StructLayout(LayoutKind.Auto)]
    public readonly struct ExposedValueTask<TResult>(object? obj, TResult? result, short token, bool continueOnCapturedContext)
    {
        /// <summary>null if <see cref="_result"/> has the result, otherwise a <see cref="Task{TResult}"/> or a <see cref="IValueTaskSource{TResult}"/>.</summary>
        internal readonly object? Obj = obj;
        /// <summary>The result to be used if the operation completed successfully synchronously.</summary>
        internal readonly TResult? Result = result;
        /// <summary>Opaque value passed through to the <see cref="IValueTaskSource{TResult}"/>.</summary>
        internal readonly short Token = token;
        /// <summary>true to continue on the captured context; otherwise, false.</summary>
        /// <remarks>Stored in the <see cref="ValueTask{TResult}"/> rather than in the configured awaiter to utilize otherwise padding space.</remarks>
        internal readonly bool ContinueOnCapturedContext = continueOnCapturedContext;
    }
}
