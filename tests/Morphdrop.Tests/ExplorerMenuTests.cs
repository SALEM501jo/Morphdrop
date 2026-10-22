using System.Windows.Threading;
using Microsoft.Win32;
using Morphdrop.Shell;

namespace Morphdrop.Tests;

public sealed class ExplorerMenuTests : IDisposable
{
    // A scratch key, never the real HKEY_CURRENT_USER\Software\Classes.
    private readonly string _scratch = $@"Software\MorphdropTests\{Guid.NewGuid():N}";
    private readonly string _folder = Directory.CreateTempSubdirectory("morphdrop-menu-").FullName;

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\MorphdropTests", throwOnMissingSubKey: false);
        TestFolder.Delete(_folder);
    }

    [Fact]
    public void TheMenuEntry_IsAddedForFilesAndFolders_AndRemovedAgain()
    {
        var menu = new ExplorerMenu(Registry.CurrentUser, _scratch);
        const string exe = @"C:\Apps\Morphdrop\Morphdrop.exe";

        menu.SetEnabled(true, exe);

        Assert.True(menu.IsInstalledFor(exe));
        foreach (string target in new[] { @"*\shell", @"Directory\shell" })
        {
            using RegistryKey? command = Registry.CurrentUser.OpenSubKey($@"{_scratch}\{target}\Morphdrop\command");
            Assert.Equal($"\"{exe}\" --menu \"%1\"", command?.GetValue(null));
        }

        Assert.False(menu.IsInstalledFor(@"D:\Other\Morphdrop.exe"), "an entry for an old location gets rewritten");

        menu.SetEnabled(false, exe);

        Assert.False(menu.IsInstalledFor(exe));
        Assert.Null(Registry.CurrentUser.OpenSubKey($@"{_scratch}\Directory\shell\Morphdrop"));
    }

    [Fact]
    public void FilesSentBySeveralMenuProcesses_ArriveAsOneRequest()
    {
        string a = Path.Combine(_folder, "a.png"), b = Path.Combine(_folder, "b.png");
        File.WriteAllText(a, "");
        File.WriteAllText(b, "");
        string pipe = $"Morphdrop.Tests.{Guid.NewGuid():N}";
        string[]? received = null;

        Sta.Run(() =>
        {
            var requests = new MenuRequests(Dispatcher.CurrentDispatcher, pipe);
            var frame = new DispatcherFrame();
            requests.Requested += files =>
            {
                received = files;
                frame.Continue = false;
            };
            requests.Start();

            // Explorer starts one process per selected file; they arrive one after another.
            Task.Run(() =>
            {
                Assert.True(MenuRequests.TrySend(new[] { a }, pipe));
                Assert.True(MenuRequests.TrySend(new[] { b }, pipe));
            });
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            timeout.Tick += (_, _) => frame.Continue = false;
            timeout.Start();
            Dispatcher.PushFrame(frame);
            requests.Dispose();
        });

        Assert.Equal(new[] { a, b }, received);
    }

    [Fact]
    public void WithNoMorphdropListening_SendingSaysSo() =>
        Assert.False(MenuRequests.TrySend(new[] { "x" }, $"Morphdrop.Tests.Nobody.{Guid.NewGuid():N}"));
}
