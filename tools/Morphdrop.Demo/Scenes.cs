using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Morphdrop.Actions;
using Morphdrop.Editing;
using Morphdrop.Overlay;

namespace Morphdrop.Demo;

internal abstract class Scene
{
    private FrameworkElement? _root;

    public string? Caption { get; init; }

    public string? Subcaption { get; init; }

    /// <summary>Uses the whole frame (intro, outro) instead of the stage under the captions.</summary>
    public bool FullFrame { get; init; }

    public bool FadeIn { get; init; } = true;

    public bool FadeOut { get; init; } = true;

    public abstract double Duration { get; }

    public FrameworkElement Root(Size size) => _root ??= Build(size);

    protected abstract FrameworkElement Build(Size size);

    public abstract void Update(double t);
}

internal static class Script
{
    public static IReadOnlyList<Scene> Create(DemoAssets a)
    {
        const string media = "Word, video and audio too";
        const string mediaSub = "DOCX → PDF,  MOV → GIF,  WAV → MP3  ·  all on your PC";
        return new Scene[]
        {
            new IntroScene(),
            new WheelScene(a.Sunset, "WEBP", page: 0, fast: false)
            {
                Caption = "Drag a file + press Shift",
                Subcaption = "Drop it on a format: PNG, JPG, WebP, AVIF, PDF, ICO…",
                Result = a.SunsetWebP,
                Toast = ($"Saved {Path.GetFileName(a.SunsetWebP)}", $"{Size(a.Sunset)} → {Size(a.SunsetWebP)}. Click to show it in File Explorer."),
            },
            new WheelScene(a.Sunset, "Compress", page: 1, fast: true)
            {
                Caption = "Press Shift again for tools",
                Subcaption = "Compress, resize, remove location & camera info",
                Others = new[] { a.SunsetWebP },
                Result = a.SunsetCompressed,
                Toast = ($"Saved {Path.GetFileName(a.SunsetCompressed)}", $"{Size(a.Sunset)} → {Size(a.SunsetCompressed)}. Click to show it in File Explorer."),
            },
            new CropScene(a)
            {
                Caption = "Once more for the editors",
                Subcaption = "Crop without opening another app",
            },
            new BackgroundScene(a)
            {
                Caption = "Put it on a background",
                Subcaption = "Gradients, padding, rounded corners, shadow",
            },
            new RedactScene(a)
            {
                Caption = "Hide private details",
                Subcaption = "Black boxes or pixelate. Location data goes too.",
            },
            new WheelScene(a.Scans[0], "PDF", page: 0, fast: false)
            {
                Caption = "Many images → one PDF",
                Subcaption = "Drag them together, drop on PDF: a page each, in order",
                Together = a.Scans.Skip(1).ToArray(),
                Result = a.ScansPdf,
            },
            new WheelScene(a.Report, "PDF", page: 0, fast: true) { Caption = media, Subcaption = mediaSub, Result = a.ReportPdf },
            new WheelScene(a.Clip, "GIF", page: 0, fast: true) { Caption = media, Subcaption = mediaSub, Result = a.ClipGif },
            new WheelScene(a.Song, "MP3", page: 0, fast: true) { Caption = media, Subcaption = mediaSub, Result = a.SongMp3 },
            new OutroScene(),
        };
    }

    private static string Size(string path) => ByteSize.Format(new FileInfo(path).Length);
}

internal sealed class IntroScene : Scene
{
    private readonly ScaleTransform _logoScale = new();
    private readonly TranslateTransform _titleSlide = new();
    private FrameworkElement _logo = null!;
    private FrameworkElement _title = null!;
    private FrameworkElement _tagline = null!;

    public IntroScene()
    {
        FullFrame = true;
        FadeIn = false;
    }

    public override double Duration => 2.4;

