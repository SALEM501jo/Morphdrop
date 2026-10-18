using System.Windows;
using ShiftDrop.Actions;
using ShiftDrop.Input;
using ShiftDrop.Overlay;
using ShiftDrop.Tray;

namespace ShiftDrop;

public partial class App : Application
{
    private Mutex? _singleInstance;
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

        _singleInstance = new Mutex(initiallyOwned: false, @"Local\ShiftDrop.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("ShiftDrop is already running. Look for its icon in the notification area.", "ShiftDrop");
            Shutdown();
            return;
        }

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

        var tray = new TrayIcon();
        _tray = tray;
        runner.Completed += tray.ShowResult;
        tray.ExitRequested += () => Shutdown();

        var watcher = new DragWatcher();
        _watcher = watcher;
        watcher.Triggered += point => Dispatcher.InvokeAsync(() => wheel.ShowAt(point));
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
        Log.Info("ShiftDrop stopped");
        Log.Flush();
        base.OnExit(e);
    }
}
