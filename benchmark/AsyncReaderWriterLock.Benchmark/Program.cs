// See https://aka.ms/new-console-template for more information
using System.Threading.Tests.Fuzzing;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

BenchmarkRunner.Run<LockBenchmarks>();

public class LockBenchmarks
{
    [Params(1000000)]
    public int Iterations { get; set; }
    [Params(1.00, 0.99, 0.90, 0.70, 0.50, 0.30, 0.10, 0.01, 0.00)]
    public double ReadWeight { get; set; }
    [Params(0.00, 0.50, 1.00)]
    public double ReadUpgradableWeight { get; set; }
    [Params(0.00, 0.50, 1.00)]
    public double WriteUpgradedWeight { get; set; }

    public FuzzOptions FuzzOptions => new(ReadWeight, 1.00 - ReadWeight, ReadUpgradableWeight, WriteUpgradedWeight);

    [Benchmark]
    public async Task ARWLAsync()
    {
        AsyncReaderWriterLock rwLock = new();
        await FuzzHelper.FuzzExecuteAsync(rwLock, FuzzAsyncReaderWriterLockStates.CreateAsync(FuzzOptions), new(unchecked((int)0xbeefcace)), Iterations);
    }

    [Benchmark]
    public async Task ARWLSync()
    {
        AsyncReaderWriterLock rwLock = new();
        await FuzzHelper.FuzzExecuteSync(rwLock, FuzzAsyncReaderWriterLockStates.CreateSync(FuzzOptions), new(unchecked((int)0xbeefcace)), Iterations);
    }
}