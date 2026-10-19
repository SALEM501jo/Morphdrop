using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shapes = System.Windows.Shapes;

namespace Morphdrop.Editing;

/// <summary>Drag boxes over anything private; they become black or pixelated. Saves "name (redacted).ext".</summary>
internal sealed class RedactEditor : IImageEditor
{
    private const double MinBoxSize = 4; // preview pixels

    private static readonly Brush RubberFill = EditorUi.Frozen(Color.FromArgb(0x33, 0xFF, 0x5F, 0x3A));

    private readonly EditableImage _image;
    private readonly ImageSurface _surface;
    private readonly List<(Rect Area, FrameworkElement Visual)> _boxes = new();
    private RedactStyle _style = RedactStyle.BlackBox;
    private Point? _start;
    private Shapes.Rectangle? _rubber;

    public RedactEditor(EditableImage image)
    {
        _image = image;
        _surface = new ImageSurface(image.Preview);

        Canvas overlay = _surface.Overlay;
        overlay.Background = Brushes.Transparent; // receive the mouse everywhere
        overlay.Cursor = Cursors.Cross;
        overlay.MouseLeftButtonDown += (_, e) => BeginBox(e.GetPosition(overlay));
        overlay.MouseMove += (_, e) => DragBox(e.GetPosition(overlay));
        overlay.MouseLeftButtonUp += (_, e) => EndBox(e.GetPosition(overlay));

        Options = EditorUi.Row(
            EditorUi.Chip("Black box", "redact", true, () => SetStyle(RedactStyle.BlackBox)),
            EditorUi.Chip("Pixelate", "redact", false, () => SetStyle(RedactStyle.Pixelate)),
            new Border { Width = 10 },
            EditorUi.Button("Undo", Undo),
            EditorUi.Button("Clear", Clear),
            EditorUi.Label("Drag over anything private. Location and camera details are removed too."));
    }

    public event Action? CanSaveChanged;

    public string SaveLabel => "Save redacted copy";

    public FrameworkElement View => _surface.View;

    public FrameworkElement Options { get; }

    public bool CanSave => _boxes.Count > 0;

    public Func<string> PrepareSave()
    {
        List<PixelRect> areas = _boxes.Select(box => PixelRect.FromPreview(box.Area, _image.PreviewScale)).ToList();
        RedactStyle style = _style;
        return () => _image.Save("redacted", frames => ImageEdits.Redact(frames, areas, style));
    }

    public void Undo()
    {
        if (_boxes.Count == 0)
            return;

        _surface.Overlay.Children.Remove(_boxes[^1].Visual);
        _boxes.RemoveAt(_boxes.Count - 1);
        CanSaveChanged?.Invoke();
    }

    private void Clear()
    {
        foreach ((_, FrameworkElement visual) in _boxes)
            _surface.Overlay.Children.Remove(visual);
        _boxes.Clear();
        CanSaveChanged?.Invoke();
    }

    private void SetStyle(RedactStyle style)
    {
        _style = style;
        for (int i = 0; i < _boxes.Count; i++)
        {
            Rect area = _boxes[i].Area;
            int index = _surface.Overlay.Children.IndexOf(_boxes[i].Visual);
            FrameworkElement visual = CreateVisual(area);
            _surface.Overlay.Children[index] = visual;
            _boxes[i] = (area, visual);
        }
    }

    private void BeginBox(Point point)
    {
        _start = point;
        _surface.Overlay.CaptureMouse();
        _rubber = new Shapes.Rectangle
        {
            Stroke = EditorUi.Accent,
            StrokeThickness = _surface.ScreenToPreview(1.5),
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = RubberFill,
            IsHitTestVisible = false,
        };
        _surface.Overlay.Children.Add(_rubber);
        DragBox(point);
    }

    private void DragBox(Point point)
    {
        if (_start is not Point start || _rubber is null)
            return;

        Rect area = Clamp(new Rect(start, point));
        Canvas.SetLeft(_rubber, area.X);
        Canvas.SetTop(_rubber, area.Y);
        _rubber.Width = area.Width;
        _rubber.Height = area.Height;
    }

    private void EndBox(Point point)
    {
        if (_start is not Point start)
            return;

        _surface.Overlay.ReleaseMouseCapture();
        _surface.Overlay.Children.Remove(_rubber);
        _rubber = null;
        _start = null;

        Rect area = Clamp(new Rect(start, point));
        if (area.Width < MinBoxSize || area.Height < MinBoxSize)
            return;

        FrameworkElement visual = CreateVisual(area);
        _surface.Overlay.Children.Add(visual);
        _boxes.Add((area, visual));
        CanSaveChanged?.Invoke();
    }

    private Rect Clamp(Rect area) => Rect.Intersect(area, new Rect(_surface.Bounds)) is { IsEmpty: false } inside ? inside : Rect.Empty;

    /// <summary>What the box will look like in the saved copy, drawn on the preview.</summary>
    private FrameworkElement CreateVisual(Rect area)
    {
        FrameworkElement visual;
        if (_style == RedactStyle.BlackBox)
        {
            visual = new Shapes.Rectangle { Fill = Brushes.Black };
        }
        else
        {
            var pixels = new Int32Rect((int)area.X, (int)area.Y, Math.Max(1, (int)area.Width), Math.Max(1, (int)area.Height));
            // Same block size as ImageEdits.Pixelate, converted to preview pixels.
            double block = Math.Max(8, Math.Max(area.Width, area.Height) * _image.PreviewScale / 10) / _image.PreviewScale;
            var coarse = new TransformedBitmap(new CroppedBitmap(_image.Preview, pixels), new ScaleTransform(1 / block, 1 / block));
            var image = new Image { Source = coarse, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            visual = image;
        }

        visual.IsHitTestVisible = false;
        Canvas.SetLeft(visual, area.X);
        Canvas.SetTop(visual, area.Y);
        visual.Width = area.Width;
        visual.Height = area.Height;
        return visual;
    }
}
