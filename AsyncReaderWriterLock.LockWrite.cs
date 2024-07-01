namespace System.Threading;

public partial class AsyncReaderWriterLock
{
    internal bool TryEnterWrite(ref SpinWait wait, bool writeQueued)
    {
        var state = _state.Read();
        while (true)
        {
            State prev;
            if (state.CanEnterWrite)
            {
                prev = _state.Cx(state, State.Write);
                if (prev == state)
                {
                    return true;
                }
            }
            else if (writeQueued)
            {
                if (state.IsQueueChanged)
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

    internal ValueTask<bool> EnterWrite(long timeoutMilliseconds, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMilliseconds, -1);
        SpinWait wait = new();
        if (TryEnterWrite(ref wait, false))
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

        var value = RequestNode.CreateWrite(timeoutMilliseconds, cancellationToken);
        ref var queue = ref WriteQueue;

        queue.IncrementCount();
        while (true)
        {
            if (queue.TryEnqueue(value))
            {
                if (!TryEnterWrite(ref wait, true))
                {
                    return value.Task;
                }
                value.TryReleaseBeforeAcquired();
                return new(true);
            }

            var tryReacquire = wait.NextSpinWillYield;
            wait.SpinOnce();
            if (tryReacquire && TryEnterWrite(ref wait, false))
            {
                value.ReleaseUnused();
                queue.DecrementCount();
                return new(true);
            }
        }
    }

    internal void ExitWrite(ref SpinWait wait)
    {
        ConsumeQueueUnderWrite(ref wait);
    }

}
