using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Morphdrop.Media;

namespace Morphdrop.Demo;

/// <summary>A video shape. Everything is laid out in logical units and rendered at twice that.</summary>
internal sealed record VideoFormat(string Name, double Width, double Height, Rect Caption, double CaptionSize, Rect Stage, bool Footer)
{
    public const double Scale = 2;

    public static readonly VideoFormat Reel = new("instagram-reel", 540, 960, new Rect(24, 84, 492, 150), 34, new Rect(0, 250, 540, 600), true);

    public static readonly VideoFormat Square = new("linkedin-square", 540, 540, new Rect(24, 16, 492, 96), 26, new Rect(0, 112, 540, 420), false);

    public int PixelWidth => (int)(Width * Scale);

    public int PixelHeight => (int)(Height * Scale);
}

/// <summary>
/// <c>Morphdrop.Demo [output folder] [--format=instagram-reel|linkedin-square] [--stills=1,2.5,…]</c>: makes the demo
/// files, then renders each video with FFmpeg, or only PNG stills at the given seconds.
/// </summary>
internal static class Program
{
    private const int Fps = 30;

    [STAThread]
    private static int Main(string[] args)
    {
        Log.Enabled = false;
        string output = Path.GetFullPath(args.FirstOrDefault(arg => !arg.StartsWith("--")) ?? Path.Combine("artifacts", "demo"));
        string? only = Option(args, "format");
        double[]? stills = Option(args, "stills")?.Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();

        var app = new App();
        app.InitializeComponent(); // the editors' styles and Theme.* brushes

        Directory.CreateDirectory(output);
        DemoAssets assets = DemoAssets.Prepare(Path.Combine(output, "files"));
        if (Option(args, "windows") is { } which)
        {
            WindowShots.Render(assets, output, which);
            return 0;
        }

        if (args.Contains("--cover"))
        {
            Cover.Render(assets, Path.Combine(output, "cover-instagram.png"));
            return 0;
        }

        foreach (VideoFormat format in new[] { VideoFormat.Reel, VideoFormat.Square })
        {
            if (only is not null && only != format.Name)
                continue;

            var film = new Film(format, Script.Create(assets));
            Console.WriteLine($"{format.Name}: {film.Duration:0.0} s");
            if (stills is not null)
            {
                foreach (double time in stills)
                    film.SaveStill(time, Path.Combine(output, $"{format.Name}-{time.ToString("00.0", CultureInfo.InvariantCulture)}s.png"));
            }
            else
            {
                film.Encode(Path.Combine(output, $"morphdrop-{format.Name}.mp4"), Fps);
            }
        }

        return 0;
    }

    private static string? Option(string[] args, string name) =>
        args.FirstOrDefault(arg => arg.StartsWith($"--{name}="))?[(name.Length + 3)..];
}

/// <summary>Plays the scenes one after another on the wallpaper, with the captions above them.</summary>
internal sealed class Film
{
    private const double CaptionFade = 0.25;

    private readonly VideoFormat _format;
    private readonly IReadOnlyList<Scene> _scenes;
    private readonly Grid _root;
    private readonly Canvas _stageHost;
    private readonly Canvas _fullHost;
    private readonly FrameworkElement _captionBox;
    private readonly TextBlock _caption;
    private readonly TextBlock _subcaption;
    private readonly TranslateTransform _captionSlide = new();
    private readonly FrameworkElement? _footer;
    private readonly RenderTargetBitmap _bitmap;
    private Scene? _current;

