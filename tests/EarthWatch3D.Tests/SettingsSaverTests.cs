using EarthWatch3D.Core.Storage;

namespace EarthWatch3D.Tests;

public class SettingsSaverTests
{
    private static void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) Thread.Sleep(10);
        Assert.True(condition());
    }

    [Fact]
    public void SavesAreDebouncedAndRunOffTheCallingThread()
    {
        var inner = new FakeSettingsStore();
        using var saver = new SettingsSaver(inner, TimeSpan.FromMilliseconds(100));
        saver.Save(new AppSettings { NightStrength = 0.1 });
        saver.Save(new AppSettings { NightStrength = 0.2 });
        saver.Save(new AppSettings { NightStrength = 0.3 });
        Assert.Equal(0, inner.SaveCount);               // nothing written synchronously
        Assert.Equal(0.3, saver.Load().NightStrength);  // but Load sees the pending value

        WaitFor(() => inner.SaveCount >= 1);
        Thread.Sleep(300);
        Assert.Equal(1, inner.SaveCount);
        Assert.Equal(0.3, inner.Saved[0].NightStrength);
        Assert.NotEqual(Environment.CurrentManagedThreadId, inner.SaveThreadIds[0]);
    }

    [Fact]
    public void EachSaveRestartsTheDelay()
    {
        var inner = new FakeSettingsStore();
        using var saver = new SettingsSaver(inner, TimeSpan.FromMilliseconds(400));
        for (int i = 0; i < 5; i++)
        {
            saver.Save(new AppSettings { QuakeWindowDays = i });
            Thread.Sleep(100);
        }
        Assert.Equal(0, inner.SaveCount);   // 500 ms in total, but never 400 ms after the last change
        WaitFor(() => inner.SaveCount >= 1);
        Assert.Equal(1, inner.SaveCount);
        Assert.Equal(4, inner.Saved[^1].QuakeWindowDays);
    }

    [Fact]
    public void FlushWritesPendingOnceAndLoadComesFromInner()
    {
        var inner = new FakeSettingsStore(new AppSettings { MinMagnitude = 5 });
        using var saver = new SettingsSaver(inner, TimeSpan.FromSeconds(30));
        Assert.Equal(5, saver.Load().MinMagnitude);
        saver.Flush();
        Assert.Equal(0, inner.SaveCount);   // nothing pending
        saver.Save(new AppSettings { MinMagnitude = 6 });
        saver.Flush();
        Assert.Equal(1, inner.SaveCount);
        Assert.Equal(6, inner.Saved[0].MinMagnitude);
        saver.Flush();
        Assert.Equal(1, inner.SaveCount);
    }
}
