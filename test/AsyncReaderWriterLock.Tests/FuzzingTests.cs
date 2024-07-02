using System.Diagnostics;
using NUnit.Framework.Internal;
namespace System.Threading.Tests;

public sealed class FuzzingTests
{
    private static IEnumerable<FuzzOptions> GetFuzzOptions()
    {
        double[] readWeights = [0.80, 0.79, 0.70, 0.50, 0.25, 0.00];
        double[] writeWeights = [0.00, 0.01, 0.10, 0.25];
        double[] readUpgradeableWeights = [0.00, 0.10, 0.25];
        double[] writeUpgradedWeights = [0.00, 0.10, 0.25];
        return readWeights
            .CombineIntersection(writeWeights)
            .CombineIntersection(readUpgradeableWeights)
            .CombineIntersection(writeUpgradedWeights)
            .Select(t => new FuzzOptions(t.Inner.Inner.Inner, t.Inner.Inner.Outer, t.Inner.Outer, t.Outer))
            .Where(o => o.ReadWeight + o.WriteWeight + o.ReadUpgradableWeight + o.WriteUpgradedWeight > double.Epsilon); // remove cases with no read or write weight
    }

    private static IEnumerable<TestCaseData> GetParameters()
    {
        return TestHelper.GetOptionCombinations()
            .CombineIntersection(GetFuzzOptions())
            .Select(t => new TestCaseData(t.Inner, t.Outer));
    }

    [Test, TestCaseSource(nameof(GetParameters))]
    public async Task FuzzAsyncReaderWriterLock(AsyncReaderWriterLockOptions lockOptions, FuzzOptions fuzzOptions)
    {
        AsyncReaderWriterLock rwLock = new(lockOptions);
        await FuzzHelper.FuzzExecuteAsync(rwLock, FuzzAsyncReaderWriterLockStates.CreateAsync(fuzzOptions), new Random(unchecked((int)0xbeefcace)), 100000);
    }
}