using System.Diagnostics;

namespace System.Threading;

public partial class AsyncReaderWriterLock
{
    internal bool TryEnterReadUpgrade(ref SpinWait wait, bool readQueued)
    {
        var state = _state.Read();
        while (true)
        {
            State prev;
            if (state.CanEnterReadUpgrade)
            {
                prev = _state.Cx(state, state.AddRead().Upgrade(true));
                if (prev == state)
                {
                    return true;
                }
            }
            else if (readQueued)
            {
                if (!state.IsWrite || state.IsQueueChanged || state.IsUpgrade)
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

    internal ValueTask<bool> EnterReadUpgrade(long timeoutMilliseconds, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMilliseconds, -1);
        SpinWait wait = new();
        if (TryEnterReadUpgrade(ref wait, false))
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

        var value = RequestNode.CreateReadUpgrade(timeoutMilliseconds, cancellationToken);
        ref var queue = ref ReadQueue;

        queue.IncrementCount();
        while (true)
        {
            if (queue.TryEnqueue(value))
            {
                if (!TryEnterReadUpgrade(ref wait, true))
                {
                    return value.Task;
                }
                value.TryReleaseBeforeAcquired();
                return new(true);
            }


            var tryReacquire = wait.NextSpinWillYield;
            wait.SpinOnce();
            if (tryReacquire && TryEnterReadUpgrade(ref wait, false))
            {
                value.ReleaseUnused();
                queue.DecrementCount();
                return new(true);
            }
        }
    }

    internal void ExitReadUpgrade(ref SpinWait wait)
    {
        State prev;
        var state = _state.Decrement();
        Debug.Assert(!state.IsWrite);
        while (true)
        {
            Debug.Assert(state.IsUpgrade && !state.IsWrite);
            prev = _state.Cx(state, state.Upgrade(false));
            if (prev == state)
            {
                break;
            }
            var reloadState = wait.NextSpinWillYield;
            wait.SpinOnce();
            state = reloadState ? _state.Read() : prev;
        }

        if (state.ReadCount == 0 && state.IsQueueChanged)
        {
            prev = _state.Cx(state, State.Write);
            if (prev == state)
            {
                ConsumeQueueUnderWrite(ref wait);
            }
        }
    }

}
