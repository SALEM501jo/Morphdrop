using System.Windows;
using ImageMagick;
using ShiftDrop.Actions;
using ShiftDrop.Input;
using ShiftDrop.Overlay;
using ShiftDrop.Tray;

namespace ShiftDrop;

public partial class App : Application
{
    private const string ExitSignalName = @"Local\ShiftDrop.Exit";

    private Mutex? _singleInstance;
    private EventWaitHandle? _exitSignal;
    private DragWatcher? _watcher;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args is ["--render-wheel", var previewPath])
        {
            WheelPreview.Render(previewPath);
            Shutdown();
            return;
        }

        // "ShiftDrop.exe --exit" asks a running instance to quit (handy when rebuilding or updating).
        var exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ExitSignalName);
        if (e.Args is ["--exit"])
        {
            exitSignal.Set();
            Shutdown();
            return;
        }

        _singleInstance = new Mutex(initiallyOwned: false, @"Local\ShiftDrop.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("ShiftDrop is already running. Look for its icon in the notification area.", "ShiftDrop");
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

        Log.Info($"ShiftDrop {typeof(App).Assembly.GetName().Version?.ToString(3)} starting (pid {Environment.ProcessId})");

        var runner = new ActionRunner();
        var wheel = new WheelWindow(runner);
        wheel.Prewarm();

        var autoStart = new AutoStart(Environment.ProcessPath!);
        var tray = new TrayIcon(autoStart.IsEnabled);
        _tray = tray;
        runner.Working += tray.ShowWorking;
        runner.Completed += tray.ShowResult;
        tray.ExitRequested += () => Shutdown();
        tray.StartWithWindowsChanged += enabled =>
        {
            try
            {
                autoStart.SetEnabled(enabled);
                Log.Info(enabled ? $"Start with Windows on: {Environment.ProcessPath}" : "Start with Windows off");
            }
            catch (Exception ex)
            {
                Log.Error("Could not change Start with Windows", ex);
                tray.ShowError("Couldn't change Start with Windows", ex.Message);
            }
        };

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

        try
        {
            watcher.Start();
            Log.Info("Mouse hook installed; waiting for drag + Shift");
            tray.ShowMessage("ShiftDrop is running", "Drag a file, then press Shift to open the wheel.");
        }
        catch (Exception ex)
        {
            Log.Error("Could not install the mouse hook", ex);
            tray.ShowError("ShiftDrop can't watch the mouse", ex.Message);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _watcher?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        _exitSignal?.Dispose();
        if (_tray is not null) // not for --exit, --render-wheel or a second instance
            Log.Info("ShiftDrop stopped");
        Log.Flush();
        base.OnExit(e);
    }
}