    public Film(VideoFormat format, IReadOnlyList<Scene> scenes)
    {
        _format = format;
        _scenes = scenes;
        _root = new Grid { Width = format.Width, Height = format.Height };
        _root.Children.Add(new Image { Source = Visuals.Wallpaper(format), Stretch = Stretch.Fill });

        var overlay = new Canvas { Width = format.Width, Height = format.Height };
        _root.Children.Add(overlay);

        _caption = new TextBlock
        {
            FontFamily = Visuals.DisplayFont,
            FontSize = format.CaptionSize,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        _subcaption = new TextBlock
        {
            FontFamily = Visuals.TextFont,
            FontSize = format.CaptionSize * 0.46,
            Foreground = Visuals.Frozen(Color.FromRgb(0xD9, 0xD5, 0xF2)),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, format.CaptionSize * 0.28, 0, 0),
        };
        _captionBox = new Grid
        {
            Width = format.Caption.Width,
            Height = format.Caption.Height,
            RenderTransform = _captionSlide,
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Direction = 270, Opacity = 0.45 },
            Children = { new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _caption, _subcaption } } },
        };
        Visuals.Place(overlay, _captionBox, format.Caption.X, format.Caption.Y);

        _stageHost = new Canvas { Width = format.Stage.Width, Height = format.Stage.Height };
        Visuals.Place(overlay, _stageHost, format.Stage.X, format.Stage.Y);
        _fullHost = new Canvas { Width = format.Width, Height = format.Height };
        overlay.Children.Add(_fullHost);

        if (format.Footer)
        {
            _footer = Visuals.Footer();
            _footer.Width = format.Width;
            Visuals.Place(overlay, _footer, 0, format.Height - 62);
        }

        _root.Measure(new Size(format.Width, format.Height));
        _root.Arrange(new Rect(0, 0, format.Width, format.Height));
        _bitmap = new RenderTargetBitmap(format.PixelWidth, format.PixelHeight, 96 * VideoFormat.Scale, 96 * VideoFormat.Scale, PixelFormats.Pbgra32);
    }

    public double Duration => _scenes.Sum(scene => scene.Duration);

    public void SaveStill(double time, string path)
    {
        Render(time);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(_bitmap));
        using FileStream file = File.Create(path);
        encoder.Save(file);
    }

    public void Encode(string path, int fps)
    {
        string ffmpeg = Ffmpeg.Find() ?? throw new InvalidOperationException($"FFmpeg not found; set {Ffmpeg.OverrideVariable}.");
        var start = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in new[]
                 {
                     "-y", "-hide_banner", "-loglevel", "error",
                     "-f", "rawvideo", "-pix_fmt", "bgra", "-s", $"{_format.PixelWidth}x{_format.PixelHeight}", "-r", $"{fps}", "-i", "-",
                     "-c:v", "libx264", "-preset", "slow", "-crf", "18", "-pix_fmt", "yuv420p", "-movflags", "+faststart", path,
                 })
            start.ArgumentList.Add(argument);

        using Process process = Process.Start(start)!;
        var errors = new System.Text.StringBuilder();
        process.ErrorDataReceived += (_, e) => errors.AppendLine(e.Data);
        process.BeginErrorReadLine();

        int stride = _format.PixelWidth * 4;
        var pixels = new byte[stride * _format.PixelHeight];
        int frames = (int)Math.Round(Duration * fps);
        Stream input = process.StandardInput.BaseStream;
        for (int frame = 0; frame < frames; frame++)
        {
            Render(frame / (double)fps);
            _bitmap.CopyPixels(pixels, stride, 0);
            input.Write(pixels);
            if (frame % fps == 0)
                Console.Write($"\r  {frame / fps} / {frames / fps} s");
        }

        input.Close();
        process.WaitForExit();
        Console.WriteLine();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg failed: {errors}");
    }

    private void Render(double time)
    {
        Show(time);
        _bitmap.Clear();
        _bitmap.Render(_root);
    }

    private void Show(double time)
    {
        int index = 0;
        double t = time;
        while (index < _scenes.Count - 1 && t >= _scenes[index].Duration)
        {
            t -= _scenes[index].Duration;
            index++;
        }

        Scene scene = _scenes[index];
        Canvas host = scene.FullFrame ? _fullHost : _stageHost;
        if (scene != _current)
        {
            _stageHost.Children.Clear();
            _fullHost.Children.Clear();
            host.Children.Add(scene.Root(new Size(host.Width, host.Height)));
            _current = scene;
            _caption.Text = scene.Caption ?? "";
            _subcaption.Text = scene.Subcaption ?? "";
            _root.UpdateLayout();
        }

        scene.Update(t);

        double fadeIn = scene.FadeIn ? Ease.Progress(t, 0, 0.22) : 1;
        double fadeOut = scene.FadeOut ? 1 - Ease.Progress(t, scene.Duration - 0.2, scene.Duration) : 1;
        scene.Root(default).Opacity = Math.Min(fadeIn, fadeOut);

        // Captions only fade when they change, so a line stays put across scenes that share it.
        string? previous = index > 0 ? _scenes[index - 1].Caption : null;
        string? next = index < _scenes.Count - 1 ? _scenes[index + 1].Caption : null;
        double captionIn = previous == scene.Caption ? 1 : Ease.Out(Ease.Progress(t, 0.05, 0.05 + CaptionFade));
        double captionOut = next == scene.Caption ? 1 : 1 - Ease.Progress(t, scene.Duration - 0.2, scene.Duration);
        _captionBox.Opacity = scene.FullFrame || scene.Caption is null ? 0 : Math.Min(captionIn, captionOut);
        _captionSlide.Y = (1 - captionIn) * 14;
        if (_footer is not null)
            _footer.Opacity = scene.FullFrame ? 0 : 0.85;

        _root.UpdateLayout();
    }
}
