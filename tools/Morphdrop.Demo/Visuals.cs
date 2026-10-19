using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Morphdrop.Branding;
using Morphdrop.Editing;
using Shapes = System.Windows.Shapes;

namespace Morphdrop.Demo;

internal static class Ease
{
    public static double Clamp01(double x) => Math.Clamp(x, 0, 1);

    public static double Progress(double t, double start, double end) => end <= start ? (t >= end ? 1 : 0) : Clamp01((t - start) / (end - start));

    public static double InOut(double x) => x < 0.5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2;

    public static double Out(double x) => 1 - Math.Pow(1 - x, 3);

    public static double OutBack(double x)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        return 1 + c3 * Math.Pow(x - 1, 3) + c1 * Math.Pow(x - 1, 2);
    }

    public static Point Lerp(Point a, Point b, double k) => new(a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k);

    public static double Lerp(double a, double b, double k) => a + (b - a) * k;

    /// <summary>A move from <paramref name="a"/> to <paramref name="b"/> between two times, eased at both ends.</summary>
    public static Point Move(double t, double start, double end, Point a, Point b) => Lerp(a, b, InOut(Progress(t, start, end)));
}

internal static class Visuals
{
    public static readonly FontFamily DisplayFont = new("Segoe UI Variable Display, Segoe UI");
    public static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static void Place(Canvas canvas, UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        canvas.Children.Add(element);
    }

    public static void Move(UIElement element, Point at)
    {
        Canvas.SetLeft(element, at.X);
        Canvas.SetTop(element, at.Y);
    }

    public static ImageSource LogoImage()
    {
        var image = new DrawingImage(Logo.Create());
        image.Freeze();
        return image;
    }

    /// <summary>Deep navy with soft orange and violet glows, the logo's colors.</summary>
    public static ImageSource Wallpaper(VideoFormat format)
    {
        double w = format.Width, h = format.Height;
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x0B, 0x0F, 0x22), Color.FromRgb(0x18, 0x0F, 0x36), 90), null, new Rect(0, 0, w, h));
            Glow(dc, w, h, new Point(0.08 * w, 0.02 * h), 0.85 * Math.Max(w, h) * 0.75, Color.FromArgb(0x8C, 0xFF, 0x65, 0x40));
            Glow(dc, w, h, new Point(0.98 * w, 0.72 * h), 0.95 * Math.Max(w, h) * 0.7, Color.FromArgb(0x80, 0x7B, 0x5C, 0xFF));
            Glow(dc, w, h, new Point(0.35 * w, 1.05 * h), 0.6 * Math.Max(w, h), Color.FromArgb(0x40, 0xD9, 0x46, 0x8F));
        }

        var bitmap = new RenderTargetBitmap(format.PixelWidth, format.PixelHeight, 96 * VideoFormat.Scale, 96 * VideoFormat.Scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void Glow(DrawingContext dc, double w, double h, Point center, double radius, Color color)
    {
        var brush = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = center,
            GradientOrigin = center,
            RadiusX = radius,
            RadiusY = radius,
            GradientStops =
            {
                new GradientStop(color, 0),
                new GradientStop(Color.FromArgb((byte)(color.A * 0.35), color.R, color.G, color.B), 0.5),
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1),
            },
        };
        dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
    }

    public static FrameworkElement Footer() => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center,
        Children =
        {
            new Image { Source = LogoImage(), Width = 22, Height = 22, Margin = new Thickness(0, 0, 8, 0) },
            new TextBlock { Text = "Morphdrop", FontFamily = DisplayFont, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = "  ·  free & open source for Windows", FontFamily = TextFont, FontSize = 13, Foreground = Frozen(Color.FromRgb(0xC4, 0xC0, 0xE0)), VerticalAlignment = VerticalAlignment.Center },
        },
    };

    /// <summary>The Windows arrow; its tip is the element's top-left corner.</summary>
    public static FrameworkElement Cursor() => new Shapes.Path
    {
        Data = Geometry.Parse("M0,0 L0,17 L4.2,13 L7.3,19.6 L10,18.4 L7,12 L12.6,12 Z"),
        Fill = Brushes.White,
        Stroke = Brushes.Black,
        StrokeThickness = 1,
        StrokeLineJoin = PenLineJoin.Round,
        RenderTransform = new ScaleTransform(1.3, 1.3),
        Effect = new DropShadowEffect { BlurRadius = 5, ShadowDepth = 1.5, Direction = 300, Opacity = 0.4 },
    };
}

