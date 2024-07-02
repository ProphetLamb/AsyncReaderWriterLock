using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System.Threading;

public partial class AsyncReaderWriterLock
{
    /// <summary>
    /// Consumes <see cref="RequestNode"/>s from the queue according to (I) elevated kind, (II) upgrade state, (III) queue order.
    /// <br/>
    /// Rotates the queue at most once, until a fitting <see cref="RequestNode"/> is found.
    /// <br/>
    /// Requires a write locked state
    /// </summary>
    private void ConsumeQueueUnderWrite(ref SpinWait wait)
    {
        DequeueState dequeue = new();
        while (true)
        {
            var state = _state.Read();
            Debug.Assert(state.IsWrite);
            dequeue.IsUpgrade |= state.IsUpgrade;
            RequestNode? write = _options.Elevated switch
            {
                ElevatedKind.Read => DequeueElevatedRead(ref wait, ref dequeue),
                ElevatedKind.Write => DequeueElevatedWrite(ref wait, ref dequeue),
                _ => _queue.DequeueWriteOrReadChain(ref wait, ref dequeue),
            };

            if (write is not null)
            {
                Debug.Assert(dequeue.IsReadEmpty);
                Debug.Assert(state.IsUpgrade == dequeue.IsUpgrade);

                if (!write.TryCompleteAcquired(true))
                {
                    continue;
                }
                return;
            }

            Debug.Assert(state.IsWrite);
            if (state.IsQueueChanged)
            {
                _state.Cx(state, state.QueueChanged(false));
                continue;
            }

            Debug.Assert(state.IsWrite);
            // exiting a writeupgrade into readupgrade => one additional read remaining
            if (!_state.Cas(state, State.FromRead(dequeue.ReadCount + (nuint)(dequeue.IsUpgrade ? 1 : 0)).QueueChanged(dequeue.IsQueueRemaining).Upgrade(dequeue.IsUpgrade)))
            {
                continue;
            }

            if (dequeue.ReadCount > 0)
            {
                CompleteReadDequeueUnderRead(ref wait, ref dequeue);
            }

            return;
        }
    }

    /// <summary>
    /// Completes all dequeued reads.
    /// <br/>
    /// Requires a read locked state
    /// </summary>
    private void CompleteReadDequeueUnderRead(ref SpinWait wait, ref DequeueState dequeue)
    {
        var phantomReadCount = 0;
        while (dequeue.Dequeue(out var value))
        {
            phantomReadCount += value.TryCompleteAcquired(true) ? 0 : 1;
        }

        if (phantomReadCount != 0)
        {
            ExitRead(ref wait, phantomReadCount);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private RequestNode? DequeueElevatedRead(ref SpinWait wait, ref DequeueState dequeue)
    {
        _elevatedQueue.DequeueReadChain(ref wait, ref dequeue);

        if (dequeue.IsReadEmpty)
        {
            var write = _queue.DequeueWriteOrReadChain(ref wait, ref dequeue);
            Debug.Assert(dequeue.IsReadEmpty);
            return write;
        }

        dequeue.IsQueueRemaining |= _queue.Count > 0;
        return null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private RequestNode? DequeueElevatedWrite(ref SpinWait wait, ref DequeueState dequeue)
    {
        if (!dequeue.IsReadEmpty)
        {
            dequeue.IsQueueRemaining |= _elevatedQueue.Count > 0;
            return null;
        }

        if (_elevatedQueue.DequeueWriteOrReadChain(ref wait, ref dequeue) is { } write)
        {
            Debug.Assert(dequeue.IsReadEmpty);
            return write;
        }
        Debug.Assert(dequeue.IsReadEmpty);
        _queue.DequeueReadChain(ref wait, ref dequeue);
        return null;
    }
}