    protected override FrameworkElement Build(Size size)
    {
        bool tall = size.Height > size.Width * 1.2;
        double logo = tall ? 170 : 140;
        _logo = new Image { Source = Visuals.LogoImage(), Width = logo, Height = logo, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _logoScale, Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Direction = 270, Opacity = 0.5 } };
        _title = new TextBlock { Text = "Morphdrop", FontFamily = Visuals.DisplayFont, FontSize = tall ? 52 : 44, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 0), RenderTransform = _titleSlide };
        _tagline = new TextBlock { Text = "Convert and edit any file\nwith drag + Shift", FontFamily = Visuals.TextFont, FontSize = tall ? 22 : 19, Foreground = Visuals.Frozen(Color.FromRgb(0xDD, 0xD9, 0xF5)), TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        return new Grid
        {
            Width = size.Width,
            Height = size.Height,
            Children = { new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, tall ? 60 : 10), Children = { _logo, _title, _tagline } } },
        };
    }

    public override void Update(double t)
    {
        double logo = Ease.Progress(t, 0.1, 0.65);
        _logo.Opacity = Ease.Progress(t, 0.1, 0.3);
        _logoScale.ScaleX = _logoScale.ScaleY = 0.55 + 0.45 * Ease.OutBack(logo);
        double title = Ease.Out(Ease.Progress(t, 0.45, 0.8));
        _title.Opacity = title;
        _titleSlide.Y = (1 - title) * 16;
        _tagline.Opacity = Ease.Out(Ease.Progress(t, 0.75, 1.1));
    }
}

internal sealed class OutroScene : Scene
{
    private readonly List<(FrameworkElement Element, double Start)> _parts = new();

    public OutroScene()
    {
        FullFrame = true;
    }

    public override double Duration => 3.6;

    protected override FrameworkElement Build(Size size)
    {
        bool tall = size.Height > size.Width * 1.2;
        var logo = new Image { Source = Visuals.LogoImage(), Width = tall ? 128 : 104, Height = tall ? 128 : 104, Effect = new DropShadowEffect { BlurRadius = 36, ShadowDepth = 8, Direction = 270, Opacity = 0.5 } };
        var title = new TextBlock { Text = "Morphdrop", FontFamily = Visuals.DisplayFont, FontSize = tall ? 46 : 40, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 20, 0, 0) };
        var line = new TextBlock { Text = "Free & open source for Windows", FontFamily = Visuals.TextFont, FontSize = tall ? 21 : 18, Foreground = Visuals.Frozen(Color.FromRgb(0xDD, 0xD9, 0xF5)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        var link = new Border
        {
            Background = Visuals.Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            BorderBrush = Visuals.Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(20, 10, 20, 11),
            Margin = new Thickness(0, 26, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock { Text = "github.com/SALEM501jo/Morphdrop", FontFamily = Visuals.TextFont, FontSize = tall ? 18 : 16, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White },
        };
        _parts.Add((logo, 0.1));
        _parts.Add((title, 0.3));
        _parts.Add((line, 0.5));
        _parts.Add((link, 0.75));
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, tall ? 40 : 0) };
        foreach ((FrameworkElement element, _) in _parts)
        {
            element.RenderTransform = new TranslateTransform();
            stack.Children.Add(element);
        }

        return new Grid { Width = size.Width, Height = size.Height, Children = { stack } };
    }

    public override void Update(double t)
    {
        foreach ((FrameworkElement element, double start) in _parts)
        {
            double k = Ease.Out(Ease.Progress(t, start, start + 0.4));
            element.Opacity = k;
            ((TranslateTransform)element.RenderTransform).Y = (1 - k) * 18;
        }
    }
}

/// <summary>
/// A desktop with a file: the cursor drags it, Shift opens the wheel (and turns pages), the file is dropped on a slice
/// and the result appears next to it, maybe with Morphdrop's notification. The wheel is the app's real WheelView with
/// the app's real pages for that file.
/// </summary>
internal class WheelScene : Scene
{
    private const double PageGapFull = 0.55;
    private const double PageGapFast = 0.45;

    private readonly string _file;
    private readonly string _target;
    private readonly int _page;
    private readonly bool _fast;
    private readonly IReadOnlyList<ActionPage> _pages;
    private readonly List<double> _presses = new();
    private readonly double _grab;
    private readonly double _dragEnd;
    private readonly double _moveStart;
    private readonly double _moveEnd;

    private WheelView _wheel = null!;
    private readonly List<DesktopFile> _dragged = new();
    private DesktopFile? _result;
    private FrameworkElement _ghost = null!;
    private KeyCap _keyCap = null!;
    private Toast? _toast;
    private int _shownPage = -1;
    private Point _wheelOrigin;
    private Point _start;
    private Point _filePoint;
    private Point _hubPoint;
    private Point _targetPoint;

