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
    private const double LabelBoxHeight = 48;
    private const double RestingOpacity = 0.92;

    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private Brush _sliceBrush = Frozen(Colors.White);
    private static readonly Brush SliceHotBrush = Frozen(Color.FromRgb(0xFF, 0x5F, 0x3A));
    private Brush _labelBrush = Frozen(Color.FromRgb(0x26, 0x26, 0x26));
    private static readonly Brush LabelHotBrush = Frozen(Colors.White);

    private readonly List<Slice> _slices = new();
    private int _highlighted = WheelGeometry.OutsideIndex;

    public WheelView()
    {
        InitializeComponent();
    }

    public void SetSlices(IReadOnlyList<WheelItem> items)
    {
        SliceLayer.Children.Clear();
        _slices.Clear();
        _highlighted = WheelGeometry.OutsideIndex;

        for (int i = 0; i < items.Count; i++)
        {
            // Opacity is set on the whole shape (not on the brush) so the stroke and fill don't double up where they overlap.
            var shape = new Shapes.Path
            {
                Data = WheelGeometry.CreateSlice(i, items.Count),
                Fill = _sliceBrush,
                Stroke = _sliceBrush,
                StrokeThickness = WheelGeometry.CornerRadius * 2,
                StrokeLineJoin = PenLineJoin.Round,
                Opacity = RestingOpacity,
            };

            (FrameworkElement content, TextBlock[] texts) = CreateLabel(items[i]);
            var labelBox = new Grid { Width = LabelBoxWidth, Height = LabelBoxHeight, Children = { content } };
            Point at = WheelGeometry.PointAt(WheelGeometry.LabelRadius, WheelGeometry.SliceCenterAngle(i, items.Count));
            Canvas.SetLeft(labelBox, at.X - LabelBoxWidth / 2);
            Canvas.SetTop(labelBox, at.Y - LabelBoxHeight / 2);

            SliceLayer.Children.Add(shape);
            SliceLayer.Children.Add(labelBox);
            _slices.Add(new Slice(shape, texts));
        }
    }

    /// <summary>Light or dark colors; call before <see cref="SetSlices"/> so new slices use them.</summary>
    public void SetDarkTheme(bool dark)
    {
        _sliceBrush = Frozen(dark ? Color.FromRgb(0x2D, 0x2D, 0x2D) : Colors.White);
        _labelBrush = Frozen(dark ? Color.FromRgb(0xF0, 0xF0, 0xF0) : Color.FromRgb(0x26, 0x26, 0x26));
        Ring.Fill = Frozen(dark ? Color.FromArgb(0x8C, 0x1E, 0x1E, 0x1E) : Color.FromArgb(0x73, 0xFF, 0xFF, 0xFF));
        Ring.Stroke = Frozen(dark ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));
        Hub.Background = Frozen(dark ? Color.FromArgb(0xF7, 0x20, 0x20, 0x20) : Color.FromArgb(0xF7, 0xFF, 0xFF, 0xFF));
        Hub.BorderBrush = Frozen(dark ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1F, 0, 0, 0));
        HubTitle.Foreground = Frozen(dark ? Colors.White : Color.FromRgb(0x1F, 0x1F, 0x1F));
        HubSubtitle.Foreground = Frozen(dark ? Color.FromRgb(0xB4, 0xB4, 0xB4) : Color.FromRgb(0x5C, 0x5C, 0x5C));
    }

    public void SetHub(string title, string subtitle)
    {
        HubTitle.Text = title;
        HubSubtitle.Text = subtitle;
    }

    /// <summary>A short accent-colored line under the hub text, e.g. which page Shift opens next.</summary>
    public void SetHint(string? hint)
    {
        HubHint.Text = hint ?? "";
        HubHint.Visibility = hint is null ? Visibility.Collapsed : Visibility.Visible;
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

    /// <summary>The new page's slices swing into place.</summary>
    public void PlayPageTurnAnimation()
    {
        var duration = TimeSpan.FromMilliseconds(180);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        SliceTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-30, 0, duration) { EasingFunction = ease });
        SliceLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, duration) { EasingFunction = ease });
    }

    private (FrameworkElement Content, TextBlock[] Texts) CreateLabel(WheelItem item)
    {
        if (item.Glyph is null)
        {
            var label = new TextBlock
            {
                Text = item.Label,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = _labelBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return (label, new[] { label });
        }

        var glyph = new TextBlock
        {
            Text = item.Glyph,
            FontFamily = IconFont,
            FontSize = 20,
            Foreground = _labelBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var caption = new TextBlock
        {
            Text = item.Label.ToUpperInvariant(),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = _labelBrush,
            Margin = new Thickness(0, 4, 0, 0),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { glyph, caption },
        };
        return (stack, new[] { glyph, caption });
    }

    private void Paint(Slice slice, bool hot)
    {
        slice.Shape.Fill = slice.Shape.Stroke = hot ? SliceHotBrush : _sliceBrush;
        slice.Shape.Opacity = hot ? 1 : RestingOpacity;
        foreach (TextBlock text in slice.Texts)
            text.Foreground = hot ? LabelHotBrush : _labelBrush;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private sealed record Slice(Shapes.Path Shape, TextBlock[] Texts);
}
