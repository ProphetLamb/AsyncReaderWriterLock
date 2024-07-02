using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System.Threading;

public partial class AsyncReaderWriterLock
{
    [DebuggerDisplay("Count={Count,nq}"), DebuggerTypeProxy(typeof(DebugView))]
    private struct Queue
    {
        private RequestNode? _head;
        private RequestNode? _tail;
        private nuint _count;

        public nuint Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Volatile.Read(ref _count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public nuint IncrementCount()
        {
            var incremented = InterlockedHelper.Increment(ref _count);
            if (incremented <= MaxReadCount)
            {
                return incremented;
            }

            InterlockedHelper.Decrement(ref _count);
            ThrowHelper.ThrowInvalidOperation($"The lock can hold up to {nameof(MaxReadCount)} readers, but more attempted to enter", null);
            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public nuint DecrementCount() => InterlockedHelper.Decrement(ref _count);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryEnqueue(RequestNode value)
        {
            return TryEnqueueConcurrent(ref _tail, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryDequeue([MaybeNullWhen(false)] out RequestNode value)
        {
            return TryDequeueConcurrent(ref _head, out value);
        }

        /// <summary>
        /// Dequeues a chain of reads, including maybe a single upgrade read, if allowed
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void DequeueReadChain(ref SpinWait wait, ref DequeueState dequeue)
        {
            var count = Volatile.Read(ref _count);
            for (nuint iter = 0; true; iter++)
            {
                // dequeue value
                RequestNode? value;
                while (!TryDequeueConcurrent(ref _head, out value))
                {
                    wait.SpinOnce();
                }

                if (value is null)
                {
                    return;
                }

                if (!value.CheckQueueCanHold())
                {
                    InterlockedHelper.Decrement(ref _count);
                    continue;
                }

                // process dequeud value
                if (dequeue.TryEnqueueRead(value))
                {
                    InterlockedHelper.Decrement(ref _count);
                    continue;
                }

                // enqueue and retry
                while (!TryEnqueueConcurrent(ref _tail, value))
                {
                    wait.SpinOnce();
                }

                if (dequeue.IsReadEmpty && iter <= Math.Max(Volatile.Read(ref _count), count))
                {
                    continue;
                }

                dequeue.IsQueueRemaining = true;
                return;
            }
        }

        /// <summary>
        /// Dequeues a single write, maybe an upgrade write, if allowed
        /// <br/>-OR-<br/>
        /// Dequeues a chain or reads, including maybe a single upgrade read, if allowed.
        /// </summary>
        /// <returns>The write if a write was dequeued.</returns>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public RequestNode? DequeueWriteOrReadChain(ref SpinWait wait, ref DequeueState dequeue)
        {
            var count = Volatile.Read(ref _count);
            for (nuint iter = 0; true; iter++)
            {
                // dequeue a value
                RequestNode? value;
                while (!TryDequeueConcurrent(ref _head, out value))
                {
                    wait.SpinOnce();
                }

                if (value is null)
                {
                    return null;
                }

                if (!value.CheckQueueCanHold())
                {
                    InterlockedHelper.Decrement(ref _count);
                    continue;
                }

                // process dequeud value
                if (dequeue.TryEnqueueRead(value))
                {
                    InterlockedHelper.Decrement(ref _count);
                    continue;
                }

                if (value.IsWrite && dequeue.IsReadEmpty && (value.IsUpgrade == dequeue.IsUpgrade))
                {
                    dequeue.IsQueueRemaining |= InterlockedHelper.Decrement(ref _count) > 0;
                    return value;
                }

                // enqueue and retry
                while (!TryEnqueueConcurrent(ref _tail, value))
                {
                    wait.SpinOnce();
                }

                if (dequeue.IsReadEmpty && iter <= Math.Max(Volatile.Read(ref _count), count))
                {
                    continue;
                }

                dequeue.IsQueueRemaining = true;
                return null;
            }
        }

        public void CleanQueueState()
        {
            var head = Volatile.Read(ref _head);

            for (var current = head; current is not null; current = Volatile.Read(ref _head) == head ? Volatile.Read(ref current.QueueNext) : null)
            {
                if (Volatile.Read(ref _head) != head)
                {
                    return;
                }

                if (current.CheckQueueCanHold())
                {
                    continue;
                }

                if (Volatile.Read(ref _head) != head)
                {
                    return;
                }

                // attempt to remove the current request form the queue
                var next = Volatile.Read(ref current.QueueNext);
                if (next is null)
                {
                    return;
                }

                var overNext = Volatile.Read(ref next.QueueNext);
                if (Cas(ref current.QueueNext, next, overNext))
                {
                    DecrementCount();
                }
            }
        }

        public void Dispose()
        {
            while (true)
            {
                RequestNode? node;
                while (!TryDequeue(out node))
                {
                }

                if (node is null)
                {
                    return;
                }

                node.TryCompleteAcquired(new ObjectDisposedException(nameof(AsyncReaderWriterLock)));
                DecrementCount();
            }
        }

        public readonly List<RequestNode> ToList()
        {
            List<RequestNode> values = new(unchecked((int)(_count & int.MaxValue)));
            for (var value = _head; value is not null; value = value.QueueNext)
            {
                values.Add(value);
            }
            return values;
        }

        private readonly struct DebugView(Queue queue)
        {
            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public RequestNode[] Items => queue.ToList().ToArray();
        }
    }

    /// <summary>
    /// Attempts to replace the <paramref name="locationHead"/> with <see cref="QueueNext"/> in a concurrent safe fashion.
    /// </summary>
    /// <param name="locationHead">The location of the head of the queue.</param>
    /// <param name="head">The value dequeued from the head of the queue.</param>
    /// <returns><c>true</c> if <paramref name="head"/> has been dequeued; otherwise <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryDequeueConcurrent(ref RequestNode? locationHead, out RequestNode? head)
    {
        bool result;
        head = Volatile.Read(ref locationHead);
        if (head is not null)
        {
            var next = Volatile.Read(ref head.QueueNext);
            result = Cas(ref locationHead!, head, next);
        }
        else
        {
            result = Volatile.Read(ref locationHead) is null;
        }
        return result;
    }

    /// <summary>
    /// Attempts to replace <paramref name="locationTail"/> with <paramref name="value"/>, and the <see cref="QueueNext"/> with <paramref name="value"/> in a concurrent safe fashion.
    /// </summary>
    /// <param name="locationTail">The location of the tail of the queue.</param>
    /// <param name="value">The value to enqueue to the queue.</param>
    /// <returns><c>true</c> if <paramref name="value"/> has been enqueued; otherwise <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryEnqueueConcurrent(ref RequestNode? locationTail, RequestNode value)
    {
        var tail = Volatile.Read(ref locationTail);
        if (tail is not null)
        {
            var next = Volatile.Read(ref tail.QueueNext);
            if (next is not null)
            {
                if (!Cas(ref locationTail, tail, next))
                {
                    return false;
                }
                tail = next;
            }
            // !! next may no longer be used !!
            if (Cas(ref tail.QueueNext, null, value))
            {
                Cx(ref locationTail, tail, value);
                return true;
            }
            return false;
        }

        return Cas(ref locationTail, null, value);
    }

    /// <inheritdoc cref="Interlocked.CompareExchange(ref object?, object?, object?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Cas(ref RequestNode? location1, RequestNode? comparand, RequestNode? value)
    {
        return Interlocked.CompareExchange(ref location1, value, comparand) == comparand;
    }

    /// <inheritdoc cref="Interlocked.CompareExchange(ref object?, object?, object?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static RequestNode? Cx(ref RequestNode? location1, RequestNode? comparand, RequestNode? value)
    {
        return Interlocked.CompareExchange(ref location1, value, comparand);
    }

}
