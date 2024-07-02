using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace System.Threading;

public partial class AsyncReaderWriterLock
{
    [DebuggerDisplay("{ToString(),nq}")]
    private struct StateManager
    {
        private nuint _value;

        public readonly State Value => _value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public State Read() => Volatile.Read(ref _value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(State state) => Volatile.Write(ref _value, state.Value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public State Increment() => InterlockedHelper.Increment(ref _value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public State Decrement() => InterlockedHelper.Decrement(ref _value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public State Subtract(nint count) => InterlockedHelper.Add(ref _value, unchecked((nuint)(-count)));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public State Cx(State comparand, State value) => Interlocked.CompareExchange(ref _value, value.Value, comparand.Value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Cas(State comparand, State value) => Interlocked.CompareExchange(ref _value, value.Value, comparand.Value) == comparand.Value;

        public override readonly string ToString()
        {
            return Value.ToString();
        }
    }

    [DebuggerDisplay("{ToString(),nq}")]
    internal readonly ref struct State(nuint value)
    {
        // 31/63 =============================== 0
        //       |||___________________________|
        //       ||          ReadCount
        //       |Upgrade
        //  QueueChanged
        private static readonly nuint FlagQueueChanged = unchecked(~(nuint.MaxValue >> 1));
        private static readonly nuint FlagUpgrade = unchecked(~nuint.RotateRight(nuint.MaxValue >> 1, 1));
        private static readonly nuint FlagWrite = unchecked(nuint.MaxValue >> 2);

        public static readonly nuint MaxReadCount = FlagWrite - 1;
        public static State Write => FlagWrite;

        public readonly nuint Value = value;

        public bool IsQueueChanged => (Value & FlagQueueChanged) == FlagQueueChanged;
        public bool IsUpgrade => (Value & FlagUpgrade) == FlagUpgrade;
        public bool IsWrite => (Value & FlagWrite) == FlagWrite;
        public nuint ReadCount => (Value & FlagWrite) == FlagWrite ? 0 : (Value & FlagWrite);

        /// <summary>Equivalent to <c>!IsQueueChanged && !IsUpgrade && ReadCount == 0</c></summary>
        public bool CanEnterWrite => Value == default;

        /// <summary>Equivalent to <c>IsUpgrade && ReadCount <= 1</c></summary>
        public bool CanEnterWriteUpgrade => (Value & ~(nuint)1) == FlagUpgrade;

        /// <summary>Equaivalent to <c>!IsWrite && !IsQueueChanged && !IsUpgrade</c></summary>
        public bool CanEnterReadUpgrade => Value <= MaxReadCount;

        /// <summary>Equaivalent to <c>!IsWrite && !IsQueueChanged && ReadCount < MaxReadCount</c></summary>
        public bool CanEnterRead => (Value & ~FlagUpgrade) < MaxReadCount;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public State AddRead()
        {
            State value = Value + 1;
            Debug.Assert(!value.IsWrite);
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public State QueueChanged(bool isSet)
        {
            return isSet ? (Value | FlagQueueChanged) : (Value & ~FlagQueueChanged);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public State Upgrade(bool isSet)
        {
            return isSet ? (Value | FlagUpgrade) : (Value & ~FlagUpgrade);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static State FromRead(nuint count)
        {
            Debug.Assert(count < FlagWrite);
            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator State(nuint value) => new(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(State lhs, State rhs) => lhs.Value == rhs.Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(State lhs, State rhs) => lhs.Value != rhs.Value;

        public bool Equals(State state)
        {
            return Value == state.Value;
        }

#pragma warning disable CS0809, CS8765
        [Obsolete]
        public override bool Equals(object obj)
        {
            return false;
        }
#pragma warning restore CS0809, CS8765

        public override int GetHashCode()
        {
            return unchecked((int)Value);
        }

        public override string ToString()
        {
            StringBuilder sb = new();
            sb.Append('{');
            sb.Append(nameof(QueueChanged)).Append('=').Append(IsQueueChanged);
            sb.Append(", ").Append(nameof(IsUpgrade)).Append('=').Append(IsUpgrade);
            if (IsWrite)
            {
                sb.Append(", ").Append(nameof(IsWrite)).Append('=').Append(true);
            }
            else
            {
                sb.Append(", ").Append(nameof(ReadCount)).Append('=').Append(ReadCount);
            }
            sb.Append('}');
            return sb.ToString();
        }
    }
}
