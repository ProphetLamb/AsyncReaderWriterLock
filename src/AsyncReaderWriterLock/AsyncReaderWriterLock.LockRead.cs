using System.Diagnostics;

namespace System.Threading;

public partial class AsyncReaderWriterLock
{
    internal bool TryEnterRead(ref SpinWait wait, bool readQueued)
    {
        var state = _state.Read();
        while (true)
        {
            State prev;
            if (state.CanEnterRead)
            {
                prev = _state.Cx(state, state.AddRead());
                if (prev == state)
                {
                    return true;
                }
            }
            else if (readQueued)
            {
                if (!state.IsWrite || state.IsQueueChanged)
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

    internal ValueTask<bool> EnterRead(long timeoutMilliseconds, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMilliseconds, -1);
        SpinWait wait = new();
        if (TryEnterRead(ref wait, false))
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

        var value = RequestNode.CreateRead(timeoutMilliseconds, cancellationToken);
        ref var queue = ref ReadQueue;

        queue.IncrementCount();
        while (true)
        {
            if (queue.TryEnqueue(value))
            {
                if (!TryEnterRead(ref wait, true))
                {
                    return value.Task;
                }

                value.TryReleaseBeforeAcquired();

                return new(true);
            }

            var tryReacquire = wait.NextSpinWillYield;
            wait.SpinOnce();
            if (tryReacquire && TryEnterRead(ref wait, false))
            {
                value.ReleaseUnused();
                queue.DecrementCount();
                return new(true);
            }
        }
    }

    internal void ExitRead(ref SpinWait wait, int count)
    {
        var state = _state.Subtract(count);
        Debug.Assert(!state.IsWrite);

        if (state.ReadCount == 0 && state.IsQueueChanged)
        {
            var prev = _state.Cx(state, State.Write);
            if (prev == state)
            {
                ConsumeQueueUnderWrite(ref wait);
            }
        }
    }
}
