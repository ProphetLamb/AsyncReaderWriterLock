using System.Diagnostics;
using System.Threading.Tasks.Sources;

namespace System.Threading;

/// <summary>
/// Configures the behaviour of the <see cref="AsyncReaderWriterLock"/>.
/// </summary>
public sealed record AsyncReaderWriterLockOptions
{
    /// <summary>
    /// Configures the <see cref="IValueTaskSource"/> to run continuations asynchronously.
    /// <br/>
    /// Acquiring and releasing the lock is faster with this option set to <c>false</c>.
    /// </summary>
    public bool RunContinuationsAsynchronously { get; set; }

    /// <summary>
    /// Confiugres the lock to prefer queued read locks over write locks.
    /// <br/>
    /// Default is fair: first come first served.
    /// <br/>
    /// Mutaually exclusive with <see cref="ElevateWriteQueue"/>
    /// </summary>
    public bool ElevateReadQueue { get; set; }

    /// <summary>
    /// Confiugres the lock to prefer queued write locks over read locks.
    /// <br/>
    /// Default is fair: first come first served.
    /// <br/>
    /// Mutaually exclusive with <see cref="ElevateReadQueue"/>
    /// </summary>
    public bool ElevateWriteQueue { get; set; }

    /// <summary>
    /// The interval in which to vacuum the queue.
    /// <br/>
    /// Disable vacuuming the queue by setting this to <c>null<c/>.
    /// </summary>
    public TimeSpan? VacuumQueueInterval { get; set; } = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// The time provider initializing the vacuum queue <see cref="ITimer"/>.
    /// </summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    internal AsyncReaderWriterLock.Options ToOptions()
    {
        var elevated = (ElevateReadQueue, ElevateWriteQueue) switch
        {
            (true, false) => AsyncReaderWriterLock.ElevatedKind.Read,
            (false, true) => AsyncReaderWriterLock.ElevatedKind.Write,
            (false, false) => AsyncReaderWriterLock.ElevatedKind.Fair,
            _ => throw new InvalidOperationException($"At most one of {nameof(ElevateReadQueue)} and {nameof(ElevateWriteQueue)} can be true"),
        };

        if (VacuumQueueInterval is { } interval && interval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"The {nameof(VacuumQueueInterval)} must be null, or greater than zero.");
        }

        if (TimeProvider is null)
        {
            throw new InvalidOperationException($"The {nameof(TimeProvider)} must not be null");
        }

        return new(RunContinuationsAsynchronously, elevated, VacuumQueueInterval, TimeProvider);
    }

    public static AsyncReaderWriterLockOptions Default { get; } = new();
}

/// <summary>
/// Concurrent safe synchronization primitive allowing discrete read, write states, as well as upgrading from read to write state.
/// </summary>
/// <remarks>
/// Available states:
/// <list type="bullet">
/// <item>Free: No reader, or writer holds the lock. Transfers into read, write, or read upgradable.</item>
/// <item>Read: Up to <see cref="MaxReadCount"/> readers can concurrently hold the lock. Transfers into free, or read upgradable.</item>
/// <item>Write: Exactly one writer can hold the lock. Transfers into free, read, or read upgradable.</item>
/// <item>Read upgradable: At most one read upgradable can hold the lock, simultaniously up to <see cref="MaxReadCount"/> readers can concurrently hold the lock. Transfers into read, or free.</item>
/// <item>Write upgraded: Exactly one upgraded writer can hold the lock. Upgrade from read upgradable when only one reader holds the lock. Transfers into read upgradable.</item>
/// </list>
/// </remarks>
[DebuggerDisplay("{ToString(),nq}"), DebuggerTypeProxy(typeof(DebugView))]
public sealed partial class AsyncReaderWriterLock : IDisposable
{
    private readonly Options _options;
    private readonly ITimer? _cleanQueueStateTimer;
    private StateManager _state;
    private Queue _queue;
    private Queue _elevatedQueue;

    /// <summary>
    /// The optimistic maximum number of readers the lock can hold concurrently.
    /// </summary>
    public static readonly nuint MaxReadCount = State.MaxReadCount;

    /// <inheritdoc cref="AsyncReaderWriterLock"/>
    public AsyncReaderWriterLock(AsyncReaderWriterLockOptions options)
    {
        _options = options.ToOptions();
        _cleanQueueStateTimer = CreateTimer(_options);
    }

    public AsyncReaderWriterLock()
        : this(AsyncReaderWriterLockOptions.Default)
    {
    }

    private ITimer? CreateTimer(Options options)
    {
        return options.VacuumQueueInterval is { } interval ? TimeProvider.System.CreateTimer(OnCleanQueueStateTimerElapsed, this, interval, interval) : null;
    }

    private ref Queue ReadQueue => ref (_options.Elevated == ElevatedKind.Read ? ref _elevatedQueue : ref _queue);

    private ref Queue WriteQueue => ref (_options.Elevated == ElevatedKind.Write ? ref _elevatedQueue : ref _queue);

    internal State GetState() => _state.Read();

    private static void OnCleanQueueStateTimerElapsed(object? state)
    {
        var self = (AsyncReaderWriterLock)state!;
        self._elevatedQueue.CleanQueueState();
        self._queue.CleanQueueState();
    }

    /// <summary>
    /// Cancels all waiting lock requests with <see cref="OperationCanceledException"/>. Prevents future lock requests.
    /// </summary>
    public void Dispose()
    {
        _cleanQueueStateTimer?.Dispose();
        _elevatedQueue.Dispose();
        _queue.Dispose();
        GC.SuppressFinalize(this);
    }

    public override string ToString()
    {
        return _state.ToString();
    }

    internal readonly struct Options(bool runContinuationsAsynchronously, ElevatedKind elevated, TimeSpan? vacuumQueueInterval, TimeProvider timeProvider)
    {
        public readonly bool RunContinuationsAsynchronously = runContinuationsAsynchronously;
        public readonly ElevatedKind Elevated = elevated;
        public readonly TimeSpan? VacuumQueueInterval = vacuumQueueInterval;
        public readonly TimeProvider TimeProvider = timeProvider;
    }

    internal enum ElevatedKind
    {
        Fair,
        Read,
        Write
    }

    private readonly struct DebugView(AsyncReaderWriterLock rwLock)
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public RequestNode[] Items => rwLock._elevatedQueue.ToList().Concat(rwLock._queue.ToList()).ToArray();
    }
}
