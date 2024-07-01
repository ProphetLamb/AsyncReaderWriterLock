namespace System.Threading;

public partial class AsyncReaderWriterLock
{
    internal partial class RequestNode
    {
        internal static class Pool
        {
            [ThreadStatic] private static RequestNode? t_localValue;
            private static RequestNode? s_head;
            private static RequestNode? s_tail;

            internal static RequestNode Rent(int kind, long timeoutMilliseconds, CancellationToken lockCancellation)
            {
                if (t_localValue is { } value)
                {
                    t_localValue = null;
                    value.PoolInitialize(kind, timeoutMilliseconds, lockCancellation);
                    return value;
                }

                if (!TryDequeueConcurrent(ref s_head, out value) || value is null)
                {
                    value = new();
                }

                value.PoolInitialize(kind, timeoutMilliseconds, lockCancellation);
                return value;
            }

            internal static void Return(RequestNode value)
            {
                var result = false;
                value.PoolDeinitialize();
                if ((ushort)value._taskSource.Version == ushort.MaxValue)
                {
                    return; // drop the node
                }
                if (TryEnqueueConcurrent(ref s_tail, value))
                {
                    result = true;
                }
                if (t_localValue is not null)
                {
                    // attempt to enqueue the local tail to the queue
                    TryEnqueueConcurrent(ref s_tail, t_localValue);
                }
                else if (!result)
                {
                    t_localValue = value;
                    result = true;
                }

                if (!result)
                {
                    // retry enqueuing
                    result = TryEnqueueConcurrent(ref s_tail, value);
                }

                // if result is false, let the GC destroy the value
            }
        }
    }
}