    public WheelScene(string file, string target, int page, bool fast)
    {
        _file = file;
        _target = target;
        _page = page;
        _fast = fast;
        _pages = ActionCatalog.ForFileName(file);

        double gap = fast ? PageGapFast : PageGapFull;
        _grab = fast ? 0 : 0.6;
        _dragEnd = fast ? 0.35 : 1.25;
        double open = _dragEnd + (fast ? 0.07 : 0.15);
        for (int i = 0; i <= page; i++)
            _presses.Add(open + i * gap);
        _moveStart = _presses[^1] + (fast ? 0.2 : 0.35);
        _moveEnd = _moveStart + (fast ? 0.35 : 0.55);
        Release = _moveEnd + (fast ? 0.2 : 0.3);
    }

    public string[] Others { get; init; } = Array.Empty<string>();

    /// <summary>More files dragged together with the first one (they're already selected).</summary>
    public string[] Together { get; init; } = Array.Empty<string>();

    public string? Result { get; init; }

    public (string Title, string Body)? Toast { get; init; }

    protected double Release { get; }

    protected Canvas Stage { get; private set; } = null!;

    protected FrameworkElement Pointer { get; private set; } = null!;

    protected Point ReleasePoint => _targetPoint;

    public override double Duration => Release + (Toast is not null ? 2.1 : Result is not null ? 0.9 : 0.6);

    protected override FrameworkElement Build(Size size)
    {
        Stage = new Canvas { Width = size.Width, Height = size.Height };
        string[] dragged = Files;
        for (int i = 0; i < dragged.Length; i++)
        {
            var file = new DesktopFile(dragged[i]);
            file.PlaceAt(Stage, i);
            _dragged.Add(file);
        }

        for (int i = 0; i < Others.Length; i++)
            new DesktopFile(Others[i]).PlaceAt(Stage, dragged.Length + i);
        if (Result is not null)
        {
            _result = new DesktopFile(Result);
            _result.PlaceAt(Stage, dragged.Length + Others.Length);
            _result.Pop(0);
        }

        _ghost = Ghost();
        Stage.Children.Add(_ghost);

        var center = new Point(size.Width / 2 + 40, size.Height / 2);
        _wheelOrigin = new Point(center.X - WheelGeometry.Size / 2, center.Y - WheelGeometry.Size / 2);
        _wheel = new WheelView { Opacity = 0 };
        _wheel.SetDarkTheme(false);
        Visuals.Place(Stage, _wheel, _wheelOrigin.X, _wheelOrigin.Y);

        if (Toast is { } toast)
        {
            _toast = new Toast(toast.Title, toast.Body);
            Visuals.Place(Stage, _toast, size.Width - 300 - 16, size.Height - 104);
        }

        _keyCap = new KeyCap();
        bool tall = size.Height > 500;
        Visuals.Place(Stage, _keyCap, tall ? size.Width / 2 - 52 : size.Width - 128, size.Height - (tall ? 58 : 52));

        Pointer = Visuals.Cursor();
        Stage.Children.Add(Pointer);

        Point fileCenter = DesktopFile.SlotCenter(0, size.Height);
        _filePoint = new Point(fileCenter.X + 4, fileCenter.Y + 6);
        _start = new Point(size.Width * 0.78, size.Height * 0.86);
        _hubPoint = new Point(center.X + 28, center.Y + 30); // still on the hub, without hiding its text
        IReadOnlyList<IFileAction> actions = _pages[_page].Actions;
        int index = actions.Select(action => action.Label).ToList().IndexOf(_target);
        if (index < 0)
            throw new InvalidOperationException($"No '{_target}' slice for {Path.GetFileName(_file)}.");
        Point slice = WheelGeometry.PointAt(WheelGeometry.LabelRadius + 10, WheelGeometry.SliceCenterAngle(index, actions.Count));
        _targetPoint = new Point(_wheelOrigin.X + slice.X, _wheelOrigin.Y + slice.Y);
        return Stage;
    }

    private string[] Files => Together.Prepend(_file).ToArray();

