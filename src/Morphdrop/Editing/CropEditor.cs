using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;

namespace Morphdrop.Editing;

/// <summary>Drag the box or its handles; aspect presets lock the shape. Saves "name (cropped).ext".</summary>
internal sealed class CropEditor : IImageEditor
{
    private const double HandleSize = 12; // on screen

    private static readonly Brush Shade = EditorUi.Frozen(Color.FromArgb(0x99, 0, 0, 0));

    private readonly EditableImage _image;
    private readonly ImageSurface _surface;
    private readonly Shapes.Path _shade = new() { Fill = Shade, IsHitTestVisible = false };
    private readonly Shapes.Rectangle _frame = new() { Stroke = Brushes.White, IsHitTestVisible = false };
    private readonly Thumb _mover = EditorUi.Handle(Cursors.SizeAll, Brushes.Transparent);
    private readonly List<(Thumb Thumb, Edges Edges)> _handles = new();
    private readonly TextBlock _size = EditorUi.Label("");
    private Rect _crop;
    private double? _ratio;
    private bool _couldSave;

    public CropEditor(EditableImage image)
    {
        _image = image;
        _surface = new ImageSurface(image.Preview);
        _crop = new Rect(_surface.Bounds);

        Canvas overlay = _surface.Overlay;
        overlay.Children.Add(_shade);
        overlay.Children.Add(_frame);
        overlay.Children.Add(_mover);
        _mover.DragDelta += (_, e) => Update(CropMath.Move(_crop, e.HorizontalChange, e.VerticalChange, _surface.Bounds));

        (Edges Edges, Cursor Cursor)[] handles =
        {
            (Edges.Left | Edges.Top, Cursors.SizeNWSE), (Edges.Top, Cursors.SizeNS), (Edges.Right | Edges.Top, Cursors.SizeNESW),
            (Edges.Right, Cursors.SizeWE), (Edges.Right | Edges.Bottom, Cursors.SizeNWSE), (Edges.Bottom, Cursors.SizeNS),
            (Edges.Left | Edges.Bottom, Cursors.SizeNESW), (Edges.Left, Cursors.SizeWE),
        };
        foreach ((Edges edges, Cursor cursor) in handles)
        {
            Thumb thumb = EditorUi.Handle(cursor, Brushes.White, EditorUi.Accent);
            thumb.DragDelta += (_, e) => Update(CropMath.Resize(_crop, edges, e.HorizontalChange, e.VerticalChange, _surface.Bounds, _ratio));
            overlay.Children.Add(thumb);
            _handles.Add((thumb, edges));
        }

        _surface.ScaleChanged += Layout;

        double original = _surface.Width / _surface.Height;
        Options = EditorUi.Row(
            EditorUi.Label("Aspect"),
            EditorUi.Chip("Free", "crop", true, () => SetRatio(null)),
            EditorUi.Chip("Original", "crop", false, () => SetRatio(original)),
            EditorUi.Chip("Square", "crop", false, () => SetRatio(1)),
            EditorUi.Chip("4:3", "crop", false, () => SetRatio(4.0 / 3)),
            EditorUi.Chip("3:2", "crop", false, () => SetRatio(3.0 / 2)),
            EditorUi.Chip("16:9", "crop", false, () => SetRatio(16.0 / 9)),
            EditorUi.Chip("9:16", "crop", false, () => SetRatio(9.0 / 16)),
            _size);
        Layout();
    }

    public event Action? CanSaveChanged;

    public string SaveLabel => "Save cropped copy";

    public FrameworkElement View => _surface.View;

    public FrameworkElement Options { get; }

    /// <summary>Nothing to save until the box is smaller than the whole image.</summary>
    public bool CanSave => PixelArea != new PixelRect(0, 0, (int)_image.Width, (int)_image.Height);

    private PixelRect PixelArea => PixelRect.FromPreview(_crop, _image.PreviewScale).ClampTo(_image.Width, _image.Height);

    public Func<string> PrepareSave()
    {
        PixelRect area = PixelArea;
        return () => _image.Save("cropped", frames => ImageEdits.Crop(frames, area));
    }

    private void SetRatio(double? ratio)
    {
        _ratio = ratio;
        Update(ratio is double r ? CropMath.Largest(_surface.Bounds, r) : _crop);
    }

    private void Update(Rect crop)
    {
        _crop = crop;
        Layout();
    }

    private void Layout()
    {
        _shade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(_surface.Bounds)), new RectangleGeometry(_crop));

        Place(_frame, _crop);
        _frame.StrokeThickness = _surface.ScreenToPreview(1.5);
        Place(_mover, _crop);

        double size = _surface.ScreenToPreview(HandleSize);
        foreach ((Thumb thumb, Edges edges) in _handles)
        {
            double x = edges.HasFlag(Edges.Left) ? _crop.Left : edges.HasFlag(Edges.Right) ? _crop.Right : _crop.Left + _crop.Width / 2;
            double y = edges.HasFlag(Edges.Top) ? _crop.Top : edges.HasFlag(Edges.Bottom) ? _crop.Bottom : _crop.Top + _crop.Height / 2;
            Place(thumb, new Rect(x - size / 2, y - size / 2, size, size));
        }

        PixelRect area = PixelArea;
        _size.Text = $"{area.Width} × {area.Height} px";
        _size.Margin = new Thickness(10, 0, 0, 0);

        if (CanSave != _couldSave)
        {
            _couldSave = CanSave;
            CanSaveChanged?.Invoke();
        }
    }

    private static void Place(FrameworkElement element, Rect rect)
    {
        Canvas.SetLeft(element, rect.X);
        Canvas.SetTop(element, rect.Y);
        element.Width = Math.Max(0, rect.Width);
        element.Height = Math.Max(0, rect.Height);
    }
}
