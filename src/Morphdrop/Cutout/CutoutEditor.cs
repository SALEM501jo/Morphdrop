using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;
using Morphdrop.Editing;
using Shapes = System.Windows.Shapes;

namespace Morphdrop.Cutout;

internal enum CutoutTool { Keep, Remove, Wand }

/// <summary>
/// Removes the background: the AI finds the subject (on this PC, once its model is downloaded), then the Keep and
/// Remove brushes fix what it missed and the magic wand clears an area of one color. Saves "name (no background).png",
/// or hands the cut-out to the Background editor.
/// </summary>
internal sealed class CutoutEditor : IImageEditor
{
    private const int WandTolerance = 40; // per channel, out of 255
    private const int UndoDepth = 25;
    private const double DefaultBrush = 40; // on screen

    private static readonly Brush KeepBrush = EditorUi.Frozen(Color.FromRgb(0x2E, 0xB8, 0x6B));
    private static readonly Brush RemoveBrush = EditorUi.Frozen(Color.FromRgb(0xE5, 0x48, 0x3B));

    private readonly EditableImage _image;
    private readonly ImageSurface _surface;
    private readonly WriteableBitmap _bitmap;
    private readonly byte[] _colors; // the preview, BGRA, straight alpha
    private readonly byte[] _mask;
    private readonly int _width;
    private readonly int _height;
    private readonly List<byte[]> _undo = new();
    private readonly Shapes.Ellipse _brushOutline = new() { IsHitTestVisible = false, StrokeThickness = 2, Visibility = Visibility.Collapsed };
    private readonly TextBlock _status = EditorUi.Label("");
    private readonly Button _download;
    private readonly Slider _size = new() { Minimum = 8, Maximum = 160, Value = DefaultBrush, Width = 110, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
    private byte[]? _found;
    private CutoutTool _tool = CutoutTool.Remove;
    private Point? _last;
    private bool _busy;
    private bool _couldSave;

    public CutoutEditor(EditableImage image)
    {
        _image = image;
        BitmapSource preview = image.Preview.Format == PixelFormats.Bgra32 ? image.Preview : new FormatConvertedBitmap(image.Preview, PixelFormats.Bgra32, null, 0);
        _width = preview.PixelWidth;
        _height = preview.PixelHeight;
        _colors = new byte[_width * _height * 4];
        preview.CopyPixels(_colors, _width * 4, 0);
        _mask = new byte[_width * _height];
        for (int i = 0; i < _mask.Length; i++)
            _mask[i] = _colors[i * 4 + 3]; // already-transparent pixels stay transparent

        _bitmap = new WriteableBitmap(_width, _height, 96, 96, PixelFormats.Pbgra32, null);
        _surface = new ImageSurface(_bitmap) { Backdrop = Checkerboard() };
        Refresh(new Int32Rect(0, 0, _width, _height));

        Canvas overlay = _surface.Overlay;
        overlay.Background = Brushes.Transparent; // receive the mouse everywhere
        overlay.Children.Add(_brushOutline);
        overlay.MouseLeftButtonDown += (_, e) => Press(e.GetPosition(overlay));
        overlay.MouseMove += (_, e) => Move(e.GetPosition(overlay), e.LeftButton == MouseButtonState.Pressed);
        overlay.MouseLeftButtonUp += (_, _) => Release();
        overlay.MouseLeave += (_, _) => _brushOutline.Visibility = Visibility.Collapsed;

        _download = EditorUi.Button($"Download the AI ({CutoutModel.DownloadSize / 1_000_000} MB)", () => _ = DownloadAsync());
        Options = new StackPanel
        {
            Children =
            {
                EditorUi.Row(
                    EditorUi.Chip("Remove", "cutout", true, () => SetTool(CutoutTool.Remove)),
                    EditorUi.Chip("Keep", "cutout", false, () => SetTool(CutoutTool.Keep)),
                    EditorUi.Chip("Magic wand", "cutout", false, () => SetTool(CutoutTool.Wand)),
                    new Border { Width = 8 },
                    EditorUi.Label("Brush"), _size,
                    EditorUi.Button("Undo", Undo),
                    EditorUi.Button("Start over", StartOver),
                    EditorUi.Button("Put on a background…", () => ContinueWithBackground?.Invoke())),
                new Border { Height = 8 },
                EditorUi.Row(_download, _status),
            },
        };

        if (CutoutModel.IsAvailable)
            _ = FindSubjectAsync();
        else
            ShowStatus("Automatic cut-out needs a one-time download; it runs on this PC and nothing is uploaded. Or paint with the brushes and the magic wand.", download: true);
    }

    public event Action? CanSaveChanged;

    /// <summary>"Put on a background…": the window switches to the Background editor with <see cref="CreateCutout"/>.</summary>
    public event Action? ContinueWithBackground;

    public string SaveLabel => "Save without background";

    public FrameworkElement View => _surface.View;

    public FrameworkElement Options { get; }

    public bool CanSave => !_busy && _mask.Any(value => value < 255);

    public Func<string> PrepareSave()
    {
        byte[] mask = (byte[])_mask.Clone();
        return () =>
        {
            using MagickImage cutout = CutoutImage.Apply(_image.Frames[0], mask, _width, _height);
            return CutoutImage.Save(_image.Path, _image.Source, cutout);
        };
    }

    /// <summary>The cut-out at full size, for the next editor.</summary>
    public MagickImage CreateCutout() => CutoutImage.Apply(_image.Frames[0], _mask, _width, _height);

    public void Undo()
    {
        if (_undo.Count == 0)
            return;

        _undo[^1].CopyTo(_mask, 0);
        _undo.RemoveAt(_undo.Count - 1);
        Changed(new Int32Rect(0, 0, _width, _height));
    }

    private async Task FindSubjectAsync()
    {
        SetBusy(true, "Finding the subject…");
        try
        {
            IMagickImage<byte> frame = _image.Frames[0];
            byte[] found = await Task.Run(() => SubjectFinder.FindMask(frame, _width, _height));
            Remember();
            for (int i = 0; i < _mask.Length; i++)
                _mask[i] = Math.Min(_mask[i], found[i]);
            _found = (byte[])_mask.Clone();
            Changed(new Int32Rect(0, 0, _width, _height));
            ShowStatus("Paint red to remove and green to keep. The magic wand clears an area of one color.", download: false);
        }
        catch (Exception ex)
        {
            Log.Error("The cut-out model failed", ex);
            ShowStatus($"The automatic cut-out didn't work ({ex.Message}). The brushes and magic wand still do.", download: false);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async Task DownloadAsync()
    {
        _download.IsEnabled = false;
        var progress = new Progress<double>(share => ShowStatus($"Downloading the AI: {share:P0}", download: true));
        try
        {
            await CutoutModel.InstallAsync(progress);
            Log.Info($"Cut-out model {CutoutModel.Name} installed at {CutoutModel.InstalledPath}");
            await FindSubjectAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Could not download the cut-out model", ex);
            _download.IsEnabled = true;
            ShowStatus($"Couldn't download the AI: {ex.Message}", download: true);
        }
    }

    private void ShowStatus(string text, bool download)
    {
        _status.Text = text;
        _download.Visibility = download ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetBusy(bool busy, string? status)
    {
        _busy = busy;
        _surface.Overlay.Cursor = busy ? Cursors.Wait : Cursors.None;
        if (status is not null)
            ShowStatus(status, download: false);
        RaiseCanSave();
    }

    private void SetTool(CutoutTool tool)
    {
        _tool = tool;
        _brushOutline.Stroke = tool == CutoutTool.Keep ? KeepBrush : RemoveBrush;
    }

    private double Radius => _surface.ScreenToPreview(_size.Value) / 2;

    private void Press(Point point)
    {
        if (_busy)
            return;

        Remember();
        if (_tool == CutoutTool.Wand)
        {
            Changed(Wand((int)point.X, (int)point.Y));
            return;
        }

        _last = point;
        Changed(Stamp(point));
        _surface.Overlay.CaptureMouse();
    }

    private void Move(Point point, bool pressed)
    {
        ShowOutline(point);
        if (!pressed || _last is not Point last || _tool == CutoutTool.Wand)
            return;

        // Stamp along the way so a quick stroke doesn't leave gaps.
        double distance = (point - last).Length;
        double step = Math.Max(1, Radius / 4);
        Int32Rect dirty = Stamp(point);
        for (double travelled = step; travelled < distance; travelled += step)
            dirty = Union(dirty, Stamp(last + (point - last) * (travelled / distance)));
        _last = point;
        Changed(dirty);
    }

    private void Release()
    {
        _last = null;
        _surface.Overlay.ReleaseMouseCapture();
    }

    private void ShowOutline(Point point)
    {
        if (_tool == CutoutTool.Wand || _busy)
        {
            _brushOutline.Visibility = Visibility.Collapsed;
            _surface.Overlay.Cursor = _busy ? Cursors.Wait : Cursors.Cross;
            return;
        }

        double radius = Radius;
        _surface.Overlay.Cursor = Cursors.None;
        _brushOutline.Stroke ??= RemoveBrush;
        _brushOutline.StrokeThickness = _surface.ScreenToPreview(2);
        _brushOutline.Width = _brushOutline.Height = radius * 2;
        Canvas.SetLeft(_brushOutline, point.X - radius);
        Canvas.SetTop(_brushOutline, point.Y - radius);
        _brushOutline.Visibility = Visibility.Visible;
    }

    /// <summary>A soft round brush: full strength in the middle, fading out over the outer 40 %.</summary>
    private Int32Rect Stamp(Point center)
    {
        double radius = Radius;
        int left = Math.Max(0, (int)(center.X - radius)), top = Math.Max(0, (int)(center.Y - radius));
        int right = Math.Min(_width - 1, (int)Math.Ceiling(center.X + radius)), bottom = Math.Min(_height - 1, (int)Math.Ceiling(center.Y + radius));
        if (right < left || bottom < top)
            return default;

        bool keep = _tool == CutoutTool.Keep;
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                double distance = Math.Sqrt((x + 0.5 - center.X) * (x + 0.5 - center.X) + (y + 0.5 - center.Y) * (y + 0.5 - center.Y));
                if (distance > radius)
                    continue;

                double strength = Math.Clamp((radius - distance) / (radius * 0.4), 0, 1);
                int i = y * _width + x;
                byte target = (byte)Math.Round(strength * 255);
                if (keep)
                    _mask[i] = Math.Max(_mask[i], Math.Min(target, _colors[i * 4 + 3]));
                else
                    _mask[i] = Math.Min(_mask[i], (byte)(255 - target));
            }
        }

        return new Int32Rect(left, top, right - left + 1, bottom - top + 1);
    }

    /// <summary>Clears the connected area whose color is close to the clicked pixel's.</summary>
    private Int32Rect Wand(int startX, int startY)
    {
        if (startX < 0 || startY < 0 || startX >= _width || startY >= _height)
            return default;

        int seed = (startY * _width + startX) * 4;
        byte b0 = _colors[seed], g0 = _colors[seed + 1], r0 = _colors[seed + 2];
        var visited = new bool[_width * _height];
        var pending = new Stack<int>();
        pending.Push(startY * _width + startX);
        visited[startY * _width + startX] = true;
        int left = startX, right = startX, top = startY, bottom = startY;
        while (pending.Count > 0)
        {
            int i = pending.Pop();
            _mask[i] = 0;
            int x = i % _width, y = i / _width;
            left = Math.Min(left, x);
            right = Math.Max(right, x);
            top = Math.Min(top, y);
            bottom = Math.Max(bottom, y);
            foreach (int next in new[] { x > 0 ? i - 1 : -1, x < _width - 1 ? i + 1 : -1, y > 0 ? i - _width : -1, y < _height - 1 ? i + _width : -1 })
            {
                if (next < 0 || visited[next])
                    continue;
                visited[next] = true;
                int p = next * 4;
                if (Math.Abs(_colors[p] - b0) <= WandTolerance && Math.Abs(_colors[p + 1] - g0) <= WandTolerance && Math.Abs(_colors[p + 2] - r0) <= WandTolerance)
                    pending.Push(next);
            }
        }

        return new Int32Rect(left, top, right - left + 1, bottom - top + 1);
    }

    private void StartOver()
    {
        if (_busy)
            return;

        Remember();
        if (_found is not null)
            _found.CopyTo(_mask, 0);
        else
            for (int i = 0; i < _mask.Length; i++)
                _mask[i] = _colors[i * 4 + 3];
        Changed(new Int32Rect(0, 0, _width, _height));
    }

    private void Remember()
    {
        _undo.Add((byte[])_mask.Clone());
        if (_undo.Count > UndoDepth)
            _undo.RemoveAt(0);
    }

    private void Changed(Int32Rect dirty)
    {
        if (dirty.Width > 0 && dirty.Height > 0)
            Refresh(dirty);
        RaiseCanSave();
    }

    private void RaiseCanSave()
    {
        if (CanSave != _couldSave)
        {
            _couldSave = CanSave;
            CanSaveChanged?.Invoke();
        }
    }

    /// <summary>Redraws part of the picture: its colors with the mask as transparency (premultiplied for WPF).</summary>
    private void Refresh(Int32Rect area)
    {
        var pixels = new byte[area.Width * area.Height * 4];
        for (int y = 0; y < area.Height; y++)
        {
            for (int x = 0; x < area.Width; x++)
            {
                int i = (area.Y + y) * _width + area.X + x;
                int source = i * 4, target = (y * area.Width + x) * 4;
                int alpha = _mask[i];
                pixels[target] = (byte)(_colors[source] * alpha / 255);
                pixels[target + 1] = (byte)(_colors[source + 1] * alpha / 255);
                pixels[target + 2] = (byte)(_colors[source + 2] * alpha / 255);
                pixels[target + 3] = (byte)alpha;
            }
        }

        _bitmap.WritePixels(area, pixels, area.Width * 4, 0);
    }

    private static Int32Rect Union(Int32Rect a, Int32Rect b)
    {
        if (a.Width == 0)
            return b;
        if (b.Width == 0)
            return a;
        int left = Math.Min(a.X, b.X), top = Math.Min(a.Y, b.Y);
        int right = Math.Max(a.X + a.Width, b.X + b.Width), bottom = Math.Max(a.Y + a.Height, b.Y + b.Height);
        return new Int32Rect(left, top, right - left, bottom - top);
    }

    /// <summary>The gray checks image editors use to show transparency.</summary>
    private static Brush Checkerboard()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        var gray = EditorUi.Frozen(Color.FromRgb(0xDD, 0xDD, 0xDD));
        group.Children.Add(new GeometryDrawing(gray, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        group.Children.Add(new GeometryDrawing(gray, null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
        var brush = new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }
}
