using System.Runtime.InteropServices;
using System.Windows;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace ShiftDrop.Interop;

/// <summary>
/// Wraps the shell's IDropTargetHelper so Windows keeps drawing the dragged file's thumbnail over the wheel.
/// Purely cosmetic: the first failure is logged and the rest are ignored.
/// </summary>
internal sealed class DropTargetHelper
{
    private readonly IDropTargetHelper? _helper;
    private bool _failureLogged;

    public DropTargetHelper()
    {
        try
        {
            _helper = (IDropTargetHelper)new DragDropHelper();
        }
        catch (Exception ex)
        {
            Log.Error("IDropTargetHelper is unavailable", ex);
        }
    }

    public void DragEnter(IntPtr window, IDataObject data, DragDropEffects effects) => Call(helper =>
    {
        NativeMethods.POINT point = Cursor();
        helper.DragEnter(window, (ComIDataObject)data, ref point, (int)effects);
    });

    public void DragOver(DragDropEffects effects) => Call(helper =>
    {
        NativeMethods.POINT point = Cursor();
        helper.DragOver(ref point, (int)effects);
    });

    public void DragLeave() => Call(helper => helper.DragLeave());

    /// <summary>Hides or shows the dragged file's thumbnail (it would cover the wheel's hub).</summary>
    public void ShowDragImage(bool show) => Call(helper => helper.Show(show));

    public void Drop(IDataObject data, DragDropEffects effects) => Call(helper =>
    {
        NativeMethods.POINT point = Cursor();
        helper.Drop((ComIDataObject)data, ref point, (int)effects);
    });

    private static NativeMethods.POINT Cursor()
    {
        NativeMethods.GetCursorPos(out NativeMethods.POINT point);
        return point;
    }

    private void Call(Action<IDropTargetHelper> call)
    {
        if (_helper is null)
            return;

        try
        {
            call(_helper);
        }
        catch (Exception ex) when (!_failureLogged)
        {
            _failureLogged = true;
            Log.Error("IDropTargetHelper call failed", ex);
        }
        catch
        {
            // Already logged once.
        }
    }
}

[ComImport]
[Guid("4657278A-411B-11D2-839A-00C04FD918D0")]
internal class DragDropHelper
{
}

[ComImport]
[Guid("4657278B-411B-11D2-839A-00C04FD918D0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDropTargetHelper
{
    void DragEnter(IntPtr hwndTarget, ComIDataObject dataObject, ref NativeMethods.POINT point, int effect);

    void DragLeave();

    void DragOver(ref NativeMethods.POINT point, int effect);

    void Drop(ComIDataObject dataObject, ref NativeMethods.POINT point, int effect);

    void Show([MarshalAs(UnmanagedType.Bool)] bool show);
}
