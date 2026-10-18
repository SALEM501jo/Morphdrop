using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shapes = System.Windows.Shapes;

namespace ShiftDrop.Overlay;

/// <summary>Draws the wheel: slices with labels around a hub that describes the dragged files.</summary>
public partial class WheelView : UserControl
{
    private const double LabelBoxWidth = 80;
    private const double LabelBoxHeight = 36;
    private const double RestingOpacity = 0.92;

    private static readonly Brush SliceBrush = Frozen(Colors.White);
    private static readonly Brush SliceHotBrush = Frozen(Color.FromRgb(0xFF, 0x5F, 0x3A));
    private static readonly Brush LabelBrush = Frozen(Color.FromRgb(0x26, 0x26, 0x26));
    private static readonly Brush LabelHotBrush = Frozen(Colors.White);

    private readonly List<Slice> _slices = new();
    private int _highlighted = WheelGeometry.OutsideIndex;

    public WheelView()
    {
        InitializeComponent();
    }

    public void SetSlices(IReadOnlyList<string> labels)
    {
        SliceLayer.Children.Clear();
        _slices.Clear();
        _highlighted = WheelGeometry.OutsideIndex;

        for (int i = 0; i < labels.Count; i++)
        {
            // Opacity is set on the whole shape (not on the brush) so the stroke and fill don't double up where they overlap.
            var shape = new Shapes.Path
            {
                Data = WheelGeometry.CreateSlice(i, labels.Count),
                Fill = SliceBrush,
                Stroke = SliceBrush,
                StrokeThickness = WheelGeometry.CornerRadius * 2,
                StrokeLineJoin = PenLineJoin.Round,
                Opacity = RestingOpacity,
            };
            var label = new TextBlock
            {
                Text = labels[i],
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = LabelBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var labelBox = new Grid { Width = LabelBoxWidth, Height = LabelBoxHeight, Children = { label } };
            Point at = WheelGeometry.PointAt(WheelGeometry.LabelRadius, WheelGeometry.SliceCenterAngle(i, labels.Count));
            Canvas.SetLeft(labelBox, at.X - LabelBoxWidth / 2);
            Canvas.SetTop(labelBox, at.Y - LabelBoxHeight / 2);

            SliceLayer.Children.Add(shape);
            SliceLayer.Children.Add(labelBox);
            _slices.Add(new Slice(shape, label));
        }
    }

    public void SetHub(string title, string subtitle)
    {
        HubTitle.Text = title;
        HubSubtitle.Text = subtitle;
    }

    /// <summary>Highlights one slice; any other index (hub, outside) clears the highlight.</summary>
    public void Highlight(int index)
    {
        if (index == _highlighted)
            return;

        if (_highlighted >= 0 && _highlighted < _slices.Count)
            Paint(_slices[_highlighted], hot: false);
        _highlighted = index;
        if (index >= 0 && index < _slices.Count)
            Paint(_slices[index], hot: true);
    }

    public void PlayOpenAnimation()
    {
        var grow = new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        ZoomTransform.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        ZoomTransform.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    private static void Paint(Slice slice, bool hot)
    {
        slice.Shape.Fill = slice.Shape.Stroke = hot ? SliceHotBrush : SliceBrush;
        slice.Shape.Opacity = hot ? 1 : RestingOpacity;
        slice.Label.Foreground = hot ? LabelHotBrush : LabelBrush;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private sealed record Slice(Shapes.Path Shape, TextBlock Label);
}
