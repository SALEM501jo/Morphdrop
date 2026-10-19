using System.Windows;
using ImageMagick;
using Morphdrop.Actions;
using Morphdrop.Editing;
using Morphdrop.Media;
using Morphdrop.Settings;
using Velopack;
using Morphdrop.Input;
using Morphdrop.Overlay;
using Morphdrop.Tray;

namespace Morphdrop;

public partial class App : Application
{
    private const string ExitSignalName = @"Local\Morphdrop.Exit";

    private Mutex? _singleInstance;
    private EventWaitHandle? _exitSignal;
    private string _triggerName = "Shift";
    private DragWatcher? _watcher;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        // First of all: Velopack's install/update/uninstall hooks run the app with special arguments and exit here.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ =>
            {
                try
                {
                    new AutoStart(Environment.ProcessPath!).SetEnabled(false); // don't leave a Run entry pointing nowhere
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
                {
                    // Uninstalling goes on regardless.
                }
            })
            .Run();

        base.OnStartup(e);

        if (e.Args is ["--render-logo", var logoFolder])
        {
            Branding.Logo.WriteAssets(logoFolder);
            Shutdown();
            return;
        }

        if (e.Args is ["--render-wheel", var previewPath])
        {
            WheelPreview.Render(previewPath);
            Shutdown();
            return;
        }

        // "Morphdrop.exe --exit" asks a running instance to quit (handy when rebuilding or updating).
        var exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ExitSignalName);
        if (e.Args is ["--exit"])
        {
            exitSignal.Set();
            Shutdown();
            return;
        }

        _singleInstance = new Mutex(initiallyOwned: false, @"Local\Morphdrop.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("Morphdrop is already running. Look for its icon in the notification area.", "Morphdrop");
            Shutdown();
            return;
        }

        _exitSignal = exitSignal;
        ThreadPool.RegisterWaitForSingleObject(exitSignal, (_, _) => Dispatcher.InvokeAsync(() => Shutdown()), null, Timeout.Infinite, executeOnlyOnce: true);

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled exception on the UI thread", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        Log.Info($"Morphdrop {typeof(App).Assembly.GetName().Version?.ToString(3)} starting (pid {Environment.ProcessId})");

        var runner = new ActionRunner();
        FfmpegSetup? ffmpegSetup = null; // created with the tray icon below; the wheel only needs it once files are dropped
        var wheel = new WheelWindow(
            runner,
            (action, path) => EditorWindow.Open(action, path, runner),
            (action, files) =>
            {
                if (action is MediaAction)
                    ffmpegSetup?.RunWhenReady(() => runner.Run(action, files));
                else
                    runner.Run(action, files);
            });
        wheel.Prewarm();

        var autoStart = new AutoStart(Environment.ProcessPath!);
        var settings = new SettingsStore(SettingsStore.DefaultPath);
        var tray = new TrayIcon();
        _tray = tray;
        ffmpegSetup = new FfmpegSetup(tray);
        FfmpegRunner.ProgressChanged += (description, share) =>
            Dispatcher.InvokeAsync(() => tray.SetStatus(share >= 1 ? null : $"{description}: {share:P0}"));
        runner.Working += tray.ShowWorking;
        runner.Completed += tray.ShowResult;
        tray.ExitRequested += () => Shutdown();
        tray.SettingsRequested += () => SettingsWindow.Show(settings, autoStart);

        // Load ImageMagick's native library in the background so the first conversion doesn't wait for it.
        Task.Run(() =>
        {
            try
            {
                using var warmUp = new MagickImage(MagickColors.Transparent, 1, 1);
            }
            catch (Exception ex)
            {
                Log.Error("Could not load ImageMagick", ex);
            }
        });

        var watcher = new DragWatcher();
        _watcher = watcher;
        watcher.Triggered += point => Dispatcher.InvokeAsync(() => wheel.OnShiftPressed(point));
        watcher.DragEnded += () => Dispatcher.InvokeAsync(wheel.OnDragEnded);
        tray.PausedChanged += paused =>
        {
            watcher.Enabled = !paused;
            Log.Info(paused ? "Paused" : "Resumed");
        };

        void Apply(AppSettings current)
        {
            (int key, string name) = current.TriggerKey switch
            {
                TriggerKey.Ctrl => (Interop.NativeMethods.VK_CONTROL, "Ctrl"),
                TriggerKey.Alt => (Interop.NativeMethods.VK_MENU, "Alt"),
                _ => (Interop.NativeMethods.VK_SHIFT, "Shift"),
            };
            watcher.TriggerKey = key;
            wheel.TriggerName = name;
            tray.SetTriggerName(name);
            wheel.EnabledPages = current.EnabledPages;
            wheel.Theme = current.Theme;
            AppTheme.Use(current.Theme);
            Imaging.ImageWriter.Preset = current.Quality;
            OutputFile.Folder = current.OutputFolder;
            tray.OnlyNotifyProblems = current.Notifications == NotificationLevel.ErrorsOnly;
            _triggerName = name;
        }

        AppTheme.StartFollowingWindows();
        Apply(settings.Current);
        settings.Changed += changed =>
        {
            Apply(changed);
            Log.Info($"Settings changed: {changed}");
        };

        try
        {
            watcher.Start();
            Log.Info($"Mouse hook installed; waiting for drag + {_triggerName}");
            tray.ShowMessage("Morphdrop is running", $"Drag a file, then press {_triggerName} to open the wheel.");
        }
        catch (Exception ex)
        {
            Log.Error("Could not install the mouse hook", ex);
            tray.ShowError("Morphdrop can't watch the mouse", ex.Message);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _watcher?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        _exitSignal?.Dispose();
        if (_tray is not null) // not for --exit, --render-wheel or a second instance
            Log.Info("Morphdrop stopped");
        Log.Flush();
        base.OnExit(e);
    }
}
