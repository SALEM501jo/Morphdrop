using Microsoft.Win32;
using ShiftDrop.Actions;
using ShiftDrop.Tray;

namespace ShiftDrop.Tests;

public sealed class AutoStartTests : IDisposable
{
    // A private key, so the test never touches the real Run key.
    private const string TestKey = @"Software\ShiftDrop.Tests\Run";

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\ShiftDrop.Tests", throwOnMissingSubKey: false);

    [Fact]
    public void Toggling_WritesAndRemovesTheRunValue()
    {
        var autoStart = new AutoStart(@"C:\Apps\ShiftDrop\ShiftDrop.exe", TestKey);
        Assert.False(autoStart.IsEnabled);

        autoStart.SetEnabled(true);
        Assert.True(autoStart.IsEnabled);
        using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(TestKey))
            Assert.Equal("\"C:\\Apps\\ShiftDrop\\ShiftDrop.exe\"", key?.GetValue("ShiftDrop"));

        autoStart.SetEnabled(false);
        Assert.False(autoStart.IsEnabled);
        autoStart.SetEnabled(false); // turning it off twice is fine
    }

    [Fact]
    public void AnEntryForAnotherCopy_DoesNotCountAsEnabled()
    {
        new AutoStart(@"C:\Old\ShiftDrop.exe", TestKey).SetEnabled(true);

        Assert.False(new AutoStart(@"C:\New\ShiftDrop.exe", TestKey).IsEnabled);
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
        var runner = new ActionRunner(slowAfter: TimeSpan.FromMilliseconds(400));
        bool working = false;
        ActionResult? result = null;
        runner.Working += (_, _) => working = true;
        runner.Completed += r => result = r;

        await runner.Run(new SleepAction(TimeSpan.Zero), new[] { _file })!;
        await Task.Delay(700); // past the threshold: a stray notice would have fired by now

        Assert.False(working);
        Assert.NotNull(result);
        Assert.Single(result.Outputs);
    }
}
