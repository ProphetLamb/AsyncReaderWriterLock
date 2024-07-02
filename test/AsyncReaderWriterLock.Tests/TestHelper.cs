namespace System.Threading.Tests;

public static class TestHelper
{
    internal static IEnumerable<AsyncReaderWriterLockOptions> GetOptionCombinations()
    {
        yield return new();
        yield return new() { ElevateReadQueue = true, };
        yield return new() { ElevateWriteQueue = true, };
        yield return new() { RunContinuationsAsynchronously = true, };
        yield return new() { ElevateReadQueue = true, RunContinuationsAsynchronously = true, };
        yield return new() { ElevateWriteQueue = true, RunContinuationsAsynchronously = true, };
        yield return new() { VacuumQueueInterval = null, };
        yield return new() { ElevateReadQueue = true, VacuumQueueInterval = null, };
        yield return new() { ElevateWriteQueue = true, VacuumQueueInterval = null, };
        yield return new() { RunContinuationsAsynchronously = true, VacuumQueueInterval = null, };
        yield return new() { ElevateReadQueue = true, RunContinuationsAsynchronously = true, VacuumQueueInterval = null, };
        yield return new() { ElevateWriteQueue = true, RunContinuationsAsynchronously = true, VacuumQueueInterval = null, };
    }

    internal static IEnumerable<(TInner Inner, TOuter Outer)> CombineIntersection<TInner, TOuter>(this IEnumerable<TInner> innerSeq, IEnumerable<TOuter> outerSeq)
    {
        if (innerSeq.TryGetNonEnumeratedCount(out _))
        {
            foreach (var outer in outerSeq)
            {
                foreach (var inner in innerSeq)
                {
                    yield return (inner, outer);
                }
            }
            yield break;
        }

        if (outerSeq.TryGetNonEnumeratedCount(out _))
        {
            foreach (var inner in innerSeq)
            {
                foreach (var outer in outerSeq)
                {
                    yield return (inner, outer);
                }
            }
            yield break;
        }

        var outerCol = outerSeq.ToList();
        foreach (var inner in innerSeq)
        {
            foreach (var outer in outerCol)
            {
                yield return (inner, outer);
            }
        }
    }
}