/// <summary>A file on the desktop: its real Explorer icon or thumbnail with the name under it.</summary>
internal sealed class DesktopFile : Grid
{
    public const double IconSize = 60;
    private readonly Border _selection;
    private readonly ScaleTransform _pop = new(1, 1);

    public DesktopFile(string path, string? label = null)
    {
        Width = 104;
        Height = 104;
        RenderTransformOrigin = new Point(0.5, 0.35);
        RenderTransform = _pop;
        Icon = ShellIcons.Get(path);
        _selection = new Border
        {
            CornerRadius = new CornerRadius(4),
            Background = Visuals.Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderBrush = Visuals.Frozen(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Opacity = 0,
        };
        Children.Add(_selection);
        Children.Add(new StackPanel
        {
            Margin = new Thickness(4, 4, 4, 0),
            Children =
            {
                new Image { Source = Icon, Width = IconSize, Height = IconSize, Stretch = Stretch.Uniform },
                new TextBlock
                {
                    Text = label ?? System.IO.Path.GetFileName(path),
                    FontFamily = Visuals.TextFont,
                    FontSize = 11,
                    Foreground = Brushes.White,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxHeight = 30,
                    Margin = new Thickness(0, 4, 0, 0),
                    Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = 0.8 },
                },
            },
        });
    }

    public ImageSource Icon { get; }

    public bool Selected
    {
        set => _selection.Opacity = value ? 1 : 0;
    }

    /// <summary>0 = not there yet, 1 = fully in; overshoots a little on the way.</summary>
    public void Pop(double k)
    {
        Opacity = k <= 0 ? 0 : 1;
        double scale = k <= 0 ? 0.01 : Ease.OutBack(k);
        _pop.ScaleX = _pop.ScaleY = Math.Max(0.01, scale);
    }

    /// <summary>Where the middle of the icon goes for <paramref name="slot"/>.</summary>
    public static Point SlotCenter(int slot, double stageHeight)
    {
        int rows = Math.Max(1, (int)((stageHeight - 20) / 100));
        return new Point(56 + 106 * (slot / rows), 52 + 100 * (slot % rows));
    }

    public void PlaceAt(Canvas canvas, int slot)
    {
        Point center = SlotCenter(slot, canvas.Height);
        Visuals.Place(canvas, this, center.X - Width / 2, center.Y - 34);
    }
}

/// <summary>A Shift key that pops up whenever the script presses it, since keystrokes don't show on screen.</summary>
internal sealed class KeyCap : Border
{
    private readonly TranslateTransform _press = new();

    public KeyCap()
    {
        CornerRadius = new CornerRadius(10);
        Background = Visuals.Frozen(Color.FromArgb(0xF7, 0xFF, 0xFF, 0xFF));
        BorderBrush = Visuals.Frozen(Color.FromArgb(0x50, 0x00, 0x00, 0x00));
        BorderThickness = new Thickness(1, 1, 1, 4);
        Padding = new Thickness(16, 7, 16, 6);
        RenderTransform = _press;
        Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Direction = 270, Opacity = 0.45 };
        Opacity = 0;
        Child = new TextBlock
        {
            Text = "⇧  Shift",
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI, Segoe UI Symbol"),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = Visuals.Frozen(Color.FromRgb(0x22, 0x22, 0x22)),
        };
    }

    public void Update(double t, IReadOnlyList<double> presses)
    {
        double opacity = 0;
        bool down = false;
        foreach (double p in presses)
        {
            opacity = Math.Max(opacity, Math.Min(Ease.Progress(t, p - 0.1, p - 0.02), 1 - Ease.Progress(t, p + 0.4, p + 0.52)));
            down |= t >= p && t < p + 0.13;
        }

        Opacity = opacity;
        _press.Y = down ? 3 : 0;
        BorderThickness = new Thickness(1, 1, 1, down ? 1.5 : 4);
    }
}

/// <summary>The notification Morphdrop shows when a file is saved.</summary>
internal sealed class Toast : Border
{
    private readonly TranslateTransform _slide = new();

