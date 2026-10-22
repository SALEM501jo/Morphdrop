using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Morphdrop.Editing;

/// <summary>
/// The preview with a drawing layer on top, shrunk to fit the window. Everything on <see cref="Overlay"/> is measured
/// in preview pixels; <see cref="DisplayScale"/> says how big one of those is on screen, so handles can keep a
/// constant on-screen size.
/// </summary>
internal sealed class ImageSurface
{
    private readonly Grid _content;

    public ImageSurface(BitmapSource preview)
    {
        Width = preview.PixelWidth;
        Height = preview.PixelHeight;

        var image = new Image { Source = preview, Width = Width, Height = Height, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        Overlay = new Canvas { Width = Width, Height = Height, ClipToBounds = true };
        _content = new Grid { Width = Width, Height = Height, Children = { image, Overlay } };
        View = new Viewbox
        {
            Child = _content,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly, // never blow small images up
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        View.SizeChanged += (_, _) => ScaleChanged?.Invoke();
    }

    public event Action? ScaleChanged;

    /// <summary>Drawn behind the picture, where it is transparent (e.g. a checkerboard).</summary>
    public Brush? Backdrop
    {
        get => _content.Background;
        set => _content.Background = value;
    }

    public double Width { get; }

    public double Height { get; }

    public Size Bounds => new(Width, Height);

    public Canvas Overlay { get; }

    public Viewbox View { get; }

    public double DisplayScale => View.ActualWidth > 0 ? View.ActualWidth / Width : 1;

    /// <summary>A length in on-screen pixels, expressed in preview pixels.</summary>
    public double ScreenToPreview(double length) => length / DisplayScale;
}
