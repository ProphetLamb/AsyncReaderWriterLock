namespace System.Threading;

internal readonly struct TimeoutState(long startMs, long timeoutMilliseconds)
{
    public readonly long StartMs = startMs;
    public readonly long timeoutMilliseconds = timeoutMilliseconds;

    public static TimeoutState Create(long timeoutMillis)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMillis, Timeout.Infinite);
        return new(timeoutMillis <= 0 ? 0 : Environment.TickCount64, timeoutMillis);
    }

    public bool CheckElapsed()
    {
        return timeoutMilliseconds switch
        {
            0 => true,
            -1 => false,
            _ => unchecked(Environment.TickCount64 >= StartMs + timeoutMilliseconds),
        };
    }
}