    public Toast(string title, string body)
    {
        Width = 300;
        CornerRadius = new CornerRadius(8);
        Background = Visuals.Frozen(Color.FromArgb(0xF5, 0x2B, 0x2B, 0x2E));
        BorderBrush = Visuals.Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        BorderThickness = new Thickness(1);
        Padding = new Thickness(14, 12, 14, 14);
        RenderTransform = _slide;
        Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.5 };
        Opacity = 0;
        Child = new StackPanel
        {
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 8),
                    Children =
                    {
                        new Image { Source = Visuals.LogoImage(), Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0) },
                        new TextBlock { Text = "Morphdrop", FontFamily = Visuals.TextFont, FontSize = 11, Foreground = Visuals.Frozen(Color.FromRgb(0xC8, 0xC8, 0xC8)), VerticalAlignment = VerticalAlignment.Center },
                    },
                },
                new TextBlock { Text = title, FontFamily = Visuals.TextFont, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = body, FontFamily = Visuals.TextFont, FontSize = 12, Foreground = Visuals.Frozen(Color.FromRgb(0xD0, 0xD0, 0xD0)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) },
            },
        };
    }

    public void Show(double k)
    {
        double eased = Ease.Out(k);
        Opacity = eased;
        _slide.X = (1 - eased) * 48;
    }
}

/// <summary>A ring that spreads out where the cursor clicks.</summary>
internal sealed class Ripple : Border
{
    private const double Size = 34;
    private readonly ScaleTransform _scale = new();

    public Ripple()
    {
        Width = Height = Size;
        CornerRadius = new CornerRadius(Size / 2);
        BorderBrush = EditorUi.Accent;
        BorderThickness = new Thickness(3);
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _scale;
        Opacity = 0;
        IsHitTestVisible = false;
    }

    /// <summary>Shows the ripple of the latest click at or before <paramref name="t"/>.</summary>
    public void Update(double t, IEnumerable<(double Time, Point At)> clicks)
    {
        Opacity = 0;
        foreach ((double time, Point at) in clicks)
        {
            if (t < time || t > time + 0.4)
                continue;
            double k = Ease.Progress(t, time, time + 0.4);
            Canvas.SetLeft(this, at.X - Size / 2);
            Canvas.SetTop(this, at.Y - Size / 2);
            _scale.ScaleX = _scale.ScaleY = 0.3 + Ease.Out(k) * 1.1;
            Opacity = 0.9 * (1 - k);
        }
    }
}

/// <summary>
/// Morphdrop's editor window, drawn with the window chrome so it can be rendered off screen: the real editor's view
/// and options, the Cancel and Save buttons, in the app's own styles.
/// </summary>
internal sealed class EditorFrame : Border
{
    private readonly ScaleTransform _zoom = new();
    private readonly ScaleTransform _savePress = new(1, 1);
    private double _fit = 1;