    /// <summary>What Explorer drags along: the icon, or a little stack with a count for several files.</summary>
    private FrameworkElement Ghost()
    {
        var ghost = new Grid { Width = 84, Height = 84, Opacity = 0 };
        for (int i = Math.Min(_dragged.Count, 3) - 1; i >= 0; i--)
        {
            ghost.Children.Add(new Image
            {
                Source = _dragged[i].Icon,
                Width = 64,
                Height = 64,
                Margin = new Thickness(i * 7, i * 7, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            });
        }

        if (_dragged.Count > 1)
        {
            ghost.Children.Add(new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = Visuals.Frozen(Color.FromRgb(0x00, 0x67, 0xC0)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = $"{_dragged.Count}", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
        }

        return ghost;
    }

    protected virtual Point CursorAt(double t)
    {
        if (!_fast && t < _grab)
            return Ease.Move(t, 0, _grab - 0.05, _start, _filePoint);
        if (t < _moveStart)
            return Ease.Move(t, _grab + 0.03, _dragEnd, _filePoint, _hubPoint);
        return Ease.Move(t, _moveStart, _moveEnd, _hubPoint, _targetPoint);
    }

    public override void Update(double t)
    {
        Point cursor = CursorAt(t);
        Visuals.Move(Pointer, cursor);

        bool dragging = t >= _grab && t < Release;
        for (int i = 0; i < _dragged.Count; i++)
            _dragged[i].Selected = t >= _grab || i > 0;
        _ghost.Opacity = dragging && t >= _grab + 0.06 ? 0.7 : 0;
        Visuals.Move(_ghost, new Point(cursor.X - 36, cursor.Y - 32));

        double open = _presses[0];
        double visible = t < Release ? Ease.Progress(t, open, open + 0.12) : 1 - Ease.Progress(t, Release, Release + 0.15);
        _wheel.Opacity = visible;
        double grow = 0.86 + 0.14 * Ease.Out(Ease.Progress(t, open, open + 0.16));
        _wheel.ZoomTransform.ScaleX = _wheel.ZoomTransform.ScaleY = grow;

        int page = Math.Max(0, _presses.Count(p => p <= t) - 1);
        if (page != _shownPage)
        {
            _shownPage = page;
            IReadOnlyList<IFileAction> actions = _pages[page].Actions;
            _wheel.SetSlices(actions.Select(action => new WheelItem(action.Label, action.Glyph)).ToList());
            _wheel.SetHint(_pages.Count > 1 ? $"Shift → {_pages[(page + 1) % _pages.Count].Name}" : null);
        }

        double turnStart = _presses[page];
        double turn = page == 0 ? 1 : Ease.Out(Ease.Progress(t, turnStart, turnStart + 0.18));
        _wheel.SliceTurn.Angle = -30 * (1 - turn);
        _wheel.SliceLayer.Opacity = 0.2 + 0.8 * turn;

        if (t < Release + 0.01)
        {
            IReadOnlyList<IFileAction> actions = _pages[page].Actions;
            int index = WheelGeometry.HitTest(new Point(cursor.X - _wheelOrigin.X, cursor.Y - _wheelOrigin.Y), actions.Count);
            _wheel.Highlight(index);
            // The same wording as WheelWindow.
            string[] files = Files;
            _wheel.SetHub(files.Length > 1 ? $"{files.Length} items" : ByteSize.Format(new FileInfo(_file).Length), index switch
            {
                >= 0 => actions[index] is ICombiningAction combining && combining.Combines(files)
                    ? combining.CombinedDescription(files.Length)
                    : actions[index].Description,
                WheelGeometry.HubIndex => "Release here to cancel",
                _ => files.Length > 1 ? "Drop on an action" : Path.GetFileName(_file),
            });
        }

        _keyCap.Update(t, _presses);
        _result?.Pop(Ease.Progress(t, Release + 0.15, Release + 0.5));
        _toast?.Show(Ease.Progress(t, Release + 0.35, Release + 0.7));
    }
}

/// <summary>Helpers to drive the real editors the way a mouse would, through their private handlers.</summary>
internal static class EditorControl
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    public static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;

    public static void Call(object target, string name, params object[] arguments) =>
        target.GetType().GetMethod(name, Private)!.Invoke(target, arguments);

    public static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (T nested in Descendants<T>(child))
                yield return nested;
        }
    }

    public static Point CenterOf(FrameworkElement element, Visual stage) =>
        element.TranslatePoint(new Point(element.ActualWidth / 2, element.ActualHeight / 2), (UIElement)stage);

    // The crop the scene ends on, as fractions of the image; DemoAssets saves the real cropped copy with it.
    public static readonly Point CropTopLeft = new(0.2, 0.12);
    public static readonly Point CropBottomRight = new(0.86, 0.95);

    public static Rect CropRect(Size image, Point topLeft, Point bottomRight) =>
        new(new Point(topLeft.X * image.Width, topLeft.Y * image.Height), new Point(bottomRight.X * image.Width, bottomRight.Y * image.Height));

