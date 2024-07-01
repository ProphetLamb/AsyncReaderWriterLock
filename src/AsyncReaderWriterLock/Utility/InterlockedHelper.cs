using System.Runtime.CompilerServices;

namespace System.Threading;

internal static class InterlockedHelper
{
    // Fucking poc api crap fuck why the hell dont we have pointer sized implementations for fucking Interlocked Increment & Add?
    // Well lets hope the bloody jit is fucking smart enought to compile the `IntPtr.Size == 8` branch away.
    // Moving from AnyCPU to x64/x86 target is unacceptable. The TARGET_64BIT preprocessor is unavailable for the AnyCPU target.

    /// <inheritdoc cref="Interlocked.Increment(ref ulong)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static nuint Increment(ref nuint location)
    {
        return (nuint)(IntPtr.Size == 8 ? Interlocked.Increment(ref Unsafe.As<nuint, ulong>(ref location)) : Interlocked.Increment(ref Unsafe.As<nuint, uint>(ref location)));
    }

    /// <inheritdoc cref="Interlocked.Decrement(ref ulong)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static nuint Decrement(ref nuint location)
    {
        return (nuint)(IntPtr.Size == 8 ? Interlocked.Decrement(ref Unsafe.As<nuint, ulong>(ref location)) : Interlocked.Decrement(ref Unsafe.As<nuint, uint>(ref location)));
    }

    /// <inheritdoc cref="Interlocked.Add(ref ulong, ulong)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static nuint Add(ref nuint location, nuint value)
    {
        return (nuint)(IntPtr.Size == 8 ? Interlocked.Add(ref Unsafe.As<nuint, ulong>(ref location), value) : Interlocked.Add(ref Unsafe.As<nuint, uint>(ref location), unchecked((uint)value)));
    }
}
