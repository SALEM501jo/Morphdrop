using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Morphdrop.Actions;
using Morphdrop.Overlay;

namespace Morphdrop.Demo;

/// <summary>
/// The Reel's cover: the headline, the wheel mid-drop and the Shift key. Everything that matters stays inside the
/// middle 3:4 of the frame, which is all the profile grid shows.
/// </summary>
internal static class Cover
{
    public static void Render(DemoAssets assets, string path)
    {
        VideoFormat format = VideoFormat.Reel;
        double w = format.Width, h = format.Height;
        var canvas = new Canvas { Width = w, Height = h };
        canvas.Children.Add(new Image { Source = Visuals.Wallpaper(format), Width = w, Height = h, Stretch = Stretch.Fill });

        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new Image { Source = Visuals.LogoImage(), Width = 34, Height = 34, Margin = new Thickness(0, 0, 10, 0) },
                new TextBlock { Text = "Morphdrop", FontFamily = Visuals.DisplayFont, FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        var headline = new TextBlock
        {
            Text = "حوّل أي ملف",
            FlowDirection = FlowDirection.RightToLeft,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 58,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
        };
        var subline = new TextBlock
        {
            FlowDirection = FlowDirection.RightToLeft,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 34,
            FontWeight = FontWeights.SemiBold,
            Foreground = Visuals.Frozen(Color.FromRgb(0xFF, 0xB0, 0x8F)),
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, -4, 0, 0),
        };
        subline.Inlines.Add("بسحبة + ");
        subline.Inlines.Add(new System.Windows.Documents.Run("Shift") { FlowDirection = FlowDirection.LeftToRight });
        var top = new StackPanel
        {
            Width = w,
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.5 },
            Children = { brand, headline, subline },
        };
        Visuals.Place(canvas, top, 0, 150);

        // The wheel just as a photo is about to be dropped on WEBP.
        IReadOnlyList<IFileAction> actions = ActionCatalog.ForFileName(assets.Sunset)[0].Actions;
        int webp = actions.Select(action => action.Label).ToList().IndexOf("WEBP");
        var wheel = new WheelView();
        wheel.SetDarkTheme(false);
        wheel.SetSlices(actions.Select(action => new WheelItem(action.Label, action.Glyph)).ToList());
        wheel.Highlight(webp);
        wheel.SetHub(ByteSize.Format(new FileInfo(assets.Sunset).Length), actions[webp].Description);
        wheel.SetHint("Shift → Tools");
        const double wheelSize = 400;
        double scale = wheelSize / WheelGeometry.Size;
        var center = new Point(w / 2, 545);
        var wheelBox = new Viewbox { Width = wheelSize, Height = wheelSize, Child = wheel };
        Visuals.Place(canvas, wheelBox, center.X - wheelSize / 2, center.Y - wheelSize / 2);

        Point slice = WheelGeometry.PointAt(WheelGeometry.OuterRadius - 14, WheelGeometry.SliceCenterAngle(webp, actions.Count));
        var tip = new Point(center.X + (slice.X - WheelGeometry.Size / 2) * scale, center.Y + (slice.Y - WheelGeometry.Size / 2) * scale);
        var photo = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(3),
            Opacity = 0.85,
            RenderTransform = new RotateTransform(-8),
            Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 6, Direction = 270, Opacity = 0.5 },
            Child = new Image { Source = new BitmapImage(new Uri(assets.Sunset)) { DecodePixelWidth = 400 }, Width = 112, Stretch = Stretch.Uniform },
        };
        Visuals.Place(canvas, photo, tip.X + 16, tip.Y - 92); // above the slice, clear of its label and the next one
        var cursor = Visuals.Cursor();
        cursor.RenderTransform = new ScaleTransform(1.9, 1.9);
        Visuals.Place(canvas, cursor, tip.X, tip.Y);

        var key = new KeyCap();
        key.Update(1, new[] { 1.0 });
        key.LayoutTransform = new ScaleTransform(1.3, 1.3);
        canvas.Children.Add(key);
        key.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(key, (w - key.DesiredSize.Width) / 2);
        Canvas.SetTop(key, 762);

        var footer = new TextBlock
        {
            Text = "مجاني ومفتوح المصدر لويندوز",
            FlowDirection = FlowDirection.RightToLeft,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 22,
            Foreground = Visuals.Frozen(Color.FromRgb(0xDD, 0xD9, 0xF5)),
            TextAlignment = TextAlignment.Center,
            Width = w,
        };
        Visuals.Place(canvas, footer, 0, 880);

        canvas.Measure(new Size(w, h));
        canvas.Arrange(new Rect(0, 0, w, h));
        canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap(format.PixelWidth, format.PixelHeight, 96 * VideoFormat.Scale, 96 * VideoFormat.Scale, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream file = File.Create(path);
        encoder.Save(file);
    }
}