    public static BackgroundOptions FinalBackground =>
        BackgroundComposer.Default with { Background = BackgroundComposer.Presets[1], Padding = 0.14, AspectRatio = 1 };
}

/// <summary>The Edit page's Crop slice, then the crop window: two handles dragged in, Save, the cropped copy appears.</summary>
internal sealed class CropScene : WheelScene
{
    private readonly DemoAssets _assets;
    private EditableImage _image = null!;
    private CropEditor _editor = null!;
    private EditorFrame _frame = null!;
    private ImageSurface _surface = null!;
    private DesktopFile _cropped = null!;
    private Ripple _ripple = null!;
    private Point _save;

    public CropScene(DemoAssets assets)
        : base(assets.Sunset, "Crop", page: 2, fast: true)
    {
        _assets = assets;
    }

    private double R => Release;

    public override double Duration => R + 4.4;

    protected override FrameworkElement Build(Size size)
    {
        FrameworkElement root = base.Build(size);
        _cropped = new DesktopFile(_assets.SunsetCropped);
        _cropped.PlaceAt(Stage, 1);
        _cropped.Pop(0);
        _image = EditableImage.Load(_assets.Sunset);
        _editor = new CropEditor(_image);
        _surface = EditorControl.Field<ImageSurface>(_editor, "_surface");
        _frame = new EditorFrame($"Crop – {Path.GetFileName(_assets.Sunset)}", _editor, size);
        Stage.Children.Remove(Pointer);
        _frame.PlaceOn(Stage);
        _ripple = new Ripple();
        Stage.Children.Add(_ripple);
        Stage.Children.Add(Pointer);
        return root;
    }

    private Point ImagePoint(Point fraction) =>
        _surface.Overlay.TranslatePoint(new Point(fraction.X * _surface.Width, fraction.Y * _surface.Height), Stage);

    protected override Point CursorAt(double t)
    {
        if (t < R + 0.5)
            return base.CursorAt(t);

        Point topLeft = ImagePoint(new Point(0, 0)), topLeftEnd = ImagePoint(EditorControl.CropTopLeft);
        Point bottomRight = ImagePoint(new Point(1, 1)), bottomRightEnd = ImagePoint(EditorControl.CropBottomRight);
        if (t < R + 1.0)
            return Ease.Move(t, R + 0.5, R + 0.95, ReleasePoint, topLeft);
        if (t < R + 1.65)
            return Ease.Move(t, R + 1.0, R + 1.6, topLeft, topLeftEnd);
        if (t < R + 2.05)
            return Ease.Move(t, R + 1.65, R + 2.0, topLeftEnd, bottomRight);
        if (t < R + 2.7)
            return Ease.Move(t, R + 2.05, R + 2.65, bottomRight, bottomRightEnd);
        return Ease.Move(t, R + 2.7, R + 3.1, bottomRightEnd, _save);
    }

    public override void Update(double t)
    {
        double opening = Ease.Progress(t, R + 0.15, R + 0.45);
        double closing = Ease.Progress(t, R + 3.3, R + 3.5);
        _frame.Show(t < R + 3.3 ? opening : 1 - closing);
        Stage.UpdateLayout();
        _save = EditorControl.CenterOf(_frame.SaveButton, Stage);

        var size = new Size(1, 1);
        Point topLeft = Ease.Lerp(new Point(0, 0), EditorControl.CropTopLeft, Ease.InOut(Ease.Progress(t, R + 1.0, R + 1.6)));
        Point bottomRight = Ease.Lerp(new Point(1, 1), EditorControl.CropBottomRight, Ease.InOut(Ease.Progress(t, R + 2.05, R + 2.65)));
        Rect fraction = EditorControl.CropRect(size, topLeft, bottomRight);
        EditorControl.Call(_editor, "Update", new Rect(fraction.X * _surface.Width, fraction.Y * _surface.Height, fraction.Width * _surface.Width, fraction.Height * _surface.Height));

        base.Update(t);
        _frame.PressSave(t >= R + 3.15 && t < R + 3.27);
        _ripple.Update(t, new[] { (R + 3.15, _save) });
        _cropped.Pop(Ease.Progress(t, R + 3.55, R + 3.9));
    }
}

