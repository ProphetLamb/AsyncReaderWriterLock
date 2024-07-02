using System.Runtime.CompilerServices;

namespace System.Threading;

internal static class QueueHelper
{
    public interface IQueueNode<TSelf>
    {
        ref TSelf? GetNext();
    }

    /// <summary>
    /// Attempts to replace the <paramref name="locationHead"/> with <see cref="QueueNext"/> in a concurrent safe fashion.
    /// </summary>
    /// <param name="locationHead">The location of the head of the queue.</param>
    /// <param name="head">The value dequeued from the head of the queue.</param>
    /// <returns><c>true</c> if <paramref name="head"/> has been dequeued; otherwise <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryDequeueConcurrent<T>(ref T? locationHead, out T? head) where T : class, IQueueNode<T>
    {
        bool result;
        head = Volatile.Read(ref locationHead);
        if (head is not null)
        {
            var next = Volatile.Read(ref head.GetNext());
            result = Cas(ref locationHead, head, next);
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
    public static bool TryEnqueueConcurrent<T>(ref T? locationTail, T value) where T : class, IQueueNode<T>
    {
        var tail = Volatile.Read(ref locationTail);
        if (tail is not null)
        {
            var next = Volatile.Read(ref tail.GetNext());
            if (next is not null)
            {
                if (!Cas(ref locationTail, tail, next))
                {
                    return false;
                }
                tail = next;
            }
            // !! next may no longer be used !!
            if (Cas(ref tail.GetNext()!, null, value))
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
    public static bool Cas<T>(ref T? location1, T? comparand, T? value) where T : class
    {
        return Interlocked.CompareExchange(ref location1, value, comparand) == comparand;
    }

    /// <inheritdoc cref="Interlocked.CompareExchange(ref object?, object?, object?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? Cx<T>(ref T? location1, T? comparand, T? value) where T : class
    {
        return Interlocked.CompareExchange(ref location1, value, comparand);
    }

}
