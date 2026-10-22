using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Morphdrop.Actions;
using Morphdrop.Editing;
using Morphdrop.Imaging;
using Morphdrop.Settings;

namespace Morphdrop.Pdf;

/// <summary>
/// Makes a PDF from pictures, one per page, with optional text above and below each picture: drag the pages into
/// order, type the text, pick the paper, and save. The preview is drawn with the same layout as the PDF.
/// </summary>
internal sealed class PictureDocumentWindow : Window
{
    private const uint PreviewSide = 1400;

    private readonly IFileAction _action;
    private readonly ActionRunner _runner;
    private readonly TileList<Page> _pages;
    private readonly PagePreview _preview = new();
    private readonly TextBox _above = TextArea();
    private readonly TextBox _below = TextArea();
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _save;
    private PageOptions _options = PageOptions.Default;
    private Page? _selected;
    private bool _loadingText;
    private Task _adding = Task.CompletedTask;

    private PictureDocumentWindow(IFileAction action, IReadOnlyList<string> files, ActionRunner runner)
    {
        _action = action;
        _runner = runner;
        Title = "Morphdrop – PDF with text";
        Width = 1180;
        Height = 780;
        MinWidth = 900;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        AppTheme.Attach(this);
        AllowDrop = true;
        Drop += (_, e) => AddDropped(e);

        _pages = new TileList<Page>(new StackPanel(), Tile);
        _pages.Clicked += Select;
        _pages.Changed += Refresh;

        _above.TextChanged += (_, _) => TextChanged(_above, (page, text) => page.Above = text);
        _below.TextChanged += (_, _) => TextChanged(_below, (page, text) => page.Below = text);

        _save = new Button { Content = "Save PDF", IsEnabled = false };
        _save.SetResourceReference(StyleProperty, "AccentButton");
        _save.Click += (_, _) => Save();
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        cancel.SetResourceReference(StyleProperty, "ActionButton");
        cancel.Click += (_, _) => Close();

        Content = Layout(cancel);
        _ = AddAsync(files.Order(ImageConverter.NaturalOrder.Instance));
    }

    public static void Open(IFileAction action, IReadOnlyList<string> files, ActionRunner runner)
    {
        var window = new PictureDocumentWindow(action, files, runner);
        window.Show();
        window.Topmost = true; // Morphdrop runs in the background, so make sure the window is seen
        window.Activate();
        window.Topmost = false;
    }