/// <summary>An editor window over the desktop with a scripted cursor; subclasses say what it does.</summary>
internal abstract class EditorScene : Scene
{
    private readonly string _file;
    private readonly string _result;
    private DesktopFile _resultIcon = null!;
    private Ripple _ripple = null!;
    private Point _start;

    protected EditorScene(string file, string result)
    {
        _file = file;
        _result = result;
    }

    protected Canvas Stage { get; private set; } = null!;

    protected EditorFrame Frame { get; private set; } = null!;

    protected FrameworkElement Pointer { get; private set; } = null!;

    protected abstract string Tool { get; }

    /// <summary>When Save is clicked; the window closes and the copy appears right after.</summary>
    protected abstract double SaveAt { get; }

    public override double Duration => SaveAt + 1.05;

    protected abstract IImageEditor CreateEditor(EditableImage image);

    protected override FrameworkElement Build(Size size)
    {
        Stage = new Canvas { Width = size.Width, Height = size.Height };
        new DesktopFile(_file).PlaceAt(Stage, 0);
        _resultIcon = new DesktopFile(_result);
        _resultIcon.PlaceAt(Stage, 1);
        _resultIcon.Pop(0);

        IImageEditor editor = CreateEditor(EditableImage.Load(_file));
        Frame = new EditorFrame($"{Tool} – {Path.GetFileName(_file)}", editor, size);
        Frame.PlaceOn(Stage);
        _ripple = new Ripple();
        Stage.Children.Add(_ripple);
        Pointer = Visuals.Cursor();
        Stage.Children.Add(Pointer);
        _start = new Point(size.Width * 0.72, size.Height * 0.8);
        return Stage;
    }

    /// <summary>Applies the script's state for <paramref name="t"/> and returns where the cursor is.</summary>
    protected abstract Point Script(double t, Point start);

    /// <summary>The clicks so far, for the ripples.</summary>
    protected abstract IEnumerable<(double Time, Point At)> Clicks { get; }

    protected Point SavePoint => EditorControl.CenterOf(Frame.SaveButton, Stage);

    public override void Update(double t)
    {
        double opening = Ease.Progress(t, 0.1, 0.4);
        double closing = Ease.Progress(t, SaveAt + 0.15, SaveAt + 0.35);
        Frame.Show(t < SaveAt + 0.15 ? opening : 1 - closing);
        Stage.UpdateLayout();

        Point cursor = t < SaveAt - 0.45 ? Script(t, _start) : Ease.Move(t, SaveAt - 0.45, SaveAt - 0.05, Script(SaveAt - 0.45, _start), SavePoint);
        Visuals.Move(Pointer, cursor);
        Frame.PressSave(t >= SaveAt && t < SaveAt + 0.12);
        _ripple.Update(t, Clicks.Append((SaveAt, SavePoint)));
        _resultIcon.Pop(Ease.Progress(t, SaveAt + 0.4, SaveAt + 0.75));
    }
}

/// <summary>Two swatches, the Padding slider, the Square size, Save.</summary>
internal sealed class BackgroundScene : EditorScene
{
    private BackgroundEditor _editor = null!;
    private readonly List<(double Time, Point At)> _clicks = new();
    private int _applied;

    public BackgroundScene(DemoAssets assets)
        : base(assets.Sunset, assets.SunsetBackground)
    {
    }

    protected override string Tool => "Background";

    protected override double SaveAt => 3.45;

    protected override IEnumerable<(double Time, Point At)> Clicks => _clicks;

    protected override IImageEditor CreateEditor(EditableImage image) => _editor = new BackgroundEditor(image);

    private FrameworkElement Swatch(int index) => EditorControl.Field<System.Collections.IList>(_editor, "_swatches") is var list
        ? (FrameworkElement)list[index]!.GetType().GetField("Item1")!.GetValue(list[index])!
        : throw new InvalidOperationException();

    private Slider Padding => EditorControl.Descendants<Slider>(_editor.Options).First();

    private RadioButton SquareChip => EditorControl.Descendants<RadioButton>(_editor.Options).First(chip => (string)chip.Content == "Square");

    private Point SliderPoint(double value)
    {
        Slider slider = Padding;
        double k = (value - slider.Minimum) / (slider.Maximum - slider.Minimum);
        return slider.TranslatePoint(new Point(5.5 + k * (slider.ActualWidth - 11), slider.ActualHeight / 2), Stage);
    }

