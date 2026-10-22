using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Morphdrop.Actions;
using Morphdrop.Editing;
using Morphdrop.Imaging;
using Morphdrop.Settings;
using Shapes = System.Windows.Shapes;

namespace Morphdrop.Pdf;

/// <summary>
/// Sign a PDF or fill it in: put text, today's date or your signature on any page, drag them into place, and save a
/// signed copy ("name (signed).pdf"). The page is shown in points, so what you see is where it lands.
/// </summary>
internal sealed class PdfSignWindow : Window
{
    private const double RenderSide = 1600;
    private const double SignatureWidth = 170;
    private static readonly double[] TextSizes = { 10, 14, 20 };

    private readonly IFileAction _action;
    private readonly string _path;
    private readonly ActionRunner _runner;
    private readonly IReadOnlyList<Size> _sizes;
    private readonly BitmapSource?[] _pages;
    private readonly List<Item> _items = new();
    private readonly Grid _paper = new();
    private readonly Image _pageImage = new() { Stretch = Stretch.Fill };
    private readonly Canvas _overlay = new() { Background = Brushes.Transparent };
    private readonly TextBlock _pageLabel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0), MinWidth = 90, TextAlignment = TextAlignment.Center };
    private readonly Button _previous;
    private readonly Button _next;
    private readonly Button _save;
    private int _page;
    private double _textSize = TextSizes[1];
    private Item? _selected;

    private PdfSignWindow(IFileAction action, string path, IReadOnlyList<Size> sizes, ActionRunner runner)
    {
        _action = action;
        _path = path;
        _runner = runner;
        _sizes = sizes;
        _pages = new BitmapSource?[sizes.Count];
        Title = $"Sign or fill in – {Path.GetFileName(path)}";
        Width = 1000;
        Height = 820;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        AppTheme.Attach(this);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete && _selected is { Box: null } picture)
            {
                Remove(picture);
                e.Handled = true;
            }
        };

        _paper.Children.Add(_pageImage);
        _paper.Children.Add(_overlay);
        _overlay.MouseLeftButtonDown += (_, _) => Select(null);

        _previous = IconButton("", "Previous page", () => ShowPage(_page - 1));
        _next = IconButton("", "Next page", () => ShowPage(_page + 1));
        _save = new Button { Content = "Save signed copy", IsEnabled = false };
        _save.SetResourceReference(StyleProperty, "AccentButton");
        _save.Click += (_, _) => Save();
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        cancel.SetResourceReference(StyleProperty, "ActionButton");
        cancel.Click += (_, _) => Close();

        var tools = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        tools.Children.Add(EditorUi.Button("Add text", () => AddTextBox("")));
        tools.Children.Add(EditorUi.Button("Add date", () => AddTextBox(DateTime.Today.ToString("d", System.Globalization.CultureInfo.CurrentCulture))));
        tools.Children.Add(EditorUi.Button("Add signature", () => AddSignature(redraw: false)));
        tools.Children.Add(EditorUi.Button("New signature", () => AddSignature(redraw: true)));
        tools.Children.Add(new Border { Width = 10 });
        tools.Children.Add(EditorUi.Label("Text size"));
        for (int i = 0; i < TextSizes.Length; i++)
        {
            double size = TextSizes[i];
            tools.Children.Add(EditorUi.Chip(new[] { "Small", "Medium", "Large" }[i], "signtext", size == _textSize, () => SetTextSize(size)));
        }

        var bottom = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 12, 20, 12),
            Child = new DockPanel
            {
                Children =
                {
                    Docked(new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { cancel, _save } }, Dock.Right),
                    tools,
                },
            },
        };
        bottom.SetResourceReference(Border.BackgroundProperty, "Theme.Panel");
        bottom.SetResourceReference(Border.BorderBrushProperty, "Theme.PanelBorder");

        var navigation = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { _previous, _pageLabel, _next },
        };
        var hint = EditorUi.Label("Drag the handle to move an item; drag a signature's corner to resize it.");
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.Margin = new Thickness(0, 6, 0, 0);

        var sheet = new Border
        {
            Child = _paper,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Direction = 270, Opacity = 0.35 },
        };
        Content = new DockPanel
        {
            Children =
            {
                Docked(bottom, Dock.Bottom),
                Docked(navigation, Dock.Top),
                Docked(hint, Dock.Top),
                new Viewbox { Child = sheet, Margin = new Thickness(28, 16, 28, 20), Stretch = Stretch.Uniform },
            },
        };

        ShowPage(0);
        _ = Task.Run(RenderPages);
    }

    public static void Open(IFileAction action, IReadOnlyList<string> files, ActionRunner runner)
    {
        string path = files[0];
        IReadOnlyList<Size> sizes;
        try
        {
            sizes = PdfStamps.PageSizes(path);
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't open '{path}' to sign it", ex);
            runner.RunJob(action, path, () => throw new InvalidDataException(ex.Message));
            return;
        }

        var window = new PdfSignWindow(action, path, sizes, runner);
        window.Show();
        window.Topmost = true; // Morphdrop runs in the background, so make sure the window is seen
        window.Activate();
        window.Topmost = false;
    }

    private void RenderPages()
    {
        try
        {
            PdfPages.RenderThumbnails(_path, RenderSide, (index, _, png) =>
            {
                BitmapSource picture = Thumbnails.FromPng(png);
                Dispatcher.Invoke(() =>
                {
                    _pages[index] = picture;
                    if (index == _page)
                        _pageImage.Source = picture;
                });
            });
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't draw the pages of '{_path}'", ex);
        }
    }

    private void ShowPage(int page)
    {
        _page = Math.Clamp(page, 0, _sizes.Count - 1);
        Size size = _sizes[_page];
        _paper.Width = _overlay.Width = size.Width;
        _paper.Height = _overlay.Height = size.Height;
        _pageImage.Source = _pages[_page];
        _overlay.Children.Clear();
        foreach (Item item in _items.Where(item => item.Page == _page))
            _overlay.Children.Add(item.Host);
        _pageLabel.Text = $"Page {_page + 1} of {_sizes.Count}";
        _previous.IsEnabled = _page > 0;
        _next.IsEnabled = _page < _sizes.Count - 1;
        Select(null);
    }

    private void AddTextBox(string text)
    {
        Size page = _sizes[_page];
        int already = _items.Count(item => item.Page == _page && item.Box is not null);
        var box = new TextBox
        {
            Text = text,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = _textSize,
            Foreground = new SolidColorBrush(PdfText.Ink),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            AcceptsReturn = true,
            MinWidth = 40,
            CaretBrush = Brushes.Black,
        };
        box.TextChanged += (_, _) => box.FlowDirection = PdfText.IsRightToLeft(box.Text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var item = new Item(_page, box, null) { Position = new Point(page.Width * 0.12, page.Height * 0.12 + already * (_textSize * 2.2)) };
        Build(item);
        box.GotKeyboardFocus += (_, _) => Select(item);
        Add(item);
        box.Focus();
        box.SelectAll();
    }

    private void AddSignature(bool redraw)
    {
        byte[]? png = redraw ? null : SignaturePad.Saved;
        png ??= SignaturePad.Ask(this);
        if (png is null)
            return;

        BitmapSource picture = Thumbnails.FromPng(png);
        Size page = _sizes[_page];
        double height = SignatureWidth * picture.PixelHeight / Math.Max(1, picture.PixelWidth);
        var item = new Item(_page, null, png)
        {
            Picture = picture,
            Size = new Size(SignatureWidth, height),
            Position = new Point(page.Width - SignatureWidth - page.Width * 0.1, page.Height - height - page.Height * 0.12),
        };
        Build(item);
        Add(item);
        Select(item);
    }

    private void Add(Item item)
    {
        _items.Add(item);
        _overlay.Children.Add(item.Host);
        _save.IsEnabled = true;
    }

    private void Remove(Item item)
    {
        _items.Remove(item);
        _overlay.Children.Remove(item.Host);
        _save.IsEnabled = _items.Count > 0;
        if (_selected == item)
            _selected = null;
    }

    /// <summary>The item on the page: its content, a move handle, a delete button and, for pictures, a resize corner.</summary>
    private void Build(Item item)
    {
        var frame = new Shapes.Rectangle { Stroke = EditorUi.Accent, StrokeThickness = 0.75, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false, Visibility = Visibility.Hidden };
        var host = new Grid { Tag = frame };
        item.Host = host;
        item.Frame = frame;

        var move = Handle(Cursors.SizeAll, "");
        move.HorizontalAlignment = HorizontalAlignment.Left;
        move.VerticalAlignment = VerticalAlignment.Top;
        move.Margin = new Thickness(-16, -16, 0, 0);
        move.DragStarted += (_, _) => Select(item);
        move.DragDelta += (_, e) => Move(item, e.HorizontalChange, e.VerticalChange);

        var delete = new Button { Content = new TextBlock { Text = "", FontFamily = EditorUi.IconFont, FontSize = 7 }, Width = 14, Height = 14, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -16, -16, 0), ToolTip = "Remove", Cursor = Cursors.Hand };
        delete.SetResourceReference(StyleProperty, "ActionButton");
        delete.Click += (_, _) => Remove(item);

        if (item.Box is { } box)
        {
            host.Children.Add(box);
            host.Children.Add(frame);
        }
        else
        {
            var picture = new Image { Source = item.Picture, Stretch = Stretch.Fill };
            var body = new Thumb { Cursor = Cursors.SizeAll, Opacity = 0 }; // the whole signature drags
            body.DragStarted += (_, _) => Select(item);
            body.DragDelta += (_, e) => Move(item, e.HorizontalChange, e.VerticalChange);
            var resize = Handle(Cursors.SizeNWSE, null);
            resize.Width = resize.Height = 9;
            resize.HorizontalAlignment = HorizontalAlignment.Right;
            resize.VerticalAlignment = VerticalAlignment.Bottom;
            resize.Margin = new Thickness(0, 0, -5, -5);
            resize.DragDelta += (_, e) => Resize(item, e.HorizontalChange);
            host.Children.Add(picture);
            host.Children.Add(frame);
            host.Children.Add(body);
            host.Children.Add(resize);
            host.Width = item.Size.Width;
            host.Height = item.Size.Height;
        }

        host.Children.Add(move);
        host.Children.Add(delete);
        host.PreviewMouseLeftButtonDown += (_, _) => Select(item);
        Place(item);
    }

    private static Thumb Handle(Cursor cursor, string? glyph)
    {
        var face = new FrameworkElementFactory(typeof(Border));
        face.SetValue(Border.BackgroundProperty, EditorUi.Accent);
        face.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        if (glyph is not null)
        {
            var icon = new FrameworkElementFactory(typeof(TextBlock));
            icon.SetValue(TextBlock.TextProperty, glyph);
            icon.SetValue(TextBlock.FontFamilyProperty, EditorUi.IconFont);
            icon.SetValue(TextBlock.FontSizeProperty, 8.0);
            icon.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            icon.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            icon.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            face.AppendChild(icon);
        }

        return new Thumb { Width = 14, Height = 14, Cursor = cursor, Template = new ControlTemplate(typeof(Thumb)) { VisualTree = face } };
    }

    private void Move(Item item, double dx, double dy)
    {
        Size page = _sizes[item.Page];
        item.Position = new Point(Math.Clamp(item.Position.X + dx, 0, page.Width - 10), Math.Clamp(item.Position.Y + dy, 0, page.Height - 10));
        Place(item);
    }

    private static void Resize(Item item, double dx)
    {
        double width = Math.Max(40, item.Size.Width + dx);
        item.Size = new Size(width, width * item.Size.Height / item.Size.Width);
        item.Host.Width = item.Size.Width;
        item.Host.Height = item.Size.Height;
    }

    private static void Place(Item item)
    {
        Canvas.SetLeft(item.Host, item.Position.X);
        Canvas.SetTop(item.Host, item.Position.Y);
    }

    private void Select(Item? item)
    {
        _selected = item;
        foreach (Item each in _items)
            each.Frame.Visibility = each == item ? Visibility.Visible : Visibility.Hidden;
    }

    private void SetTextSize(double size)
    {
        _textSize = size;
        if (_selected?.Box is { } box)
            box.FontSize = size;
    }

    private void Save()
    {
        Stamp[] stamps = _items.Select(item => item.Box is { } box
                ? (Stamp)new TextStamp(item.Page, new Rect(item.Position, new Size(box.ActualWidth, box.ActualHeight)), box.Text, box.FontSize)
                : new PictureStamp(item.Page, new Rect(item.Position, item.Size), item.Png!))
            .Where(stamp => stamp is not TextStamp { Text: var text } || text.Trim().Length > 0)
            .ToArray();
        if (stamps.Length == 0)
            return;

        string path = _path;
        _runner.RunJob(_action, path, () => PdfStamps.Save(path, stamps));
        Close();
    }

    private static Button IconButton(string glyph, string tip, Action click)
    {
        var button = new Button { Content = new TextBlock { Text = glyph, FontFamily = EditorUi.IconFont, FontSize = 12 }, ToolTip = tip, Padding = new Thickness(10, 5, 10, 5) };
        button.SetResourceReference(StyleProperty, "ActionButton");
        button.Click += (_, _) => click();
        return button;
    }

    private static T Docked<T>(T element, Dock dock) where T : UIElement
    {
        DockPanel.SetDock(element, dock);
        return element;
    }

    /// <summary>A text box or a signature on one page, at a position in points.</summary>
    private sealed class Item(int page, TextBox? box, byte[]? png)
    {
        public int Page { get; } = page;

        public TextBox? Box { get; } = box;

        public byte[]? Png { get; } = png;

        public BitmapSource? Picture { get; init; }

        public Point Position { get; set; }

        public Size Size { get; set; }

        public Grid Host { get; set; } = null!;

        public Shapes.Rectangle Frame { get; set; } = null!;
    }
}