    /// <summary>A tall stage gets a tall window (the real one can be resized to anything this wide).</summary>
    public EditorFrame(string title, IImageEditor editor, Size stage)
    {
        bool tall = stage.Height > stage.Width;
        Width = tall ? 800 : 900;
        Height = tall ? 900 : 640;
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        BorderBrush = Visuals.Frozen(Color.FromArgb(0x40, 0x00, 0x00, 0x00));
        SetResourceReference(BackgroundProperty, "Theme.Window");
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _zoom;
        Effect = new DropShadowEffect { BlurRadius = 46, ShadowDepth = 12, Direction = 270, Opacity = 0.55 };
        TextElement.SetFontFamily(this, new FontFamily("Segoe UI"));
        TextElement.SetFontSize(this, 13);

        var captionButtons = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (string glyph in new[] { "", "", "" })
            captionButtons.Children.Add(new TextBlock { Text = glyph, FontFamily = Visuals.IconFont, FontSize = 10, Width = 46, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Visuals.Frozen(Color.FromRgb(0x30, 0x30, 0x30)) });
        DockPanel.SetDock(captionButtons, Dock.Right);
        var titleBar = new DockPanel
        {
            Height = 34,
            Children =
            {
                captionButtons,
                new Image { Source = Visuals.LogoImage(), Width = 16, Height = 16, Margin = new Thickness(12, 0, 10, 0) },
                new TextBlock { Text = title, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = Visuals.Frozen(Color.FromRgb(0x1F, 0x1F, 0x1F)) },
            },
        };
        DockPanel.SetDock(titleBar, Dock.Top);

        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        cancel.SetResourceReference(StyleProperty, "ActionButton");
        SaveButton = new Button { Content = editor.SaveLabel, IsEnabled = editor.CanSave, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _savePress };
        SaveButton.SetResourceReference(StyleProperty, "AccentButton");
        editor.CanSaveChanged += () => SaveButton.IsEnabled = editor.CanSave;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), Children = { cancel, SaveButton } };
        DockPanel.SetDock(buttons, Dock.Right);

        var bottomBar = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 12, 20, 12),
            Child = new DockPanel { Children = { buttons, new ContentControl { Content = editor.Options, VerticalAlignment = VerticalAlignment.Center } } },
        };
        bottomBar.SetResourceReference(BackgroundProperty, "Theme.Panel");
        bottomBar.SetResourceReference(BorderBrushProperty, "Theme.PanelBorder");
        DockPanel.SetDock(bottomBar, Dock.Bottom);

        Child = new DockPanel
        {
            Clip = new RectangleGeometry(new Rect(0, 0, Width - 2, Height - 2), 7, 7),
            Children = { titleBar, bottomBar, new ContentControl { Content = editor.View, Margin = new Thickness(24) } },
        };
    }

    public Button SaveButton { get; }

    /// <summary>Centers the window on the stage, shrunk to fit.</summary>
    public void PlaceOn(Canvas stage)
    {
        _fit = Math.Min((stage.Width - 16) / Width, (stage.Height - 12) / Height);
        Visuals.Place(stage, this, (stage.Width - Width) / 2, (stage.Height - Height) / 2);
        Show(0);
    }

    /// <summary>0 = closed, 1 = open; opening and closing both grow from slightly smaller, like Windows does.</summary>
    public void Show(double k)
    {
        Opacity = Ease.Clamp01(k);
        _zoom.ScaleX = _zoom.ScaleY = _fit * (0.94 + 0.06 * Ease.Out(Ease.Clamp01(k)));
    }

    public void PressSave(bool down) => _savePress.ScaleX = _savePress.ScaleY = down ? 0.95 : 1;
}

/// <summary>Explorer's own icons and thumbnails for files, so the desktop looks like the real one.</summary>
internal static class ShellIcons
{
    private const int IconOnly = 0x4;
    private static readonly Dictionary<string, BitmapSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static BitmapSource Get(string path)
    {
        if (Cache.TryGetValue(path, out BitmapSource? cached))
            return cached;

        // Thumbnails for pictures; files that are only placeholders (no real content) get their type's icon.
        bool picture = new FileInfo(path).Length > 0 && Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".png" or ".webp";
        SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out IShellItemImageFactory factory);
        Marshal.ThrowExceptionForHR(factory.GetImage(new NativeSize { Width = 256, Height = 256 }, picture ? 0 : IconOnly, out IntPtr bitmap));
        try
        {
            return Cache[path] = FromHBitmap(bitmap);
        }
        finally
        {
            DeleteObject(bitmap);
            Marshal.ReleaseComObject(factory);
        }
    }

    private static BitmapSource FromHBitmap(IntPtr bitmap)
    {
        var info = new BitmapHeader();
        GetObject(bitmap, Marshal.SizeOf<NativeBitmap>(), out NativeBitmap native);
        int width = native.Width, height = native.Height;
        info.Size = Marshal.SizeOf<BitmapHeader>();
        info.Width = width;
        info.Height = -height; // top-down rows
        info.Planes = 1;
        info.BitCount = 32;
        var pixels = new byte[width * height * 4];
        IntPtr screen = GetDC(IntPtr.Zero);
        try
        {
            GetDIBits(screen, bitmap, 0, (uint)height, pixels, ref info, 0);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }

        // Photo thumbnails often come without alpha at all; they're opaque.
        bool anyAlpha = false;
        for (int i = 3; i < pixels.Length && !anyAlpha; i += 4)
            anyAlpha = pixels[i] != 0;
        if (!anyAlpha)
        {
            for (int i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;
        }

        BitmapSource source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        source.Freeze();
        return source;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, int flags, out IntPtr bitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
        public uint Color0;
        public uint Color1;
        public uint Color2;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItemImageFactory item);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr handle, int size, out NativeBitmap bitmap);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BitmapHeader info, uint usage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);
}