    protected override Point Script(double t, Point start)
    {
        Point peach = EditorControl.CenterOf(Swatch(3), Stage);
        Point ocean = EditorControl.CenterOf(Swatch(1), Stage);
        const double from = 0.08, to = 0.14;
        Point sliderFrom = SliderPoint(from), sliderTo = SliderPoint(to);
        Point square = EditorControl.CenterOf(SquareChip, Stage);

        // Each step happens once, in order, so jumping straight to a later time still ends in the right state.
        (double Time, Point At, Action Apply)[] steps =
        {
            (0.85, peach, () => EditorControl.Call(_editor, "Change", Options() with { Background = BackgroundComposer.Presets[3] })),
            (1.4, ocean, () => EditorControl.Call(_editor, "Change", Options() with { Background = BackgroundComposer.Presets[1] })),
            (2.95, square, () => SquareChip.IsChecked = true),
        };
        while (_applied < steps.Length && t >= steps[_applied].Time)
        {
            steps[_applied].Apply();
            _clicks.Add((steps[_applied].Time, steps[_applied].At));
            _applied++;
        }

        if (t >= 1.95)
            Padding.Value = Ease.Lerp(from, to, Ease.InOut(Ease.Progress(t, 1.95, 2.5)));

        if (t < 0.85)
            return Ease.Move(t, 0.45, 0.8, start, peach);
        if (t < 1.4)
            return Ease.Move(t, 1.0, 1.35, peach, ocean);
        if (t < 1.95)
            return Ease.Move(t, 1.5, 1.9, ocean, sliderFrom);
        if (t < 2.55)
            return Ease.Move(t, 1.95, 2.5, sliderFrom, sliderTo);
        return Ease.Move(t, 2.55, 2.9, sliderTo, square);
    }

    private BackgroundOptions Options() => EditorControl.Field<BackgroundOptions>(_editor, "_options");
}

/// <summary>Two boxes dragged over the email and phone, then the card number; Save.</summary>
internal sealed class RedactScene : EditorScene
{
    private readonly DemoAssets _assets;
    private readonly List<(double Time, Point At)> _clicks = new();
    private RedactEditor _editor = null!;
    private ImageSurface _surface = null!;
    private int _begun;
    private int _ended;

    public RedactScene(DemoAssets assets)
        : base(assets.Invoice, assets.InvoiceRedacted)
    {
        _assets = assets;
    }

    protected override string Tool => "Redact";

    protected override double SaveAt => 3.0;

    protected override IEnumerable<(double Time, Point At)> Clicks => _clicks;

    private (Rect Area, double Start, double End)[] Boxes => new[]
    {
        (_assets.ContactArea, 0.9, 1.4),
        (_assets.CardArea, 1.85, 2.35),
    };

    protected override IImageEditor CreateEditor(EditableImage image)
    {
        _editor = new RedactEditor(image);
        _surface = EditorControl.Field<ImageSurface>(_editor, "_surface");
        return _editor;
    }

    private Point OnStage(Point preview) => _surface.Overlay.TranslatePoint(preview, Stage);

    protected override Point Script(double t, Point start)
    {
        (Rect Area, double Start, double End)[] boxes = Boxes;
        for (int i = 0; i < boxes.Length; i++)
        {
            (Rect area, double begin, double end) = boxes[i];
            Point current = Ease.Lerp(area.TopLeft, area.BottomRight, Ease.InOut(Ease.Progress(t, begin, end)));
            if (_begun == i && t >= begin)
            {
                EditorControl.Call(_editor, "BeginBox", area.TopLeft);
                _begun++;
            }

            if (_begun > i && _ended == i)
            {
                EditorControl.Call(_editor, "DragBox", current);
                if (t >= end)
                {
                    EditorControl.Call(_editor, "EndBox", area.BottomRight);
                    _ended++;
                }
            }
        }

        Point first = OnStage(boxes[0].Area.TopLeft), firstEnd = OnStage(boxes[0].Area.BottomRight);
        Point second = OnStage(boxes[1].Area.TopLeft), secondEnd = OnStage(boxes[1].Area.BottomRight);
        if (t < 0.9)
            return Ease.Move(t, 0.45, 0.85, start, first);
        if (t < 1.45)
            return Ease.Move(t, 0.9, 1.4, first, firstEnd);
        if (t < 1.85)
            return Ease.Move(t, 1.45, 1.8, firstEnd, second);
        return Ease.Move(t, 1.85, 2.35, second, secondEnd);
    }
}
