using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Tasks.Sources;

namespace System.Threading;

public partial class AsyncReaderWriterLock
{
    [DebuggerDisplay("{ToString(),nq}")]
    internal partial class RequestNode : IValueTaskSource, IValueTaskSource<bool>
    {
        private const int KindUninitialized = 0;
        private const int KindRead = 1;
        private const int KindWrite = 3;
        private const int KindMaskReadWrite = KindWrite;
        private const int KindUpgrade = 4;
        private const int StatePending = 0; // no result
        private const int StateCompleted = 1; // true or false result
        private const int StateFailed = 2; // exception result
        private const int StateDeadborn = 3; // a deadborn node was enqueued, but will never be consumed, is returned to pool by queue
        private int _kind;
        private int _rc; // count two references, 1. the queue, 2. the task
        private int _state;
        private TimeoutState _timeout;
        private ManualResetValueTaskSourceCore<bool> _taskSource;
        private CancellationTokenRegistration _cancellationRegistration;
        public RequestNode? QueueNext;

        [DebuggerHidden] public bool IsInitialized => _kind != KindUninitialized;

        [DebuggerHidden] public bool IsRead => (_kind & KindMaskReadWrite) == KindRead;

        [DebuggerHidden] public bool IsWrite => (_kind & KindMaskReadWrite) == KindWrite;

        [DebuggerHidden] public bool IsUpgrade => (_kind & KindUpgrade) == KindUpgrade;


        [DebuggerHidden] public bool IsPending => Volatile.Read(ref _state) == StatePending;

        [DebuggerHidden] public bool IsCompleted => Volatile.Read(ref _state) == StateCompleted;

        [DebuggerHidden] public bool IsFailed => Volatile.Read(ref _state) == StateFailed;

        [DebuggerHidden] public bool IsDeadborn => Volatile.Read(ref _state) == StateDeadborn;

        public ValueTask<bool> Task => new(this, _taskSource.Version);

        private void PoolInitialize(int kind, long timeoutMilliseconds, CancellationToken lockCancellation)
        {
            Debug.Assert(!IsInitialized);
            _kind = kind;
            _timeout = TimeoutState.Create(timeoutMilliseconds);
            _taskSource.RunContinuationsAsynchronously = false;
            _cancellationRegistration = !lockCancellation.CanBeCanceled ? default : lockCancellation.UnsafeRegister(static o => ((RequestNode)o!).OnCancellationRegistrationCancelled(), this);
            _rc = 2;
        }

        private void PoolDeinitialize()
        {
            _kind = KindUninitialized;
            _timeout = default;
            Volatile.Write(ref _state, StatePending);
        }

        /// <summary>
        /// Release the request before the task has been acquired by a user
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReleaseBeforeAcquired()
        {
            // deadborn signals the queue to immediatly release the reference via ValidateCanConsumeDequeued.
            var result = Cas(StatePending, StateDeadborn);
            if (result)
            {
                // the cancellation doesnt matter for a dead task, consider it already cancelled
                _cancellationRegistration.Dispose();
                _cancellationRegistration = default;
            }
            return result;
        }

        /// <summary>
        /// Release the request as unused
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReleaseUnused()
        {
            // deadborn signals the queue to immediatly release the reference via ValidateCanConsumeDequeued.
            Volatile.Write(ref _state, StateDeadborn);
            // the cancellation doesnt matter for a dead task, consider it already cancelled
            _cancellationRegistration.Dispose();
            _cancellationRegistration = default;
            ReleaseReference(); // releases the task reference, the user is not acquiring the task, ever
        }

        /// <summary>
        /// Complete request after the task has been acquired by a user
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryCompleteAcquired(bool isSuccessfulResult)
        {
            if (!Cas(StatePending, isSuccessfulResult ? StateCompleted : StateFailed))
            {
                return false;
            }
            // the completed task may no longer be cancelled
            _cancellationRegistration.Dispose();
            _cancellationRegistration = default;
            try
            {
                _taskSource.SetResult(isSuccessfulResult);
            }
            catch (Exception ex)
            {
                ThreadPool.QueueUserWorkItem(static o => ((ExceptionDispatchInfo)o!).Throw(), ExceptionDispatchInfo.Capture(ex));
            }
            ReleaseReference(); // releases the queue reference
            return true;
        }

