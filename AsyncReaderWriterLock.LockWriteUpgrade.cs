namespace System.Threading;

public partial class AsyncReaderWriterLock
{
    internal bool TryEnterWriteUpgrade(ref SpinWait wait, bool writeQueued)
    {
        var state = _state.Read();
        while (true)
        {
            State prev;
            if (state.CanEnterWriteUpgrade)
            {
                prev = _state.Cx(state, State.Write.Upgrade(true));
                if (prev == state)
                {
                    return true;
                }
            }
            else if (writeQueued)
            {
                if (state.IsQueueChanged || !state.IsUpgrade)
                {
                    return false;
                }

                prev = _state.Cx(state, state.QueueChanged(true));
                if (prev == state)
                {
                    return true;
                }
            }
            else
            {
                return false;
            }

            var reloadState = wait.NextSpinWillYield;
            wait.SpinOnce();
            state = reloadState ? _state.Read() : prev;
        }
    }

    internal ValueTask<bool> EnterWriteUpgrade(long timeoutMilliseconds, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMilliseconds, -1);
        SpinWait wait = new();
        if (TryEnterWriteUpgrade(ref wait, false))
        {
            return new(true);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromException<bool>(new OperationCanceledException(cancellationToken));
        }

        if (timeoutMilliseconds == 0)
        {
            return new(false);
        }

        var value = RequestNode.CreateWriteUpgrade(timeoutMilliseconds, cancellationToken);
        ref var queue = ref WriteQueue;

        queue.IncrementCount();
        while (true)
        {
            if (queue.TryEnqueue(value))
            {
                if (!TryEnterWriteUpgrade(ref wait, true))
                {
                    return value.Task;
                }
                value.TryReleaseBeforeAcquired();
                return new(true);
            }

            var tryReacquire = wait.NextSpinWillYield;
            wait.SpinOnce();
            if (tryReacquire && TryEnterWriteUpgrade(ref wait, false))
            {
                value.ReleaseUnused();
                queue.DecrementCount();
                return new(true);
            }
        }
    }

    internal void ExitWriteUpgrade(ref SpinWait wait)
    {
        ConsumeQueueUnderWrite(ref wait);
    }
}
