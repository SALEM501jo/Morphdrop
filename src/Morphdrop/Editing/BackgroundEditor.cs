using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;

namespace Morphdrop.Editing;

/// <summary>Puts the image on a gradient or color with padding, rounded corners and a shadow. Saves "name (background).ext".</summary>
internal sealed class BackgroundEditor : IImageEditor
{
    private static readonly Brush SwatchBorder = EditorUi.Frozen(Color.FromRgb(0xD0, 0xD0, 0xD0));

    private readonly EditableImage _image;
    private readonly Viewbox _view = new() { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
    private readonly List<(Shapes.Ellipse Swatch, BackgroundPreset Preset)> _swatches = new();
    private BackgroundOptions _options = BackgroundComposer.Default;

    public BackgroundEditor(EditableImage image)
    {
        _image = image;

        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0) };
        foreach (BackgroundPreset preset in BackgroundComposer.Presets)
        {
            var swatch = new Shapes.Ellipse
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 6, 0),
                Fill = preset.CreateBrush(),
                Cursor = Cursors.Hand,
                ToolTip = preset.Name,
            };
            swatch.MouseLeftButtonUp += (_, _) => Change(_options with { Background = preset });
            swatches.Children.Add(swatch);
            _swatches.Add((swatch, preset));
        }

        var shadow = new CheckBox { Content = "Shadow", IsChecked = _options.Shadow, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        shadow.Click += (_, _) => Change(_options with { Shadow = shadow.IsChecked == true });

        Options = new StackPanel
        {
            Children =
            {
                EditorUi.Row(
                    swatches,
                    EditorUi.Label("Padding"), Slider(0, 0.25, _options.Padding, value => _options with { Padding = value }),
                    EditorUi.Label("Corners"), Slider(0, 0.12, _options.Corners, value => _options with { Corners = value }),
                    shadow),
                new Border { Height = 8 },
                EditorUi.Row(
                    EditorUi.Label("Size"),
                    EditorUi.Chip("Fit image", "background", true, () => Change(_options with { AspectRatio = null })),
                    EditorUi.Chip("Square", "background", false, () => Change(_options with { AspectRatio = 1 })),
                    EditorUi.Chip("4:5 portrait", "background", false, () => Change(_options with { AspectRatio = 4.0 / 5 })),
                    EditorUi.Chip("16:9", "background", false, () => Change(_options with { AspectRatio = 16.0 / 9 })),
                    EditorUi.Chip("9:16 story", "background", false, () => Change(_options with { AspectRatio = 9.0 / 16 }))),
            },
        };
        Change(_options);
    }

    public event Action? CanSaveChanged
    {
        add { }
        remove { }
    }

    public string SaveLabel => "Save with background";

    public FrameworkElement View => _view;

    public FrameworkElement Options { get; }

    public bool CanSave => true;

    /// <summary>Renders at full size on the UI thread (WPF composes the picture); encoding happens in the job.</summary>
    public Func<string> PrepareSave()
    {
        byte[] png = BackgroundComposer.RenderPng(_image.FullSizeBitmap(), _options);
        return () => _image.SaveComposite("background", png);
    }

    private Slider Slider(double minimum, double maximum, double value, Func<double, BackgroundOptions> apply)
    {
        var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = value, Width = 110, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        slider.ValueChanged += (_, e) => Change(apply(e.NewValue));
        return slider;
    }

    private void Change(BackgroundOptions options)
    {
        _options = options;
        foreach ((Shapes.Ellipse swatch, BackgroundPreset preset) in _swatches)
        {
            bool selected = preset == options.Background;
            swatch.Stroke = selected ? EditorUi.Accent : SwatchBorder;
            swatch.StrokeThickness = selected ? 3 : 1;
        }

        _view.Child = BackgroundComposer.Compose(_image.Preview, _image.Preview.PixelWidth, _image.Preview.PixelHeight, options);
    }
}
