using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace AsyncReaderWriterLock;

internal static class ThrowHelper
{
    [MethodImpl(MethodImplOptions.NoInlining), DebuggerStepThrough, DoesNotReturn]
    public static void ThrowTimeout(string? message, Exception? innerException)
    {
        throw new TimeoutException(message, innerException);
    }
    [MethodImpl(MethodImplOptions.NoInlining), DebuggerStepThrough, DoesNotReturn]
    public static void ThrowInvalidOperation(string? message, Exception? innerException)
    {
        throw new InvalidOperationException(message, innerException);
    }
}
