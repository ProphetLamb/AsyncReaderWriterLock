using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System.Threading;

public partial class AsyncReaderWriterLock
{
    [DebuggerDisplay("{ToString(),nq}"), DebuggerTypeProxy(typeof(DebugView))]
    private ref struct DequeueState
    {
        public RequestNode? ReadHead;
        public RequestNode? ReadTail;
        public nuint ReadCount;
        public bool IsUpgrade;
        public bool IsQueueRemaining;

        public readonly bool IsReadEmpty => ReadTail is null;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryEnqueueRead(RequestNode value)
        {
            Debug.Assert(value.QueueNext is null);
            if (value.IsWrite || IsUpgrade && value.IsUpgrade)
            {
                return false;
            }

            ReadCount++;

            IsUpgrade |= value.IsUpgrade;
            if (ReadTail is null)
            {
                Debug.Assert(ReadHead is not null);
                ReadHead = ReadTail = value;
            }
            else
            {
                Debug.Assert(ReadTail.QueueNext is null);
                ReadTail = ReadTail.QueueNext = value;
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Dequeue([MaybeNullWhen(false)] out RequestNode value)
        {
            value = ReadHead;
            if (ReadHead == ReadTail)
            {
                ReadHead = ReadTail = null;
            }
            else
            {
                ReadHead = value!.QueueNext;
                value!.QueueNext = null;
            }

            ReadCount -= value is not null ? (nuint)1 : 0;
            return value is not null;
        }

        public readonly List<RequestNode> ToList()
        {
            List<RequestNode> values = [];
            for (var value = ReadHead; value is not null; value = value.QueueNext)
            {
                values.Add(value);
            }
            return values;
        }

        private readonly ref struct DebugView(DequeueState queue)
        {
            private readonly DequeueState _queue = queue;

            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public RequestNode[] Items => _queue.ToList().ToArray();
        }

        public override string ToString()
        {
            return $"{{{nameof(ReadCount)}={ReadCount}, {nameof(IsUpgrade)}={IsUpgrade}, {nameof(IsQueueRemaining)}={IsQueueRemaining}}}";
        }
    }
}
