using Microsoft.Win32;
using Morphdrop.Actions;
using Morphdrop.Tray;

namespace Morphdrop.Tests;

public sealed class AutoStartTests : IDisposable
{
    // A private key, so the test never touches the real Run key.
    private const string TestKey = @"Software\Morphdrop.Tests\Run";

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\Morphdrop.Tests", throwOnMissingSubKey: false);

    [Fact]
    public void Toggling_WritesAndRemovesTheRunValue()
    {
        var autoStart = new AutoStart(@"C:\Apps\Morphdrop\Morphdrop.exe", TestKey);
        Assert.False(autoStart.IsEnabled);

        autoStart.SetEnabled(true);
        Assert.True(autoStart.IsEnabled);
        using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(TestKey))
            Assert.Equal("\"C:\\Apps\\Morphdrop\\Morphdrop.exe\"", key?.GetValue("Morphdrop"));

        autoStart.SetEnabled(false);
        Assert.False(autoStart.IsEnabled);
        autoStart.SetEnabled(false); // turning it off twice is fine
    }

    [Fact]
    public void AnEntryForAnotherCopy_DoesNotCountAsEnabled()
    {
        new AutoStart(@"C:\Old\Morphdrop.exe", TestKey).SetEnabled(true);

        Assert.False(new AutoStart(@"C:\New\Morphdrop.exe", TestKey).IsEnabled);
    }
}

public sealed class ActionRunnerTests : IDisposable
{
    private readonly string _file = Path.GetTempFileName();

    public void Dispose() => File.Delete(_file);

    private sealed class SleepAction(TimeSpan duration) : IFileAction
    {
        public string Label => "Sleep";
        public string? Glyph => null;
        public string Description => "Sleep";
        public bool AppliesTo(string path) => true;

        public string Run(string inputPath)
        {
            Thread.Sleep(duration);
            return inputPath;
        }
    }

    [Fact]
    public async Task ASlowJob_SaysItIsWorking()
    {
        var runner = new ActionRunner(slowAfter: TimeSpan.FromMilliseconds(100));
        var working = new TaskCompletionSource<IReadOnlyList<string>>();
        runner.Working += (_, inputs) => working.TrySetResult(inputs);

        Task job = runner.Run(new SleepAction(TimeSpan.FromMilliseconds(600)), new[] { _file })!;

        Assert.Equal(new[] { _file }, await working.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await job;
    }

    [Fact]
    public async Task AQuickJob_StaysQuiet()
    {
        // A generous threshold: when other tests keep the CPU busy, the job can wait a while just to start.
        var runner = new ActionRunner(slowAfter: TimeSpan.FromSeconds(1.5));
        bool working = false;
        ActionResult? result = null;
        runner.Working += (_, _) => working = true;
        runner.Completed += r => result = r;

        await runner.Run(new SleepAction(TimeSpan.Zero), new[] { _file })!;
        await Task.Delay(TimeSpan.FromSeconds(2)); // past the threshold: a stray notice would have fired by now

        Assert.False(working);
        Assert.NotNull(result);
        Assert.Single(result.Outputs);
    }
}
