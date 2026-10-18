using System.ComponentModel;
using System.Runtime.InteropServices;
using static ShiftDrop.Interop.NativeMethods;

namespace ShiftDrop.Input;

/// <summary>
/// Watches global mouse input through a low-level mouse hook and raises <see cref="Triggered"/> when Shift is
/// pressed in the middle of a drag. The hook lives on its own thread so a busy UI thread can never stall the
/// system's mouse input. Events are raised on that thread: handlers must only post work elsewhere.
/// </summary>
internal sealed class DragWatcher : IDisposable
{
    private const uint PollIntervalMs = 40;

    private readonly DragGesture _gesture = new(
        Math.Max(4, GetSystemMetrics(SM_CXDRAG)),
        Math.Max(4, GetSystemMetrics(SM_CYDRAG)));

    private LowLevelMouseProc? _hookProc; // must stay referenced while the hook is installed
    private IntPtr _hook;
    private UIntPtr _pollTimer;
    private Thread? _thread;
    private uint _threadId;
    private volatile bool _enabled = true;

    /// <summary>Shift was pressed mid-drag; the argument is the cursor position in physical screen pixels.</summary>
    public event Action<POINT>? Triggered;

    /// <summary>The mouse button that started the drag was released.</summary>
    public event Action? DragEnded;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public void Start()
    {
        if (_thread is not null)
            throw new InvalidOperationException("The drag watcher is already running.");

        Exception? failure = null;
        var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE); // gives this thread a message queue
            _hookProc = OnHook;
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
                failure = new Win32Exception(Marshal.GetLastWin32Error(), "Installing the mouse hook failed.");
            ready.Set();
            if (_hook == IntPtr.Zero)
                return;

            try
            {
                PumpMessages();
            }
            finally
            {
                StopPolling();
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        })
        {
            IsBackground = true,
            Name = "ShiftDrop mouse hook",
            Priority = ThreadPriority.AboveNormal,
        };

        thread.Start();
        ready.Wait();
        if (failure is not null)
            throw failure;
        _thread = thread;
    }

    public void Dispose()
    {
        Thread? thread = _thread;
        if (thread is null)
            return;

        _thread = null;
        PostThreadMessage(_threadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        thread.Join(TimeSpan.FromSeconds(2));
    }

    private void PumpMessages()
    {
        while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_TIMER && _pollTimer != UIntPtr.Zero && msg.wParam == _pollTimer)
            {
                Poll();
                continue;
            }

            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private IntPtr OnHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            try
            {
                int msg = (int)message;
                bool relevant = msg is WM_LBUTTONDOWN or WM_LBUTTONUP or WM_RBUTTONDOWN or WM_RBUTTONUP
                    || (msg == WM_MOUSEMOVE && !_gesture.IsIdle);
                if (relevant)
                {
                    POINT point = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(data).pt;
                    GestureEvent result = msg switch
                    {
                        WM_MOUSEMOVE => _gesture.Move(point, IsDown(VK_SHIFT)),
                        WM_LBUTTONDOWN => _gesture.ButtonDown(DragButton.Left, point, IsDown(VK_SHIFT)),
                        WM_RBUTTONDOWN => _gesture.ButtonDown(DragButton.Right, point, IsDown(VK_SHIFT)),
                        WM_LBUTTONUP => _gesture.ButtonUp(DragButton.Left),
                        _ => _gesture.ButtonUp(DragButton.Right),
                    };
                    Raise(result, point);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Mouse hook callback failed", ex);
            }
        }

        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private void Poll()
    {
        GetCursorPos(out POINT point);
        Raise(_gesture.Poll(IsDown(VK_SHIFT), IsDown(VK_LBUTTON) || IsDown(VK_RBUTTON)), point);
    }

    private void Raise(GestureEvent result, POINT point)
    {
        if (_gesture.IsDragging)
            StartPolling();
        else
            StopPolling();

        if (result == GestureEvent.Triggered && _enabled)
            Triggered?.Invoke(point);
        else if (result == GestureEvent.DragEnded)
            DragEnded?.Invoke();
    }

    private void StartPolling()
    {
        if (_pollTimer == UIntPtr.Zero)
            _pollTimer = SetTimer(IntPtr.Zero, UIntPtr.Zero, PollIntervalMs, IntPtr.Zero);
    }

    private void StopPolling()
    {
        if (_pollTimer == UIntPtr.Zero)
            return;

        KillTimer(IntPtr.Zero, _pollTimer);
        _pollTimer = UIntPtr.Zero;
    }

    private static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
}