        /// <summary>
        /// Complete the request after the acter has been acquired by a user
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryCompleteAcquired(Exception taskException)
        {
            if (!Cas(StatePending, StateFailed))
            {
                return false;
            }
            // the completed task may no longer be cancelled
            _cancellationRegistration.Dispose();
            _cancellationRegistration = default;
            try
            {
                _taskSource.SetException(taskException);
            }
            catch (Exception ex)
            {
                ThreadPool.QueueUserWorkItem(static o => ((ExceptionDispatchInfo)o!).Throw(), ExceptionDispatchInfo.Capture(ex));
            }
            ReleaseReference(); // releases the queue reference
            return true;
        }

        /// <summary>
        /// Validates whether the request can be returned from the queue, after a dequeue. If not discard the request.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CheckQueueCanHold()
        {
            if (_timeout.CheckElapsed())
            {
                TryCompleteAcquired(false);
                return false;
            }

            var isPending = IsPending;
            if (!isPending)
            {
                ReleaseReference(); // releases the queue reference
            }
            return isPending;
        }

        /// <summary>
        /// Decrements the reference counter, and returns the request once zero
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReleaseReference()
        {
            if (Interlocked.Decrement(ref _rc) == 0)
            {
                Pool.Return(this);
            }
        }

        void IValueTaskSource.GetResult(short token)
        {
            GetResult(token);
        }

        public bool GetResult(short token)
        {
            ArgumentOutOfRangeException.ThrowIfNotEqual(token, _taskSource.Version);
            try
            {
                return _taskSource.GetResult(token);
            }
#if DEBUG
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.Assert(ex == null, "exception thrown by task source");
                return false;
            }
#endif
            finally
            {
                _taskSource.Reset();
                ReleaseReference(); // release the task reference
            }
        }

        public ValueTaskSourceStatus GetStatus(short token)
        {
            return _taskSource.GetStatus(token);
        }

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            _taskSource.OnCompleted(continuation, state, token, flags);
        }

        private void OnCancellationRegistrationCancelled()
        {
            if (!Cas(StatePending, StateFailed))
            {
                return;
            }
            _taskSource.SetException(new OperationCanceledException(_cancellationRegistration.Token));
            _cancellationRegistration = default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Cas(int comparand, int value)
        {
            return Interlocked.CompareExchange(ref _state, value, comparand) == comparand;
        }

        public override string ToString()
        {
            StringBuilder sb = new();
            sb.Append('{');
            sb.Append("Kind=");
            sb.Append(!IsInitialized ? "Uninitialized" : IsWrite ? "Write" : "Read");
            if (IsUpgrade)
            {
                sb.Append("|Upgrade");
            }

            sb.Append(", State=");
            sb.Append(IsPending ? "Pending" : IsCompleted ? "Completed" : IsFailed ? "Cancelled" : "Deadborn");
            sb.Append('}');
            return sb.ToString();
        }

        public static RequestNode CreateUninitialized(long timeoutMilliseconds, CancellationToken cancellationToken) => Pool.Rent(KindUninitialized, timeoutMilliseconds, cancellationToken);
        public static RequestNode CreateRead(long timeoutMilliseconds, CancellationToken cancellationToken) => Pool.Rent(KindRead, timeoutMilliseconds, cancellationToken);
        public static RequestNode CreateReadUpgrade(long timeoutMilliseconds, CancellationToken cancellationToken) => Pool.Rent(KindRead | KindUpgrade, timeoutMilliseconds, cancellationToken);
        public static RequestNode CreateWrite(long timeoutMilliseconds, CancellationToken cancellationToken) => Pool.Rent(KindWrite, timeoutMilliseconds, cancellationToken);
        public static RequestNode CreateWriteUpgrade(long timeoutMilliseconds, CancellationToken cancellationToken) => Pool.Rent(KindWrite | KindUpgrade, timeoutMilliseconds, cancellationToken);
    }
}
