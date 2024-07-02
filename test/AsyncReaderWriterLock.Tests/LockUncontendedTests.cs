using System.Diagnostics;

namespace System.Threading.Tests;

public sealed class LockUncontendedTests
{
    private static IEnumerable<TestCaseData> GetParameters()
    {
        return TestHelper.GetOptionCombinations().Select(o => new TestCaseData(new AsyncReaderWriterLock(o)));
    }

    [Test, TestCaseSource(nameof(GetParameters))]
    public async Task LockReadAsync(AsyncReaderWriterLock rwLock)
    {
        using (await rwLock.UsingReadAsync())
        {
            Debug.Assert(rwLock.GetState() is { ReadCount: 1 });
        }
        Debug.Assert(rwLock.GetState() == default);
    }

    [Test, TestCaseSource(nameof(GetParameters))]
    public void LockReadSync(AsyncReaderWriterLock rwLock)
    {
        using (rwLock.UsingRead())
        {
            Debug.Assert(rwLock.GetState() is { ReadCount: 1 });
        }
        Debug.Assert(rwLock.GetState() == default);
    }

    [Test, TestCaseSource(nameof(GetParameters))]
    public async Task LockReadUpgradableAsync(AsyncReaderWriterLock rwLock)
    {
        using (await rwLock.UsingReadUpgradableAsync())
        {
            Debug.Assert(rwLock.GetState() is { ReadCount: 1, IsUpgrade: true });
        }
        Debug.Assert(rwLock.GetState() == default);
    }

    [Test, TestCaseSource(nameof(GetParameters))]
    public void LockReadUpgradableSync(AsyncReaderWriterLock rwLock)
    {
        using (rwLock.UsingReadUpgradable())
        {
            Debug.Assert(rwLock.GetState() is { ReadCount: 1, IsUpgrade: true });
        }
        Debug.Assert(rwLock.GetState() == default);
    }

    [Test, TestCaseSource(nameof(GetParameters))]
    public async Task LockWriteAsync(AsyncReaderWriterLock rwLock)
    {
        using (await rwLock.UsingWriteAsync())
        {
            Debug.Assert(rwLock.GetState() is { IsWrite: true });
        }
        Debug.Assert(rwLock.GetState().ReadCount == default);
    }

    [Test, TestCaseSource(nameof(GetParameters))]
    public void LockWriteSync(AsyncReaderWriterLock rwLock)
    {
        using (rwLock.UsingWrite())
        {
            Debug.Assert(rwLock.GetState() is { IsWrite: true });
        }
        Debug.Assert(rwLock.GetState().ReadCount == default);
    }

    [Test, TestCaseSource(nameof(GetParameters))]
    public async Task LockWriteUpgradedAsync(AsyncReaderWriterLock rwLock)
    {
        using (var l = await rwLock.UsingReadUpgradableAsync())
        {
            Debug.Assert(rwLock.GetState() is { ReadCount: 1, IsUpgrade: true });
            using (await l.UsingWriteUpgradeAsync())
            {
                Debug.Assert(rwLock.GetState() is { IsWrite: true, IsUpgrade: true });
            }
            Debug.Assert(rwLock.GetState() is { ReadCount: 1, IsUpgrade: true });
        }
        Debug.Assert(rwLock.GetState().ReadCount == default);
    }

    [Test, TestCaseSource(nameof(GetParameters))]
    public void LockWriteUpgradedSync(AsyncReaderWriterLock rwLock)
    {
        using (var l = rwLock.UsingReadUpgradable())
        {
            Debug.Assert(rwLock.GetState() is { ReadCount: 1, IsUpgrade: true });
            using (l.UsingWriteUpgrade())
            {
                Debug.Assert(rwLock.GetState() is { IsWrite: true, IsUpgrade: true });
            }
            Debug.Assert(rwLock.GetState() is { ReadCount: 1, IsUpgrade: true });
        }
        Debug.Assert(rwLock.GetState().ReadCount == default);
    }
}
