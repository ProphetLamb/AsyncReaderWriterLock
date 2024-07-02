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
            Assert.That(rwLock.GetState() is { ReadCount: 1 });
        }
        Assert.That(rwLock.GetState() == default);
    }

    [Test, TestCaseSource(nameof(GetParameters))]
    public void LockReadSync(AsyncReaderWriterLock rwLock)
    {
        using (rwLock.UsingRead())
        {
            Assert.That(rwLock.GetState() is { ReadCount: 1 });
        }
        Assert.That(rwLock.GetState() == default);
    }

    [Test, TestCaseSource(nameof(GetParameters))]
    public async Task LockReadUpgradableAsync(AsyncReaderWriterLock rwLock)
    {
        using (await rwLock.UsingReadUpgradableAsync())
        {
            Assert.That(rwLock.GetState() is { ReadCount: 1, IsUpgrade: true });
        }
        Assert.That(rwLock.GetState() == default);
    }

    [Test, TestCaseSource(nameof(GetParameters))]
    public void LockReadUpgradableSync(AsyncReaderWriterLock rwLock)
    {
        using (rwLock.UsingReadUpgradable())
        {
            Assert.That(rwLock.GetState() is { ReadCount: 1, IsUpgrade: true });
        }
        Assert.That(rwLock.GetState() == default);
    }

    [Test, TestCaseSource(nameof(GetParameters))]
    public async Task LockWriteAsync(AsyncReaderWriterLock rwLock)
    {
        using (await rwLock.UsingWriteAsync())
        {
            Assert.That(rwLock.GetState() is { IsWrite: true });
        }
        Assert.That(rwLock.GetState().ReadCount == default);
    }

    [Test, TestCaseSource(nameof(GetParameters))]
    public void LockWriteSync(AsyncReaderWriterLock rwLock)
    {
        using (rwLock.UsingWrite())
        {
            Assert.That(rwLock.GetState() is { IsWrite: true });
        }
        Assert.That(rwLock.GetState().ReadCount == default);
    }

    [Test, TestCaseSource(nameof(GetParameters))]
    public async Task LockWriteUpgradedAsync(AsyncReaderWriterLock rwLock)
    {
        using (var l = await rwLock.UsingReadUpgradableAsync())
        {
            Assert.That(rwLock.GetState() is { ReadCount: 1, IsUpgrade: true });
            using (await l.UsingWriteUpgradeAsync())
            {
                Assert.That(rwLock.GetState() is { IsWrite: true, IsUpgrade: true });
            }
            Assert.That(rwLock.GetState() is { ReadCount: 1, IsUpgrade: true });
        }
        Assert.That(rwLock.GetState().ReadCount == default);
    }

    [Test, TestCaseSource(nameof(GetParameters))]
    public void LockWriteUpgradedSync(AsyncReaderWriterLock rwLock)
    {
        using (var l = rwLock.UsingReadUpgradable())
        {
            Assert.That(rwLock.GetState() is { ReadCount: 1, IsUpgrade: true });
            using (l.UsingWriteUpgrade())
            {
                Assert.That(rwLock.GetState() is { IsWrite: true, IsUpgrade: true });
            }
            Assert.That(rwLock.GetState() is { ReadCount: 1, IsUpgrade: true });
        }
        Assert.That(rwLock.GetState().ReadCount == default);
    }
}