    private UIElement Layout(Button cancel)
    {
        var bottom = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 12, 20, 12),
            Child = new DockPanel
            {
                Children =
                {
                    Docked(new StackPanel { Orientation = Orientation.Horizontal, Children = { cancel, _save } }, Dock.Right),
                    _count,
                },
            },
        };
        bottom.SetResourceReference(Border.BackgroundProperty, "Theme.Panel");
        bottom.SetResourceReference(Border.BorderBrushProperty, "Theme.PanelBorder");

        var pagesHeader = Heading("Pages");
        var hint = EditorUi.Label("Drag to reorder. Drop more pictures anywhere in this window.");
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(4, 0, 4, 8);
        var left = new DockPanel
        {
            Width = 220,
            Margin = new Thickness(16, 16, 0, 16),
            Children =
            {
                Docked(pagesHeader, Dock.Top),
                Docked(hint, Dock.Top),
                new ScrollViewer { Content = _pages.Panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            },
        };

        var paper = new Border
        {
            Child = _preview,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Direction = 270, Opacity = 0.35 },
        };
        var middle = new Viewbox { Child = paper, Margin = new Thickness(28), Stretch = Stretch.Uniform };

        var copyToAll = EditorUi.Button("Use this text on every page", CopyTextToAll);
        copyToAll.Margin = new Thickness(0, 8, 0, 0);
        copyToAll.HorizontalAlignment = HorizontalAlignment.Left;
        var numbers = new CheckBox { Content = "Number the pages", Margin = new Thickness(0, 4, 0, 0) };
        numbers.Click += (_, _) => Change(_options with { PageNumbers = numbers.IsChecked == true });

        var settings = new StackPanel
        {
            Children =
            {
                Heading("Text above the picture"),
                _above,
                Heading("Text below the picture", top: 14),
                _below,
                copyToAll,
                Heading("Paper", top: 22),
                Chips("paper", (PaperSize.A4, "A4"), (PaperSize.Letter, "Letter"), (PaperSize.Picture, "Picture's shape")),
                Heading("Orientation", top: 14),
                Chips("orientation", (PageOrientation.Auto, "Auto"), (PageOrientation.Portrait, "Portrait"), (PageOrientation.Landscape, "Landscape")),
                Heading("Margins", top: 14),
                Chips("margins", (PageMargin.None, "None"), (PageMargin.Small, "Small"), (PageMargin.Normal, "Normal")),
                Heading("Text size", top: 14),
                Chips("textsize", (TextSize.Small, "Small"), (TextSize.Medium, "Medium"), (TextSize.Large, "Large")),
                Heading("Page numbers", top: 14),
                numbers,
            },
        };
        var right = new ScrollViewer
        {
            Width = 320,
            Padding = new Thickness(0, 16, 20, 16),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = settings,
        };

        return new DockPanel
        {
            Children = { Docked(bottom, Dock.Bottom), Docked(left, Dock.Left), Docked(right, Dock.Right), middle },
        };
    }

    private UIElement Chips<TChoice>(string group, params (TChoice Value, string Text)[] choices) where TChoice : struct, Enum
    {
        TChoice current = group switch
        {
            "paper" => (TChoice)(object)_options.Paper,
            "orientation" => (TChoice)(object)_options.Orientation,
            "margins" => (TChoice)(object)_options.Margin,
            _ => (TChoice)(object)_options.TextSize,
        };
        var row = new WrapPanel();
        foreach ((TChoice value, string text) in choices)
        {
            RadioButton chip = EditorUi.Chip(text, group, value.Equals(current), () => Change(value switch
            {
                PaperSize paper => _options with { Paper = paper },
                PageOrientation orientation => _options with { Orientation = orientation },
                PageMargin margin => _options with { Margin = margin },
                TextSize size => _options with { TextSize = size },
                _ => _options,
            }));
            chip.Margin = new Thickness(0, 0, 6, 6);
            row.Children.Add(chip);
        }

        return row;
    }

    private UIElement Tile(Page page)
    {
        int number = _pages.Items.ToList().IndexOf(page) + 1;
        var remove = new Button { Content = "", FontFamily = EditorUi.IconFont, FontSize = 10, Padding = new Thickness(6, 4, 6, 4), VerticalAlignment = VerticalAlignment.Top, ToolTip = "Remove this page" };
        remove.SetResourceReference(StyleProperty, "ActionButton");
        remove.Click += (_, _) => Remove(page);
        var name = new TextBlock { Text = Path.GetFileName(page.Path), TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = number > 0 ? $"{number}" : "", FontWeight = FontWeights.SemiBold, Width = 22, VerticalAlignment = VerticalAlignment.Center };
        var picture = new Image { Source = page.Picture, Width = 56, Height = 56, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 8, 0) };
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(remove, Dock.Right);
        row.Children.Add(remove);
        row.Children.Add(label);
        row.Children.Add(picture);
        row.Children.Add(name);
        return row;
    }

    /// <summary>
    /// Reads the pictures in parallel but adds them in the order given, after any added before (another drop).
    /// </summary>
    private async Task AddAsync(IEnumerable<string> paths)
    {
        var loads = paths.Select(path => (Path: path, Load: Task.Run(() => Thumbnails.OfPicture(path, PreviewSide)))).ToList();
        Task previous = _adding;
        var done = new TaskCompletionSource();
        _adding = done.Task;
        await previous;
        try
        {
            foreach ((string path, Task<(BitmapSource Picture, int Width, int Height)> load) in loads)
            {
                try
                {
                    (BitmapSource picture, int width, int height) = await load;
                    var page = new Page(path, picture, new Size(width, height));
                    _pages.Add(page);
                    if (_selected is null)
                        Select(page);
                }
                catch (Exception ex)
                {
                    Log.Error($"Couldn't open '{path}' for the PDF", ex);
                    _count.Text = $"Couldn't open {Path.GetFileName(path)}: {ex.Message}";
                }
            }
        }
        finally
        {
            done.SetResult();
        }
    }

    private void AddDropped(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            return;

        _ = AddAsync(files.Order(ImageConverter.NaturalOrder.Instance)
            .Where(file => File.Exists(file) && ImageFormat.FromPath(file) is { CanRead: true } format && format.Kind != ImageKind.Pdf));
    }

    private void Remove(Page page)
    {
        _pages.Remove(page);
        if (_selected == page)
            Select(_pages.Items.FirstOrDefault());
    }

    private void Select(Page? page)
    {
        _selected = page;
        _pages.Select(page);
        _loadingText = true;
        _above.Text = page?.Above ?? "";
        _below.Text = page?.Below ?? "";
        _loadingText = false;
        _above.IsEnabled = _below.IsEnabled = page is not null;
        ShowPreview();
    }

    private void TextChanged(TextBox box, Action<Page, string> store)
    {
        box.FlowDirection = PdfText.IsRightToLeft(box.Text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        if (_loadingText || _selected is null)
            return;

        store(_selected, box.Text.Trim());
        ShowPreview();
    }

    private void CopyTextToAll()
    {
        if (_selected is null)
            return;

        foreach (Page page in _pages.Items)
        {
            page.Above = _selected.Above;
            page.Below = _selected.Below;
        }
    }

    private void Change(PageOptions options)
    {
        _options = options;
        ShowPreview();
    }

    private void Refresh()
    {
        foreach (Page page in _pages.Items)
            _pages.Refresh(page);
        _pages.Select(_selected);
        int count = _pages.Count;
        _count.Text = count == 1 ? "1 page" : $"{count} pages";
        _save.IsEnabled = count > 0;
        ShowPreview();
    }

    private void ShowPreview()
    {
        IReadOnlyList<Page> pages = _pages.Items;
        int index = _selected is null ? -1 : pages.ToList().IndexOf(_selected);
        _preview.Show(_selected, _options, index + 1, pages.Count);
    }

    private void Save()
    {
        IReadOnlyList<Page> pages = _pages.Items;
        if (pages.Count == 0)
            return;

        PicturePage[] document = pages.Select(page => new PicturePage(page.Path, page.Above, page.Below)).ToArray();
        PageOptions options = _options;
        _runner.RunJob(_action, document[0].Path, () => PictureDocument.Save(document, options));
        Close();
    }

    private static TextBox TextArea()
    {
        var box = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 64,
            MaxHeight = 140,
            Padding = new Thickness(6, 4, 6, 4),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        box.SetResourceReference(BackgroundProperty, "Theme.Panel");
        box.SetResourceReference(ForegroundProperty, "Theme.Text");
        box.SetResourceReference(BorderBrushProperty, "Theme.ButtonBorder");
        box.SetResourceReference(TextBox.CaretBrushProperty, "Theme.Text");
        return box;
    }

    private static TextBlock Heading(string text, double top = 0) =>
        new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, top, 0, 6) };

    private static T Docked<T>(T element, Dock dock) where T : UIElement
    {
        DockPanel.SetDock(element, dock);
        return element;
    }

    /// <summary>A page in the list: the picture, its preview and its text.</summary>
    private sealed class Page(string path, BitmapSource picture, Size size)
    {
        public string Path { get; } = path;

        public BitmapSource Picture { get; } = picture;

        public Size Size { get; } = size;

        public string Above { get; set; } = "";

        public string Below { get; set; } = "";
    }

    /// <summary>The selected page as it will be in the PDF, in points.</summary>
    private sealed class PagePreview : FrameworkElement
    {
        private Page? _page;
        private PageOptions _options = PageOptions.Default;
        private int _number;
        private int _count;
        private PageLayout? _layout;

        public void Show(Page? page, PageOptions options, int number, int count)
        {
            _page = page;
            _options = options;
            _number = number;
            _count = count;
            _layout = page is null ? null : PictureDocument.Layout(new PicturePage(page.Path, page.Above, page.Below), page.Size, options);
            InvalidateMeasure();
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize) => _layout?.Page ?? new Size(595, 842);

        protected override void OnRender(DrawingContext context)
        {
            Size size = _layout?.Page ?? new Size(595, 842);
            context.DrawRectangle(Brushes.White, null, new Rect(size));
            if (_page is null || _layout is null)
                return;

            context.DrawImage(_page.Picture, _layout.Picture);
            if (_page.Above.Length > 0)
                PdfText.Draw(context, PdfText.Format(_page.Above, _layout.Above.Width, _options.FontSize, PdfText.SemiBold), _layout.Above);
            if (_page.Below.Length > 0)
                PdfText.Draw(context, PdfText.Format(_page.Below, _layout.Below.Width, _options.FontSize, PdfText.Regular), _layout.Below);
            if (_options.PageNumbers)
                PdfText.Draw(context, PdfText.Format($"{_number} / {_count}", _layout.Number.Width, PageLayout.NumberSize, PdfText.Regular, PictureDocument.NumberColor), _layout.Number);
        }
    }
}
